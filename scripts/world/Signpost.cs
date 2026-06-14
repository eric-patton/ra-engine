using Godot;

namespace RAEngine.World;

/// <summary>A placeable scripture signpost / lectern: a small wooden post with a
/// board that shows a billboarded text label (a verse or note). Teachers drop
/// these into a world to caption a scene; they persist with the world.</summary>
public sealed partial class Signpost : Node3D
{
    public string Text = "";

    public static Signpost Create(Vector3 position, string text)
    {
        var s = new Signpost { Text = text };
        s.Position = position;
        return s;
    }

    public override void _Ready()
    {
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.30f, 0.16f), Roughness = 0.9f };

        var post = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 1.5f, 0.14f) },
            MaterialOverride = wood,
            Position = new Vector3(0, 0.75f, 0),
        };
        AddChild(post);

        var board = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.1f, 0.6f, 0.08f) },
            MaterialOverride = wood,
            Position = new Vector3(0, 1.6f, 0),
        };
        AddChild(board);

        var label = new Label3D
        {
            Text = Text,
            Position = new Vector3(0, 1.6f, 0.06f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,
            PixelSize = 0.0016f,
            OutlineSize = 24,
            Modulate = new Color(0.15f, 0.08f, 0.02f),
            OutlineModulate = new Color(0.95f, 0.9f, 0.75f),
            DoubleSided = true,
            Width = 600,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            NoDepthTest = false,
        };
        AddChild(label);
    }
}
