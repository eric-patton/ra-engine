using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>The world's fire conductor. Spawns and removes <see cref="Fire"/> nodes,
/// drives every fire's flicker, breathing light and ember/smoke from one shared noise
/// source (so the whole world breathes together without each fire allocating its own
/// noise), and budgets the live lights + particles by distance and count — the FX-LOD
/// cap, since per-fire OmniLights are the real performance risk (the X4 roadmap item).
///
/// Owned by the session: reads the player position for LOD and the environment for the
/// day/night factor (fires read brighter at night) and wind (which bends the smoke).
/// Also auto-lights an altar fire wherever a player places the <c>altar_fire</c> block
/// (in lessons, where block-change events are armed).</summary>
public sealed partial class FireController : Node3D
{
    public Node3D Player;          // LOD distance reference
    public EnvironmentController Env;
    public VoxelWorld World;       // for altar-fire auto-light

    private const int LightBudget = 10;     // max simultaneous fire lights
    private const float LightCull = 30f;    // lights switch off beyond this
    private const float ParticleCull = 38f; // embers/smoke stop beyond this
    private const float FarCull = 96f;      // whole fire hidden beyond this

    private readonly List<Fire> _fires = new();
    // Cached so the per-frame Sort doesn't allocate a comparer delegate each frame.
    private static readonly System.Comparison<Fire> ByDistance = (a, b) => a.Dist.CompareTo(b.Dist);
    private FastNoiseLite _noise;
    private float _t;
    private int _spawned;

    public int Count => _fires.Count;

    public override void _Ready()
    {
        _noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 1f,
        };
        if (World != null) World.BlockChanged += OnBlockChanged;
    }

    public override void _ExitTree()
    {
        if (World != null && GodotObject.IsInstanceValid(World))
            World.BlockChanged -= OnBlockChanged;
    }

    /// <summary>Light a fire of <paramref name="kind"/> at a world position (its base —
    /// the top of the block it sits on).</summary>
    public Fire AddFire(Vector3 worldPos, FireKind kind, FirePalette palette = FirePalette.Normal, Vector3I? cell = null)
    {
        var fire = new Fire { Name = $"Fire{_spawned}" };
        AddChild(fire);
        fire.GlobalPosition = worldPos;
        // Golden-ratio sequence: well-spread seeds in [0,1) so no two fires pulse alike.
        float seed = Mathf.PosMod(_spawned * 0.6180339f, 1f);
        fire.Configure(kind, palette, seed, cell);
        _spawned++;
        _fires.Add(fire);
        return fire;
    }

    /// <summary>Extinguish the fire anchored to a block cell (called when its block breaks).</summary>
    public void RemoveFireAt(Vector3I cell)
    {
        for (int i = _fires.Count - 1; i >= 0; i--)
            if (_fires[i].Cell == cell)
            {
                _fires[i].QueueFree();
                _fires.RemoveAt(i);
            }
    }

    /// <summary>Recolour every live fire (the showcase's [F8] holy/forge toggle).</summary>
    public void SetAllPalette(FirePalette palette)
    {
        foreach (var f in _fires) f.ApplyPalette(palette);
    }

    public override void _Process(double delta)
    {
        if (_fires.Count == 0 || Player == null) return;
        _t += (float)delta;

        Vector3 pp = Player.GlobalPosition;
        float dayF = Env?.DayFactor ?? 0.5f;
        float nightBoost = Mathf.Lerp(1f, 0.5f, dayF); // brighter at night, subtle at noon
        Vector2 wind = Env?.Wind ?? Vector2.Zero;

        foreach (var f in _fires) f.Dist = pp.DistanceTo(f.GlobalPosition);
        _fires.Sort(ByDistance);

        int lights = 0;
        foreach (var f in _fires)
        {
            if (f.Dist >= FarCull) { f.Sleep(); continue; } // far: zero cost
            f.Visible = true;

            bool emit = f.Dist < ParticleCull;
            bool lightOn = f.Dist < LightCull && lights < LightBudget;
            if (lightOn) lights++;

            // One shared noise field, sampled at a per-fire phase: a fast twitch, a slow
            // swell, and an occasional surge crescendo (ported from pillar_of_fire.gd).
            float phase = f.Seed;
            float fast = _noise.GetNoise1D(_t * 9f + phase * 130f);
            float slow = _noise.GetNoise1D(_t * 1.7f + 500f + phase * 70f);
            float surge = f.SurgeStr * Mathf.SmoothStep(0.55f, 0.95f, slow);
            float flicker = Mathf.Clamp(1f + f.FlickerStr * fast + f.SwellStr * slow + surge, 0.45f, 2f);
            f.Tick(_t, flicker, slow, nightBoost, lightOn, emit, wind);
        }
    }

    private void OnBlockChanged(Vector3I pos, int oldId, int newId, int cause)
    {
        var nb = BlockRegistry.Get((ushort)newId);
        var ob = BlockRegistry.Get((ushort)oldId);
        if (nb.Name == "altar_fire" && ob.Name != "altar_fire")
            AddFire(new Vector3(pos.X + 0.5f, pos.Y + 1f, pos.Z + 0.5f), FireKind.Altar, cell: pos);
        else if (ob.Name == "altar_fire" && nb.Name != "altar_fire")
            RemoveFireAt(pos);
    }
}
