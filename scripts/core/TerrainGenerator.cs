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

    private static uint Hash(int x, int y, int seed)
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

    private static float Corner(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / (float)0xFFFFFF;
    private static float Fade(float t) => t * t * (3f - 2f * t);

    /// <summary>Single-octave value noise in 0..1.</summary>
    public float Noise(float x, float y)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Fade(x - x0), fy = Fade(y - y0);
        float v00 = Corner(x0, y0, _seed), v10 = Corner(x0 + 1, y0, _seed);
        float v01 = Corner(x0, y0 + 1, _seed), v11 = Corner(x0 + 1, y0 + 1, _seed);
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

/// <summary>A simple heightfield terrain generator: rolling hills with stone, a
/// dirt/grass cap, beaches and a sea, and snow on the peaks. Phase 4 layers real
/// biomes, caves, ores and vegetation on top of this; for now it exists to give
/// the streaming engine an endless world to load.</summary>
public sealed class TerrainGenerator : VoxelWorld.IChunkGenerator
{
    public int Seed { get; }

    public const int SeaLevel = 26;
    private const int BaseHeight = 24;
    private const float HeightAmp = 26f;
    private const float Frequency = 1f / 110f;

    private readonly ValueNoise2D _noise;
    private readonly ushort _grass, _dirt, _stone, _sand, _water, _snow;

    public TerrainGenerator(int seed)
    {
        Seed = seed;
        BlockRegistry.EnsureInit();
        _noise = new ValueNoise2D(seed);
        _grass = BlockRegistry.IdOf("grass");
        _dirt = BlockRegistry.IdOf("dirt");
        _stone = BlockRegistry.IdOf("stone");
        _sand = BlockRegistry.IdOf("sand");
        _water = BlockRegistry.IdOf("water");
        _snow = BlockRegistry.IdOf("snow");
    }

    public int SurfaceHeight(int worldX, int worldZ)
    {
        float n = _noise.Fractal(worldX * Frequency, worldZ * Frequency, 4);
        return BaseHeight + Mathf.RoundToInt((n - 0.5f) * 2f * HeightAmp);
    }

    public void Generate(Vector3I coord, ushort[] blocks)
    {
        Vector3I baseW = coord * Chunk.Size;
        for (int lz = 0; lz < Chunk.Size; lz++)
        for (int lx = 0; lx < Chunk.Size; lx++)
        {
            int height = SurfaceHeight(baseW.X + lx, baseW.Z + lz);
            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                int wy = baseW.Y + ly;
                ushort id;
                if (wy > height)
                    id = wy <= SeaLevel ? _water : (ushort)0;
                else if (wy == height)
                {
                    if (height <= SeaLevel + 1) id = _sand;
                    else if (height >= SeaLevel + 22) id = _snow;
                    else id = _grass;
                }
                else if (wy > height - 4)
                    id = _dirt;
                else
                    id = _stone;
                blocks[Chunk.Index(lx, ly, lz)] = id;
            }
        }
    }
}
