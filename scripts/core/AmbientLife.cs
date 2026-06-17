using System;
using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>The "ambient life" conductor — the FX-roadmap <b>D-arch AmbientLifeDirector</b>: one
/// component that reads the biome, time of day, weather and wind around the player and drives every
/// living-world effect from it. Craft here is <i>restraint</i> — each effect's density is tuned
/// continuously from context and most sit near zero until the player is somewhere they belong, so
/// the world feels alive without clutter.
///
/// It hosts two kinds of effect:
///  • <b>Particle fields</b> (player-following <see cref="GpuParticles3D"/>, the same pattern as the
///    motes/fireflies in <see cref="EnvironmentController"/>): drifting <b>leaves</b> (D1) and
///    <b>blossom petals</b> (D8) near trees, light-catching <b>pollen</b> (D2) and wind-borne
///    <b>dandelion fluff</b> (D7) in daytime meadows.
///  • <b>Creatures</b> (self-moving billboard quads with a 2-frame wing/tail flap, modelled on the
///    Fire/FireController "home + sin(t)" kernel): <b>butterflies</b> (D3) fluttering in meadows,
///    <b>birds</b> (D4) crossing the sky (plus a scriptable <see cref="ReleaseDove"/>), and
///    <b>fish</b> (D9) darting under nearby water with the odd splashing jump.
///
/// Built once per session in <c>GameSession.Setup</c> and parented to the session; it follows the
/// player. It needs no <see cref="TerrainGenerator"/> to run — without one (showcase, lessons) it
/// reads context straight from nearby world blocks (leaves → trees, water → fish); with one (the
/// streamed sandbox) it also keys off the climate biome via <see cref="SetGenerator"/>.</summary>
public sealed partial class AmbientLifeDirector : Node3D
{
    // ---- wired by the owner before AddChild ----
    public Node3D Player;
    public EnvironmentController Env;
    public VoxelWorld World;
    private TerrainGenerator _gen;                 // optional: only the streamed sandbox has one
    public void SetGenerator(TerrainGenerator gen) => _gen = gen;

    /// <summary>Global density multiplier (showcase hotkey: Off/Sparse/Normal/Lush). 1 = normal.</summary>
    private float _densityScale = 1f;
    public float DensityScale => _densityScale;
    public void SetDensityScale(float s) => _densityScale = Mathf.Clamp(s, 0f, 2f);

    // ---- particle fields ----
    private GpuParticles3D _leaves, _pollen, _dandelion, _petals;
    private ParticleProcessMaterial _leavesMat, _dandelionMat, _petalsMat; // wind-driven: gravity re-aimed each frame

    // ---- creatures (populations capped + recomputed on the throttled context tick) ----
    private readonly List<Flyer> _birds = new();
    private readonly List<Flyer> _butterflies = new();
    private readonly List<Flyer> _fish = new();
    private readonly List<Flyer> _doves = new();   // scripted, one-shot; NEVER population-capped

    // ---- context, refreshed on a throttle (cheap pure queries) ----
    private float _ctxTimer;
    private Biome _biome = Biome.Plains;
    private bool _nearTrees;
    private bool _overWater;
    private float _waterSurfaceY;
    private Vector3 _waterPoint;

    public override void _Ready()
    {
        BuildFields();
    }

    private bool EnvOk => Env != null && GodotObject.IsInstanceValid(Env);
    private bool WorldOk => World != null && GodotObject.IsInstanceValid(World);

    // =====================================================================================
    //  Particle fields
    // =====================================================================================

