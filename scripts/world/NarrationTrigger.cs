using Godot;
using RAEngine.UI;

namespace RAEngine.World;

/// <summary>An invisible volume that pushes narration lines when the player first
/// enters it. Used to pace a lesson's story beats as the player explores.</summary>
public partial class NarrationTrigger : Area3D
{
    public string[] Lines = System.Array.Empty<string>();
    public bool Once = true;
    public Narrator Narrator;
    private bool _fired;

    public static NarrationTrigger Create(Vector3 position, Vector3 size, Narrator narrator, params string[] lines)
    {
        var t = new NarrationTrigger { Lines = lines, Narrator = narrator };
        t.Position = position;
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size } };
        t.AddChild(shape);
        t.BodyEntered += t.OnBodyEntered;
        return t;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_fired && Once) return;
        if (body is not PlayerSys.Player) return;
        _fired = true;
        Narrator?.ShowMany(Lines);
    }
}
