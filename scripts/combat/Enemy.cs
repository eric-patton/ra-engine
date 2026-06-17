using Godot;
using RAEngine.Core;

namespace RAEngine.Combat;

/// <summary>Config for an enemy archetype.</summary>
public sealed class EnemyType
{
    public string Name = "Foe";
    public float Health = 30f;
    public float Speed = 3.2f;
    public float Damage = 8f;
    public float AttackRange = 1.8f;
    public float AttackCooldown = 1.1f;
    public float Scale = 1f;
    public bool Beast = false;
    public Color Skin = new(0.82f, 0.68f, 0.55f);
    public Color Cloth = new(0.45f, 0.4f, 0.35f);
    public Color Accent = new(0.5f, 0.3f, 0.25f);
    /// <summary>Optional res:// path to a rigged glTF model for this archetype. When set
    /// and loadable, it replaces the procedural box model; null = procedural (default).</summary>
    public string ModelScene = null;
    /// <summary>Yaw (degrees) to face the imported model the right way (180 if it imports
    /// facing backwards). Ignored for the procedural box model.</summary>
    public float ModelYawDeg = 0f;

    public static EnemyType Soldier() => new()
    {
        Name = "Soldier", Health = 40f, Speed = 3.4f, Damage = 9f, AttackRange = 2.0f,
        Skin = new(0.82f, 0.66f, 0.52f), Cloth = new(0.35f, 0.33f, 0.4f), Accent = new(0.55f, 0.45f, 0.3f),
    };

    public static EnemyType Wolf() => new()
    {
        Name = "Wolf", Health = 22f, Speed = 5.0f, Damage = 6f, AttackRange = 1.6f, AttackCooldown = 0.9f,
        Beast = true, Skin = new(0.4f, 0.36f, 0.32f), Cloth = new(0.6f, 0.56f, 0.5f),
    };

    public static EnemyType Giant() => new()
    {
        Name = "Goliath", Health = 220f, Speed = 2.6f, Damage = 26f, AttackRange = 3.2f,
        AttackCooldown = 1.6f, Scale = 1.9f,
        Skin = new(0.78f, 0.6f, 0.46f), Cloth = new(0.32f, 0.3f, 0.34f), Accent = new(0.68f, 0.46f, 0.2f),
    };
}

/// <summary>A chasing melee enemy with a billboard health bar and a non-graphic
/// defeat effect (a dust/light puff, then it vanishes — no gore).</summary>
public partial class Enemy : CharacterBody3D, IDamageable
{
    [Signal] public delegate void DefeatedEventHandler();

    public EnemyType Type = EnemyType.Soldier();
    public Node3D Target;
    public VoxelWorld World; // for step-up probing; null = no climbing (still walks)
    public float Health;
    public float MaxHealth;

    private HealthBar3D _bar;
    private Node3D _modelNode;          // the visual root (box rig or rigged glTF)
    private ICharacterModel _model;     // same object, for animation calls
    private CapsuleShape3D _capsule;
    private bool _dead;
    private float _attackTimer;
    private Vector3 _lastPos;
    private const float Gravity = 22f;

    public bool IsAlive => !_dead;

