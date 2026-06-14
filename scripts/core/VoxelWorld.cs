using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace RAEngine.Core;

/// <summary>The voxel world: a sparse grid of <see cref="Chunk"/>s with block
/// get/set in world coordinates, neighbour-aware remeshing, and collision.
///
/// Meshing is asynchronous: dirty chunks are snapshotted on the main thread and
/// turned into geometry on worker threads (see <see cref="ChunkMesher"/>), then
/// the finished data is applied back on the main thread a few chunks per frame.
/// This keeps frame times flat while large numbers of chunks (re)build, which is
/// what makes render-distance streaming feasible. Tests that need a finished
/// world up front call <see cref="RebuildAllNow"/>, which meshes synchronously.</summary>
public sealed partial class VoxelWorld : Node3D
{
    public BlockTextures Textures { get; private set; }
    public Material WaterMaterial { get; private set; }
    private ushort _waterId;

    private readonly Dictionary<Vector3I, Chunk> _chunks = new();
    private readonly HashSet<Vector3I> _dirty = new();
    private readonly Queue<Vector3I> _dirtyQueue = new();

    // Async meshing state.
    private readonly HashSet<Vector3I> _meshing = new();          // jobs in flight (main thread only)
    private readonly ConcurrentQueue<MeshJobResult> _results = new();
    /// <summary>How many mesh jobs may be in flight at once. Bounds CPU + memory.</summary>
    public int MaxConcurrentMeshes = 8;
    /// <summary>How many finished meshes to upload per frame (caps the main-thread spike).</summary>
    public int ApplyPerFrame = 6;

    private struct MeshJobResult
    {
        public Vector3I Coord;
        public int Version;
        public ChunkMesher.MeshData Data;
    }

    public IReadOnlyDictionary<Vector3I, Chunk> Chunks => _chunks;

    public override void _Ready()
    {
        BlockRegistry.EnsureInit();
        Textures = BlockTextures.Build();
        WaterMaterial = MakeWaterMaterial();
        _waterId = BlockRegistry.IdOf("water");
    }

    public override void _Process(double delta)
    {
        if (_streaming)
        {
            UpdateStreaming();
            PumpGeneration();
            PumpWaterFill();
        }
        PumpMeshing();
    }

    // ---- async meshing pump -----------------------------------------------

    private void PumpMeshing()
    {
        // Dispatch: snapshot dirty chunks on the main thread and mesh them on
        // worker threads, up to the in-flight budget.
        while (_meshing.Count < MaxConcurrentMeshes && _dirtyQueue.Count > 0)
        {
            var coord = _dirtyQueue.Dequeue();
            _dirty.Remove(coord);
            if (!_chunks.TryGetValue(coord, out var chunk) || !chunk.Dirty) continue;
            if (_meshing.Contains(coord)) continue; // already meshing; staleness handled on apply
            if (_streaming && !MeshEligible(coord)) continue; // wait for neighbours to generate

            var snap = ChunkMesher.Capture(this, chunk);
            int version = chunk.MeshVersion;
            chunk.Dirty = false;
            _meshing.Add(coord);
            Task.Run(() =>
            {
                ChunkMesher.MeshData data = null;
                try { data = ChunkMesher.BuildData(snap); }
                catch (Exception) { /* surfaced on apply; chunk will retry */ }
                _results.Enqueue(new MeshJobResult { Coord = coord, Version = version, Data = data });
            });
        }

        // Apply: upload finished geometry on the main thread, a few per frame.
        int applied = 0;
        while (applied < ApplyPerFrame && _results.TryDequeue(out var r))
        {
            _meshing.Remove(r.Coord);
            if (!_chunks.TryGetValue(r.Coord, out var chunk)) continue; // unloaded meanwhile
            if (r.Data == null || chunk.MeshVersion != r.Version) { MarkDirty(r.Coord); continue; }
            ChunkMesher.Apply(this, chunk, r.Data);
            chunk.Dirty = false;
            applied++;
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
        // In a streamed world every SetBlock is a player edit (generation writes
        // block arrays directly), so record it as a delta over the generated
        // baseline — that, plus the seed, is all we need to persist the world.
        if (_streaming) RecordEdit(c, x, y, z, id);
        var ch = GetOrCreate(c);
        int lx = Mod(x, Chunk.Size), ly = Mod(y, Chunk.Size), lz = Mod(z, Chunk.Size);
        ch.SetLocal(lx, ly, lz, id);
        // Let water reclaim any space opened at/below sea level near it (and cascade
        // as new water cells appear). Seeding the edited cell + its 6 neighbours
        // covers both "dug a hole next to water" and "removed a wall holding it back".
        if (_streaming) EnqueueWaterArea(x, y, z);
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
        var snap = ChunkMesher.Capture(this, chunk);
        var data = ChunkMesher.BuildData(snap);
        ChunkMesher.Apply(this, chunk, data);
        chunk.Dirty = false;
    }

    /// <summary>Mark every existing chunk dirty (e.g. after bulk generation).</summary>
    public void MarkAllDirty()
    {
        foreach (var c in _chunks.Keys) MarkDirty(c);
    }

    /// <summary>Synchronously remesh all dirty chunks now (used for initial load and
    /// headless tests, which need a finished world before asserting on it).</summary>
    public void RebuildAllNow()
    {
        foreach (var ch in _chunks.Values)
            RebuildChunk(ch);
        _dirty.Clear();
        _dirtyQueue.Clear();
    }

    public int ChunkCount => _chunks.Count;
    /// <summary>Mesh jobs currently in flight on worker threads (for the debug HUD).</summary>
    public int MeshingCount => _meshing.Count;
    /// <summary>Chunks queued for (re)meshing (for the debug HUD).</summary>
    public int DirtyCount => _dirty.Count;

    public void Clear()
    {
        foreach (var ch in _chunks.Values) ch.QueueFree();
        _chunks.Clear();
        _dirty.Clear();
        _dirtyQueue.Clear();
        _meshing.Clear();
        while (_results.TryDequeue(out _)) { }
        ResetStreaming();
    }

    // ---- water material (animated rippling shader) ------------------------

    private static Material MakeWaterMaterial()
    {
        var shader = GD.Load<Shader>("res://assets/shaders/water.gdshader");
        if (shader == null)
        {
            // Fallback: flat translucent water if the shader is missing.
            return new StandardMaterial3D
            {
                AlbedoColor = new Color(0.18f, 0.42f, 0.62f, 0.72f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.12f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }
        return new ShaderMaterial { Shader = shader };
    }
}
