using Godot;
using RAEngine.Combat;
using RAEngine.Dialogue;

namespace RAEngine.NpcSys;

/// <summary>A friendly, talkable character. Stands its ground, turns to face a
/// nearby player, and carries a conversation. Appearance reuses the blocky mob
/// model with friendly colours.</summary>
public partial class Npc : CharacterBody3D
{
    [Signal] public delegate void TalkedEventHandler();

    public string NpcName = "Villager";
    public DialogueData Dialogue;
    public Color Skin = new(0.85f, 0.7f, 0.55f);
    public Color Robe = new(0.55f, 0.5f, 0.7f);
    public Color Accent = new(0.7f, 0.6f, 0.4f);
    public float FaceRange = 7f;
    public bool Beast = false; // use the animal model instead of a humanoid

    private Node3D _model;
    private Node3D _player;
    private const float Gravity = 22f;

    public override void _Ready()
    {
        AddToGroup("npc");
        _model = Beast ? MobModel.BuildBeast(Skin, Robe) : MobModel.BuildHumanoid(Skin, Robe, Accent);
        AddChild(_model);

        var col = new CollisionShape3D
        {
            Shape = Beast
                ? new CapsuleShape3D { Radius = 0.5f, Height = 1.0f }
                : new CapsuleShape3D { Radius = 0.35f, Height = 1.7f },
            Position = new Vector3(0, Beast ? 0.5f : 0.85f, 0),
        };
        AddChild(col);

        var label = new Label3D
        {
            Text = NpcName,
            Position = new Vector3(0, Beast ? 1.4f : 2.15f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            OutlineSize = 12,
            Modulate = new Color(1f, 0.95f, 0.8f),
            NoDepthTest = false,
        };
        AddChild(label);
    }

    public void SetDialogueId(string id) => Dialogue = Dialogues.Load(id);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 vel = Velocity;
        if (!IsOnFloor()) vel.Y = Mathf.Max(vel.Y - Gravity * dt, -50f);
        else if (vel.Y < 0) vel.Y = 0;
        vel.X = 0; vel.Z = 0;
        Velocity = vel;
        MoveAndSlide();

        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player != null)
        {
            Vector3 to = _player.GlobalPosition - GlobalPosition;
            var flat = new Vector3(to.X, 0, to.Z);
            if (flat.LengthSquared() > 0.04f && flat.Length() < FaceRange)
                RotationDegrees = new Vector3(0, Mathf.RadToDeg(Mathf.Atan2(flat.X, flat.Z) + Mathf.Pi), 0);
        }
    }
}
