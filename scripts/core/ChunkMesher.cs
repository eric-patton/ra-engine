using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Builds the render mesh(es) and collision shape for a chunk using
/// neighbour-aware face culling and classic per-vertex ambient occlusion.
///
/// The pipeline is split into three steps so the expensive middle one can run
/// off the main thread:
///   1. <see cref="Capture"/> (main thread) copies the chunk plus a one-cell
///      border into an immutable <see cref="Snapshot"/>.
///   2. <see cref="BuildData"/> (any thread) turns a snapshot into plain
///      <see cref="MeshData"/> — only value-type structs, no Godot objects, so
///      it is safe to call from a worker thread.
///   3. <see cref="Apply"/> (main thread) uploads the data to the GPU and wires
///      up collision.
/// Every face-cull and AO sample reads at most one cell outside the chunk, so a
/// one-cell border is exactly enough to mesh borders correctly in isolation.</summary>
public static class ChunkMesher
{
    public const int Pad = 1;
    public const int SnapSize = Chunk.Size + 2 * Pad; // 18

    public static int SnapIndex(int x, int y, int z) =>
        ((y + Pad) * SnapSize + (z + Pad)) * SnapSize + (x + Pad);

    /// <summary>An immutable copy of a chunk and its one-cell border. Safe to read
    /// from a worker thread because nothing else mutates it after capture.</summary>
    public sealed class Snapshot
    {
        public Vector3I Coord;
        public ushort[] Cells; // length SnapSize^3, local coords -1..Chunk.Size
        public ushort Get(int x, int y, int z) => Cells[SnapIndex(x, y, z)];
    }

    /// <summary>Plain mesh data (no Godot resources) produced on a worker thread.</summary>
    public sealed class MeshData
    {
        public readonly Surface Opaque = new();
        public readonly Surface Water = new();
        public readonly List<Vector3> Collision = new();
        public bool IsEmpty => Opaque.Count == 0 && Water.Count == 0 && Collision.Count == 0;
        public int VertexCount => Opaque.Count + Water.Count;
    }

    public sealed class Surface
    {
        public readonly List<Vector3> Verts = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Plane> Tangents = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<Color> Custom = new();
        public readonly List<int> Indices = new();
        public int Count => Verts.Count;

        public void AddVertex(Vector3 pos, Vector3 normal, Plane tangent, Vector2 uv, Color custom)
        {
            Verts.Add(pos); Normals.Add(normal); Tangents.Add(tangent); Uvs.Add(uv); Custom.Add(custom);
        }
    }

    // ---- step 1: capture (main thread) ------------------------------------

