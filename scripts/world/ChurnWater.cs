using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.World;

/// <summary>
/// Volumetric "churning water" — turns the flat, greedy-meshed showcase waterfall into a body of
/// small, translucent, hard-stepped voxel cubes so it reads with real 3D depth (layered cubes you
/// can see through), and makes the pools visibly churn near each drop. The look is GEOMETRY, not a
/// shader trick on a flat face: each turbulent macro-cell is subdivided into a sub-grid of cubes
/// rendered through ONE <see cref="MultiMeshInstance3D"/>.
///
/// The field is BAKED once over the static showcase water — no per-frame fluid sim, no per-frame
/// buffer upload. We classify each water cell as a falling CURTAIN (a vertical sheet) or surface
/// FOAM (turbulence radiating out from where a fall lands, via BFS), then emit sub-cubes. In this
/// blocky world the cascade is a solid water staircase, so "air directly below" finds nothing; the
/// real signal for falling water is an exposed vertical face with water above it.
///
/// Per cube we bake a static off-lattice jitter, a random size, and ~30-40% dropout so the body is
/// broken up in space and never flickers. All MOTION is in the shader and DIRECTIONAL: falls stream
/// straight DOWN; foam streams DOWNSTREAM + sideways (never upstream) and only over interior water,
/// so it can't flow backward over the brink or bleed onto the surrounding ground (churn.gdshader).
/// </summary>
public static class ChurnWater
{
    private const int Sub = 4;            // sub-cubes per axis across a 1 m macro-cell (finer = smaller cubes)
    private const float Step = 1f / Sub;
    private const float Jitter = 0.30f;   // static off-lattice spread, in cell fractions

    private enum State { Falling = 0, Edge = 1, Impact = 2, Spread = 3 }

    private struct Inst { public Vector3 Pos; public Vector3 Scale; public Color Data; }

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

        // 2a. CURTAINS — water with water directly above AND an exposed (air) vertical side.
        var curtains = new List<(Vector3I p, Vector3I outDir)>();
        var curtainSet = new HashSet<Vector3I>();
        foreach (var p in water)
        {
            if (!IsWater(p.X, p.Y + 1, p.Z)) continue;
            Vector3I outDir = Vector3I.Zero;
            foreach (var (dx, dz) in horiz)
                if (IsAir(p.X + dx, p.Y, p.Z + dz)) outDir += new Vector3I(dx, 0, dz);
            if (outDir == Vector3I.Zero) continue;
            curtains.Add((p, outDir));
            curtainSet.Add(p);
        }

        // Dominant spill (downstream) direction — the average of all curtain exits. Foam is steered
        // toward this and never allowed upstream, so the top can't flow backward over the brink.
        Vector3 spill = Vector3.Zero;
        foreach (var (_, outDir) in curtains)
            spill += new Vector3(Mathf.Sign(outDir.X), 0, Mathf.Sign(outDir.Z));
        spill = spill.Length() > 0.001f ? spill.Normalized() : new Vector3(0, 0, 1);
        float spillAng = Angle01(spill.X, spill.Z);

        // Surface cells = water with air (non-water) above — foam rides here.
        var surface = new HashSet<Vector3I>();
        foreach (var p in water)
            if (!IsWater(p.X, p.Y + 1, p.Z) && !curtainSet.Contains(p))
                surface.Add(p);

        // 2b. Seed surface-foam turbulence: 10 where a fall lands (IMPACT), 6 at the brink. Remember
        // impact XZ so foam can radiate from it.
        var turb = new Dictionary<Vector3I, float>();
        var impacts = new HashSet<Vector2I>();
        void Seed(Vector3I p, float t)
        {
            if (!surface.Contains(p)) return;
            turb[p] = Mathf.Max(turb.TryGetValue(p, out var e) ? e : 0f, t);
        }
        foreach (var (p, outDir) in curtains)
        {
            foreach (var (dx, dz) in horiz)
                for (int dy = -1; dy <= 1; dy++)
                    Seed(new Vector3I(p.X + dx, p.Y + dy, p.Z + dz), 6f);

            int sx = Mathf.Sign(outDir.X), sz = Mathf.Sign(outDir.Z);
            for (int y = p.Y; y >= p.Y - 24; y--)
                if (IsWater(p.X + sx, y, p.Z + sz) && !IsWater(p.X + sx, y + 1, p.Z + sz))
                { Seed(new Vector3I(p.X + sx, y, p.Z + sz), 10f); impacts.Add(new Vector2I(p.X + sx, p.Z + sz)); break; }
        }

        // 2c. Spread turbulence over the pool surface (BFS, decaying ~2 per block).
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

