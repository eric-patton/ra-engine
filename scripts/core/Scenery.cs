using Godot;

namespace RAEngine.Core;

/// <summary>Sky, sun and ambient lighting setup shared by gameplay and tests.</summary>
public static class Scenery
{
    public static (WorldEnvironment env, DirectionalLight3D sun) AddDaylight(Node parent)
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.38f, 0.55f, 0.85f),
            SkyHorizonColor = new Color(0.78f, 0.83f, 0.86f),
            GroundHorizonColor = new Color(0.70f, 0.66f, 0.56f),
            GroundBottomColor = new Color(0.45f, 0.40f, 0.32f),
            SunAngleMax = 30f,
        } };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.5f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.1f,
        };
        environment.SetGlowLevel(2, 1f);
        environment.SetGlowLevel(3, 1f);
        environment.FogEnabled = true;
        environment.FogLightColor = new Color(0.75f, 0.80f, 0.85f);
        environment.FogDensity = 0.0015f;

        var we = new WorldEnvironment { Environment = environment, Name = "WorldEnvironment" };
        parent.AddChild(we);

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightEnergy = 1.15f,
            LightColor = new Color(1f, 0.96f, 0.88f),
        };
        sun.RotationDegrees = new Vector3(-52f, -130f, 0f);
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        parent.AddChild(sun);

        return (we, sun);
    }
}
