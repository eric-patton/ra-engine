using Godot;

namespace RAEngine.Core;

/// <summary>The kinds of one-shot particle burst the game emits.</summary>
public enum FxKind { Poof, Debris, Splash, Sparkle, Dust }

/// <summary>App-wide visual "juice": a small pool of reusable one-shot particle
/// emitters plus a static facade for screen effects (camera shake, full-screen
/// flash, hit-stop). A single persistent instance lives at the game root, next to
/// <see cref="AudioManager"/>. Like the audio facade, every static call is a safe
/// no-op when there is no instance (headless logic tests never set FX up), so
/// gameplay code can fire effects in one line without null checks.
///
/// Particles are pooled (round-robin) so rapid mining/breaking never allocates.
/// Screen effects are delegated to handlers a session registers in
/// <c>OnShake/OnFlash</c> (the camera shaker and the HUD overlay) and clears on
/// teardown, so the facade never holds a freed node.</summary>
public sealed partial class Fx : Node3D
{
    public static Fx Instance { get; private set; }

    /// <summary>Camera-shake handler (trauma 0..1), registered by the session.</summary>
    public static System.Action<float> OnShake;
    /// <summary>Full-screen flash handler (colour, amount 0..1), registered by the HUD.</summary>
    public static System.Action<Color, float> OnFlash;

    private const int PoolSize = 16;
    private GpuParticles3D[] _pool;
    private int _next;
    private Mesh _quad, _cube;
    private bool _hitStopped;

    // Ripple rings (B13): a small pool of flat decals on the water surface, each driven
    // by its own ShaderMaterial `prog` uniform so several rings can overlap independently.
    private const int RingPool = 10;
    private MeshInstance3D[] _rings;
    private ShaderMaterial[] _ringMats;
    private float[] _ringElapsed, _ringLife; // _ringLife <= 0 means the slot is idle
    private int _ringNext;
    private static Shader _rippleShader;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // effects keep playing even while paused

        // A small unshaded billboard for puffs/splashes/sparkles and a tiny lit cube
        // for break debris. Both use vertex colour as albedo so a per-burst tint
        // shows through the shared pooled emitters.
        _quad = MakeQuad();
        _cube = MakeCube();

        _pool = new GpuParticles3D[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new GpuParticles3D
            {
                Name = $"Fx{i}",
                OneShot = true,
                Emitting = false,
                Explosiveness = 1f,
                Amount = 16,
                Lifetime = 0.7,
                ProcessMaterial = new ParticleProcessMaterial(),
                DrawPass1 = _quad,
            };
            AddChild(p);
            _pool[i] = p;
        }

