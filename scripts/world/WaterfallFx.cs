using Godot;
using RAEngine.Core;

namespace RAEngine.World;

/// <summary>Particle dressing that turns a flat water curtain into a believable waterfall:
/// a churning spray line at the LIP (the tipping point), a falling-water SHEET down the face
/// for volume, and a big SPLASH burst where it lands. All soft billboards that sort in front
/// of the translucent water (render_priority) with proximity-fade so they melt into the pool.
/// Hand-placed for the showcase today; reusable for any waterfall — give it the lip, the base,
/// the width and the spill direction.</summary>
public static class WaterfallFx
{
    public static Node3D Build(Vector3 lipCenter, Vector3 baseCenter, float width, float fallHeight, Vector3 spillDir)
    {
        spillDir = spillDir.Normalized();
        float hw = width * 0.5f;
        var root = new Node3D { Name = "WaterfallFx" };

        // 1) Lip churn — a thin wide line at the brink throwing froth out over the edge.
        root.AddChild(Emitter("LipSpray", lipCenter,
            box: new Vector3(hw, 0.15f, 0.25f),
            dir: (spillDir * 0.7f + Vector3.Down * 0.5f).Normalized(),
            spread: 30f, vMin: 1.4f, vMax: 3.2f, gravity: -9f,
            amount: 44, life: 0.85f, scaleMin: 0.4f, scaleMax: 1.0f,
            peak: 0.7f, tint: new Color(0.96f, 0.98f, 1f)));

        // 2) Falling sheet — white droplets pouring down the face, giving the curtain volume.
        root.AddChild(Emitter("FallSheet", lipCenter.Lerp(baseCenter, 0.5f),
            box: new Vector3(hw, fallHeight * 0.5f, 0.16f),
            dir: Vector3.Down, spread: 7f, vMin: 3.0f, vMax: 6.0f, gravity: -12f,
            amount: 60, life: Mathf.Clamp(fallHeight / 6f, 0.7f, 1.5f),
            scaleMin: 0.4f, scaleMax: 0.95f, peak: 0.5f, tint: new Color(0.85f, 0.92f, 1f)));

        // 3) Base splash — a dense, energetic burst of mist where the sheet hits the pool.
        root.AddChild(Emitter("BaseSplash", baseCenter,
            box: new Vector3(hw + 0.6f, 0.12f, 0.7f),
            dir: Vector3.Up, spread: 60f, vMin: 2.4f, vMax: 5.2f, gravity: -8f,
            amount: 100, life: 1.0f, scaleMin: 0.7f, scaleMax: 1.8f,
            peak: 0.75f, tint: new Color(0.97f, 0.99f, 1f)));
        return root;
    }

    private static GpuParticles3D Emitter(string name, Vector3 pos, Vector3 box, Vector3 dir,
        float spread, float vMin, float vMax, float gravity, int amount, float life,
        float scaleMin, float scaleMax, float peak, Color tint)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(tint, 0f));         // fade in from nothing
        ramp.AddPoint(0.18f, new Color(tint, peak));   // quick rise to peak
        ramp.SetColor(1, new Color(tint, 0f));         // fade out over the rest of life
        return new GpuParticles3D
        {
            Name = name,
            Position = pos,
            Amount = amount,
            Lifetime = life,
            DrawPass1 = Quad(),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = box,
                Direction = dir,
                Spread = spread,
                InitialVelocityMin = vMin,
                InitialVelocityMax = vMax,
                Gravity = new Vector3(0, gravity, 0),
                ScaleMin = scaleMin,
                ScaleMax = scaleMax,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 1.3f,
                TurbulenceNoiseScale = 1.5f,
                ColorRamp = new GradientTexture1D { Gradient = ramp },
            },
        };
    }

    private static Mesh Quad()
    {
        var mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = Fx.SoftDot(),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 2,                     // draw in front of the water (priority -1)
            ProximityFadeEnabled = true,            // soft-particle melt into pool/rocks
            ProximityFadeDistance = 0.5f,
        });
        return mesh;
    }
}
