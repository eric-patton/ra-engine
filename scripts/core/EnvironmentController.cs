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

    /// <summary>The sky shader material, exposed so tests can tweak uniforms — e.g. drop
    /// cloud_coverage to inspect the star field on a clear sky.</summary>
    public ShaderMaterial SkyMaterial => _skyMat;

    private GpuParticles3D _rain, _snow;
    private GpuParticles3D _motes, _fireflies; // ambient life: day motes / night fireflies
    private Node3D _weatherFollow;
    private readonly ValueNoise2D _windNoise = new(9001);
    public Weather Weather { get; private set; } = Weather.Clear;
    public Vector2 Wind { get; private set; } = new(1f, 0f);
    private float _dayFactor = 1f; // 0 at night, 1 at midday (drives ambience blend)

    /// <summary>0 at night, 1 at midday. Lets effects (e.g. fires reading brighter after
    /// dark) scale with the light without recomputing the time-of-day arc themselves.</summary>
    public float DayFactor => _dayFactor;

    // Weather transition smoothing: SetWeather sets the *targets*; the live values ease
    // toward them each frame so a storm rolls in instead of snapping on.
    private float _cloudCur = 0.5f, _cloudTarget = 0.5f;
    private float _rainCur, _rainTarget, _snowCur, _snowTarget;
    private float _weatherFog, _weatherFogTarget; // extra fog density from precipitation

    // Subtle colour grade, eased by time-of-day + weather (brightness/contrast/saturation).
    private float _gradeBri = 1f, _gradeCon = 1f, _gradeSat = 1f;

    // Glow/bloom preset (lerped) so lessons can swell the bloom for divine beats etc.
    private GlowCfg _glowCur = GlowCfg.Normal, _glowTarget = GlowCfg.Normal;

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
            // Colour grading: enabled so the time/weather grade in UpdatePostFx can nudge
            // brightness/contrast/saturation. Neutral (1,1,1) until driven.
            AdjustmentEnabled = true,
            // Volumetric depth haze: distant terrain dissolves into a tinted atmosphere
            // for real sense of scale. Kept light (1/5 the default density) and, like the
            // flat fog, NOT applied to the sky dome so the sun/moon/stars stay crisp.
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.0035f,       // very light — depth without murk (esp. at night)
            VolumetricFogAlbedo = HorizonDay,     // re-tinted with the sky each frame in Apply
            VolumetricFogAmbientInject = 0.8f,    // pull the sky's ambient colour into the haze
            VolumetricFogLength = 64f,
            VolumetricFogSkyAffect = 0f,
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
        BuildAmbientParticles();
        Apply();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (CycleEnabled)
        {
            TimeOfDay += dt / DayLengthSeconds;
            TimeOfDay -= Mathf.Floor(TimeOfDay); // wrap to 0..1
            Apply();
        }
        UpdateWeather(dt);
        PushAmbience();
        UpdatePostFx(dt); // colour grade + glow preset easing
    }

    /// <summary>Blend the ambience beds with the light and weather: day birdsong vs
    /// night crickets, ducked under a rain bed while it's raining.</summary>
    private void PushAmbience()
    {
        float wet = Weather == Weather.Rain ? 1f : 0f;
        float day = _dayFactor * (1f - wet);
        float night = (1f - _dayFactor) * (1f - wet);
        AudioManager.SetAmbienceMix(day, night, wet);
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
        // Set transition TARGETS only; the live values ease toward them in UpdateWeather
        // so the sky clouds over, precipitation ramps in and fog thickens gradually
        // instead of snapping. Overcast while precipitating; clear otherwise.
        _cloudTarget = weather == Weather.Clear ? 0.5f : 0.82f;
        _rainTarget = weather == Weather.Rain ? 1f : 0f;
        _snowTarget = weather == Weather.Snow ? 1f : 0f;
        _weatherFogTarget = weather switch
        {
            Weather.Rain => 0.0008f,
            Weather.Snow => 0.0010f,
            _ => 0f,
        };
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
            // Rain: thin slab high above the player so streaks fall through view.
            // Snow: tall column centred around eye level so flakes are visible
            //       looking straight ahead, not just overhead.
            EmissionBoxExtents = rain ? new Vector3(16f, 0.5f, 16f) : new Vector3(16f, 10f, 16f),
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
            Amount = rain ? 900 : 600,
            Lifetime = rain ? 1.1f : 7f,
            ProcessMaterial = mat,
            DrawPass1 = mesh,
            Emitting = false,
            Preprocess = 1f,
            // Snow needs a taller AABB to match the taller emission box (±10 m on Y)
            // and the lower emitter offset (+6 m vs rain's +16 m).
            VisibilityAabb = rain
                ? new Aabb(new Vector3(-20, -30, -20), new Vector3(40, 40, 40))
                : new Aabb(new Vector3(-20, -18, -20), new Vector3(40, 32, 40)),
        };
    }

    // ---- ambient life (motes by day, fireflies by night) ------------------

    private void BuildAmbientParticles()
    {
        _motes = MakeMotes();
        _fireflies = MakeFireflies();
        AddChild(_motes);
        AddChild(_fireflies);
    }

    /// <summary>Pale dust/pollen motes drifting slowly in a volume around the player —
    /// barely-there atmosphere that catches the daylight.</summary>
    private static GpuParticles3D MakeMotes()
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(14f, 8f, 14f),
            Direction = Vector3.Up,
            Spread = 180f,
            Gravity = new Vector3(0f, 0.04f, 0f),
            InitialVelocityMin = 0.08f,
            InitialVelocityMax = 0.45f,
            ScaleMin = 0.4f,
            ScaleMax = 1.0f,
            Color = new Color(1f, 0.97f, 0.82f, 0.5f),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 0.6f,
            TurbulenceNoiseScale = 1.2f,
        };
        var mesh = new QuadMesh { Size = new Vector2(0.04f, 0.04f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return new GpuParticles3D
        {
            Name = "Motes",
            Amount = 90,
            Lifetime = 7f,
            Emitting = true,
            Preprocess = 3f,
            LocalCoords = false,
            ProcessMaterial = mat,
            DrawPass1 = mesh,
            VisibilityAabb = new Aabb(new Vector3(-22, -22, -22), new Vector3(44, 44, 44)),
        };
    }

    /// <summary>Warm glowing fireflies that wander near the ground at night. They use
    /// an additive material (so the Glow blooms them) and a fade-in/out colour ramp
    /// over each particle's life, so a field of them twinkles.</summary>
    private static GpuParticles3D MakeFireflies()
    {
        var ramp = new Gradient
        {
            Offsets = new float[] { 0f, 0.5f, 1f },
            Colors = new Color[] { new(1, 1, 1, 0), new(1, 1, 1, 1), new(1, 1, 1, 0) },
        };
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(12f, 2.5f, 12f),
            Direction = Vector3.Up,
            Spread = 180f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.7f,
            ScaleMin = 0.5f,
            ScaleMax = 1.2f,
            Color = new Color(1f, 0.85f, 0.35f),
            ColorRamp = new GradientTexture1D { Gradient = ramp },
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 1.4f,
            TurbulenceNoiseScale = 1.6f,
        };
        var mesh = new QuadMesh { Size = new Vector2(0.07f, 0.07f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return new GpuParticles3D
        {
            Name = "Fireflies",
            Amount = 44,
            Lifetime = 3.5f,
            Emitting = true,
            Preprocess = 2f,
            LocalCoords = false,
            ProcessMaterial = mat,
            DrawPass1 = mesh,
            VisibilityAabb = new Aabb(new Vector3(-18, -18, -18), new Vector3(36, 36, 36)),
        };
    }

    private void UpdateWeather(float dt)
    {
        // Wind drifts slowly and deterministically; it pushes precipitation and
        // speeds up the clouds.
        float wx = (_windNoise.Noise(GodotTime() * 0.03f, 0f) - 0.5f) * 2f;
        float wz = (_windNoise.Noise(0f, GodotTime() * 0.03f + 50f) - 0.5f) * 2f;
        Wind = new Vector2(wx, wz) * 6f;
        _skyMat.SetShaderParameter("cloud_speed", 0.004f + Wind.Length() * 0.0016f);

        // Push the live wind into the shared grass material: amplitude scales with wind
        // strength, and blades lean along the wind direction (a wiring fix — the shader's
        // sway_amount was a constant). One call drives every tuft in the world.
        if (Vegetation.Material is ShaderMaterial veg)
        {
            float windStr = Mathf.Clamp(Wind.Length() / 8f, 0f, 1f);
            veg.SetShaderParameter("sway_amount", Mathf.Lerp(0.04f, 0.30f, windStr));
            veg.SetShaderParameter("wind_dir",
                Wind.Length() > 0.01f ? Wind.Normalized() : new Vector2(1f, 0f));
        }

        // Ease the weather state toward its target (clouds, precipitation, fog) so
        // transitions are gradual. Emitters stay alive only while their ratio is > 0.
        _cloudCur = Mathf.MoveToward(_cloudCur, _cloudTarget, dt * 0.12f);
        _skyMat.SetShaderParameter("cloud_coverage", _cloudCur);
        _rainCur = Mathf.MoveToward(_rainCur, _rainTarget, dt * 0.5f);
        _snowCur = Mathf.MoveToward(_snowCur, _snowTarget, dt * 0.5f);
        if (_rain != null) { _rain.AmountRatio = _rainCur; _rain.Emitting = _rainCur > 0.001f; }
        if (_snow != null) { _snow.AmountRatio = _snowCur; _snow.Emitting = _snowCur > 0.001f; }
        _weatherFog = Mathf.MoveToward(_weatherFog, _weatherFogTarget, dt * 0.0004f);
        // Fog density is owned here (not Apply) so it folds the time-of-day base together
        // with the eased weather contribution and stays correct even on a pinned time.
        if (_we?.Environment is { } fenv)
            fenv.FogDensity = Mathf.Lerp(0.0020f, 0.0012f, _dayFactor) + _weatherFog;

        foreach (var p in new[] { _rain, _snow })
        {
            if (p == null || !p.Emitting) continue;
            if (p.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Gravity = new Vector3(Wind.X, p == _rain ? -35f : -2.5f, Wind.Y);
            if (_weatherFollow != null && GodotObject.IsInstanceValid(_weatherFollow))
            {
                // Rain spawns high overhead (thin slab) so streaks fall through the view.
                // Snow uses a tall column centred near eye level so flakes are visible
                // looking forward; the +6 m offset means the box spans roughly -4..+16
                // around the player given the ±10 m EmissionBoxExtents half-height.
                float yOffset = p == _rain ? 16f : 6f;
                p.GlobalPosition = _weatherFollow.GlobalPosition + new Vector3(0f, yOffset, 0f);
            }
        }

        // Ambient life follows the player too (motes centred, fireflies near the ground).
        if (_weatherFollow != null && GodotObject.IsInstanceValid(_weatherFollow))
        {
            Vector3 fp = _weatherFollow.GlobalPosition;
            if (_motes != null) _motes.GlobalPosition = fp + new Vector3(0f, 2f, 0f);
            if (_fireflies != null) _fireflies.GlobalPosition = fp + new Vector3(0f, 0.6f, 0f);
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
        _dayFactor = dayFactor;
        _sun.LightEnergy = dayFactor * 1.4f;
        _sun.LightColor = SunWarm.Lerp(SunNoon, Mathf.SmoothStep(0f, 0.35f, sunUp));
        _sun.Visible = dayFactor > 0.01f;

        float moonFactor = Mathf.SmoothStep(-0.1f, 0.30f, moonUp);
        _moon.LightEnergy = moonFactor * 0.35f;
        _moon.Visible = moonFactor > 0.01f;

        _skyMat.SetShaderParameter("sun_dir", toSun);
        _skyMat.SetShaderParameter("moon_dir", toMoon);
        _skyMat.SetShaderParameter("day", dayFactor);

        // Crossfade ambient life with the light: motes by day, fireflies by night.
        if (_motes != null) _motes.AmountRatio = Mathf.Clamp(dayFactor, 0f, 1f);
        if (_fireflies != null) _fireflies.AmountRatio = Mathf.Clamp(1f - dayFactor, 0f, 1f);

        var env = _we.Environment;
        Color fogCol = HorizonNight.Lerp(HorizonDay, dayFactor);
        env.FogLightColor = fogCol;
        env.VolumetricFogAlbedo = fogCol; // keep the volumetric haze tinted with the sky
        // (FogDensity is owned by UpdateWeather, which folds in the weather contribution.)
    }

    // ---- post-processing: colour grade + bloom presets --------------------

    /// <summary>Named bloom moods (see <see cref="SetGlowPreset"/>).</summary>
    public enum GlowPreset { Normal, Divine, Plague, Cave }

    /// <summary>Bloom settings for a mood, lerped toward by <see cref="UpdatePostFx"/>.</summary>
    private struct GlowCfg
    {
        public float Intensity, Bloom, Threshold, Strength;
        public GlowCfg(float i, float b, float t, float s) { Intensity = i; Bloom = b; Threshold = t; Strength = s; }
        public static readonly GlowCfg Normal = new(0.5f, 0.1f, 1.0f, 1.0f);  // matches _Ready
        public static readonly GlowCfg Divine = new(1.4f, 0.4f, 0.7f, 1.6f);  // sacred beats bloom out
        public static readonly GlowCfg Plague = new(0.15f, 0.02f, 1.2f, 0.8f); // ominous, flat
        public static readonly GlowCfg Cave   = new(0.8f, 0.05f, 0.85f, 1.2f); // make torch glow pop
    }

    /// <summary>Smoothly swell or crush the scene bloom for a mood: Divine for sacred
    /// beats, Plague for ominous flatness, Cave to make torch glow pop, Normal to reset.
    /// Lessons call this; the change eases in over ~1 s.</summary>
    public void SetGlowPreset(GlowPreset p) => _glowTarget = p switch
    {
        GlowPreset.Divine => GlowCfg.Divine,
        GlowPreset.Plague => GlowCfg.Plague,
        GlowPreset.Cave => GlowCfg.Cave,
        _ => GlowCfg.Normal,
    };

    /// <summary>Ease the colour grade (by time-of-day + weather) and the active glow
    /// preset toward their targets and push them to the WorldEnvironment. All deltas are
    /// small — the world's palette shifts without the player consciously noticing a cut.</summary>
    private void UpdatePostFx(float dt)
    {
        var env = _we?.Environment;
        if (env == null) return;

        // Punchier + slightly brighter by day, calmer + a touch desaturated at night; rain
        // pulls saturation down for a cold, moody look. Kept subtle (±~6%).
        float sat = Mathf.Lerp(0.95f, 1.06f, _dayFactor);
        float con = Mathf.Lerp(0.98f, 1.03f, _dayFactor);
        float bri = 1.0f;
        if (Weather == Weather.Rain) { sat *= 0.88f; con *= 1.02f; bri *= 0.97f; }
        _gradeSat = Mathf.MoveToward(_gradeSat, sat, dt * 0.25f);
        _gradeCon = Mathf.MoveToward(_gradeCon, con, dt * 0.25f);
        _gradeBri = Mathf.MoveToward(_gradeBri, bri, dt * 0.25f);
        env.AdjustmentSaturation = _gradeSat;
        env.AdjustmentContrast = _gradeCon;
        env.AdjustmentBrightness = _gradeBri;

        // Glow preset: ease each channel toward the active preset (cheap; runs every frame).
        _glowCur.Intensity = Mathf.MoveToward(_glowCur.Intensity, _glowTarget.Intensity, dt * 1.5f);
        _glowCur.Bloom = Mathf.MoveToward(_glowCur.Bloom, _glowTarget.Bloom, dt * 0.6f);
        _glowCur.Threshold = Mathf.MoveToward(_glowCur.Threshold, _glowTarget.Threshold, dt * 0.8f);
        _glowCur.Strength = Mathf.MoveToward(_glowCur.Strength, _glowTarget.Strength, dt * 1.2f);
        env.GlowIntensity = _glowCur.Intensity;
        env.GlowBloom = _glowCur.Bloom;
        env.GlowHdrThreshold = _glowCur.Threshold;
        env.GlowStrength = _glowCur.Strength;
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
