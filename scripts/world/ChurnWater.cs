using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.World;

/// <summary>
/// Volumetric "churning water" — turns the flat, greedy-meshed showcase waterfall into a body of
/// small, translucent, hard-stepped voxel cubes so it reads with real 3D depth (layered cubes you
/// can see through), and makes the pools visibly churn near each drop. The look is GEOMETRY, not a
/// shader trick on a flat face: each turbulent macro-cell is subdivided into a sub-grid of cubes
/// rendered through ONE <see cref="MultiMeshInstance3D"/>; the shader (churn.gdshader) does all the
/// per-frame work (collapse sub-cubes below a turbulence-scaled density threshold, jitter + scroll
/// the survivors, hard-stepped blue→white palette at translucent alpha).
///
/// The field is BAKED once over the static showcase water — no per-frame fluid sim and no per-frame
/// buffer upload. We classify each water cell as a falling CURTAIN (a vertical sheet → tall streak
/// cubes) or surface FOAM (turbulence radiating out from where a fall lands → chunky foam cubes),
/// then emit the sub-cubes. In this blocky world the cascade is a solid water staircase, so "air
/// directly below" finds nothing; the real signal for falling water is an exposed vertical face
/// with water above it (the same signal the mesher uses to flag a curtain).
/// </summary>
public static class ChurnWater
{
    private const int Sub = 3;            // sub-cubes per axis across a 1 m macro-cell
    private const float Step = 1f / Sub;

    // Drives the palette bias (edge/impact read whiter); packed into per-instance custom data.
    private enum State { Falling = 0, Edge = 1, Impact = 2, Spread = 3 }

    private struct Inst { public Vector3 Pos; public Vector3 Scale; public float Turb; public State S; public uint Key; }