        BuildRingPool();
    }

    /// <summary>A pool of flat ripple-ring decals (PlaneMesh + ripple.gdshader). Each ring
    /// keeps its own material so overlapping rings animate independently. Skipped silently
    /// if the shader is missing, so rings simply don't appear rather than crashing.</summary>
    private void BuildRingPool()
    {
        _rippleShader ??= GD.Load<Shader>("res://assets/shaders/ripple.gdshader");
        _rings = new MeshInstance3D[RingPool];
        _ringMats = new ShaderMaterial[RingPool];
        _ringElapsed = new float[RingPool];
        _ringLife = new float[RingPool];
        if (_rippleShader == null) return;

        var plane = new PlaneMesh { Size = new Vector2(2f, 2f) }; // unit ring radius = 1 m at scale 1
        for (int i = 0; i < RingPool; i++)
        {
            var mat = new ShaderMaterial { Shader = _rippleShader };
            var mi = new MeshInstance3D
            {
                Name = $"Ring{i}",
                Mesh = plane,
                MaterialOverride = mat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 4f,
            };
            AddChild(mi);
            _rings[i] = mi;
            _ringMats[i] = mat;
        }
    }

    public override void _Process(double delta)
    {
        if (_rings == null) return;
        for (int i = 0; i < RingPool; i++)
        {
            if (_ringLife[i] <= 0f) continue;
            _ringElapsed[i] += (float)delta;
            float prog = _ringElapsed[i] / _ringLife[i];
            if (prog >= 1f) { _ringLife[i] = 0f; _rings[i].Visible = false; continue; }
            _ringMats[i].SetShaderParameter("prog", prog);
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ---- static facade (no-ops when there is no instance) -----------------

    /// <summary>Emit a one-shot burst of <paramref name="count"/> particles (0 =
    /// the kind's default) at <paramref name="pos"/>, tinted <paramref name="tint"/>.</summary>
    public static void Burst(Vector3 pos, FxKind kind, Color tint, int count = 0) =>
        Instance?.DoBurst(pos, kind, tint, count);

    /// <summary>Bloom one expanding ripple ring (B13) flat on a surface at <paramref name="pos"/>
    /// — water entry, a thrown stone's impact, rain later. <paramref name="radius"/> is the
    /// final ring radius in metres; it fades out over <paramref name="life"/> seconds.</summary>
    public static void Ring(Vector3 pos, Color tint, float radius = 1.6f, float life = 0.7f) =>
        Instance?.DoRing(pos, tint, radius, life);

    /// <summary>Add camera-shake trauma (0..1). Forwarded to the session's shaker.</summary>
    public static void Shake(float trauma) => OnShake?.Invoke(trauma);

    /// <summary>Full-screen colour flash (amount 0..1). Forwarded to the HUD overlay.</summary>
    public static void Flash(Color color, float amount) => OnFlash?.Invoke(color, amount);

    /// <summary>Briefly slow time for a punchy "impact" beat, then restore. Uses an
    /// unscaled timer so it always recovers regardless of the slow-down.</summary>
    public static void HitStop(float seconds = 0.06f) => Instance?.DoHitStop(seconds);

    // ---- particles --------------------------------------------------------

    private void DoBurst(Vector3 pos, FxKind kind, Color tint, int count)
    {
        var p = _pool[_next];
        _next = (_next + 1) % PoolSize;
        var m = (ParticleProcessMaterial)p.ProcessMaterial;

        switch (kind)
        {
            case FxKind.Debris: // chunks of the broken block: arc up, spin, fall
                p.DrawPass1 = _cube;
                m.Direction = Vector3.Up; m.Spread = 70f;
                m.InitialVelocityMin = 2.0f; m.InitialVelocityMax = 4.5f;
                m.Gravity = new Vector3(0, -14f, 0);
                m.ScaleMin = 0.6f; m.ScaleMax = 1.2f;
                m.AngularVelocityMin = -320f; m.AngularVelocityMax = 320f;
                p.Lifetime = 0.7; p.Amount = count > 0 ? count : 14;
                break;
            case FxKind.Poof: // soft dust cloud when placing a block
                p.DrawPass1 = _quad;
                m.Direction = Vector3.Up; m.Spread = 80f;
                m.InitialVelocityMin = 1.2f; m.InitialVelocityMax = 3.0f;
                m.Gravity = new Vector3(0, -2f, 0);
                m.ScaleMin = 0.9f; m.ScaleMax = 2.2f;
                m.AngularVelocityMin = -60f; m.AngularVelocityMax = 60f;
                p.Lifetime = 0.6; p.Amount = count > 0 ? count : 12;
                break;
            case FxKind.Splash: // droplets thrown up on entering water
                p.DrawPass1 = _quad;
                m.Direction = Vector3.Up; m.Spread = 35f;
                m.InitialVelocityMin = 3.0f; m.InitialVelocityMax = 6.5f;
                m.Gravity = new Vector3(0, -16f, 0);
                m.ScaleMin = 0.4f; m.ScaleMax = 1.1f;
                m.AngularVelocityMin = 0f; m.AngularVelocityMax = 0f;
                p.Lifetime = 0.8; p.Amount = count > 0 ? count : 24;
                break;
            case FxKind.Sparkle: // celebratory floaty motes
                p.DrawPass1 = _quad;
                m.Direction = Vector3.Up; m.Spread = 90f;
                m.InitialVelocityMin = 0.4f; m.InitialVelocityMax = 1.8f;
                m.Gravity = new Vector3(0, 0.5f, 0);
                m.ScaleMin = 0.5f; m.ScaleMax = 1.3f;
                m.AngularVelocityMin = -120f; m.AngularVelocityMax = 120f;
                p.Lifetime = 0.9; p.Amount = count > 0 ? count : 16;
                break;
            default: // Dust: faint kicked-up motes (footsteps etc.)
                p.DrawPass1 = _quad;
                m.Direction = Vector3.Up; m.Spread = 50f;
                m.InitialVelocityMin = 0.3f; m.InitialVelocityMax = 1.0f;
                m.Gravity = new Vector3(0, -1.2f, 0);
                m.ScaleMin = 0.6f; m.ScaleMax = 1.4f;
                m.AngularVelocityMin = 0f; m.AngularVelocityMax = 0f;
                p.Lifetime = 0.6; p.Amount = count > 0 ? count : 8;
                break;
        }
        m.Color = tint;
        p.GlobalPosition = pos;
        p.Restart(); // replay the one-shot from t=0 (also sets Emitting = true)
    }

    private void DoRing(Vector3 pos, Color tint, float radius, float life)
    {
        if (_rings == null || _rippleShader == null || life <= 0f) return;
        int i = _ringNext;
        _ringNext = (_ringNext + 1) % RingPool;
        var ring = _rings[i];
        ring.GlobalPosition = pos + new Vector3(0f, 0.02f, 0f); // just above the surface, no z-fight
        ring.Scale = new Vector3(radius, 1f, radius);
        _ringMats[i].SetShaderParameter("ring_color", tint);
        _ringMats[i].SetShaderParameter("prog", 0f);
        ring.Visible = true;
        _ringElapsed[i] = 0f;
        _ringLife[i] = life;
    }

    private void DoHitStop(float seconds)
    {
        if (_hitStopped) return;
        _hitStopped = true;
        Engine.TimeScale = 0.05f;
        // ignoreTimeScale = true so the restore fires after real seconds, not slowed ones.
        GetTree().CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true)
            .Timeout += () => { Engine.TimeScale = 1f; _hitStopped = false; };
    }

    // ---- pooled draw meshes ----------------------------------------------

    private static Mesh MakeQuad()
    {
        var mesh = new QuadMesh { Size = new Vector2(0.16f, 0.16f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return mesh;
    }

    private static Mesh MakeCube()
    {
        var mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, // tinted per-burst by the broken block's colour
            Roughness = 0.9f,
        });
        return mesh;
    }
}