    private void BuildFields()
    {
        // Leaves (D1): tumbling green→gold blades that fall from canopy height and drift on the wind.
        _leavesMat = FieldMat(emit: new Vector3(15f, 3f, 15f), grav: -1.1f, velMin: 0.15f, velMax: 0.6f,
            scaleMin: 0.55f, scaleMax: 1.25f, angVel: 140f, turb: 0.7f,
            ramp: Ramp((0f, C(0.42f, 0.60f, 0.16f, 0f)), (0.15f, C(0.42f, 0.60f, 0.16f, 1f)),
                       (0.7f, C(0.78f, 0.62f, 0.20f, 1f)), (1f, C(0.78f, 0.62f, 0.20f, 0f))));
        _leaves = MakeField("AmbientLeaves", BillboardQuad(0.20f, AmbientTex.Leaf(), Colors.White, false, true,
            BaseMaterial3D.BillboardModeEnum.Particles), _leavesMat, amount: 80, lifetime: 5.5f, preprocess: 2.5f);
        AddChild(_leaves);

        // Blossom petals (D8): soft pink-white petals shed near trees / in forests, gentler than leaves.
        _petalsMat = FieldMat(emit: new Vector3(13f, 3f, 13f), grav: -0.7f, velMin: 0.1f, velMax: 0.4f,
            scaleMin: 0.5f, scaleMax: 1.0f, angVel: 90f, turb: 0.6f,
            ramp: Ramp((0f, C(1f, 0.86f, 0.92f, 0f)), (0.2f, C(1f, 0.86f, 0.92f, 1f)),
                       (0.75f, C(0.98f, 0.74f, 0.86f, 1f)), (1f, C(0.98f, 0.74f, 0.86f, 0f))));
        _petals = MakeField("AmbientPetals", BillboardQuad(0.16f, AmbientTex.Petal(), Colors.White, false, true,
            BaseMaterial3D.BillboardModeEnum.Particles), _petalsMat, amount: 64, lifetime: 6f, preprocess: 2.5f);
        AddChild(_petals);

        // Pollen (D2): tiny light-catching motes, additive so the Environment glow blooms them in sunbeams.
        var pollenMat = FieldMat(emit: new Vector3(12f, 5f, 12f), grav: 0.03f, velMin: 0.04f, velMax: 0.3f,
            scaleMin: 0.4f, scaleMax: 0.9f, angVel: 0f, turb: 0.9f,
            ramp: Ramp((0f, C(1f, 0.95f, 0.7f, 0f)), (0.4f, C(1f, 0.96f, 0.78f, 0.9f)), (1f, C(1f, 0.95f, 0.7f, 0f))));
        _pollen = MakeField("AmbientPollen", BillboardQuad(0.05f, Fx.SoftDot(), Colors.White, true, true,
            BaseMaterial3D.BillboardModeEnum.Particles), pollenMat, amount: 70, lifetime: 6f, preprocess: 3f);
        AddChild(_pollen);

        // Dandelion fluff (D7): white seed tufts that loft and stream on the wind in open meadows.
        _dandelionMat = FieldMat(emit: new Vector3(12f, 2.5f, 12f), grav: -0.04f, velMin: 0.1f, velMax: 0.5f,
            scaleMin: 0.5f, scaleMax: 1.0f, angVel: 30f, turb: 1.3f,
            ramp: Ramp((0f, C(1f, 1f, 1f, 0f)), (0.25f, C(1f, 1f, 1f, 0.95f)), (1f, C(1f, 1f, 1f, 0f))));
        _dandelion = MakeField("AmbientDandelion", BillboardQuad(0.07f, AmbientTex.Fluff(), Colors.White, false, true,
            BaseMaterial3D.BillboardModeEnum.Particles), _dandelionMat, amount: 40, lifetime: 7f, preprocess: 3f);
        AddChild(_dandelion);
    }

    private static GpuParticles3D MakeField(string name, Mesh mesh, ParticleProcessMaterial mat,
        int amount, float lifetime, float preprocess) => new()
    {
        Name = name,
        Amount = amount,
        Lifetime = lifetime,
        Preprocess = preprocess,
        Emitting = false,                 // gated on per-frame density (AmountRatio starts at 0)
        OneShot = false,
        Explosiveness = 0f,
        LocalCoords = false,              // world-anchored particles; moving the node only moves the spawn box
        ProcessMaterial = mat,
        DrawPass1 = mesh,
        AmountRatio = 0f,
        VisibilityAabb = new Aabb(new Vector3(-28, -28, -28), new Vector3(56, 56, 56)),
    };

