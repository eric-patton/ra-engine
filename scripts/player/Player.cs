using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>First-person character controller: walking, sprinting, crouching,
/// jumping with fall damage, and swimming with an air/drowning meter. Tuned for
/// a mixed-age audience (forgiving acceleration, no twitchy speeds). Creative
/// mode enables noclip-free flight and disables damage.</summary>
public partial class Player : CharacterBody3D
{
    [Signal] public delegate void HealthChangedEventHandler(float current, float max);
    [Signal] public delegate void AirChangedEventHandler(float current, float max);
    [Signal] public delegate void DiedEventHandler();

    public VoxelWorld World;
    public float MouseSensitivity = 0.0026f;

    // movement tuning
    private const float WalkSpeed = 4.6f, SprintSpeed = 7.2f, CrouchSpeed = 2.4f;
    private const float SwimSpeed = 3.6f, FlySpeed = 9f;
    private const float Accel = 12f, AirAccel = 4f, Gravity = 24f, MaxFall = 45f;
    private const float JumpVel = 8.0f;
    private const float FallSafe = 5f, FallDamagePerBlock = 7f;

    // body dimensions
    private const float StandHeight = 1.7f, CrouchHeight = 1.1f;
    private const float EyeStand = 1.5f, EyeCrouch = 0.9f, Radius = 0.35f;

    public float MaxHealth = 100f, Health = 100f;
    public float MaxAir = 10f, Air = 10f;
    public bool Creative = false;
    public bool InputEnabled = true;
    public bool IsDead { get; private set; }
    public bool InWater { get; private set; }
    public bool HeadUnderwater { get; private set; }

    private Node3D _head;
    private Camera3D _cam;
    private CollisionShape3D _shape;
    private CapsuleShape3D _capsule;
    private bool _crouching;
    private float _airApexY;
    private bool _wasOnFloor = true;
    private float _drownTimer;

    public Camera3D Camera => _cam;
    public Node3D Head => _head;

