using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace RAEngine.Core;

/// <summary>Render-distance streaming for a near-infinite procedural world.
///
/// Chunks are generated and meshed in two asynchronous phases so neither stalls
/// the frame:
///   • Generation fills a chunk's block array from a deterministic
///     <see cref="IChunkGenerator"/> on a worker thread. We generate one chunk
///     ring <em>beyond</em> the visible radius so every visible chunk has all of
///     its neighbours' block data available.
///   • Meshing (the base <see cref="VoxelWorld"/> pump) only runs once a chunk
///     and its six face-neighbours are generated, so chunk borders are culled
///     correctly with no seams.
/// Chunks past the unload radius are freed. Everything keys off the streaming
/// target's chunk column and only rescans when it crosses into a new one.</summary>
public sealed partial class VoxelWorld : Node3D
{
    public interface IChunkGenerator
    {
        int Seed { get; }
        /// <summary>Fill <paramref name="blocks"/> (length <see cref="Chunk.Volume"/>)
        /// for the chunk at <paramref name="coord"/>. Must be deterministic and
        /// thread-safe — it runs on worker threads.</summary>
        void Generate(Vector3I coord, ushort[] blocks);
        /// <summary>The surface height (world Y of the topmost solid block) at a
        /// world column, used to place the player and spawn-area features.</summary>
        int SurfaceHeight(int worldX, int worldZ);
    }

    private bool _streaming;
    private IChunkGenerator _generator;
    private Node3D _streamTarget;
    private int _renderDistance = 8;   // visible radius, in chunks (horizontal)
    private int _minChunkY = -1;
    private int _maxChunkY = 4;
    private Vector3I _lastTargetChunk = new(int.MinValue, 0, 0);

    private readonly HashSet<Vector3I> _generated = new();   // block data ready
    private readonly HashSet<Vector3I> _generating = new();  // gen jobs in flight
    private readonly HashSet<Vector3I> _genPending = new();  // queued for generation
    private readonly Queue<Vector3I> _genQueue = new();
    private readonly ConcurrentQueue<GenResult> _genResults = new();
    private readonly List<Vector3I> _scratch = new();
    public int MaxConcurrentGen = 8;
    public int GeneratePerFrame = 8;

    private struct GenResult { public Vector3I Coord; public ushort[] Blocks; }

    public bool IsStreaming => _streaming;
    public int GeneratedChunkCount => _generated.Count;

    private static readonly Vector3I[] FaceNeighbours =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    // ---- public control ---------------------------------------------------

    /// <summary>Begin streaming chunks around <paramref name="target"/> using
    /// <paramref name="generator"/>. Call <see cref="EnsureSpawnArea"/> first if
    /// the target needs solid ground beneath it immediately.</summary>
    public void StartStreaming(IChunkGenerator generator, Node3D target,
        int renderDistance = 8, int minChunkY = -1, int maxChunkY = 4)
    {
        _generator = generator;
        _streamTarget = target;
        _renderDistance = Mathf.Max(2, renderDistance);
        _minChunkY = minChunkY;
        _maxChunkY = maxChunkY;
        _lastTargetChunk = new Vector3I(int.MinValue, 0, 0);
        _streaming = true;
    }

    public void StopStreaming() => _streaming = false;

    private void ResetStreaming()
    {
        _generated.Clear();
        _generating.Clear();
        _genPending.Clear();
        _genQueue.Clear();
        while (_genResults.TryDequeue(out _)) { }
        _lastTargetChunk = new Vector3I(int.MinValue, 0, 0);
    }