    public static Node3D Build(VoxelWorld world)
    {
        var root = new Node3D { Name = "ChurnWater" };
        ushort waterId = BlockRegistry.IdOf("water");

        // 1. Gather every water cell in the (static) world.
        var water = new HashSet<Vector3I>();
        foreach (var kv in world.Chunks)
        {
            int ox = kv.Key.X * Chunk.Size, oy = kv.Key.Y * Chunk.Size, oz = kv.Key.Z * Chunk.Size;
            for (int lx = 0; lx < Chunk.Size; lx++)
            for (int ly = 0; ly < Chunk.Size; ly++)
            for (int lz = 0; lz < Chunk.Size; lz++)
                if (world.GetBlockId(ox + lx, oy + ly, oz + lz) == waterId)
                    water.Add(new Vector3I(ox + lx, oy + ly, oz + lz));
        }
        if (water.Count == 0) return root;

        bool IsWater(int x, int y, int z) => water.Contains(new Vector3I(x, y, z));
        bool IsAir(int x, int y, int z) => world.GetBlockId(x, y, z) == 0;
        (int dx, int dz)[] horiz = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        // 2a. CURTAINS — water with water directly above AND an exposed (air) vertical side: part of
        // a falling sheet. outDir points outward across the exposed face(s) — the spill direction.
        var curtains = new List<(Vector3I p, Vector3I outDir)>();
        var curtainSet = new HashSet<Vector3I>();
        foreach (var p in water)
        {
            if (!IsWater(p.X, p.Y + 1, p.Z)) continue;            // needs water above
            Vector3I outDir = Vector3I.Zero;
            foreach (var (dx, dz) in horiz)
                if (IsAir(p.X + dx, p.Y, p.Z + dz)) outDir += new Vector3I(dx, 0, dz);
            if (outDir == Vector3I.Zero) continue;
            curtains.Add((p, outDir));
            curtainSet.Add(p);
        }

        // Surface cells = water with air (non-water) above — foam rides here.
        var surface = new HashSet<Vector3I>();
        foreach (var p in water)
            if (!IsWater(p.X, p.Y + 1, p.Z) && !curtainSet.Contains(p))
                surface.Add(p);

        // 2b. Seed the surface-foam turbulence: 10 where a fall lands (IMPACT), 6 at the brink beside
        // a curtain (EDGE).
        var turb = new Dictionary<Vector3I, float>();
        void Seed(Vector3I p, float t)
        {
            if (!surface.Contains(p)) return;
            turb[p] = Mathf.Max(turb.TryGetValue(p, out var e) ? e : 0f, t);
        }
        foreach (var (p, outDir) in curtains)
        {
            foreach (var (dx, dz) in horiz)                       // brink: surface touching the curtain
                for (int dy = -1; dy <= 1; dy++)
                    Seed(new Vector3I(p.X + dx, p.Y + dy, p.Z + dz), 6f);

            int sx = Mathf.Sign(outDir.X), sz = Mathf.Sign(outDir.Z);
            for (int y = p.Y; y >= p.Y - 24; y--)                 // impact: pool the curtain pours into
                if (IsWater(p.X + sx, y, p.Z + sz) && !IsWater(p.X + sx, y + 1, p.Z + sz))
                { Seed(new Vector3I(p.X + sx, y, p.Z + sz), 10f); break; }
        }

        // 2c. Spread turbulence outward over the pool surface (BFS, decaying ~2 per block).
        var queue = new Queue<Vector3I>(turb.Keys);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            float t = turb[c];
            if (t <= 2f) continue;
            foreach (var (dx, dz) in horiz)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = new Vector3I(c.X + dx, c.Y + dy, c.Z + dz);
                    if (!surface.Contains(n)) continue;
                    float nt = t - 2f;
                    if (nt > (turb.TryGetValue(n, out var e) ? e : 0f)) { turb[n] = nt; queue.Enqueue(n); }
                }
        }

        // 3. Emit sub-cubes.
        var insts = new List<Inst>();

        // Curtains → tall vertical streak cubes (SUB×SUB per cell), nudged out past the water face so
        // they stand proud of the flat backing.
        foreach (var (p, outDir) in curtains)
        {
            var outN = new Vector3(Mathf.Sign(outDir.X), 0, Mathf.Sign(outDir.Z));
            if (outN.Length() > 1.01f) outN = outN.Normalized();
            for (int i = 0; i < Sub; i++)
            for (int k = 0; k < Sub; k++)
                insts.Add(new Inst
                {
                    Pos = new Vector3(p.X + (i + 0.5f) * Step, p.Y + 0.5f, p.Z + (k + 0.5f) * Step) + outN * 0.25f,
                    Scale = new Vector3(Step * 0.8f, 0.92f, Step * 0.8f),   // tall streak
                    Turb = 0.9f, S = State.Falling, Key = KeyOf(p, i * Sub + k),
                });
        }

        // Foam → cubes riding the pool surface; one layer normally, two at hard impact.
        foreach (var (c, t) in turb)
        {
            if (t <= 0f) continue;
            State s = t >= 8f ? State.Impact : t >= 4f ? State.Edge : State.Spread;
            int layers = t >= 8f ? 2 : 1;
            for (int L = 0; L < layers; L++)
            for (int i = 0; i < Sub; i++)
            for (int k = 0; k < Sub; k++)
                insts.Add(new Inst
                {
                    Pos = new Vector3(c.X + (i + 0.5f) * Step, c.Y + 1f + L * Step, c.Z + (k + 0.5f) * Step),
                    Scale = new Vector3(Step * 0.95f, Step * 0.95f, Step * 0.95f),
                    Turb = Mathf.Clamp(t / 10f, 0.15f, 1f), S = s, Key = KeyOf(c, L * 99 + i * Sub + k),
                });
        }

        if (insts.Count == 0) return root;

        // 4. One MultiMesh for the whole churn body; the shader does all per-frame motion.
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = insts.Count,
        };
        for (int i = 0; i < insts.Count; i++)
        {
            var it = insts[i];
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(it.Scale), it.Pos));
            float seed = (it.Key & 0xFFFF) / 65535f;
            float phase = ((it.Key >> 16) & 0xFFFF) / 65535f * 6.2832f;
            mm.SetInstanceCustomData(i, new Color(it.Turb, (float)(int)it.S / 4f, seed, phase));
        }

        var mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/churn.gdshader"),
            RenderPriority = 1,                  // in front of the translucent water backing (-1)
        };
        root.AddChild(new MultiMeshInstance3D { Name = "Churn", Multimesh = mm, MaterialOverride = mat });
        return root;
    }

    // FNV-1a over the cell + sub-index, mixed so both the low (seed) and high (phase) 16 bits vary.
    private static uint KeyOf(Vector3I p, int sub)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)p.X) * 16777619u;
        h = (h ^ (uint)p.Y) * 16777619u;
        h = (h ^ (uint)p.Z) * 16777619u;
        h = (h ^ (uint)sub) * 16777619u;
        h ^= h >> 15;
        return h;
    }
}