    private static ParticleProcessMaterial FieldMat(Vector3 emit, float grav, float velMin, float velMax,
        float scaleMin, float scaleMax, float angVel, float turb, GradientTexture1D ramp) => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
        EmissionBoxExtents = emit,
        Direction = Vector3.Up,
        Spread = 180f,
        Gravity = new Vector3(0f, grav, 0f),
        InitialVelocityMin = velMin,
        InitialVelocityMax = velMax,
        ScaleMin = scaleMin,
        ScaleMax = scaleMax,
        AngularVelocityMin = -angVel,
        AngularVelocityMax = angVel,
        ColorRamp = ramp,
        TurbulenceEnabled = true,
        TurbulenceNoiseStrength = turb,
        TurbulenceNoiseScale = 1.3f,
    };

    // =====================================================================================
    //  Per-frame conducting
    // =====================================================================================

    public override void _Process(double delta)
    {
        if (!EnvOk || Player == null || !GodotObject.IsInstanceValid(Player)) return;
        float dt = (float)delta;
        float t = (float)Time.GetTicksMsec() * 0.001f;
        Vector3 pp = Player.GlobalPosition;

        // Refresh the cheap context queries — AND the creature populations — a few times a second
        // rather than every frame, so counts don't thrash spawn/free as the day factor sweeps.
        _ctxTimer -= dt;
        if (_ctxTimer <= 0f) { RefreshContext(pp); _ctxTimer = 0.4f; }

        float day = Env.DayFactor;                       // 0 night … 1 midday
        float windN = Mathf.Clamp(Env.Wind.Length() / 8f, 0f, 1f);
        bool meadow = _biome is Biome.Plains or Biome.Forest;
        float dry = Env.Weather == Weather.Clear ? 1f : 0.35f; // pollen/butterflies hush in rain/snow
        float ds = _densityScale;

        // --- particle-field densities (target AmountRatio, eased smoothly every frame) ---
        float leavesT = _nearTrees ? (0.45f + 0.45f * windN) * Mathf.Lerp(0.45f, 1f, day) : 0f;
        float petalsT = (_nearTrees && (_biome == Biome.Forest || meadow)) ? 0.35f * Mathf.Lerp(0.3f, 1f, day) * Mathf.Lerp(0.6f, 1f, dry) : 0f;
        float pollenT = meadow ? day * dry * 0.9f : day * dry * 0.2f;
        float dandyT  = (_biome == Biome.Plains) ? day * dry * (0.25f + 0.6f * windN) : 0f;

        EaseField(_leaves, leavesT * ds, dt);
        EaseField(_petals, petalsT * ds, dt);
        EaseField(_pollen, pollenT * ds, dt);
        EaseField(_dandelion, dandyT * ds, dt);

        // Follow the player and re-aim the wind-blown fields' gravity downstream.
        FollowField(_leaves, pp, 6.5f);
        FollowField(_petals, pp, 5.5f);
        FollowField(_pollen, pp, 2.2f);
        FollowField(_dandelion, pp, 1.6f);
        Vector3 wind = new(Env.Wind.X, 0f, Env.Wind.Y);
        if (_leavesMat != null) _leavesMat.Gravity = new Vector3(wind.X * 0.12f, -1.1f, wind.Z * 0.12f);
        if (_petalsMat != null) _petalsMat.Gravity = new Vector3(wind.X * 0.10f, -0.7f, wind.Z * 0.10f);
        if (_dandelionMat != null) _dandelionMat.Gravity = new Vector3(wind.X * 0.22f, -0.04f, wind.Z * 0.22f);

        // --- creature positions (counts are adjusted on the throttled tick, in RefreshContext) ---
        for (int i = 0; i < _birds.Count; i++) UpdateBird(_birds[i], pp, t, dt);
        for (int i = 0; i < _butterflies.Count; i++) UpdateButterfly(_butterflies[i], pp, t);
        for (int i = 0; i < _fish.Count; i++) UpdateFish(_fish[i], t, dt);
        UpdateDoves(t, dt);
    }

    private static void EaseField(GpuParticles3D p, float target, float dt)
    {
        if (p == null) return;
        p.AmountRatio = Mathf.MoveToward(p.AmountRatio, Mathf.Clamp(target, 0f, 1f), 1.5f * dt);
        bool on = p.AmountRatio > 0.01f;
        if (p.Emitting != on) p.Emitting = on;
    }

    private static void FollowField(GpuParticles3D p, Vector3 player, float yOff)
    {
        if (p != null) p.GlobalPosition = player + new Vector3(0f, yOff, 0f);
    }

    /// <summary>Cheap, throttled context probes plus the creature population update: the climate biome
    /// (if a generator is present), nearby trees (→ leaves/petals) and a nearby water surface (→ fish),
    /// read straight from the world blocks so it works in the showcase and lessons that have no
    /// generator; then the bird/butterfly/fish counts are recomputed and applied here (not every
    /// frame) so populations don't thrash spawn/free as the day factor sweeps through dawn/dusk.</summary>
    private void RefreshContext(Vector3 pp)
    {
        int px = Mathf.FloorToInt(pp.X), py = Mathf.FloorToInt(pp.Y), pz = Mathf.FloorToInt(pp.Z);

        _biome = _gen != null ? _gen.BiomeAt(px, pz, _gen.SurfaceHeight(px, pz)) : Biome.Plains;

        // Trees nearby? (leaves blocks within a coarse radius, or simply the forest climate).
        _nearTrees = _biome == Biome.Forest;
        if (!_nearTrees && WorldOk)
        {
            ushort leaf = BlockRegistry.IdOf("leaves");
            ushort olive = BlockRegistry.IdOf("olive_leaves");
            for (int dx = -12; dx <= 12 && !_nearTrees; dx += 6)
                for (int dz = -12; dz <= 12 && !_nearTrees; dz += 6)
                    for (int y = py; y <= py + 12; y++)
                    {
                        ushort id = World.GetBlockId(px + dx, y, pz + dz);
                        if (id == leaf || (olive != 0 && id == olive)) { _nearTrees = true; break; }
                    }
        }

        // Open water surface nearby? Topmost liquid cell whose cover above is open (air or a non-solid
        // decoration like reeds), near the player. Testing !IsSolid (not == air) so a decorated pond
        // surface still counts.
        _overWater = false;
        if (WorldOk)
        {
            float best = float.NegativeInfinity;
            for (int dx = -8; dx <= 8; dx += 4)
                for (int dz = -8; dz <= 8; dz += 4)
                {
                    int x = px + dx, z = pz + dz;
                    for (int y = py + 4; y >= py - 6; y--)
                        if (World.GetBlock(x, y, z).IsLiquid)
                        {
                            var above = new Vector3I(x, y + 1, z);
                            if (!World.IsSolid(above) && !World.GetBlock(above).IsLiquid && y > best)
                            {
                                best = y;
                                _waterPoint = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
                            }
                            break; // topmost liquid in this column found; deeper cells aren't the surface
                        }
                }
            if (!float.IsNegativeInfinity(best)) { _overWater = true; _waterSurfaceY = best + 1f; }
        }

        // --- populations (recomputed here, on the throttle, NOT every frame) ---
        float day = Env.DayFactor;
        float dry = Env.Weather == Weather.Clear ? 1f : 0.35f;
        bool meadow = _biome is Biome.Plains or Biome.Forest;
        float ds = _densityScale;
        int birdsWant = Mathf.RoundToInt(4.5f * day * dry * ds);   // scales to 0 at night
        int fliesWant = meadow ? Mathf.RoundToInt(6f * day * dry * ds) : 0;
        int fishWant  = _overWater ? Mathf.RoundToInt(5f * ds) : 0;
        Resize(_birds, Mathf.Min(birdsWant, 8), SpawnBird);
        Resize(_butterflies, Mathf.Min(fliesWant, 8), SpawnButterfly);
        Resize(_fish, Mathf.Min(fishWant, 6), SpawnFish);
    }

    // =====================================================================================
    //  Creatures — billboard quads with a 2-frame flap (the Fire "home + sin(t)" kernel)
    // =====================================================================================

    private sealed class Flyer
    {
        public MeshInstance3D Node;
        public StandardMaterial3D Mat;
        public float Seed, FlapRate;
        // birds/doves: a crossing/climbing segment + bob
        public Vector3 From, To;
        public float T, Speed, BobAmp, BobFreq;
        // butterflies/fish: a roaming offset around the player / water point
        public Vector3 Offset;
        public float Wander;
        // fish jump state
        public float JumpTimer; public bool Jumping; public float JumpT; public bool SplashedUp, SplashedDown;
    }

    private Flyer MakeFlyer(float size, Texture2D tex, Color tint, int frames, float flap, bool upright)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = tex,
            AlbedoColor = tint,
            BillboardMode = upright ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.FixedY,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Uv1Scale = new Vector3(1f / frames, 1f, 1f),
        };
        var mesh = new QuadMesh { Size = new Vector2(size, size) };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        return new Flyer { Node = node, Mat = mat, Seed = GD.Randf(), FlapRate = flap };
    }

    private static void Flap(Flyer f, float t, int frames)
    {
        int frame = (int)(Mathf.PosMod(t * f.FlapRate + f.Seed * 7f, 1f) * frames);
        if (frame >= frames) frame = frames - 1;
        float ox = frame / (float)frames;
        if (f.Mat.Uv1Offset.X != ox) f.Mat.Uv1Offset = new Vector3(ox, 0f, 0f); // only marshal on change
    }

    private void Resize(List<Flyer> list, int want, Func<Flyer> spawn)
    {
        while (list.Count < want)
        {
            var f = spawn();
            AddChild(f.Node);
            list.Add(f);
        }
        while (list.Count > want)
        {
            var f = list[^1];
            list.RemoveAt(list.Count - 1);
            f.Node.QueueFree();
        }
    }

    // ---- birds (D4) ----
    private Flyer SpawnBird()
    {
        var f = MakeFlyer(2.6f, AmbientTex.Bird(), new Color(0.13f, 0.13f, 0.16f), 2, 5.5f, upright: true);
        AimBird(f, Player.GlobalPosition, GD.Randf());
        return f;
    }

    private void AimBird(Flyer f, Vector3 pp, float startT)
    {
        float ang = GD.Randf() * Mathf.Tau;
        Vector3 dir = new(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        float r = 45f + GD.Randf() * 25f;
        float h = pp.Y + 18f + GD.Randf() * 12f;
        f.From = new Vector3(pp.X - dir.X * r, h, pp.Z - dir.Z * r);
        f.To = new Vector3(pp.X + dir.X * r, h + (GD.Randf() - 0.5f) * 6f, pp.Z + dir.Z * r);
        f.T = startT;
        f.Speed = 0.05f + GD.Randf() * 0.05f;   // ~10–20 s to cross
        f.BobAmp = 0.6f + GD.Randf() * 0.8f;
        f.BobFreq = 1.2f + GD.Randf() * 1.2f;
        f.Seed = GD.Randf();
    }

    private void UpdateBird(Flyer f, Vector3 pp, float t, float dt)
    {
        f.T += f.Speed * dt;
        if (f.T >= 1f) { AimBird(f, pp, 0f); return; }
        Vector3 p = f.From.Lerp(f.To, f.T);
        p.Y += Mathf.Sin(t * f.BobFreq + f.Seed * 6.28f) * f.BobAmp;
        f.Node.GlobalPosition = p;
        Flap(f, t, 2);
    }

    /// <summary>Release one or more doves rising from <paramref name="from"/> toward <paramref name="to"/>
    /// — a scriptable beat for lessons (Noah's ark, the baptism). Doves live in their OWN list (never
    /// touched by the bird population cap) and free themselves once they finish the climb, so they
    /// can't be culled mid-flight or recycled into ordinary sky birds.</summary>
    public void ReleaseDove(Vector3 from, Vector3 to, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var f = MakeFlyer(0.9f, AmbientTex.Bird(), new Color(1f, 1f, 1f), 2, 7f, upright: true);
            f.From = from + new Vector3((GD.Randf() - 0.5f) * 1.5f, 0f, (GD.Randf() - 0.5f) * 1.5f);
            f.To = to + new Vector3((GD.Randf() - 0.5f) * 6f, 6f + GD.Randf() * 6f, (GD.Randf() - 0.5f) * 6f);
            f.T = 0f;
            f.Speed = 0.18f + GD.Randf() * 0.06f;
            f.BobAmp = 0.3f; f.BobFreq = 2.5f; f.Seed = GD.Randf();
            AddChild(f.Node);
            _doves.Add(f);
        }
    }

    private void UpdateDoves(float t, float dt)
    {
        for (int i = _doves.Count - 1; i >= 0; i--)
        {
            var f = _doves[i];
            f.T += f.Speed * dt;
            if (f.T >= 1f) { f.Node.QueueFree(); _doves.RemoveAt(i); continue; } // one-shot: free at the top
            Vector3 p = f.From.Lerp(f.To, f.T);
            p.Y += Mathf.Sin(t * f.BobFreq + f.Seed * 6.28f) * f.BobAmp;
            f.Node.GlobalPosition = p;
            Flap(f, t, 2);
        }
    }

    // ---- butterflies (D3) ----
    private static readonly Color[] ButterflyWings =
    {
        new(0.95f, 0.70f, 0.45f), new(0.85f, 0.82f, 0.95f), new(0.95f, 0.90f, 0.60f), new(0.92f, 0.68f, 0.74f),
    };

    private Flyer SpawnButterfly()
    {
        var tint = ButterflyWings[Mathf.Min((int)(GD.Randf() * ButterflyWings.Length), ButterflyWings.Length - 1)];
        var f = MakeFlyer(0.42f, AmbientTex.Butterfly(), tint, 2, 9f, upright: true);
        float ang = GD.Randf() * Mathf.Tau, rad = 2f + GD.Randf() * 7f;
        f.Offset = new Vector3(Mathf.Cos(ang) * rad, 0.8f + GD.Randf() * 1.8f, Mathf.Sin(ang) * rad);
        f.Wander = 1.2f + GD.Randf() * 1.3f;
        f.Seed = GD.Randf();
        return f;
    }

    private void UpdateButterfly(Flyer f, Vector3 pp, float t)
    {
        float w = t * 0.8f + f.Seed * 12f;
        Vector3 p = pp + f.Offset + new Vector3(
            Mathf.Sin(w * 1.3f) * f.Wander,
            Mathf.Sin(w * 2.3f) * 0.4f,
            Mathf.Cos(w) * f.Wander);
        f.Node.GlobalPosition = p;
        Flap(f, t, 2);
    }

    // ---- fish (D9) ----
    private Flyer SpawnFish()
    {
        var f = MakeFlyer(0.5f, AmbientTex.Fish(), new Color(0.78f, 0.86f, 0.95f), 2, 3.5f, upright: true);
        float ang = GD.Randf() * Mathf.Tau, rad = GD.Randf() * 3f;
        f.Offset = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
        f.Wander = 0.6f + GD.Randf() * 0.8f;
        f.JumpTimer = 3f + GD.Randf() * 6f;
        f.Seed = GD.Randf();
        return f;
    }

    private void UpdateFish(Flyer f, float t, float dt)
    {
        if (!_overWater) { f.Node.Visible = false; return; }
        f.Node.Visible = true;
        float w = t * 1.1f + f.Seed * 9f;
        Vector3 home = _waterPoint + f.Offset;
        float x = home.X + Mathf.Sin(w) * f.Wander;
        float z = home.Z + Mathf.Cos(w * 0.8f) * f.Wander;
        float baseY = _waterSurfaceY - 0.6f;        // cruise clearly under the surface

        if (f.Jumping)
        {
            f.JumpT += dt * 1.4f;
            float arc = Mathf.Sin(f.JumpT * Mathf.Pi) * 0.7f;       // peaks ~0.7 m above the surface
            f.Node.GlobalPosition = new Vector3(x, _waterSurfaceY - 0.1f + arc, z);
            // Fire the splashes off the JUMP PROGRESS, not the float y-crossing, so a frame hitch
            // (e.g. the windowed test's timer stalls) can't swallow either splash. Ripple on the surface.
            if (!f.SplashedUp && f.JumpT >= 0.05f) { Splash(new Vector3(x, _waterSurfaceY, z)); f.SplashedUp = true; }
            if (!f.SplashedDown && f.JumpT >= 0.95f) { Splash(new Vector3(x, _waterSurfaceY, z)); f.SplashedDown = true; }
            if (f.JumpT >= 1f) { f.Jumping = false; f.JumpTimer = 4f + GD.Randf() * 6f; }
        }
        else
        {
            f.Node.GlobalPosition = new Vector3(x, baseY, z);
            f.JumpTimer -= dt;
            if (f.JumpTimer <= 0f) { f.Jumping = true; f.JumpT = 0f; f.SplashedUp = f.SplashedDown = false; }
        }
        Flap(f, t, 2);
    }

    private void Splash(Vector3 pos)
    {
        Fx.Burst(pos, FxKind.Splash, new Color(0.72f, 0.86f, 1f), 8);
        if (WorldOk) World.AddRipple(pos, 0.45f);
    }

    // =====================================================================================
    //  Helpers
    // =====================================================================================

    private static Color C(float r, float g, float b, float a) => new(r, g, b, a);

    private static GradientTexture1D Ramp(params (float off, Color c)[] stops)
    {
        var offs = new float[stops.Length];
        var cols = new Color[stops.Length];
        for (int i = 0; i < stops.Length; i++) { offs[i] = stops[i].off; cols[i] = stops[i].c; }
        return new GradientTexture1D { Gradient = new Gradient { Offsets = offs, Colors = cols } };
    }

    private static Mesh BillboardQuad(float size, Texture2D tex, Color albedo, bool additive, bool vertexTint,
        BaseMaterial3D.BillboardModeEnum mode)
    {
        var mesh = new QuadMesh { Size = new Vector2(size, size) };
        var sm = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = tex,
            AlbedoColor = albedo,
            VertexColorUseAsAlbedo = vertexTint,
            BillboardMode = mode,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        if (additive) sm.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        mesh.SurfaceSetMaterial(0, sm);
        return mesh;
    }
}

