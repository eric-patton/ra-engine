using System;
using Godot;

namespace RAEngine.Core;

/// <summary>Procedural and scripted world generators. All call SetBlock with
/// remesh:false then the caller marks chunks dirty in one pass.</summary>
public static class WorldGen
{
    /// <summary>Flat ground: stone, then dirt, with grass on top at <paramref name="topY"/>.</summary>
    public static void FlatGround(VoxelWorld w, int x0, int x1, int z0, int z1, int topY,
        string topBlock = "grass", string fillBlock = "dirt", string baseBlock = "stone", int depth = 4)
    {
        ushort top = BlockRegistry.IdOf(topBlock);
        ushort fill = BlockRegistry.IdOf(fillBlock);
        ushort baseB = BlockRegistry.IdOf(baseBlock);
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            w.SetBlock(x, topY, z, top, false);
            for (int d = 1; d <= depth; d++)
                w.SetBlock(x, topY - d, z, d < depth ? fill : baseB, false);
        }
    }

    /// <summary>A gentle valley heightfield, the canvas for the David &amp; Goliath
    /// scene. Returns the surface height at each column via the callback grid.</summary>
    public static int[,] Valley(VoxelWorld w, int size, int baseY, int seed = 1337)
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Seed = seed,
            Frequency = 0.025f,
            FractalOctaves = 4,
        };
        var heights = new int[size, size];
        ushort grass = BlockRegistry.IdOf("grass");
        ushort dirt = BlockRegistry.IdOf("dirt");
        ushort stone = BlockRegistry.IdOf("stone");
        ushort sand = BlockRegistry.IdOf("sand");
        ushort water = BlockRegistry.IdOf("water");

        float half = size / 2f;
        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++)
        {
            // A valley: low along the central Z axis (a dry stream bed), hills to the sides.
            float ridge = Mathf.Abs(x - half) / half;          // 0 center .. 1 edges
            float hill = ridge * ridge * 10f;
            float n = noise.GetNoise2D(x, z) * 4f;
            int h = baseY + (int)Math.Round(hill + n);
            heights[x, z] = h;

            for (int y = baseY - 6; y <= h; y++)
            {
                ushort b;
                if (y == h) b = (h <= baseY) ? sand : grass;
                else if (y > h - 3) b = dirt;
                else b = stone;
                w.SetBlock(x, y, z, b, false);
            }
            // a shallow stream in the lowest channel
            if (h <= baseY)
                for (int y = h + 1; y <= baseY; y++)
                    w.SetBlock(x, y, z, water, false);
        }
        return heights;
    }

    /// <summary>Lays out one short pillar of every block type plus a few feature
    /// props, so a single screenshot exercises the whole texture library.</summary>
    public static void Showcase(VoxelWorld w)
    {
        // Ground wide enough that every block pillar (spaced 2 apart) sits on solid ground.
        int pillars = 0;
        foreach (var b in BlockRegistry.All) if (!b.IsAir && b.Name != "water") pillars++;
        FlatGround(w, -4, 2 + pillars * 2 + 4, -4, 20, 0);

        int i = 0;
        foreach (var block in BlockRegistry.All)
        {
            if (block.IsAir || block.Name == "water") continue;
            int bx = 2 + i * 2;
            for (int y = 1; y <= 3; y++)
                w.SetBlock(bx, y, 4, block.Id, false);
            i++;
        }

        // a little water pool
        for (int x = 2; x <= 8; x++)
        for (int z = 10; z <= 16; z++)
        {
            w.SetBlock(x, 0, z, BlockRegistry.IdOf("sand"), false);
            w.SetBlock(x, -1, z, BlockRegistry.IdOf("sand"), false);
        }
        for (int x = 3; x <= 7; x++)
        for (int z = 11; z <= 15; z++)
        {
            w.SetBlock(x, 0, z, 0, false);            // dig the basin
            w.SetBlock(x, -1, z, BlockRegistry.IdOf("water"), false);
            w.SetBlock(x, 0, z, BlockRegistry.IdOf("water"), false);
        }

        // a small mud-brick hut to show off building
        BuildHut(w, new Vector3I(16, 1, 12), 6, 5, 4);
    }

    /// <summary>A hand-built, static stage that shows off the visual effects (see
    /// docs/FX-ROADMAP.md). Grows each phase. Phase 0: a long grass plain (wind grass +
    /// depth haze toward a far ridge), a runway of distinct footstep materials, a jump
    /// tower (landing dust), a water pool (splash), and an emissive lamp cluster (bloom).
    /// Driven interactively by ShowcaseController (F5 weather / F6 time / F7 glow).
    /// Player spawns at (20, 2, 100) facing north (−Z), walking the stations toward z=0.</summary>
    public static void FxShowcase(VoxelWorld w)
    {
        ushort grass = BlockRegistry.IdOf("grass");
        ushort stone = BlockRegistry.IdOf("stone");
        ushort sand = BlockRegistry.IdOf("sand");
        ushort water = BlockRegistry.IdOf("water");
        ushort lamp = BlockRegistry.IdOf("lamp");

        // Main flat grass stage (foreground apron + the far ridge base).
        FlatGround(w, -4, 40, -30, 116, 0);

        // A distant ridge rising toward the back (z < −2) so looking north shows the haze —
        // tall enough (~0..20) that the atmospheric fade across its face is clearly visible.
        for (int z = -2; z >= -30; z--)
        {
            int h = (int)((-2 - z) * 0.72f);
            for (int x = -4; x <= 40; x++)
                for (int y = 1; y <= h; y++)
                    w.SetBlock(x, y, z, y == h ? grass : stone, false);
        }

        // Footstep-material bands across Z: walking north the player crosses each in turn
        // (dirt → sand → stone → snow → planks → cloth), hearing + seeing the dust change.
        (int z0, int z1, string block)[] bands =
        {
            (90, 92, "dirt"), (87, 89, "sand"), (84, 86, "stone"),
            (81, 83, "snow"), (78, 80, "planks"), (75, 77, "cloth_red"),
        };
        foreach (var (z0, z1, name) in bands)
        {
            ushort id = BlockRegistry.IdOf(name);
            for (int x = 0; x <= 35; x++)
                for (int z = z0; z <= z1; z++)
                    w.SetBlock(x, 0, z, id, false);
        }

        // Jump tower: a 4-step staircase ascending NORTH (the way the player walks) into a
        // 4-high platform. Every step is exactly ONE block (the old version stacked the
        // platform on the top stair column, making an un-jumpable 2-block step). Open drop
        // off the north edge onto a sand pad.
        for (int i = 0; i < 4; i++)               // z76→73, heights 1..4, 3 wide (x19..21)
            for (int x = 19; x <= 21; x++)
                for (int y = 1; y <= i + 1; y++)
                    w.SetBlock(x, y, 76 - i, stone, false);
        for (int x = 19; x <= 21; x++)            // solid platform top at y=4 (z71..72)
            for (int z = 71; z <= 72; z++)
                for (int y = 1; y <= 4; y++)
                    w.SetBlock(x, y, z, stone, false);
        for (int x = 16; x <= 24; x++)            // sand landing pad north of the drop
            for (int z = 66; z <= 70; z++)
                w.SetBlock(x, 0, z, sand, false);

        // Water pool (splash + reflections), sand-rimmed.
        for (int x = 2; x <= 12; x++)
            for (int z = 56; z <= 64; z++)
                w.SetBlock(x, 0, z, sand, false);
        for (int x = 3; x <= 11; x++)
            for (int z = 57; z <= 63; z++)
            {
                w.SetBlock(x, 0, z, water, false);
                w.SetBlock(x, -1, z, water, false);
            }

        // Emissive lamp cluster on little pillars (bloom, esp. at night / Divine glow).
        foreach (var (lx, lz) in new[] { (31, 56), (35, 56), (33, 58), (33, 54) })
        {
            w.SetBlock(lx, 1, lz, stone, false);
            w.SetBlock(lx, 2, lz, lamp, false);
        }

        // Fire station (Phase 1): the props that the living fires sit on — one of every
        // size. The flame visuals + lights are spawned by FireController in
        // Game.StartShowcase (see the matching positions there); here we only place the
        // blocks, all from the existing library so no new textures are needed.
        ushort oakLog = BlockRegistry.IdOf("oak_log");
        ushort cobble = BlockRegistry.IdOf("cobblestone");
        ushort bronze = BlockRegistry.IdOf("bronze_block");
        ushort stoneBrick = BlockRegistry.IdOf("stone_brick");
        ushort altarFire = BlockRegistry.IdOf("altar_fire");
        ushort planks = BlockRegistry.IdOf("planks");

        w.SetBlock(22, 1, 102, planks, false);                 // candle stand
        w.SetBlock(22, 1, 106, oakLog, false);                 // torch post
        foreach (var (cx, cz) in new[] { (26, 104), (28, 104), (27, 103), (27, 105) })
            w.SetBlock(cx, 1, cz, oakLog, false);              // campfire ring (flame in the middle)
        w.SetBlock(24, 1, 104, cobble, false);                 // forge block
        w.SetBlock(31, 1, 102, cobble, false);                 // brazier pedestal
        w.SetBlock(31, 2, 102, bronze, false);
        w.SetBlock(31, 1, 106, stoneBrick, false);             // altar
        w.SetBlock(31, 2, 106, stoneBrick, false);
        w.SetBlock(31, 3, 106, altarFire, false);              // altar coals (emissive block)

        // Water FX station (Phase 1): a tiered, widening waterfall cascade — a source pool spills
        // down successive ledges, fanning out to a wide base pool. Voxel whitewater comes from the
        // water shader; per-drop lip/splash/foam particles are added in Game.StartShowcase.
        WaterfallCascade(w);

        // Flowing-river demo (B1): a stepped cascade that descends SOUTH toward the player so
        // every cell has a downstream drop — the flow heuristic needs a gradient to read as a
        // current, and the water shader then streams directional foam down it. Stone staircase
        // + side banks + a back wall at the spring + a catch pond at the foot.
        int rx0 = 11, rx1 = 13;
        for (int step = 0; step <= 6; step++)
        {
            int y = 6 - step, z = 44 + step;
            for (int x = rx0; x <= rx1; x++)
            {
                w.SetBlock(x, y - 1, z, stone, false);          // step floor
                w.SetBlock(x, y, z, water, false);              // water on the step
            }
            foreach (int bx in new[] { rx0 - 1, rx1 + 1 })       // side banks, one above the water
            {
                w.SetBlock(bx, y, z, stone, false);
                w.SetBlock(bx, y + 1, z, stone, false);
            }
        }
        for (int x = rx0 - 1; x <= rx1 + 1; x++)                 // back wall behind the spring
            for (int y = 0; y <= 7; y++)
                w.SetBlock(x, y, 43, stone, false);
        for (int x = rx0 - 2; x <= rx1 + 2; x++)                 // catch pond at the foot
            for (int z = 50; z <= 54; z++)
            {
                w.SetBlock(x, -1, z, stone, false);
                w.SetBlock(x, 0, z, sand, false);
            }
        for (int x = rx0 - 1; x <= rx1 + 1; x++)
            for (int z = 51; z <= 53; z++)
                w.SetBlock(x, 0, z, water, false);

        // Ambient Life station: an open meadow with a small pond and a couple of trees, so the
        // living world has somewhere to gather - birds and butterflies and dandelion/pollen by
        // day, leaves and blossom by the trees, fish in the pond. Driven by AmbientLifeDirector.
        for (int x = 4; x <= 9; x++)
            for (int z = 31; z <= 36; z++)
                w.SetBlock(x, 0, z, sand, false);
        for (int x = 5; x <= 8; x++)
            for (int z = 32; z <= 35; z++)
            {
                w.SetBlock(x, 0, z, water, false);
                w.SetBlock(x, -1, z, water, false);
            }
        Tree(w, new Vector3I(11, 1, 33));
        Tree(w, new Vector3I(7, 1, 29));

        // A few trees in the open field (ambience; future falling leaves).
        Tree(w, new Vector3I(8, 1, 42));
        Tree(w, new Vector3I(30, 1, 46));
        Tree(w, new Vector3I(16, 1, 34));
        Tree(w, new Vector3I(36, 1, 38));
    }

    /// <summary>The showcase waterfall: a tiered, widening, forward-stepping cascade. A source
    /// pool spills down successive ledges (each lower, wider, and further toward the player) into
    /// a wide base pool, carved into a back cliff with stone banks. Each ledge is a stone shelf
    /// holding a 1-deep pool; the drop between ledges is a water curtain. The blocky whitewater
    /// look is the water shader; per-drop lip/splash/foam particles are added by WaterfallFx in
    /// Game.StartShowcase (positions match the tiers here).</summary>
    public static void WaterfallCascade(VoxelWorld w)
    {
        ushort stone = BlockRegistry.IdOf("stone");
        ushort water = BlockRegistry.IdOf("water");

        // Each tier: xMin, xMax, zMin, zMax, ySurf (water-surface Y). Lower tiers are wider and
        // further south (+z, toward the player), so the cascade fans outward as it descends.
        int[,] tiers =
        {
            { 6, 10,  92,  94, 12 }, // source pool
            { 5, 11,  95,  97,  8 }, // ledge 1
            { 4, 12,  98, 100,  4 }, // ledge 2
            { 3, 13, 101, 107,  0 }, // base pool (widest)
        };

        // Back cliff so the cascade reads as carved into a hillside, not floating.
        for (int x = 2; x <= 14; x++)
            for (int z = 90; z <= 91; z++)
                for (int y = 1; y <= 13; y++)
                    w.SetBlock(x, y, z, stone, false);

        int pXMin = 0, pXMax = 0, pY = 0;
        for (int t = 0; t < tiers.GetLength(0); t++)
        {
            int xMin = tiers[t, 0], xMax = tiers[t, 1], zMin = tiers[t, 2], zMax = tiers[t, 3], ySurf = tiers[t, 4];
            int floorY = ySurf == 0 ? -1 : 1;

            // Stone shelf under the pool, with side banks one block higher to frame it.
            for (int x = xMin - 1; x <= xMax + 1; x++)
                for (int z = zMin; z <= zMax; z++)
                {
                    int top = (x < xMin || x > xMax) ? ySurf : ySurf - 1; // banks vs pool floor
                    for (int y = floorY; y <= top; y++)
                        w.SetBlock(x, y, z, stone, false);
                }
            // Pool water sitting on the shelf.
            for (int x = xMin; x <= xMax; x++)
                for (int z = zMin; z <= zMax; z++)
                    w.SetBlock(x, ySurf, z, water, false);
            // Curtain falling from the previous (narrower) tier into this pool's back edge.
            if (t > 0)
                for (int x = pXMin; x <= pXMax; x++)
                    for (int y = ySurf + 1; y <= pY; y++)
                        w.SetBlock(x, y, zMin, water, false);

            pXMin = xMin; pXMax = xMax; pY = ySurf;
        }
    }

    /// <summary>A simple tree: a log trunk with a blobby leaf canopy.</summary>
    public static void Tree(VoxelWorld w, Vector3I basePos, int trunkHeight = 4,
        string trunk = "oak_log", string leaf = "leaves")
    {
        ushort log = BlockRegistry.IdOf(trunk);
        ushort leaves = BlockRegistry.IdOf(leaf);
        for (int y = 0; y < trunkHeight; y++)
            w.SetBlock(basePos + new Vector3I(0, y, 0), log, false);

        Vector3I top = basePos + new Vector3I(0, trunkHeight, 0);
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        for (int y = -2; y <= 1; y++)
        {
            float d = Mathf.Sqrt(x * x + z * z + y * y * 1.3f);
            if (d <= 2.4f)
                w.SetBlock(top + new Vector3I(x, y, z), leaves, false);
        }
    }

    /// <summary>Hollow rectangular building with a doorway and a flat roof.</summary>
    public static void BuildHut(VoxelWorld w, Vector3I origin, int wx, int wz, int h,
        string wall = "mud_brick", string roof = "thatch")
    {
        ushort wallId = BlockRegistry.IdOf(wall);
        ushort roofId = BlockRegistry.IdOf(roof);
        for (int x = 0; x < wx; x++)
        for (int z = 0; z < wz; z++)
        for (int y = 0; y < h; y++)
        {
            bool edge = x == 0 || x == wx - 1 || z == 0 || z == wz - 1;
            bool top = y == h - 1;
            if (top) w.SetBlock(origin + new Vector3I(x, y, z), roofId, false);
            else if (edge) w.SetBlock(origin + new Vector3I(x, y, z), wallId, false);
        }
        // doorway on the +x wall
        w.SetBlock(origin + new Vector3I(wx - 1, 0, wz / 2), 0, false);
        w.SetBlock(origin + new Vector3I(wx - 1, 1, wz / 2), 0, false);
    }
}
