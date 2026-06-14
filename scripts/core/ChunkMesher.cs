using System;
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

    /// <summary>Greedy-meshes the chunk: for each of the six face directions, every
    /// 16×16 slice is reduced to as few rectangles as possible. Faces only merge
    /// when they share a texture layer and a <em>uniform</em> AO level (all four
    /// corners equal), which keeps the merged result pixel-identical to per-face
    /// meshing — a flat quad interpolates a uniform corner value to the same shade.
    /// Faces that have an AO gradient (near edges/occluders) are emitted 1×1 with
    /// their four distinct corner values, exactly as before.</summary>
    public static MeshData BuildData(Snapshot snap)
    {
        var md = new MeshData();
        const int N = Chunk.Size;

        // Per-slice scratch, reused across all six faces (one chunk = one thread).
        var has = new bool[N * N];
        var used = new bool[N * N];
        var layerArr = new int[N * N];
        var waterArr = new bool[N * N];
        var solidArr = new bool[N * N];
        var aoFlat = new int[N * N];        // 0..3 uniform level, or -1 when the AO varies
        var aoCorners = new int[N * N * 4]; // per-corner levels, used for the 1×1 path

        for (int f = 0; f < 6; f++)
        {
            Vector3I nrm = VoxelGeometry.Normals[f];
            Vector3I du = VoxelGeometry.Du[f], dv = VoxelGeometry.Dv[f];
            // Unit step along the slice (normal) axis, sign-independent.
            var naUnit = new Vector3I(Mathf.Abs(nrm.X), Mathf.Abs(nrm.Y), Mathf.Abs(nrm.Z));

            for (int s = 0; s < N; s++)
            {
                // 1. Build the visible-face mask for this slice.
                for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int m = j * N + i;
                    has[m] = false;
                    used[m] = false;

                    Vector3I cell = naUnit * s + du * i + dv * j;
                    ushort id = snap.Get(cell.X, cell.Y, cell.Z);
                    if (id == 0) continue;
                    var self = BlockRegistry.Get(id);
                    Vector3I air = cell + nrm;
                    ushort nbId = snap.Get(air.X, air.Y, air.Z);
                    if (!ShouldDraw(self, id, BlockRegistry.Get(nbId), nbId)) continue;

                    has[m] = true;
                    bool water = self.Render == RenderType.Water;
                    layerArr[m] = self.FaceLayer[f];
                    waterArr[m] = water;
                    solidArr[m] = self.Solid;

                    int l0, l1, l2, l3;
                    if (water)
                    {
                        l0 = l1 = l2 = l3 = 3; // water never bakes AO
                    }
                    else
                    {
                        l0 = AoLevel(snap, air, du, dv, VoxelGeometry.Uvs[0]);
                        l1 = AoLevel(snap, air, du, dv, VoxelGeometry.Uvs[1]);
                        l2 = AoLevel(snap, air, du, dv, VoxelGeometry.Uvs[2]);
                        l3 = AoLevel(snap, air, du, dv, VoxelGeometry.Uvs[3]);
                    }
                    aoCorners[m * 4 + 0] = l0;
                    aoCorners[m * 4 + 1] = l1;
                    aoCorners[m * 4 + 2] = l2;
                    aoCorners[m * 4 + 3] = l3;
                    aoFlat[m] = (l0 == l1 && l1 == l2 && l2 == l3) ? l0 : -1;
                }

                // 2. Greedy-merge equal flat faces into rectangles; emit gradients 1×1.
                for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int m = j * N + i;
                    if (!has[m] || used[m]) continue;

                    Vector3I baseCell = naUnit * s + du * i + dv * j;

                    if (aoFlat[m] < 0)
                    {
                        EmitQuad(md, f, baseCell, 1, 1, layerArr[m], waterArr[m], solidArr[m],
                            aoCorners[m * 4], aoCorners[m * 4 + 1], aoCorners[m * 4 + 2], aoCorners[m * 4 + 3]);
                        used[m] = true;
                        continue;
                    }

                    int kLayer = layerArr[m], kAo = aoFlat[m];
                    bool kWater = waterArr[m], kSolid = solidArr[m];

                    int w = 1;
                    while (i + w < N && Mergeable(j * N + i + w, has, used, aoFlat, layerArr, waterArr, solidArr,
                               kAo, kLayer, kWater, kSolid))
                        w++;

                    int h = 1;
                    bool stop = false;
                    while (j + h < N && !stop)
                    {
                        for (int k = 0; k < w; k++)
                            if (!Mergeable((j + h) * N + i + k, has, used, aoFlat, layerArr, waterArr, solidArr,
                                    kAo, kLayer, kWater, kSolid))
                            { stop = true; break; }
                        if (!stop) h++;
                    }

                    for (int jj = j; jj < j + h; jj++)
                    for (int ii = i; ii < i + w; ii++)
                        used[jj * N + ii] = true;

                    EmitQuad(md, f, baseCell, w, h, kLayer, kWater, kSolid, kAo, kAo, kAo, kAo);
                }
            }
        }
        return md;
    }

    private static bool Mergeable(int m, bool[] has, bool[] used, int[] aoFlat,
        int[] layerArr, bool[] waterArr, bool[] solidArr,
        int kAo, int kLayer, bool kWater, bool kSolid) =>
        has[m] && !used[m] && aoFlat[m] == kAo && layerArr[m] == kLayer
        && waterArr[m] == kWater && solidArr[m] == kSolid;

    /// <summary>Emit one (possibly merged) quad of <paramref name="w"/>×<paramref name="h"/>
    /// cells. The quad runs <paramref name="w"/> cells along the face's du axis and
    /// <paramref name="h"/> along dv; UVs run 0..w / 0..h so the texture tiles
    /// (the shader samples with repeat). Corner AO levels are passed explicitly so
    /// the gradient (1×1) and uniform (merged) paths share one code path.</summary>
    private static void EmitQuad(MeshData md, int f, Vector3I baseCell, int w, int h,
        int layer, bool water, bool solid, int ao0, int ao1, int ao2, int ao3)
    {
        Surface surf = water ? md.Water : md.Opaque;
        int baseIdx = surf.Count;

        var baseLocal = new Vector3(baseCell.X, baseCell.Y, baseCell.Z);
        Vector3 o = VoxelGeometry.Corners[f][0];
        Vector3I dui = VoxelGeometry.Du[f], dvi = VoxelGeometry.Dv[f];
        var duv = new Vector3(dui.X, dui.Y, dui.Z) * w;
        var dvv = new Vector3(dvi.X, dvi.Y, dvi.Z) * h;
        Vector3 normal = VoxelGeometry.Normals[f];
        var tangent = new Plane(VoxelGeometry.Tangents[f], 1f);

        Vector3 p0 = baseLocal + o;
        Vector3 p1 = baseLocal + o + duv;
        Vector3 p2 = baseLocal + o + duv + dvv;
        Vector3 p3 = baseLocal + o + dvv;

        surf.AddVertex(p0, normal, tangent, new Vector2(0, 0), new Color(layer, ao0 / 3f, 0f, 0f));
        surf.AddVertex(p1, normal, tangent, new Vector2(w, 0), new Color(layer, ao1 / 3f, 0f, 0f));
        surf.AddVertex(p2, normal, tangent, new Vector2(w, h), new Color(layer, ao2 / 3f, 0f, 0f));
        surf.AddVertex(p3, normal, tangent, new Vector2(0, h), new Color(layer, ao3 / 3f, 0f, 0f));
        foreach (int t in VoxelGeometry.TriOrder)
            surf.Indices.Add(baseIdx + t);

        if (solid)
        {
            Span<Vector3> c = stackalloc Vector3[4] { p0, p1, p2, p3 };
            foreach (int t in VoxelGeometry.TriOrder)
                md.Collision.Add(c[t]);
        }
    }

    private static bool ShouldDraw(BlockType self, ushort selfId, BlockType nb, ushort nbId)
    {
        if (nb.IsAir) return true;
        if (nb.Opaque) return false;
        if (nbId == selfId) return false; // same transparent material: hide internal faces
        return true;
    }

    /// <summary>Classic 0..3 ambient-occlusion level for one face corner from the
    /// three blocks diagonally in front of the face. All samples stay within the
    /// one-cell border captured in the snapshot.</summary>
    private static int AoLevel(Snapshot snap, Vector3I airCell, Vector3I du, Vector3I dv, Vector2 uv)
    {
        int su = uv.X > 0.5f ? 1 : -1;
        int sv = uv.Y > 0.5f ? 1 : -1;
        bool s1 = IsOpaque(snap, airCell + du * su);
        bool s2 = IsOpaque(snap, airCell + dv * sv);
        bool corner = IsOpaque(snap, airCell + du * su + dv * sv);
        return (s1 && s2) ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (corner ? 1 : 0));
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
