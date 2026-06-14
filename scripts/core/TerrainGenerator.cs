using Godot;

namespace RAEngine.Core;

/// <summary>Deterministic fractal value noise in pure C# (no Godot objects), so it
/// is safe to evaluate from multiple worker threads at once and produces the same
/// world from the same seed on any machine — the foundation for the seeded,
/// replication-friendly worlds the roadmap calls for.</summary>
public sealed class ValueNoise2D
{
    private readonly int _seed;
    public ValueNoise2D(int seed) => _seed = seed;

    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = (h ^ (h >> 15)) * 0xC2B2AE3Du;
            h ^= (uint)y * 0x27D4EB2Fu;
            h = (h ^ (h >> 13)) * 0x165667B1u;
            return h ^ (h >> 16);
        }
    }

    private float Corner(int x, int y) => (Hash(x, y, _seed) & 0xFFFFFF) / (float)0xFFFFFF;
    private static float Fade(float t) => t * t * (3f - 2f * t);

    /// <summary>Single-octave value noise in 0..1.</summary>
    public float Noise(float x, float y)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Fade(x - x0), fy = Fade(y - y0);
        float v00 = Corner(x0, y0), v10 = Corner(x0 + 1, y0);
        float v01 = Corner(x0, y0 + 1), v11 = Corner(x0 + 1, y0 + 1);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    /// <summary>Fractal (fBm) value noise in 0..1.</summary>
    public float Fractal(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Noise(x * freq, y * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return norm > 0f ? sum / norm : 0f;
    }
}

public enum Biome { Ocean, Beach, Plains, Forest, Desert, Snow, Mountains }

/// <summary>A biome-based heightfield generator: a continental elevation field
/// carves oceans, beaches and mountains, while two slow climate fields (warmth
/// and moisture) select plains, forest, desert, snow or rocky mountain surfaces.
/// Trees are placed from a hashed grid so they stitch seamlessly across chunk
/// borders — every chunk computes the same trees and stamps only the part that
/// falls inside it. Everything is a pure function of (seed, world position), so a
/// chunk meshes identically no matter which order chunks stream in.</summary>
public sealed class TerrainGenerator : VoxelWorld.IChunkGenerator
{
    public int Seed { get; }

    public const int SeaLevel = 26;
    private const int BaseHeight = 16;
    private const float HeightRange = 46f;
    private const int SnowLine = SeaLevel + 24;
    private const float ElevFreq = 1f / 150f;
    private const float ClimateFreq = 1f / 320f;
    private const int TreeGrid = 5;

    private readonly ValueNoise2D _elev, _detail, _warmth, _moisture;
    private readonly ushort _grass, _dirt, _stone, _sand, _sandstone, _gravel, _water, _snow, _log, _leaves;

    public TerrainGenerator(int seed)
    {
        Seed = seed;
        BlockRegistry.EnsureInit();
        _elev = new ValueNoise2D(seed);
        _detail = new ValueNoise2D(seed + 17);
        _warmth = new ValueNoise2D(seed + 101);
        _moisture = new ValueNoise2D(seed + 211);

        _grass = BlockRegistry.IdOf("grass");
        _dirt = BlockRegistry.IdOf("dirt");
        _stone = BlockRegistry.IdOf("stone");
        _sand = BlockRegistry.IdOf("sand");
        _sandstone = BlockRegistry.IdOf("sandstone");
        _gravel = BlockRegistry.IdOf("gravel");
        _water = BlockRegistry.IdOf("water");
        _snow = BlockRegistry.IdOf("snow");
        _log = BlockRegistry.IdOf("oak_log");
        _leaves = BlockRegistry.IdOf("leaves");
    }

    public int SurfaceHeight(int worldX, int worldZ)
    {
        // Continental shape with a little high-frequency roughness on top.
        float e = _elev.Fractal(worldX * ElevFreq, worldZ * ElevFreq, 5);
        float rough = (_detail.Fractal(worldX * ElevFreq * 4f, worldZ * ElevFreq * 4f, 3) - 0.5f) * 4f;
        return BaseHeight + Mathf.RoundToInt(e * HeightRange + rough);
    }

    public Biome BiomeAt(int worldX, int worldZ, int height)
    {
        if (height < SeaLevel) return Biome.Ocean;
        if (height <= SeaLevel + 1) return Biome.Beach;
        if (height >= SnowLine) return Biome.Mountains;

        float warmth = _warmth.Fractal(worldX * ClimateFreq, worldZ * ClimateFreq, 2);
        float moisture = _moisture.Fractal(worldX * ClimateFreq, worldZ * ClimateFreq, 2);
        if (warmth < 0.34f) return Biome.Snow;
        if (warmth > 0.62f && moisture < 0.45f) return Biome.Desert;
        if (moisture > 0.55f) return Biome.Forest;
        return Biome.Plains;
    }

