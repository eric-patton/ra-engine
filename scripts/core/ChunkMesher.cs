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
        public readonly List<Vector3I> Vegetation = new(); // grass cells to scatter tufts on
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
        // Per-cell water FX packed into the free Custom.b / Custom.a float channels:
        //   top faces  → (flow.x, flow.z): a unit-ish current direction for B1 rivers.
        //   side faces → (0, fall): fall = 1 on the vertical sheet of a falling waterfall (B2).
        // Still water computes flow 0 everywhere, so its faces merge exactly as before.
        var cBArr = new float[N * N];
        var cAArr = new float[N * N];

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

                    // Water FX channels (zero for everything else, so non-water merging is unchanged).
                    float cB = 0f, cA = 0f;
                    if (water)
                    {
                        if (nrm.Y > 0)                                       // top surface → river flow
                            ComputeFlow(snap, cell, out cB, out cA);
                        else if (nrm.Y == 0 &&                               // vertical sheet with water above
                                 BlockRegistry.Get(snap.Get(cell.X, cell.Y + 1, cell.Z)).Render == RenderType.Water)
                            cA = 1f;                                         // → a waterfall curtain (B2)
                    }
                    cBArr[m] = cB;
                    cAArr[m] = cA;

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
                            aoCorners[m * 4], aoCorners[m * 4 + 1], aoCorners[m * 4 + 2], aoCorners[m * 4 + 3],
                            cBArr[m], cAArr[m]);
                        used[m] = true;
                        continue;
                    }

                    int kLayer = layerArr[m], kAo = aoFlat[m];
                    bool kWater = waterArr[m], kSolid = solidArr[m];
                    float kcB = cBArr[m], kcA = cAArr[m];

                    int w = 1;
                    while (i + w < N && Mergeable(j * N + i + w, has, used, aoFlat, layerArr, waterArr, solidArr,
                               cBArr, cAArr, kAo, kLayer, kWater, kSolid, kcB, kcA))
                        w++;

                    int h = 1;
                    bool stop = false;
                    while (j + h < N && !stop)
                    {
                        for (int k = 0; k < w; k++)
                            if (!Mergeable((j + h) * N + i + k, has, used, aoFlat, layerArr, waterArr, solidArr,
                                    cBArr, cAArr, kAo, kLayer, kWater, kSolid, kcB, kcA))
                            { stop = true; break; }
                        if (!stop) h++;
                    }

                    for (int jj = j; jj < j + h; jj++)
                    for (int ii = i; ii < i + w; ii++)
                        used[jj * N + ii] = true;

                    EmitQuad(md, f, baseCell, w, h, kLayer, kWater, kSolid, kAo, kAo, kAo, kAo, kcB, kcA);
                }
            }
        }

        ScatterVegetation(snap, md);
        return md;
    }

    /// <summary>Mark grass cells (with air above) that should grow a tuft. Roughly a
    /// third of eligible cells, chosen by a hash of the world position so the
    /// scatter is deterministic and identical across runs and machines.</summary>
    private static void ScatterVegetation(Snapshot snap, MeshData md)
    {
        Vector3I baseW = snap.Coord * Chunk.Size;
        for (int y = 0; y < Chunk.Size; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            ushort id = snap.Get(x, y, z);
            if (id == 0 || !BlockRegistry.Get(id).SpawnsVegetation) continue;
            if (snap.Get(x, y + 1, z) != 0) continue; // needs open air above
            uint h = ValueNoise2D.Hash(baseW.X + x, baseW.Z + z, 7777);
            if ((h & 0xFFFF) / 65535f > 0.32f) continue;
            md.Vegetation.Add(new Vector3I(x, y, z));
        }
    }

    private static bool Mergeable(int m, bool[] has, bool[] used, int[] aoFlat,
        int[] layerArr, bool[] waterArr, bool[] solidArr, float[] cBArr, float[] cAArr,
        int kAo, int kLayer, bool kWater, bool kSolid, float kcB, float kcA) =>
        has[m] && !used[m] && aoFlat[m] == kAo && layerArr[m] == kLayer
        && waterArr[m] == kWater && solidArr[m] == kSolid
        // Water faces only merge when their flow/fall channels match too, so a river's
        // varying current splits into per-cell quads while a still pond stays one quad.
        && cBArr[m] == kcB && cAArr[m] == kcA;

    /// <summary>A water surface cell's current direction for B1 flowing rivers, derived from
    /// the static block field: water spills toward any horizontal side that is open air,
    /// and pulls harder toward a side that also drops away (a waterfall lip). Returns a
    /// unit-ish vector whose length (0..1) is the current strength — 0 for enclosed/still
    /// water, which keeps that face mergeable. Output is discrete (few neighbour patterns),
    /// so adjacent equal-current cells share one value and merge.</summary>
    private static void ComputeFlow(Snapshot snap, Vector3I c, out float fx, out float fz)
    {
        fx = 0f; fz = 0f;
        AccumFlow(snap, c, 1, 0, ref fx, ref fz);
        AccumFlow(snap, c, -1, 0, ref fx, ref fz);
        AccumFlow(snap, c, 0, 1, ref fx, ref fz);
        AccumFlow(snap, c, 0, -1, ref fx, ref fz);
        float len = Mathf.Sqrt(fx * fx + fz * fz);
        if (len > 0.0001f)
        {
            float intensity = Mathf.Min(len / 2f, 1f);
            fx = fx / len * intensity;
            fz = fz / len * intensity;
        }
    }

    private static void AccumFlow(Snapshot snap, Vector3I c, int dx, int dz, ref float fx, ref float fz)
    {
        if (snap.Get(c.X + dx, c.Y, c.Z + dz) != 0) return;     // blocked (solid or water) — no spill
        float w = snap.Get(c.X + dx, c.Y - 1, c.Z + dz) == 0 ? 2f : 1f; // a drop past the edge pulls harder
        fx += dx * w; fz += dz * w;
    }

    /// <summary>Emit one (possibly merged) quad of <paramref name="w"/>×<paramref name="h"/>
    /// cells. The quad runs <paramref name="w"/> cells along the face's du axis and
    /// <paramref name="h"/> along dv; UVs run 0..w / 0..h so the texture tiles
    /// (the shader samples with repeat). Corner AO levels are passed explicitly so
    /// the gradient (1×1) and uniform (merged) paths share one code path.</summary>
    private static void EmitQuad(MeshData md, int f, Vector3I baseCell, int w, int h,
        int layer, bool water, bool solid, int ao0, int ao1, int ao2, int ao3, float cB, float cA)
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

        Vector2 uv0, uv1, uv2, uv3;
        if (f == 2 || f == 3)
        {
            // Top / bottom faces: du/dv are both horizontal so the default mapping is fine.
            uv0 = new Vector2(0, 0);
            uv1 = new Vector2(w, 0);
            uv2 = new Vector2(w, h);
            uv3 = new Vector2(0, h);
        }
        else
        {
            // Side faces (f 0,1,4,5): remap UVs so that the texture's V axis always points
            // world-up regardless of which in-plane axis du/dv happen to be.  This keeps
            // directional side textures (grass fringe, sandstone bands, brick courses) upright
            // and identically oriented on every vertical face.
            //   V = (maxY - corner.Y)   → V=0 at the quad top   → image-top row sits at the block top.
            //   U = (corner.H - minH)   → U=0 at the quad's left horizontal edge (H = Z for ±X, X for ±Z).
            float maxY = Mathf.Max(Mathf.Max(p0.Y, p1.Y), Mathf.Max(p2.Y, p3.Y));
            float h0, h1, h2, h3, minH;
            if (f == 0 || f == 1)   // ±X: horizontal in-plane axis is Z
            {
                h0 = p0.Z; h1 = p1.Z; h2 = p2.Z; h3 = p3.Z;
            }
            else                    // ±Z: horizontal in-plane axis is X
            {
                h0 = p0.X; h1 = p1.X; h2 = p2.X; h3 = p3.X;
            }
            minH = Mathf.Min(Mathf.Min(h0, h1), Mathf.Min(h2, h3));
            uv0 = new Vector2(h0 - minH, maxY - p0.Y);
            uv1 = new Vector2(h1 - minH, maxY - p1.Y);
            uv2 = new Vector2(h2 - minH, maxY - p2.Y);
            uv3 = new Vector2(h3 - minH, maxY - p3.Y);
        }

        surf.AddVertex(p0, normal, tangent, uv0, new Color(layer, ao0 / 3f, cB, cA));
        surf.AddVertex(p1, normal, tangent, uv1, new Color(layer, ao1 / 3f, cB, cA));
        surf.AddVertex(p2, normal, tangent, uv2, new Color(layer, ao2 / 3f, cB, cA));
        surf.AddVertex(p3, normal, tangent, uv3, new Color(layer, ao3 / 3f, cB, cA));
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
        chunk.SetVegetation(BuildVegetation(chunk.Coord, md));
    }

    /// <summary>Build a per-chunk MultiMesh of grass tufts. Each instance's yaw,
    /// scale and jitter come from a hash of its world cell, so the scatter is
    /// stable. Returns null when the chunk has no vegetation.</summary>
    private static MultiMeshInstance3D BuildVegetation(Vector3I coord, MeshData md)
    {
        if (md.Vegetation.Count == 0) return null;
        Vector3I baseW = coord * Chunk.Size;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = Vegetation.CrossMesh,
            InstanceCount = md.Vegetation.Count,
        };
        for (int i = 0; i < md.Vegetation.Count; i++)
        {
            Vector3I c = md.Vegetation[i];
            uint h = ValueNoise2D.Hash(baseW.X + c.X, baseW.Z + c.Z, 31337);
            float yaw = (h & 0xFF) / 255f * Mathf.Tau;
            float scale = 0.8f + ((h >> 8) & 0xFF) / 255f * 0.5f;
            float jx = (((h >> 16) & 0xFF) / 255f - 0.5f) * 0.5f;
            float jz = (((h >> 24) & 0xFF) / 255f - 0.5f) * 0.5f;
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale));
            var origin = new Vector3(c.X + 0.5f + jx, c.Y + 1f, c.Z + 0.5f + jz);
            mm.SetInstanceTransform(i, new Transform3D(basis, origin));
        }
        return new MultiMeshInstance3D
        {
            Name = "Vegetation",
            Multimesh = mm,
            MaterialOverride = Vegetation.Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
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