    public override void _Ready()
    {
        AddToGroup("enemy");
        MaxHealth = Type.Health;
        Health = MaxHealth;
        float s = Type.Scale;

        _modelNode = Type.Beast
            ? CharacterModel.BuildBeast(Type.Skin, Type.Cloth, Type.ModelScene, Type.ModelYawDeg)
            : CharacterModel.BuildHumanoid(Type.Skin, Type.Cloth, Type.Accent, Type.ModelScene, Type.ModelYawDeg);
        _model = (ICharacterModel)_modelNode;
        _modelNode.Scale = Vector3.One * s;
        AddChild(_modelNode);

        float height = (Type.Beast ? 1.0f : 1.9f) * s;
        float radius = (Type.Beast ? 0.5f : 0.35f) * s;
        _capsule = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(height, radius * 2f + 0.1f) };
        var col = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, _capsule.Height * 0.5f, 0) };
        AddChild(col);

        _bar = new HealthBar3D { Position = new Vector3(0, height + 0.4f, 0) };
        AddChild(_bar);

        _lastPos = GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dead) return;
        float dt = (float)delta;
        Vector3 vel = Velocity;
        if (!IsOnFloor()) vel.Y = Mathf.Max(vel.Y - Gravity * dt, -50f);
        else if (vel.Y < 0) vel.Y = 0;

        bool canSee = Target != null && (Target is not IDamageable d || d.IsAlive);
        if (canSee)
        {
            Vector3 to = Target.GlobalPosition - GlobalPosition;
            var flat = new Vector3(to.X, 0, to.Z);
            float dist = flat.Length();
            // model's face is on -Z, so add PI to point the front toward the player
            if (dist > 0.05f) RotationDegrees = new Vector3(0, Mathf.RadToDeg(Mathf.Atan2(flat.X, flat.Z) + Mathf.Pi), 0);

            if (dist > Type.AttackRange)
            {
                Vector3 dir = flat.Normalized();
                vel.X = dir.X * Type.Speed;
                vel.Z = dir.Z * Type.Speed;
            }
            else
            {
                vel.X = Mathf.MoveToward(vel.X, 0, Type.Speed);
                vel.Z = Mathf.MoveToward(vel.Z, 0, Type.Speed);
                _attackTimer -= dt;
                if (_attackTimer <= 0f)
                {
                    _attackTimer = Type.AttackCooldown;
                    _model.Attack();
                    if (Target is IDamageable dd && dd.IsAlive) dd.TakeDamage(Type.Damage, this);
                }
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0, Type.Speed);
            vel.Z = Mathf.MoveToward(vel.Z, 0, Type.Speed);
        }

        var desired = new Vector3(vel.X, 0, vel.Z); // intended horizontal motion (pre-collision)
        Velocity = vel;
        MoveAndSlide();

        // Climb obstacles in the way: when grounded but blocked while trying to move,
        // jump just high enough to land on the ledge ahead, so a chasing mob follows
        // the player up terrain and low blocks instead of grinding against them. Taller
        // mobs (Goliath) reach higher. Unreachable ledges return 0 -> no hop, so it
        // waits at the base rather than bouncing in place.
        if (IsOnFloor() && desired.Length() > 0.5f)
        {
            float moved = new Vector3(GlobalPosition.X - _lastPos.X, 0, GlobalPosition.Z - _lastPos.Z).Length();
            if (moved < 0.02f)
            {
                float climb = StepUpHeight(desired.Normalized());
                if (climb > 0f)
                    Velocity = new Vector3(Velocity.X, Mathf.Sqrt(2f * Gravity * (climb + 0.35f)), Velocity.Z);
            }
        }
        _lastPos = GlobalPosition;

        // Drive the model's walk/idle by actual planar speed (procedural box rig or
        // the rigged glTF's clips — same call either way).
        _model.Animate(new Vector3(Velocity.X, 0, Velocity.Z).Length(), dt);
    }

    /// <summary>If a solid ledge blocks the path in <paramref name="dir"/> and its top is
    /// within this mob's climb reach (about half its height) with clear headroom above,
    /// return that ledge height in blocks so the caller can jump onto it; otherwise 0.</summary>
    private float StepUpHeight(Vector3 dir)
    {
        if (World == null) return 0f;
        Vector3 ahead = GlobalPosition + dir * (_capsule.Radius + 0.4f);
        int ax = Mathf.FloorToInt(ahead.X), az = Mathf.FloorToInt(ahead.Z);
        int feetY = Mathf.FloorToInt(GlobalPosition.Y + 0.05f);
        int maxClimb = Mathf.Max(1, Mathf.CeilToInt(_capsule.Height * 0.5f));
        int headroom = Mathf.Max(2, Mathf.CeilToInt(_capsule.Height));
        for (int h = 1; h <= maxClimb; h++)
        {
            var foot = new Vector3I(ax, feetY + h - 1, az); // block we'd stand on
            var stand = new Vector3I(ax, feetY + h, az);     // must be clear to stand in
            if (!World.IsSolid(foot) || World.IsSolid(stand)) continue;
            bool clear = true;
            for (int c = 1; c <= headroom; c++)
                if (World.IsSolid(stand + new Vector3I(0, c, 0))) { clear = false; break; }
            if (clear) return h;
        }
        return 0f;
    }

    public void TakeDamage(float amount, Node3D source)
    {
        if (_dead) return;
        Health = Mathf.Max(0, Health - amount);
        _bar.SetFraction(Health / MaxHealth);
        Flash();
        _model.Squash();
        if (Health > 0) AudioManager.Play("hit"); // defeat plays its own sound
        if (Health <= 0) Defeat();
    }

    private async void Flash()
    {
        SetHitFlash(true);
        await ToSignal(GetTree().CreateTimer(0.09), SceneTreeTimer.SignalName.Timeout);
        if (!_dead) SetHitFlash(false);
    }

    private void SetHitFlash(bool on) => _model.SetFlash(on);

    private void Defeat()
    {
        if (_dead) return;
        _dead = true;
        _bar.Visible = false;
        SetCollisionLayerValue(1, false);
        Velocity = Vector3.Zero;
        EmitSignal(SignalName.Defeated);
        AudioManager.Play("defeat");
        // A non-graphic dust/light puff at chest height (the pooled FX emitter
        // outlives this enemy, which is about to be freed) plus a tiny impact beat.
        Fx.Burst(GlobalPosition + new Vector3(0, _capsule.Height * 0.5f, 0),
            FxKind.Poof, new Color(0.95f, 0.92f, 0.8f), 40);
        Fx.HitStop(0.06f);
        // shrink and remove
        var tween = CreateTween();
        tween.TweenProperty(_modelNode, "scale", Vector3.One * 0.01f, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