    public void Generate(Vector3I coord, ushort[] blocks)
    {
        Vector3I baseW = coord * Chunk.Size;
        for (int lz = 0; lz < Chunk.Size; lz++)
        for (int lx = 0; lx < Chunk.Size; lx++)
        {
            int wx = baseW.X + lx, wz = baseW.Z + lz;
            int height = SurfaceHeight(wx, wz);
            Biome biome = BiomeAt(wx, wz, height);
            SurfaceBlocks(biome, height, out ushort top, out ushort sub);

            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                int wy = baseW.Y + ly;
                ushort id;
                if (wy > height)
                    id = wy <= SeaLevel ? _water : (ushort)0;
                else if (wy == height)
                    id = top;
                else if (wy > height - 4)
                    id = sub;
                else
                    id = _stone;
                blocks[Chunk.Index(lx, ly, lz)] = id;
            }
        }

        StampTrees(coord, baseW, blocks);
    }

    private void SurfaceBlocks(Biome biome, int height, out ushort top, out ushort sub)
    {
        switch (biome)
        {
            case Biome.Ocean:
                top = height > SeaLevel - 3 ? _sand : _gravel; sub = _sand; break;
            case Biome.Beach:
                top = _sand; sub = _sand; break;
            case Biome.Desert:
                top = _sand; sub = _sandstone; break;
            case Biome.Snow:
                top = _snow; sub = _dirt; break;
            case Biome.Mountains:
                top = height >= SnowLine + 3 ? _snow : _stone; sub = _stone; break;
            default: // Plains, Forest
                top = _grass; sub = _dirt; break;
        }
    }

    // ---- trees (seamless across chunk borders) ----------------------------

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q--;
        return q;
    }

    /// <summary>Stamp every tree whose canopy could reach this chunk. Trees live on
    /// a hashed grid, so the same trees are computed by every chunk and only the
    /// blocks inside this chunk are written — giving identical, seamless results at
    /// chunk borders regardless of streaming order.</summary>
    private void StampTrees(Vector3I coord, Vector3I baseW, ushort[] blocks)
    {
        const int reach = 3; // canopy radius, in blocks
        int gx0 = FloorDiv(baseW.X - reach, TreeGrid);
        int gx1 = FloorDiv(baseW.X + Chunk.Size - 1 + reach, TreeGrid);
        int gz0 = FloorDiv(baseW.Z - reach, TreeGrid);
        int gz1 = FloorDiv(baseW.Z + Chunk.Size - 1 + reach, TreeGrid);

        for (int gx = gx0; gx <= gx1; gx++)
        for (int gz = gz0; gz <= gz1; gz++)
        {
            uint h = ValueNoise2D.Hash(gx, gz, Seed ^ 0x7EE5);
            int wx = gx * TreeGrid + (int)(h % TreeGrid);
            int wz = gz * TreeGrid + (int)((h / 7u) % TreeGrid);

            int surface = SurfaceHeight(wx, wz);
            if (surface <= SeaLevel + 1) continue; // no trees in water or on the beach
            Biome biome = BiomeAt(wx, wz, surface);
            float density = biome switch { Biome.Forest => 0.55f, Biome.Plains => 0.08f, _ => 0f };
            if (density <= 0f) continue;

            float roll = ((h >> 16) & 0xFFFF) / 65535f;
            if (roll >= density) continue;

            int trunk = 4 + (int)((h >> 8) % 3u); // 4..6 tall
            StampTree(blocks, baseW, wx, surface + 1, wz, trunk);
        }
    }

    private void StampTree(ushort[] blocks, Vector3I baseW, int bx, int by, int bz, int trunkHeight)
    {
        for (int t = 0; t < trunkHeight; t++)
            SetCell(blocks, baseW, bx, by + t, bz, _log, overwrite: true);

        int topY = by + trunkHeight;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dy = -2; dy <= 1; dy++)
        {
            float d = Mathf.Sqrt(dx * dx + dz * dz + dy * dy * 1.3f);
            if (d <= 2.4f)
                SetCell(blocks, baseW, bx + dx, topY + dy, bz + dz, _leaves, overwrite: false);
        }
    }

    private static void SetCell(ushort[] blocks, Vector3I baseW, int wx, int wy, int wz, ushort id, bool overwrite)
    {
        int lx = wx - baseW.X, ly = wy - baseW.Y, lz = wz - baseW.Z;
        if (lx < 0 || lx >= Chunk.Size || ly < 0 || ly >= Chunk.Size || lz < 0 || lz >= Chunk.Size) return;
        int idx = Chunk.Index(lx, ly, lz);
        if (!overwrite && blocks[idx] != 0) return;
        blocks[idx] = id;
    }
}
