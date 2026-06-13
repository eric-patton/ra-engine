using Godot;

namespace RAEngine.Combat;

/// <summary>Builds simple blocky character models from box primitives so enemies
/// and NPCs are recognizable without external art. Origin is at the feet.</summary>
public static class MobModel
{
    public static Node3D BuildHumanoid(Color skin, Color cloth, Color accent)
    {
        var root = new Node3D { Name = "Model" };
        // legs
        root.AddChild(Box(new Vector3(0.26f, 0.8f, 0.26f), new Vector3(-0.16f, 0.4f, 0), cloth));
        root.AddChild(Box(new Vector3(0.26f, 0.8f, 0.26f), new Vector3(0.16f, 0.4f, 0), cloth));
        // torso
        root.AddChild(Box(new Vector3(0.62f, 0.72f, 0.36f), new Vector3(0, 1.16f, 0), accent));
        // arms
        root.AddChild(Box(new Vector3(0.18f, 0.72f, 0.2f), new Vector3(-0.42f, 1.16f, 0), skin));
        root.AddChild(Box(new Vector3(0.18f, 0.72f, 0.2f), new Vector3(0.42f, 1.16f, 0), skin));
        // head
        root.AddChild(Box(new Vector3(0.46f, 0.46f, 0.46f), new Vector3(0, 1.75f, 0), skin));
        return root;
    }

    public static Node3D BuildBeast(Color fur, Color belly)
    {
        var root = new Node3D { Name = "Model" };
        // body
        root.AddChild(Box(new Vector3(1.1f, 0.5f, 0.5f), new Vector3(0, 0.55f, 0), fur));
        root.AddChild(Box(new Vector3(0.6f, 0.3f, 0.45f), new Vector3(0, 0.35f, 0), belly));
        // head (front = -Z)
        root.AddChild(Box(new Vector3(0.45f, 0.4f, 0.4f), new Vector3(0, 0.65f, -0.6f), fur));
        root.AddChild(Box(new Vector3(0.18f, 0.18f, 0.2f), new Vector3(0, 0.5f, -0.85f), belly)); // snout
        // legs
        foreach (var (lx, lz) in new[] { (-0.4f, -0.4f), (0.4f, -0.4f), (-0.4f, 0.4f), (0.4f, 0.4f) })
            root.AddChild(Box(new Vector3(0.18f, 0.5f, 0.18f), new Vector3(lx, 0.25f, lz), fur));
        // tail
        root.AddChild(Box(new Vector3(0.14f, 0.14f, 0.5f), new Vector3(0, 0.6f, 0.7f), fur));
        return root;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 pos, Color color)
    {
        return new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f },
        };
    }
}