/// <summary>Tiny procedurally-generated billboard textures for the ambient creatures and fields, so
/// the batch ships no PNGs. All are white-on-transparent (tinted at runtime) except the bird, which
/// bakes a dark silhouette. Cached after first build (immutable, shared across all sessions).</summary>
internal static class AmbientTex
{
    private static Texture2D _leaf, _petal, _fluff, _butterfly, _bird, _fish;

    private static ImageTexture Bake(int w, int h, Action<Image> draw)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(1f, 1f, 1f, 0f));
        draw(img);
        return ImageTexture.CreateFromImage(img);
    }

    // White ellipse leaf with a faint midrib (vertex-tinted green/gold at runtime).
    public static Texture2D Leaf() => _leaf ??= Bake(12, 12, img =>
    {
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                float u = (x - 5.5f) / 4.0f, v = (y - 5.5f) / 5.5f;
                if (u * u + v * v <= 1f)
                {
                    float rib = Mathf.Abs(u) < 0.14f ? 0.78f : 1f;
                    img.SetPixel(x, y, new Color(rib, rib, rib, 1f));
                }
            }
    });

    // White teardrop petal.
    public static Texture2D Petal() => _petal ??= Bake(12, 12, img =>
    {
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                float u = (x - 5.5f) / 4.2f, v = (y - 4.5f) / 5.6f;
                float fat = 1f + 0.35f * Mathf.Clamp(v, 0f, 1f);   // fuller toward the bottom
                if ((u / fat) * (u / fat) + v * v <= 1f) img.SetPixel(x, y, Colors.White);
            }
    });

    // Soft round seed tuft (a faint radial dot).
    public static Texture2D Fluff() => _fluff ??= Bake(12, 12, img =>
    {
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                float u = (x - 5.5f) / 5.5f, v = (y - 5.5f) / 5.5f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp(1f - d, 0f, 1f);
                a *= a;
                if (a > 0.02f) img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
    });

    // Two butterfly frames (wings spread / wings raised), white, tinted at runtime. 24×12 = 2×(12×12).
    public static Texture2D Butterfly() => _butterfly ??= Bake(24, 12, img =>
    {
        for (int frame = 0; frame < 2; frame++)
        {
            int ox = frame * 12;
            float wy = frame == 0 ? 4.0f : 2.6f;   // wing vertical radius shrinks when raised
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    float u = x - 5.5f, v = (y - 5.5f) / wy;
                    bool body = Mathf.Abs(u) < 0.8f && Mathf.Abs(y - 5.5f) < 4.5f;
                    float wu = (Mathf.Abs(u) - 1.2f) / 3.6f;
                    bool wing = Mathf.Abs(u) >= 1.2f && wu * wu + v * v <= 1f;
                    if (body || wing) img.SetPixel(ox + x, y, Colors.White);
                }
        }
    });

    // Two bird frames (wings up / wings level) as a dark gull chevron. 32×16 = 2×(16×16).
    public static Texture2D Bird() => _bird ??= Bake(32, 16, img =>
    {
        var col = new Color(0.12f, 0.12f, 0.15f, 1f);
        for (int frame = 0; frame < 2; frame++)
        {
            int ox = frame * 16;
            float slope = frame == 0 ? 0.85f : 0.35f;   // wings up vs. nearly level
            for (int x = 0; x < 16; x++)
            {
                float dx = Mathf.Abs(x - 7.5f);
                float wingY = 7.5f - slope * dx;        // chevron: tips rise away from centre
                for (int y = 0; y < 16; y++)
                    if (Mathf.Abs(y - wingY) <= 1.6f && dx <= 7.5f)
                        img.SetPixel(ox + x, y, col);
            }
        }
    });

    // Two fish frames (tail swung left / right), white, tinted at runtime. 24×12 = 2×(12×12).
    public static Texture2D Fish() => _fish ??= Bake(24, 12, img =>
    {
        for (int frame = 0; frame < 2; frame++)
        {
            int ox = frame * 12;
            float tail = frame == 0 ? -1.5f : 1.5f;
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    float u = (x - 6.5f) / 4.5f, v = (y - 5.5f) / 2.6f;
                    bool body = u * u + v * v <= 1f;                       // body toward the front
                    float ty = 5.5f + tail * (x / 11f);
                    bool fin = x <= 3 && Mathf.Abs(y - ty) <= (3.5f - x * 0.6f); // tail fan at the back
                    if (body || fin) img.SetPixel(ox + x, y, Colors.White);
                }
        }
    });
}