        // Helpers for foam emission.
        bool Interior(Vector3I c)                                // all 4 horizontal neighbours are water
        {
            foreach (var (dx, dz) in horiz)
                if (!water.Contains(new Vector3I(c.X + dx, c.Y, c.Z + dz))) return false;
            return true;
        }
        bool NearCurtain(Vector3I c)                             // a lip cell, right beside the drop
        {
            foreach (var (dx, dz) in horiz)
                for (int dy = -1; dy <= 1; dy++)
                    if (curtainSet.Contains(new Vector3I(c.X + dx, c.Y + dy, c.Z + dz))) return true;
            return false;
        }
        float FoamFlowAngle(Vector3I cell)
        {
            float bestD = float.MaxValue; Vector2I best = default; bool found = false;
            foreach (var im in impacts)
            {
                float d = (im.X - cell.X) * (im.X - cell.X) + (im.Y - cell.Z) * (im.Y - cell.Z);
                if (d < bestD) { bestD = d; best = im; found = true; }
            }
            if (!found || bestD < 0.01f) return spillAng;
            var away = new Vector2(cell.X - best.X, cell.Z - best.Y);
            if (away.Length() < 0.01f) return spillAng;
            away = away.Normalized();
            var sp = new Vector2(spill.X, spill.Z);
            float along = away.Dot(sp);
            Vector2 res = (away - sp * along) + sp * Mathf.Max(along, 0f);   // strip the upstream part
            return res.Length() < 0.05f ? spillAng : Angle01(res.X, res.Y);
        }

        // 3. Emit sub-cubes.
        var insts = new List<Inst>();

        // Curtains → tall, slim streak cubes that stream straight down. Nudged out past the water
        // face so they stand proud of the flat backing.
        foreach (var (p, outDir) in curtains)
        {
            var outN = new Vector3(Mathf.Sign(outDir.X), 0, Mathf.Sign(outDir.Z));
            outN = outN.Length() > 0.001f ? outN.Normalized() : new Vector3(0, 0, 1);
            for (int i = 0; i < Sub; i++)
            for (int k = 0; k < Sub; k++)
                Emit(insts, new Vector3(p.X + (i + 0.5f) * Step, p.Y + 0.5f, p.Z + (k + 0.5f) * Step) + outN * 0.18f,
                    new Vector3(Step * 0.85f, Step * 2.0f, Step * 0.85f), 0.9f, State.Falling, 0f, p, i * Sub + k);
        }

        // Foam → small cubes riding the pool surface that drift downstream/outward and fade. Only on
        // interior water or right at a lip, so they never bleed onto the surrounding ground.
        foreach (var (c, t) in turb)
        {
            if (t <= 0f) continue;
            bool lip = NearCurtain(c);
            if (!lip && !Interior(c)) continue;
            State s = t >= 8f ? State.Impact : t >= 4f ? State.Edge : State.Spread;
            int layers = t >= 8f ? 2 : 1;
            float ang = FoamFlowAngle(c);
            for (int L = 0; L < layers; L++)
            for (int i = 0; i < Sub; i++)
            for (int k = 0; k < Sub; k++)
                Emit(insts, new Vector3(c.X + (i + 0.5f) * Step, c.Y + 1f + L * Step, c.Z + (k + 0.5f) * Step),
                    new Vector3(Step * 0.9f, Step * 0.9f, Step * 0.9f),
                    Mathf.Clamp(t / 10f, 0.15f, 1f), s, ang, c, L * 137 + i * Sub + k);
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
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(insts[i].Scale), insts[i].Pos));
            mm.SetInstanceCustomData(i, insts[i].Data);
        }

        var mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/churn.gdshader"),
            RenderPriority = 1,
        };
        root.AddChild(new MultiMeshInstance3D { Name = "Churn", Multimesh = mm, MaterialOverride = mat });
        return root;
    }

    // Bake one sub-cube with a static off-lattice jitter, a static size, and ~30-40% dropout, so the
    // grid is broken in SPACE and never flickers. flowAngle01 + state drive the shader's motion.
    private static void Emit(List<Inst> list, Vector3 basePos, Vector3 baseScale, float turb, State s,
        float flowAngle01, Vector3I cell, int sub)
    {
        uint h = KeyOf(cell, sub);
        float drop = s == State.Falling ? 0.40f : 0.30f;
        if ((h & 0xFF) / 255f < drop) return;

        uint h2 = KeyOf(cell, sub * 31 + 7);
        float jx = (h >> 8 & 0xFF) / 255f - 0.5f;
        float jz = (h >> 16 & 0xFF) / 255f - 0.5f;
        float jy = (h >> 24 & 0xFF) / 255f - 0.5f;
        float sizef = 0.55f + (h2 >> 16 & 0xFF) / 255f * 0.55f;    // 0.55 .. 1.10 (smaller, tighter range)
        float seed = (h2 & 0xFFFF) / 65535f;

        list.Add(new Inst
        {
            Pos = basePos + new Vector3(jx, jy * 0.6f, jz) * Jitter,
            Scale = baseScale * sizef,
            Data = new Color(turb, (float)(int)s / 4f, seed, flowAngle01),
        });
    }

    private static float Angle01(float x, float z)
    {
        float a = Mathf.Atan2(z, x) / (Mathf.Pi * 2f) + 0.5f;
        return a - Mathf.Floor(a);
    }

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