    public static Snapshot Capture(VoxelWorld world, Chunk chunk)
    {
        var snap = new Snapshot
        {
            Coord = chunk.Coord,
            Cells = new ushort[SnapSize * SnapSize * SnapSize],
        };
        Vector3I baseW = chunk.Coord * Chunk.Size;

        // The chunk's own 16^3 interior: copy straight from the block array.
        for (int y = 0; y < Chunk.Size; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
            snap.Cells[SnapIndex(x, y, z)] = chunk.GetLocal(x, y, z);

        // The one-cell shell: query the world (crosses into neighbour chunks).
        for (int y = -Pad; y < Chunk.Size + Pad; y++)
        for (int z = -Pad; z < Chunk.Size + Pad; z++)
        for (int x = -Pad; x < Chunk.Size + Pad; x++)
        {
            bool interior = x >= 0 && x < Chunk.Size && y >= 0 && y < Chunk.Size && z >= 0 && z < Chunk.Size;
            if (interior) continue;
            snap.Cells[SnapIndex(x, y, z)] = world.GetBlockId(baseW.X + x, baseW.Y + y, baseW.Z + z);
        }
        return snap;
    }

    // ---- step 2: build geometry (worker thread) ---------------------------

    public static MeshData BuildData(Snapshot snap)
    {
        var md = new MeshData();

        for (int y = 0; y < Chunk.Size; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            ushort id = snap.Get(x, y, z);
            if (id == 0) continue;
            var self = BlockRegistry.Get(id);
            var local = new Vector3(x, y, z);

            for (int f = 0; f < 6; f++)
            {
                Vector3I nrm = VoxelGeometry.Normals[f];
                var nCell = new Vector3I(x + nrm.X, y + nrm.Y, z + nrm.Z); // snapshot-local
                ushort nbId = snap.Get(nCell.X, nCell.Y, nCell.Z);
                if (!ShouldDraw(self, id, BlockRegistry.Get(nbId), nbId)) continue;

                bool water = self.Render == RenderType.Water;
                Surface surf = water ? md.Water : md.Opaque;
                int baseIdx = surf.Count;

                int layer = self.FaceLayer[f];
                Vector3 normal = nrm;
                var tangent = new Plane(VoxelGeometry.Tangents[f], 1f);
                Vector3I du = VoxelGeometry.Du[f], dv = VoxelGeometry.Dv[f];

                for (int ci = 0; ci < 4; ci++)
                {
                    Vector2 uv = VoxelGeometry.Uvs[ci];
                    float ao = water ? 1f : VertexAo(snap, nCell, du, dv, uv);
                    surf.AddVertex(local + VoxelGeometry.Corners[f][ci], normal, tangent, uv,
                        new Color(layer, ao, 0f, 0f));
                }
                foreach (int t in VoxelGeometry.TriOrder)
                    surf.Indices.Add(baseIdx + t);

                if (self.Solid)
                    foreach (int t in VoxelGeometry.TriOrder)
                        md.Collision.Add(local + VoxelGeometry.Corners[f][t]);
            }
        }
        return md;
    }

    private static bool ShouldDraw(BlockType self, ushort selfId, BlockType nb, ushort nbId)
    {
        if (nb.IsAir) return true;
        if (nb.Opaque) return false;
        if (nbId == selfId) return false; // same transparent material: hide internal faces
        return true;
    }

    /// <summary>Classic 0..1 ambient occlusion for one face corner from the three
    /// blocks diagonally in front of the face. All samples stay within the
    /// one-cell border captured in the snapshot.</summary>
    private static float VertexAo(Snapshot snap, Vector3I airCell, Vector3I du, Vector3I dv, Vector2 uv)
    {
        int su = uv.X > 0.5f ? 1 : -1;
        int sv = uv.Y > 0.5f ? 1 : -1;
        bool s1 = IsOpaque(snap, airCell + du * su);
        bool s2 = IsOpaque(snap, airCell + dv * sv);
        bool corner = IsOpaque(snap, airCell + du * su + dv * sv);
        int level = (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (corner ? 1 : 0));
        return level / 3f;
    }

    private static bool IsOpaque(Snapshot snap, Vector3I c) =>
        BlockRegistry.Get(snap.Get(c.X, c.Y, c.Z)).Opaque;

    // ---- step 3: apply (main thread) --------------------------------------

    public static void Apply(VoxelWorld world, Chunk chunk, MeshData md)
    {
        ArrayMesh opaque = Commit(md.Opaque);
        ArrayMesh water = Commit(md.Water);
        Shape3D col = null;
        if (md.Collision.Count > 0)
        {
            var shape = new ConcavePolygonShape3D();
            shape.SetFaces(md.Collision.ToArray());
            col = shape;
        }
        chunk.ApplyMesh(opaque, world.Textures.Material, water, world.WaterMaterial, col);
    }

    /// <summary>Replay one surface's pre-computed vertex data through a SurfaceTool
    /// on the main thread. The heavy decisions (which faces, AO, greedy merging)
    /// already happened off-thread in <see cref="BuildData"/>; this is just the
    /// GPU upload, which must run on the main thread.</summary>
    private static ArrayMesh Commit(Surface s)
    {
        if (s.Count == 0) return null;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        for (int i = 0; i < s.Count; i++)
        {
            st.SetNormal(s.Normals[i]);
            st.SetTangent(s.Tangents[i]);
            st.SetUV(s.Uvs[i]);
            st.SetCustom(0, s.Custom[i]);
            st.AddVertex(s.Verts[i]);
        }
        foreach (int idx in s.Indices)
            st.AddIndex(idx);
        return st.Commit();
    }
}