    /// <summary>Synchronously generate and mesh a small area around a point so the
    /// target has visible ground the instant the world appears, before the async
    /// streamer takes over. Generates one extra ring of block data for correct
    /// borders, then meshes the inner radius.</summary>
    public void EnsureSpawnArea(Vector3 center, int radius = 2)
    {
        if (_generator == null) return;
        Vector3I tc = ChunkCoord(Mathf.FloorToInt(center.X), 0, Mathf.FloorToInt(center.Z));

        // 1. generate block data for radius+1 (so the meshed inner ring has neighbours)
        int genR = radius + 1;
        for (int dz = -genR; dz <= genR; dz++)
        for (int dx = -genR; dx <= genR; dx++)
        for (int cy = _minChunkY; cy <= _maxChunkY; cy++)
        {
            var coord = new Vector3I(tc.X + dx, cy, tc.Z + dz);
            if (_generated.Contains(coord)) continue;
            var ch = GetOrCreate(coord);
            _generator.Generate(coord, ch.Blocks);
            ch.RecomputeSolid();
            _generated.Add(coord);
        }

        // 2. mesh the inner radius synchronously
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int cy = _minChunkY; cy <= _maxChunkY; cy++)
        {
            var coord = new Vector3I(tc.X + dx, cy, tc.Z + dz);
            if (_chunks.TryGetValue(coord, out var ch)) RebuildChunk(ch);
        }
    }

    /// <summary>True while the chunk under <paramref name="worldPos"/> has not been
    /// generated yet — the player uses it to avoid dropping into the void while a
    /// region streams in. Always false when not streaming (a missing chunk is then
    /// genuinely empty sky, not pending terrain).</summary>
    public bool StreamingHold(Vector3 worldPos)
    {
        if (!_streaming) return false;
        var c = ChunkCoord(Mathf.FloorToInt(worldPos.X),
            Mathf.FloorToInt(worldPos.Y - 0.1f), Mathf.FloorToInt(worldPos.Z));
        return !_generated.Contains(c);
    }

    // ---- per-frame: decide what to load/unload ----------------------------

    private void UpdateStreaming()
    {
        if (_streamTarget == null || !GodotObject.IsInstanceValid(_streamTarget)) return;
        Vector3 p = _streamTarget.GlobalPosition;
        Vector3I tc = ChunkCoord(Mathf.FloorToInt(p.X), 0, Mathf.FloorToInt(p.Z));

        // Only the expensive rescan is gated on crossing into a new chunk column;
        // the generation/mesh pumps still run every frame from _Process.
        if (tc.X == _lastTargetChunk.X && tc.Z == _lastTargetChunk.Z) return;
        _lastTargetChunk = tc;

        int genR = _renderDistance + 1;
        int meshR = _renderDistance;
        int unloadR = _renderDistance + 2;

        // Enqueue any ungenerated chunks within the generation disc, nearest first.
        _scratch.Clear();
        for (int dz = -genR; dz <= genR; dz++)
        for (int dx = -genR; dx <= genR; dx++)
        {
            if (dx * dx + dz * dz > genR * genR) continue;
            for (int cy = _minChunkY; cy <= _maxChunkY; cy++)
            {
                var coord = new Vector3I(tc.X + dx, cy, tc.Z + dz);
                if (_generated.Contains(coord) || _generating.Contains(coord) || _genPending.Contains(coord))
                    continue;
                _scratch.Add(coord);
            }
        }
        _scratch.Sort((a, b) => HDist2(a, tc).CompareTo(HDist2(b, tc)));
        foreach (var coord in _scratch)
        {
            _genPending.Add(coord);
            _genQueue.Enqueue(coord);
        }

        // Re-mesh any already-generated chunks that have entered the visible radius
        // (e.g. former margin chunks the target has now walked toward).
        for (int dz = -meshR; dz <= meshR; dz++)
        for (int dx = -meshR; dx <= meshR; dx++)
        {
            if (dx * dx + dz * dz > meshR * meshR) continue;
            for (int cy = _minChunkY; cy <= _maxChunkY; cy++)
            {
                var coord = new Vector3I(tc.X + dx, cy, tc.Z + dz);
                if (_generated.Contains(coord) && _chunks.TryGetValue(coord, out var ch) && !ch.Meshed)
                    MarkDirty(coord);
            }
        }

        // Unload everything past the unload radius.
        _scratch.Clear();
        foreach (var c in _chunks.Keys)
            if (HDist2(c, tc) > unloadR * unloadR) _scratch.Add(c);
        foreach (var c in _scratch) UnloadChunk(c);
    }

    private static int HDist2(Vector3I c, Vector3I tc)
    {
        int dx = c.X - tc.X, dz = c.Z - tc.Z;
        return dx * dx + dz * dz;
    }

    // ---- generation pump --------------------------------------------------

    private void PumpGeneration()
    {
        // Dispatch generation jobs onto worker threads.
        while (_generating.Count < MaxConcurrentGen && _genQueue.Count > 0)
        {
            var coord = _genQueue.Dequeue();
            _genPending.Remove(coord);
            if (_generated.Contains(coord) || _generating.Contains(coord)) continue;
            GetOrCreate(coord); // create the node now (main thread); fills with air until generated
            _generating.Add(coord);
            var gen = _generator;
            Task.Run(() =>
            {
                var buf = new ushort[Chunk.Volume];
                try { gen.Generate(coord, buf); }
                catch (Exception) { Array.Clear(buf, 0, buf.Length); }
                _genResults.Enqueue(new GenResult { Coord = coord, Blocks = buf });
            });
        }

        // Apply finished generation on the main thread.
        int applied = 0;
        while (applied < GeneratePerFrame && _genResults.TryDequeue(out var r))
        {
            _generating.Remove(r.Coord);
            if (!_chunks.TryGetValue(r.Coord, out var chunk)) continue; // unloaded meanwhile
            Array.Copy(r.Blocks, chunk.Blocks, Chunk.Volume);
            chunk.RecomputeSolid();
            _generated.Add(r.Coord);
            applied++;

            // This chunk — and any neighbour that was waiting on it — may now be
            // mesh-eligible. MeshEligible() gates the actual build.
            MarkDirty(r.Coord);
            foreach (var n in FaceNeighbours) MarkDirty(r.Coord + n);
        }
    }

    /// <summary>A chunk may be meshed only once it and all six face-neighbours have
    /// block data, so border faces are culled against real neighbours. Neighbours
    /// outside the streamed vertical range are always air and count as ready.</summary>
    private bool MeshEligible(Vector3I coord)
    {
        if (!_generated.Contains(coord)) return false;
        foreach (var n in FaceNeighbours)
        {
            var nc = coord + n;
            if (nc.Y < _minChunkY || nc.Y > _maxChunkY) continue; // always-air boundary
            if (!_generated.Contains(nc)) return false;
        }
        return true;
    }

    private void UnloadChunk(Vector3I coord)
    {
        if (_chunks.TryGetValue(coord, out var ch))
        {
            ch.QueueFree();
            _chunks.Remove(coord);
        }
        _generated.Remove(coord);
        _genPending.Remove(coord);
        _dirty.Remove(coord);
    }
}
