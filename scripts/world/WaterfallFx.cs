using Godot;

namespace RAEngine.World;

/// <summary>Voxel particle dressing for a waterfall — small tumbling CUBES, matching the blocky
/// aesthetic instead of soft round billboards: froth at the LIP (the tipping point), a falling
/// SHEET of cubes down the face for volume, a chunky SPLASH where it lands, and translucent
/// "BUBBLE" cubes that drift away from the base across the pool on the current. Reusable: give it
/// the lip, the base, the width and the spill direction.</summary>
public static class WaterfallFx
{
    public static Node3D Build(Vector3 lipCenter, Vector3 baseCenter, float width, float fallHeight, Vector3 spillDir)
    {
        spillDir = spillDir.Normalized();
        float hw = width * 0.5f;
        var root = new Node3D { Name = "WaterfallFx" };

        // Lip froth — cubes tumbling over the brink.
        root.AddChild(Emitter("LipSpray", lipCenter,
            box: new Vector3(hw, 0.15f, 0.25f),
            dir: (spillDir * 0.7f + Vector3.Down * 0.5f).Normalized(), spread: 30f,
            vMin: 1.4f, vMax: 3.2f, gravity: -9f, spin: 220f,
            amount: 40, life: 0.9f, sMin: 0.5f, sMax: 1.0f, peak: 0.9f,
            tint: new Color(0.95f, 0.98f, 1f)));

        // Falling sheet — a dense column of cubes pouring down the face for volume.
        root.AddChild(Emitter("FallSheet", lipCenter.Lerp(baseCenter, 0.5f),
            box: new Vector3(hw, fallHeight * 0.5f, 0.16f),
            dir: Vector3.Down, spread: 9f, vMin: 3f, vMax: 6f, gravity: -12f, spin: 160f,
            amount: 85, life: Mathf.Clamp(fallHeight / 6f, 0.7f, 1.5f),
            sMin: 0.45f, sMax: 1.1f, peak: 0.85f, tint: new Color(0.86f, 0.93f, 1f)));

        // Base splash — a big chunky burst kicking up where the sheet hits the pool.
        root.AddChild(Emitter("BaseSplash", baseCenter,
            box: new Vector3(hw + 0.6f, 0.12f, 0.7f),
            dir: Vector3.Up, spread: 58f, vMin: 2.6f, vMax: 5.4f, gravity: -8f, spin: 260f,
            amount: 110, life: 0.95f, sMin: 0.6f, sMax: 1.7f, peak: 0.95f,
            tint: new Color(0.97f, 0.99f, 1f)));

        // Foam pool — a wide, dense mat of white cubes churning on the surface around the impact,
        // the big bright foam patch a voxel waterfall throws where it lands.
        root.AddChild(Emitter("FoamPool", baseCenter + spillDir * 0.8f,
            box: new Vector3(hw + 1.0f, 0.12f, 1.6f),
            dir: Vector3.Up, spread: 80f, vMin: 0.3f, vMax: 1.2f, gravity: -1.5f, spin: 90f,
            amount: 130, life: 1.5f, sMin: 0.5f, sMax: 1.2f, peak: 0.9f,
            tint: new Color(0.95f, 0.98f, 1f)));

        // Drifting bubbles — translucent cubes carried away across the pool surface by the current.
        root.AddChild(Emitter("Bubbles", baseCenter + spillDir * 1.6f + Vector3.Down * 0.2f,
            box: new Vector3(hw + 1.0f, 0.1f, 0.6f),
            dir: spillDir, spread: 34f, vMin: 0.5f, vMax: 1.4f, gravity: 0f, spin: 50f,
            amount: 72, life: 2.8f, sMin: 0.35f, sMax: 0.7f, peak: 0.5f,
            tint: new Color(0.88f, 0.95f, 1f)));
        return root;
    }

    private static GpuParticles3D Emitter(string name, Vector3 pos, Vector3 box, Vector3 dir,
        float spread, float vMin, float vMax, float gravity, float spin, int amount, float life,
        float sMin, float sMax, float peak, Color tint)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(tint, 0f));         // fade in from nothing
        ramp.AddPoint(0.18f, new Color(tint, peak));   // quick rise to peak alpha
        ramp.SetColor(1, new Color(tint, 0f));         // fade out over the rest of life
        return new GpuParticles3D
        {
            Name = name,
            Position = pos,
            Amount = amount,
            Lifetime = life,
            DrawPass1 = Cube(),
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = box,
                Direction = dir,
                Spread = spread,
                InitialVelocityMin = vMin,
                InitialVelocityMax = vMax,
                Gravity = new Vector3(0, gravity, 0),
                ScaleMin = sMin,
                ScaleMax = sMax,
                AngularVelocityMin = -spin,            // tumble the cubes (like break debris)
                AngularVelocityMax = spin,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 1.1f,
                TurbulenceNoiseScale = 1.4f,
                ColorRamp = new GradientTexture1D { Gradient = ramp },
            },
        };
    }

    /// <summary>A small tumbling water cube; per-particle vertex colour carries the tint + fade.
    /// Alpha-blended and sorted in front of the water surface.</summary>
    private static Mesh Cube()
    {
        var mesh = new BoxMesh { Size = new Vector3(0.22f, 0.22f, 0.22f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.75f,
            RenderPriority = 2,                        // draw in front of the water (priority -1)
        });
        return mesh;
    }
}