    public override void _Ready()
    {
        GameInput.Setup();

        _capsule = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight * 0.5f, 0) };
        AddChild(_shape);

        _head = new Node3D { Name = "Head", Position = new Vector3(0, EyeStand, 0) };
        AddChild(_head);
        _cam = new Camera3D { Name = "Camera", Fov = 75f };
        _head.AddChild(_cam);

        _airApexY = GlobalPosition.Y;
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        EmitSignal(SignalName.AirChanged, Air, MaxAir);
    }

    public void MakeCurrent()
    {
        _cam.Current = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!InputEnabled) return;

        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mm.Relative.X * MouseSensitivity);
            float pitch = Mathf.Clamp(_head.Rotation.X - mm.Relative.Y * MouseSensitivity, -1.5f, 1.5f);
            _head.Rotation = new Vector3(pitch, 0, 0);
        }
        else if (e.IsActionPressed(GameInput.Actions.Pause))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
        else if (e.IsActionPressed(GameInput.Actions.ToggleMode))
        {
            SetCreative(!Creative);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        UpdateWaterState();
        UpdateCrouch();

        if (IsDead) { Velocity = Velocity.Lerp(Vector3.Zero, dt * 4f); MoveAndSlide(); return; }

        if (Creative) FlyMove(dt);
        else if (InWater) SwimMove(dt);
        else GroundMove(dt);

        UpdateAir(dt);
        CheckHazards(dt);
    }

    // ---- movement modes ---------------------------------------------------

    private Vector3 WishDir()
    {
        if (!InputEnabled) return Vector3.Zero;
        Vector2 inp = Input.GetVector(GameInput.Actions.Left, GameInput.Actions.Right,
                                      GameInput.Actions.Forward, GameInput.Actions.Back);
        Vector3 dir = Transform.Basis * new Vector3(inp.X, 0, inp.Y);
        return dir.LengthSquared() > 0.0001f ? dir.Normalized() : Vector3.Zero;
    }

    private void GroundMove(float dt)
    {
        Vector3 vel = Velocity;
        if (!IsOnFloor()) vel.Y = Mathf.Max(vel.Y - Gravity * dt, -MaxFall);

        Vector3 dir = WishDir();
        float speed = _crouching ? CrouchSpeed
            : (InputEnabled && Input.IsActionPressed(GameInput.Actions.Sprint) ? SprintSpeed : WalkSpeed);
        float a = IsOnFloor() ? Accel : AirAccel;

        var horiz = new Vector3(vel.X, 0, vel.Z);
        horiz = horiz.MoveToward(dir * speed, a * dt * speed);
        vel.X = horiz.X;
        vel.Z = horiz.Z;

        if (IsOnFloor() && InputEnabled && Input.IsActionJustPressed(GameInput.Actions.Jump) && !_crouching)
            vel.Y = JumpVel;

        Velocity = vel;
        MoveAndSlide();
        TrackFall();
    }

    private void SwimMove(float dt)
    {
        Vector3 vel = Velocity;
        if (vel.Y < -5f) vel.Y = -5f; // water immediately arrests a fall (no deep plunge)
        Vector3 dir = WishDir();

        float vy = 0;
        if (InputEnabled && Input.IsActionPressed(GameInput.Actions.Jump)) vy += 1;
        if (InputEnabled && Input.IsActionPressed(GameInput.Actions.Crouch)) vy -= 1;

        Vector3 target = dir * SwimSpeed + Vector3.Up * (vy * SwimSpeed);
        vel = vel.MoveToward(target, Accel * dt * SwimSpeed);
        if (Mathf.IsZeroApprox(vy))
        {
            // Rise while submerged, settle gently once the head breaches the
            // surface, so an idle swimmer bobs at the waterline and can breathe.
            float buoy = HeadUnderwater ? 2.0f : -0.4f;
            vel.Y = Mathf.MoveToward(vel.Y, buoy, Gravity * 0.5f * dt);
        }

        Velocity = vel;
        MoveAndSlide();
        // no fall damage while swimming
        _airApexY = GlobalPosition.Y;
        _wasOnFloor = IsOnFloor();
    }

    private void FlyMove(float dt)
    {
        Vector3 dir = Vector3.Zero;
        if (InputEnabled)
        {
            Vector2 inp = Input.GetVector(GameInput.Actions.Left, GameInput.Actions.Right,
                                          GameInput.Actions.Forward, GameInput.Actions.Back);
            dir = _head.GlobalTransform.Basis * new Vector3(inp.X, 0, inp.Y);
            if (Input.IsActionPressed(GameInput.Actions.Jump)) dir.Y += 1;
            if (Input.IsActionPressed(GameInput.Actions.Crouch)) dir.Y -= 1;
        }
        if (dir.LengthSquared() > 0.0001f) dir = dir.Normalized();
        float speed = (InputEnabled && Input.IsActionPressed(GameInput.Actions.Sprint)) ? FlySpeed * 2f : FlySpeed;
        Velocity = Velocity.MoveToward(dir * speed, Accel * dt * speed);
        MoveAndSlide();
        _airApexY = GlobalPosition.Y;
        _wasOnFloor = true;
    }

    private void TrackFall()
    {
        bool onFloor = IsOnFloor();
        if (!onFloor)
            _airApexY = Mathf.Max(_airApexY, GlobalPosition.Y);
        else if (!_wasOnFloor)
        {
            float fall = _airApexY - GlobalPosition.Y;
            if (!InWater && fall > FallSafe)
                Damage((fall - FallSafe) * FallDamagePerBlock, "fall");
            _airApexY = GlobalPosition.Y;
        }
        if (onFloor) _airApexY = GlobalPosition.Y;
        _wasOnFloor = onFloor;
    }

    // ---- environment ------------------------------------------------------

    private void UpdateWaterState()
    {
        if (World == null) { InWater = HeadUnderwater = false; return; }
        Vector3 p = GlobalPosition;
        InWater = World.GetBlock(FloorV(p + new Vector3(0, 0.5f, 0))).IsLiquid;
        HeadUnderwater = World.GetBlock(FloorV(p + new Vector3(0, EyeStand, 0))).IsLiquid;
    }

    private void UpdateCrouch()
    {
        bool want = InputEnabled && !Creative && !InWater && Input.IsActionPressed(GameInput.Actions.Crouch);
        if (want == _crouching) return;
        _crouching = want;
        float h = _crouching ? CrouchHeight : StandHeight;
        _capsule.Height = h;
        _shape.Position = new Vector3(0, h * 0.5f, 0);
        _head.Position = new Vector3(0, _crouching ? EyeCrouch : EyeStand, 0);
    }

    private void UpdateAir(float dt)
    {
        float before = Air;
        if (HeadUnderwater)
        {
            Air = Mathf.Max(0, Air - dt);
            if (Air <= 0)
            {
                _drownTimer += dt;
                if (_drownTimer >= 1f) { Damage(4f, "drown"); _drownTimer -= 1f; }
            }
        }
        else
        {
            Air = Mathf.Min(MaxAir, Air + dt * 4f);
            _drownTimer = 0;
        }
        if (!Mathf.IsEqualApprox(before, Air)) EmitSignal(SignalName.AirChanged, Air, MaxAir);
    }

    private void CheckHazards(float dt)
    {
        if (World == null || Creative) return;
        var b = World.GetBlock(FloorV(GlobalPosition + new Vector3(0, 0.5f, 0)));
        if (b.Hazard) Damage(b.HazardDamage * dt, "hazard");
    }

    private static Vector3I FloorV(Vector3 p) =>
        new(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));

    // ---- health -----------------------------------------------------------

    public void Damage(float amount, string cause = "")
    {
        if (IsDead || Creative || amount <= 0) return;
        Health = Mathf.Max(0, Health - amount);
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        if (Health <= 0) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
    }

    public void Respawn(Vector3 at)
    {
        IsDead = false;
        Health = MaxHealth;
        Air = MaxAir;
        Velocity = Vector3.Zero;
        GlobalPosition = at;
        _airApexY = at.Y;
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        EmitSignal(SignalName.AirChanged, Air, MaxAir);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        EmitSignal(SignalName.Died);
    }

    public void SetCreative(bool on)
    {
        Creative = on;
        if (on) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
    }
}
