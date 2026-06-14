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
