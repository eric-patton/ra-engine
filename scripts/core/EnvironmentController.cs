using Godot;

namespace RAEngine.Core;

/// <summary>The single umbrella for sky, sun, moon and time-of-day. Drives a custom
/// sky shader (gradient + sun + stars + clouds), swings the sun and moon across the
/// sky on a configurable day/night cycle, and shifts light colour, energy and fog
/// to match. Lessons can pin a fixed time; the sandbox runs the cycle.</summary>
public sealed partial class EnvironmentController : Node3D
{
    /// <summary>0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
    public float TimeOfDay = 0.4f;
    public float DayLengthSeconds = 480f; // a full cycle in 8 minutes
    public bool CycleEnabled = true;

    private WorldEnvironment _we;
    private DirectionalLight3D _sun, _moon;
    private ShaderMaterial _skyMat;

    public DirectionalLight3D Sun => _sun;
    public DirectionalLight3D Moon => _moon;

    private static readonly Color SunWarm = new(1f, 0.5f, 0.25f);
    private static readonly Color SunNoon = new(1f, 0.96f, 0.88f);
    private static readonly Color MoonColor = new(0.55f, 0.66f, 0.95f);
    private static readonly Color HorizonDay = new(0.74f, 0.82f, 0.90f);
    private static readonly Color HorizonNight = new(0.05f, 0.06f, 0.13f);

    public override void _Ready()
    {
        _skyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/sky.gdshader") };
        var sky = new Sky { SkyMaterial = _skyMat, ProcessMode = Sky.ProcessModeEnum.Incremental, RadianceSize = Sky.RadianceSizeEnum.Size128 };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 5f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.5f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.1f,
            FogEnabled = true,
            FogLightColor = HorizonDay,
            FogDensity = 0.0012f,
        };
        env.SetGlowLevel(2, 1f);
        env.SetGlowLevel(3, 1f);

        _we = new WorldEnvironment { Environment = env, Name = "WorldEnvironment" };
        AddChild(_we);

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightColor = SunNoon,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        AddChild(_sun);

        _moon = new DirectionalLight3D
        {
            Name = "Moon",
            ShadowEnabled = true,
            LightColor = MoonColor,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        AddChild(_moon);

        Apply();
    }

    public override void _Process(double delta)
    {
        if (CycleEnabled)
        {
            TimeOfDay += (float)delta / DayLengthSeconds;
            TimeOfDay -= Mathf.Floor(TimeOfDay); // wrap to 0..1
            Apply();
        }
    }

    /// <summary>Pin the world to a fixed time and stop the cycle (used by lessons
    /// that want a specific mood — dawn, noon, dusk, night).</summary>
    public void SetFixedTime(float timeOfDay)
    {
        CycleEnabled = false;
        TimeOfDay = Mathf.PosMod(timeOfDay, 1f);
        if (_sun != null) Apply();
    }

    public void SetCycle(bool enabled, float dayLengthSeconds = 480f)
    {
        CycleEnabled = enabled;
        DayLengthSeconds = Mathf.Max(10f, dayLengthSeconds);
    }

    /// <summary>Push the current time-of-day into the sun, moon, sky and fog.</summary>
    private void Apply()
    {
        float ang = (TimeOfDay - 0.25f) * Mathf.Tau; // sunrise=0, noon=π/2
        float sunUp = Mathf.Sin(ang);
        Vector3 toSun = new Vector3(0.4f * Mathf.Cos(ang), sunUp, -0.55f).Normalized();
        OrientLight(_sun, toSun);

        float angM = (TimeOfDay - 0.75f) * Mathf.Tau;
        float moonUp = Mathf.Sin(angM);
        Vector3 toMoon = new Vector3(0.4f * Mathf.Cos(angM), moonUp, 0.55f).Normalized();
        OrientLight(_moon, toMoon);

        float dayFactor = Mathf.SmoothStep(-0.12f, 0.30f, sunUp);
        _sun.LightEnergy = dayFactor * 1.4f;
        _sun.LightColor = SunWarm.Lerp(SunNoon, Mathf.SmoothStep(0f, 0.35f, sunUp));
        _sun.Visible = dayFactor > 0.01f;

        float moonFactor = Mathf.SmoothStep(-0.1f, 0.30f, moonUp);
        _moon.LightEnergy = moonFactor * 0.35f;
        _moon.Visible = moonFactor > 0.01f;

        _skyMat.SetShaderParameter("sun_dir", toSun);
        _skyMat.SetShaderParameter("day", dayFactor);

        var env = _we.Environment;
        env.FogLightColor = HorizonNight.Lerp(HorizonDay, dayFactor);
        env.FogDensity = Mathf.Lerp(0.0020f, 0.0012f, dayFactor);
    }

    /// <summary>Aim a directional light so its beam travels opposite <paramref name="toLight"/>
    /// (the direction toward the sun/moon in the sky), via an explicit orthonormal
    /// basis so it stays stable even when the body is directly overhead.</summary>
    private static void OrientLight(DirectionalLight3D light, Vector3 toLight)
    {
        Vector3 z = toLight.Normalized(); // light shines along -Z, i.e. away from the body
        Vector3 up = Mathf.Abs(z.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        Vector3 x = up.Cross(z).Normalized();
        Vector3 y = z.Cross(x).Normalized();
        light.Basis = new Basis(x, y, z);
    }

    /// <summary>Convenience labels for lessons.</summary>
    public const float Dawn = 0.27f, Noon = 0.5f, Dusk = 0.73f, Night = 0.0f, Morning = 0.36f;
}
