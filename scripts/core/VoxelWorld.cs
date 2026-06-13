using System;
using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>The voxel world: a sparse grid of <see cref="Chunk"/>s with block
/// get/set in world coordinates, neighbour-aware remeshing, and collision.</summary>
public sealed partial class VoxelWorld : Node3D
{
    public BlockTextures Textures { get; private set; }
    public Material WaterMaterial { get; private set; }

    private readonly Dictionary<Vector3I, Chunk> _chunks = new();
    private readonly HashSet<Vector3I> _dirty = new();
    private readonly Queue<Vector3I> _dirtyQueue = new();

    public int ChunksPerFrame = 4;
    public IReadOnlyDictionary<Vector3I, Chunk> Chunks => _chunks;

    public override void _Ready()
    {
        BlockRegistry.EnsureInit();
        Textures = BlockTextures.Build();
        WaterMaterial = MakeWaterMaterial();
    }

    public override void _Process(double delta)
    {
        int built = 0;
        while (built < ChunksPerFrame && _dirtyQueue.Count > 0)
        {
            var c = _dirtyQueue.Dequeue();
            _dirty.Remove(c);
            if (_chunks.TryGetValue(c, out var chunk) && chunk.Dirty)
            {
                RebuildChunk(chunk);
                built++;
            }
        }
    }

    // ---- coordinate helpers ----------------------------------------------

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    private static int Mod(int a, int b)
    {
        int r = a % b;
        return r < 0 ? r + b : r;
    }

    public static Vector3I ChunkCoord(int x, int y, int z) =>
        new(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), FloorDiv(z, Chunk.Size));

    // ---- block access -----------------------------------------------------

    public ushort GetBlockId(int x, int y, int z)
    {
        var c = ChunkCoord(x, y, z);
        return _chunks.TryGetValue(c, out var ch)
            ? ch.GetLocal(Mod(x, Chunk.Size), Mod(y, Chunk.Size), Mod(z, Chunk.Size))
            : (ushort)0;
    }

    public ushort GetBlockId(Vector3I p) => GetBlockId(p.X, p.Y, p.Z);
    public BlockType GetBlock(int x, int y, int z) => BlockRegistry.Get(GetBlockId(x, y, z));
    public BlockType GetBlock(Vector3I p) => BlockRegistry.Get(GetBlockId(p.X, p.Y, p.Z));
    public bool IsOpaque(Vector3I p) => BlockRegistry.Get(GetBlockId(p.X, p.Y, p.Z)).Opaque;
    public bool IsSolid(Vector3I p) => BlockRegistry.Get(GetBlockId(p.X, p.Y, p.Z)).Solid;

    public void SetBlock(int x, int y, int z, ushort id, bool remesh = true)
    {
        var c = ChunkCoord(x, y, z);
        var ch = GetOrCreate(c);
        int lx = Mod(x, Chunk.Size), ly = Mod(y, Chunk.Size), lz = Mod(z, Chunk.Size);
        ch.SetLocal(lx, ly, lz, id);
        if (!remesh) return;

        MarkDirty(c);
        if (lx == 0) MarkDirty(c + new Vector3I(-1, 0, 0));
        if (lx == Chunk.Size - 1) MarkDirty(c + new Vector3I(1, 0, 0));
        if (ly == 0) MarkDirty(c + new Vector3I(0, -1, 0));
        if (ly == Chunk.Size - 1) MarkDirty(c + new Vector3I(0, 1, 0));
        if (lz == 0) MarkDirty(c + new Vector3I(0, 0, -1));
        if (lz == Chunk.Size - 1) MarkDirty(c + new Vector3I(0, 0, 1));
    }

    public void SetBlock(Vector3I p, ushort id, bool remesh = true) => SetBlock(p.X, p.Y, p.Z, id, remesh);

    // ---- chunk management -------------------------------------------------

    private Chunk GetOrCreate(Vector3I coord)
    {
        if (_chunks.TryGetValue(coord, out var ch)) return ch;
        ch = new Chunk { Coord = coord, Name = $"Chunk_{coord.X}_{coord.Y}_{coord.Z}" };
        _chunks[coord] = ch;
        AddChild(ch);
        return ch;
    }

    /// <summary>Bulk-load a chunk's block data (used by world load). Marks it dirty.</summary>
    public void LoadChunk(Vector3I coord, ushort[] data)
    {
        var ch = GetOrCreate(coord);
        System.Array.Copy(data, ch.Blocks, Chunk.Volume);
        ch.RecomputeSolid();
        MarkDirty(coord);
    }

    private void MarkDirty(Vector3I coord)
    {
        if (!_chunks.TryGetValue(coord, out var ch)) return;
        ch.Dirty = true;
        if (_dirty.Add(coord)) _dirtyQueue.Enqueue(coord);
    }

    private void RebuildChunk(Chunk chunk)
    {
        var r = ChunkMesher.Build(this, chunk);
        chunk.ApplyMesh(r.Opaque, Textures.Material, r.Water, WaterMaterial, r.Collision);
        chunk.Dirty = false;
    }

    /// <summary>Mark every existing chunk dirty (e.g. after bulk generation).</summary>
    public void MarkAllDirty()
    {
        foreach (var c in _chunks.Keys) MarkDirty(c);
    }

    /// <summary>Synchronously remesh all chunks now (used for initial load/tests).</summary>
    public void RebuildAllNow()
    {
        foreach (var ch in _chunks.Values)
            RebuildChunk(ch);
        _dirty.Clear();
        _dirtyQueue.Clear();
    }

    public int ChunkCount => _chunks.Count;

    public void Clear()
    {
        foreach (var ch in _chunks.Values) ch.QueueFree();
        _chunks.Clear();
        _dirty.Clear();
        _dirtyQueue.Clear();
    }

    // ---- water material (placeholder; upgraded with the swimming milestone) -

    private static Material MakeWaterMaterial()
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.42f, 0.62f, 0.72f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.12f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return m;
    }
}
