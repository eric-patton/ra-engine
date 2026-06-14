using Godot;

namespace RAEngine.Core;

public enum Weather { Clear, Rain, Snow }

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

    private GpuParticles3D _rain, _snow;
    private Node3D _weatherFollow;
    private readonly ValueNoise2D _windNoise = new(9001);
    public Weather Weather { get; private set; } = Weather.Clear;
    public Vector2 Wind { get; private set; } = new(1f, 0f);

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
        _skyMat.SetShaderParameter("cloud_coverage", 0.5f);
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
            // Keep atmospheric fog on the terrain but DON'T fog the sky dome — at the
            // default (1.0) depth fog washes the whole sky toward the fog colour,
            // hiding the sun, moon, clouds and stars behind a flat wall of colour.
            FogSkyAffect = 0f,
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

        BuildWeather();
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
        UpdateWeather();
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

    // ---- weather ----------------------------------------------------------

    /// <summary>The emitters track this node (the player) so precipitation always
    /// falls around them.</summary>
    public void SetWeatherFollow(Node3D target) => _weatherFollow = target;

    public void SetWeather(Weather weather)
    {
        if (Weather == weather) return;
        Weather = weather;
        if (_rain != null) _rain.Emitting = weather == Weather.Rain;
        if (_snow != null) _snow.Emitting = weather == Weather.Snow;
        // Overcast the sky when it's precipitating; clear it otherwise.
        _skyMat?.SetShaderParameter("cloud_coverage", weather == Weather.Clear ? 0.5f : 0.82f);
    }

    private void BuildWeather()
    {
        _rain = MakePrecip(rain: true);
        _snow = MakePrecip(rain: false);
        AddChild(_rain);
        AddChild(_snow);
    }

    private static GpuParticles3D MakePrecip(bool rain)
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(16f, 0.5f, 16f),
            Direction = new Vector3(0f, -1f, 0f),
            Spread = rain ? 2f : 12f,
            Gravity = new Vector3(0f, rain ? -35f : -2.5f, 0f),
            InitialVelocityMin = rain ? 16f : 1.2f,
            InitialVelocityMax = rain ? 22f : 2.4f,
            ScaleMin = rain ? 0.7f : 0.5f,
            ScaleMax = rain ? 1.2f : 1.0f,
            Color = rain ? new Color(0.6f, 0.7f, 0.95f, 0.55f) : new Color(1f, 1f, 1f, 0.9f),
        };
        if (!rain)
        {
            mat.TurbulenceEnabled = true;
            mat.TurbulenceNoiseStrength = 2.2f;
            mat.TurbulenceNoiseScale = 1.5f;
        }

        Mesh mesh = rain
            ? new QuadMesh { Size = new Vector2(0.03f, 0.55f) }
            : new QuadMesh { Size = new Vector2(0.10f, 0.10f) };
        var drawMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            BillboardMode = rain ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = rain ? new Color(0.6f, 0.7f, 0.95f, 0.55f) : Colors.White,
        };
        mesh.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name = rain ? "Rain" : "Snow",
            Amount = rain ? 900 : 500,
            Lifetime = rain ? 1.1f : 7f,
            ProcessMaterial = mat,
            DrawPass1 = mesh,
            Emitting = false,
            Preprocess = 1f,
            VisibilityAabb = new Aabb(new Vector3(-20, -30, -20), new Vector3(40, 40, 40)),
        };
    }

    private void UpdateWeather()
    {
        // Wind drifts slowly and deterministically; it pushes precipitation and
        // speeds up the clouds.
        float wx = (_windNoise.Noise(GodotTime() * 0.03f, 0f) - 0.5f) * 2f;
        float wz = (_windNoise.Noise(0f, GodotTime() * 0.03f + 50f) - 0.5f) * 2f;
        Wind = new Vector2(wx, wz) * 6f;
        _skyMat.SetShaderParameter("cloud_speed", 0.004f + Wind.Length() * 0.0016f);

        foreach (var p in new[] { _rain, _snow })
        {
            if (p == null || !p.Emitting) continue;
            if (p.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Gravity = new Vector3(Wind.X, p == _rain ? -35f : -2.5f, Wind.Y);
            if (_weatherFollow != null && GodotObject.IsInstanceValid(_weatherFollow))
                p.GlobalPosition = _weatherFollow.GlobalPosition + new Vector3(0f, 16f, 0f);
        }
    }

    private float GodotTime() => (float)(Time.GetTicksMsec() / 1000.0);

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
        _skyMat.SetShaderParameter("moon_dir", toMoon);
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

    /// <summary>The current time of day as a friendly 12-hour clock string (e.g.
    /// "6:30 AM"), for the HUD. TimeOfDay 0 = midnight, 0.5 = noon.</summary>
    public string ClockText()
    {
        float total = Mathf.PosMod(TimeOfDay, 1f) * 24f;
        int hh = (int)total;
        int mm = (int)((total - hh) * 60f);
        string ampm = hh < 12 ? "AM" : "PM";
        int disp = hh % 12;
        if (disp == 0) disp = 12;
        return $"{disp}:{mm:00} {ampm}";
    }

    /// <summary>Convenience labels for lessons.</summary>
    public const float Dawn = 0.27f, Noon = 0.5f, Dusk = 0.73f, Night = 0.0f, Morning = 0.36f;
}
