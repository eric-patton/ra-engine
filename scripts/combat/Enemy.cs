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
    public float Health;
    public float MaxHealth;

    private HealthBar3D _bar;
    private Node3D _model;
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

        _model = Type.Beast
            ? MobModel.BuildBeast(Type.Skin, Type.Cloth)
            : MobModel.BuildHumanoid(Type.Skin, Type.Cloth, Type.Accent);
        _model.Scale = Vector3.One * s;
        AddChild(_model);

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
                    if (Target is IDamageable dd && dd.IsAlive) dd.TakeDamage(Type.Damage, this);
                }
            }
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0, Type.Speed);
            vel.Z = Mathf.MoveToward(vel.Z, 0, Type.Speed);
        }

        Velocity = vel;
        MoveAndSlide();

        // simple step-up: if grounded and stuck while wanting to move, hop a little
        if (IsOnFloor())
        {
            float moved = new Vector3(GlobalPosition.X - _lastPos.X, 0, GlobalPosition.Z - _lastPos.Z).Length();
            bool wantsMove = Mathf.Abs(Velocity.X) + Mathf.Abs(Velocity.Z) > 0.5f;
            if (wantsMove && moved < 0.01f) Velocity = new Vector3(Velocity.X, 6f, Velocity.Z);
        }
        _lastPos = GlobalPosition;
    }

    public void TakeDamage(float amount, Node3D source)
    {
        if (_dead) return;
        Health = Mathf.Max(0, Health - amount);
        _bar.SetFraction(Health / MaxHealth);
        Flash();
        if (Health > 0) AudioManager.Play("hit"); // defeat plays its own sound
        if (Health <= 0) Defeat();
    }

    private async void Flash()
    {
        SetHitFlash(true);
        await ToSignal(GetTree().CreateTimer(0.09), SceneTreeTimer.SignalName.Timeout);
        if (!_dead) SetHitFlash(false);
    }

    private void SetHitFlash(bool on)
    {
        foreach (Node n in _model.GetChildren())
            if (n is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D m)
            {
                m.EmissionEnabled = on;
                m.Emission = on ? new Color(0.9f, 0.15f, 0.12f) : Colors.Black;
            }
    }

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
        tween.TweenProperty(_model, "scale", Vector3.One * 0.01f, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
