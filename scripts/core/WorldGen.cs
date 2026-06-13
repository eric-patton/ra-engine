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
        FlatGround(w, -4, 52, -4, 20, 0);

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
