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

    // Player edits as deltas over the generated baseline, grouped by chunk so they
    // can be re-applied each time a chunk regenerates. This is what gets saved.
    private readonly Dictionary<Vector3I, Dictionary<Vector3I, ushort>> _edits = new();
    public int MaxConcurrentGen = 8;
    public int GeneratePerFrame = 8;

    private struct GenResult { public Vector3I Coord; public ushort[] Blocks; }

    public bool IsStreaming => _streaming;
    public int GeneratedChunkCount => _generated.Count;
    public int Seed => _generator?.Seed ?? 0;
    public int EditCount { get { int n = 0; foreach (var d in _edits.Values) n += d.Count; return n; } }

    // ---- edit deltas (the saveable state of an infinite world) -------------

    private void RecordEdit(Vector3I chunkCoord, int x, int y, int z, ushort id)
    {
        if (!_edits.TryGetValue(chunkCoord, out var d)) { d = new(); _edits[chunkCoord] = d; }
        d[new Vector3I(x, y, z)] = id;
    }

    private void ApplyEdits(Vector3I coord, Chunk chunk)
    {
        if (!_edits.TryGetValue(coord, out var d)) return;
        foreach (var (pos, id) in d)
            chunk.SetLocal(Mod(pos.X, Chunk.Size), Mod(pos.Y, Chunk.Size), Mod(pos.Z, Chunk.Size), id);
    }

    /// <summary>Seed edits before streaming starts (used by world load). Keyed by
    /// world position.</summary>
    public void PreloadEdits(IEnumerable<(int x, int y, int z, ushort id)> edits)
    {
        foreach (var (x, y, z, id) in edits)
            RecordEdit(ChunkCoord(x, y, z), x, y, z, id);
    }

    /// <summary>All player edits as (world position, block id) — for saving.</summary>
    public IEnumerable<(Vector3I pos, ushort id)> AllEdits()
    {
        foreach (var d in _edits.Values)
            foreach (var (pos, id) in d)
                yield return (pos, id);
    }

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
        _edits.Clear();
        _waterQueue.Clear();
        _waterQueued.Clear();
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
            ApplyEdits(coord, ch);
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
            ApplyEdits(r.Coord, chunk);
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

    // ---- water fill -------------------------------------------------------
    //
    // A deterministic, frame-amortized flood fill: any air cell at or below sea
    // level that touches water (above or to the side) becomes water, cascading into
    // newly opened space. The fixed point depends only on the block grid + sea
    // level (no RNG, no time), so it is safe to replicate to multiplayer clients by
    // shipping the resulting block edits from an authoritative server. Bounded above
    // by sea level and on the sides by solid walls, so a dig confines itself to the
    // reachable air pocket below the waterline.
    //
    // Pacing is TIME-based, not per-frame: a per-frame budget at 60 fps still drained
    // small pits in a frame or two (looked instant). We advance WaterCellsPerStep cells
    // every WaterStepSeconds instead, so the wave front is visibly gradual and frame-rate
    // independent. The final filled state is identical regardless of pacing, so the
    // multiplayer-replication guarantee above is unchanged.
    //
    // Depth-first priority: pending cells live in a single priority queue keyed by Y,
    // so the LOWEST (deepest) reachable cell is always filled next. Water therefore pours
    // straight down into the deepest pocket and fills it from the bottom up before it
    // rises to higher cells — water "finding its level", and waterfalls for free (a cell
    // with water directly above it is simply the lowest cell on the frontier). No RNG is
    // used; the priority order is deterministic for a given sequence of edits.

    // Priority = the cell's world-Y; PriorityQueue serves the lowest (deepest) first, so
    // the fill always advances bottom-up regardless of the order cells were discovered.
    private readonly PriorityQueue<Vector3I, int> _waterQueue = new();
    private readonly HashSet<Vector3I> _waterQueued = new();
    // ~1 cell / 0.08 s ≈ 12.5 cells/s: deliberately gradual so the wave front is clearly
    // visible (a stairway fills over a few seconds, a large sea over many). The final
    // filled state is identical regardless of pacing.
    public float WaterStepSeconds = 0.08f;
    public int WaterCellsPerStep = 1;
    private float _waterAccum;

    private void EnqueueWaterArea(int x, int y, int z)
    {
        // Seed the edited cell and its six neighbours. The priority queue orders them by
        // depth, so the order we add them in does not matter — the deepest reachable cell
        // is always served first. Seeding the edited cell itself (not just its neighbours)
        // is what lets a single dig into a lake fill on that one edit alone, without a
        // second nearby break to re-seed it.
        TryQueueWater(x, y, z);          // the just-edited cell
        TryQueueWater(x, y - 1, z);      // straight down
        TryQueueWater(x + 1, y, z);
        TryQueueWater(x - 1, y, z);
        TryQueueWater(x, y + 1, z);
        TryQueueWater(x, y, z + 1);
        TryQueueWater(x, y, z - 1);
    }

    private void TryQueueWater(int x, int y, int z)
    {
        if (y > TerrainGenerator.SeaLevel) return; // water never climbs above sea level
        var p = new Vector3I(x, y, z);
        if (!_waterQueued.Add(p)) return;
        _waterQueue.Enqueue(p, y);                 // priority = depth: lowest Y is served first
    }

    private void PumpWaterFill(double delta)
    {
        // Time-based pacing so the wave front is actually visible. Accumulate real
        // seconds; once a step elapses, advance a few cells. DrainWater always takes the
        // lowest (deepest) pending cell, so the fill works strictly bottom-up.
        _waterAccum += (float)delta;
        if (_waterAccum < WaterStepSeconds) return;
        _waterAccum = 0f;
        DrainWater(WaterCellsPerStep);
    }

    /// <summary>Test hook: synchronously fill up to <paramref name="maxCells"/> water cells
    /// from the pending queue, bypassing the time-based pacing, and return how many were
    /// placed. Lets tests assert the deterministic bottom-up fill ORDER, which the
    /// time-amortised pump cannot expose deterministically.</summary>
    public int DrainWaterForTest(int maxCells) => DrainWater(maxCells);

    private int DrainWater(int budget)
    {
        int placed = 0;
        while (placed < budget && _waterQueue.Count > 0)
        {
            var p = _waterQueue.Dequeue();         // lowest Y first
            _waterQueued.Remove(p);
            if (p.Y > TerrainGenerator.SeaLevel) continue;
            // Only act inside generated chunks, so the fill never conjures terrain
            // into not-yet-streamed space; the front simply pauses at the boundary.
            if (!_generated.Contains(ChunkCoord(p.X, p.Y, p.Z))) continue;
            if (GetBlockId(p.X, p.Y, p.Z) != 0) continue; // only fill air
            if (!WaterReaches(p)) continue;               // re-seeded later when a neighbour fills
            // SetBlock records the edit (persistence), remeshes, and re-seeds the
            // neighbours via the EnqueueWaterArea hook — that is the cascade.
            SetBlock(p.X, p.Y, p.Z, _waterId, cause: BlockChangeCause.Script); // engine-driven, not a player edit
            placed++;
        }
        return placed;
    }

    private bool WaterReaches(Vector3I p) =>
        GetBlockId(p.X, p.Y + 1, p.Z) == _waterId
        || GetBlockId(p.X + 1, p.Y, p.Z) == _waterId
        || GetBlockId(p.X - 1, p.Y, p.Z) == _waterId
        || GetBlockId(p.X, p.Y, p.Z + 1) == _waterId
        || GetBlockId(p.X, p.Y, p.Z - 1) == _waterId;
}
