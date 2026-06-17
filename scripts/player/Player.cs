using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>First-person character controller: walking, sprinting, crouching,
/// jumping with fall damage, and swimming with an air/drowning meter. Tuned for
/// a mixed-age audience (forgiving acceleration, no twitchy speeds). Creative
/// mode enables noclip-free flight and disables damage.</summary>
public partial class Player : CharacterBody3D, IDamageable
{
    [Signal] public delegate void HealthChangedEventHandler(float current, float max);
    [Signal] public delegate void AirChangedEventHandler(float current, float max);
    [Signal] public delegate void DiedEventHandler();
    /// <summary>Fired when damage actually lands (after Creative/SafeMode guards), so
    /// the HUD can flash and the camera can shake. <paramref name="cause"/> is e.g.
    /// "fall", "drown", "hazard", "hit".</summary>
    [Signal] public delegate void HurtEventHandler(float amount, string cause);

    public VoxelWorld World;
    public float MouseSensitivity = 0.0026f;

    // movement tuning
    private const float WalkSpeed = 4.6f, SprintSpeed = 7.2f, CrouchSpeed = 2.4f;
    private const float SwimSpeed = 3.6f, FlySpeed = 9f, SwimClimbVel = 7f;
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
    /// <summary>Teacher "safe mode": ignore all incoming damage (combat + hazards),
    /// so a class can explore a lesson without dying.</summary>
    public bool SafeMode = false;
    public bool IsDead { get; private set; }
    public bool InWater { get; private set; }
    public bool HeadUnderwater { get; private set; }
    /// <summary>True while sprinting on the ground (drives the HUD vignette tighten).</summary>
    public bool Sprinting { get; private set; }

    /// <summary>Current capsule height (shrinks while crouching). The block
    /// interactor reads this so it never wrongly rejects a placement near the body.</summary>
    public float CollisionHeight => _crouching ? CrouchHeight : StandHeight;

    /// <summary>True briefly after a mouse re-capture, so the click that re-focuses
    /// the window doesn't also break/place a block or fire a weapon.</summary>
    public bool ActionsSuppressed => _actionLock > 0f;

    private Node3D _head;
    private Camera3D _cam;
    private float _baseFov;        // resting camera FOV; widens slightly while sprinting
    private CollisionShape3D _shape;
    private CapsuleShape3D _capsule;
    private bool _crouching;
    private float _airApexY;
    private bool _wasOnFloor = true;
    private float _drownTimer;
    private float _actionLock;
    private Vector2 _mouseLook;   // mouse motion accumulated since the last physics tick (applied there, not per render frame)
    private float _stepDist;      // distance walked since the last footstep sound
    private bool _stepFlip;       // alternates footstep pitch for a natural gait
    private bool _wasInWater;     // to detect the splash when first entering water
    private float _hurtCd;        // throttles the hurt sound
    private GpuParticles3D _bubbles; // rising bubbles emitted while the head is underwater

    public Camera3D Camera => _cam;
    public Node3D Head => _head;

    public override void _Ready()
    {
        GameInput.Setup();
        AddToGroup("player");

        _capsule = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _shape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight * 0.5f, 0) };
        AddChild(_shape);

        _head = new Node3D { Name = "Head", Position = new Vector3(0, EyeStand, 0) };
        AddChild(_head);
        _cam = new Camera3D { Name = "Camera", Fov = 75f };
        _head.AddChild(_cam);
        _baseFov = _cam.Fov;

        _bubbles = MakeBubbles();
        _head.AddChild(_bubbles);
        _bubbles.Position = new Vector3(0, 0.05f, -0.25f); // rise from just in front of the face

        _airApexY = GlobalPosition.Y;
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        EmitSignal(SignalName.AirChanged, Air, MaxAir);
    }

    public void MakeCurrent()
    {
        _cam.Current = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Ignore break/place/fire input for a short window (used right after
    /// the mouse is re-captured on window focus, so that click doesn't also act).</summary>
    public void SuppressActionsFor(float seconds)
    {
        if (seconds > _actionLock) _actionLock = seconds;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!InputEnabled) return;

        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Accumulate only; the rotation is applied on the physics tick (ApplyMouseLook)
            // so look and movement share one cadence. Applying it here — at render-frame
            // rate, while the body position only updates at the physics rate — makes the
            // whole view jitter when moving and turning at once.
            _mouseLook += mm.Relative;
        }
        else if (e.IsActionPressed(GameInput.Actions.ToggleMode))
        {
            // No mouse-capture requirement: keyboard-only players toggle fly too.
            // (_UnhandledInput already returned above when input is disabled.)
            SetCreative(!Creative);
        }
    }

    /// <summary>Apply the mouse motion accumulated since the last physics tick. Run
    /// from <see cref="_PhysicsProcess"/> (not from input events) so the camera's
    /// rotation updates on the same cadence as the body's position — otherwise the
    /// two desync and the whole view jitters while moving and turning together.</summary>
    private void ApplyMouseLook()
    {
        if (_mouseLook == Vector2.Zero) return;
        if (InputEnabled && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = MouseSensitivity * Core.Settings.MouseSensitivity;
            RotateY(-_mouseLook.X * sens);
            float pitch = Mathf.Clamp(_head.Rotation.X - _mouseLook.Y * sens, -1.5f, 1.5f);
            _head.Rotation = new Vector3(pitch, 0, 0);
        }
        _mouseLook = Vector2.Zero;
    }

    /// <summary>Arrow-key / numpad camera look, so the game is fully playable
    /// without a mouse. Accumulates yaw on the body and pitch on the head at a
    /// settable degrees-per-second rate, clamped exactly like the mouse look.</summary>
    private void KeyboardLook(float dt)
    {
        if (!InputEnabled) return;
        float rate = Mathf.DegToRad(Core.Settings.KeyboardLookSpeed) * dt;
        float yaw = Input.GetActionStrength(GameInput.Actions.LookLeft)
                  - Input.GetActionStrength(GameInput.Actions.LookRight);
        float pitch = Input.GetActionStrength(GameInput.Actions.LookUp)
                    - Input.GetActionStrength(GameInput.Actions.LookDown);
        if (!Mathf.IsZeroApprox(yaw)) RotateY(yaw * rate);
        if (!Mathf.IsZeroApprox(pitch))
            _head.Rotation = new Vector3(
                Mathf.Clamp(_head.Rotation.X + pitch * rate, -1.5f, 1.5f), 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_actionLock > 0f) _actionLock -= dt;
        if (_hurtCd > 0f) _hurtCd -= dt;
        UpdateWaterState();
        UpdateCrouch();

        if (IsDead) { Velocity = Velocity.Lerp(Vector3.Zero, dt * 4f); MoveAndSlide(); return; }

        ApplyMouseLook();
        KeyboardLook(dt);

        // While the ground beneath us is still streaming in, hold position rather
        // than plummeting through not-yet-loaded terrain. (Creative flight ignores
        // gravity, so it never needs this.)
        if (!Creative && World != null && World.StreamingHold(GlobalPosition))
        {
            Velocity = Vector3.Zero;
            return;
        }

        if (Creative) FlyMove(dt);
        else if (InWater) SwimMove(dt);
        else GroundMove(dt);

        UpdateAir(dt);
        CheckHazards(dt);
        UpdateFov(dt);
    }

    /// <summary>Ease the camera FOV a touch wider while sprinting, for a felt sense of
    /// speed (a far more visible cue than the subtle HUD vignette tighten).</summary>
    private void UpdateFov(float dt)
    {
        if (_cam == null) return;
        float target = Sprinting ? _baseFov + 8f : _baseFov;
        _cam.Fov = Mathf.MoveToward(_cam.Fov, target, 45f * dt);
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
        Sprinting = IsOnFloor() && !_crouching && dir != Vector3.Zero
                    && InputEnabled && Input.IsActionPressed(GameInput.Actions.Sprint);

        var horiz = new Vector3(vel.X, 0, vel.Z);
        horiz = horiz.MoveToward(dir * speed, a * dt * speed);
        vel.X = horiz.X;
        vel.Z = horiz.Z;

        if (IsOnFloor() && InputEnabled && Input.IsActionJustPressed(GameInput.Actions.Jump) && !_crouching)
        {
            vel.Y = JumpVel;
            AudioManager.Play("jump", 1f, -4f);
        }

        Velocity = vel;
        MoveAndSlide();
        Footsteps(dt);
        TrackFall();
    }

    /// <summary>Play a footstep every stride's worth of ground distance, alternating
    /// pitch for a natural gait. Quiet, and skipped while airborne or barely moving.</summary>
    private void Footsteps(float dt)
    {
        if (!IsOnFloor()) { _stepDist = 0f; return; }
        float hsp = new Vector2(Velocity.X, Velocity.Z).Length();
        if (hsp < 0.7f) { _stepDist = 0f; return; }
        _stepDist += hsp * dt;
        float stride = _crouching ? 1.4f : (hsp > 5.5f ? 2.4f : 1.9f);
        if (_stepDist >= stride)
        {
            _stepDist = 0f;
            _stepFlip = !_stepFlip;
            AudioManager.Play($"step_{GroundMaterial()}", _stepFlip ? 1.08f : 0.92f, -5f);
            // A faint kick of material-tinted dust at the feet (skip over liquid/air).
            if (World != null)
            {
                var gb = World.GetBlock(FloorV(GlobalPosition + new Vector3(0, -0.2f, 0)));
                if (!gb.IsAir && !gb.IsLiquid)
                    Fx.Burst(GlobalPosition, FxKind.Dust, DustTint(gb.Material), 4);
            }
        }
    }

    /// <summary>A faint, material-appropriate dust colour for footstep/landing puffs.</summary>
    private static Color DustTint(MaterialSound m) => m switch
    {
        MaterialSound.Sand => new Color(0.85f, 0.74f, 0.50f, 0.5f),
        MaterialSound.Grass => new Color(0.45f, 0.58f, 0.30f, 0.5f),
        MaterialSound.Snow => new Color(0.92f, 0.95f, 1.00f, 0.5f),
        MaterialSound.Wood => new Color(0.55f, 0.43f, 0.28f, 0.5f),
        MaterialSound.Dirt => new Color(0.52f, 0.40f, 0.28f, 0.5f),
        MaterialSound.Metal => new Color(0.60f, 0.60f, 0.65f, 0.45f),
        MaterialSound.Cloth => new Color(0.70f, 0.66f, 0.60f, 0.45f),
        _ => new Color(0.62f, 0.62f, 0.64f, 0.5f), // Stone + fallback
    };

    /// <summary>The sound material of the block underfoot, for footstep audio.</summary>
    private MaterialSound GroundMaterial()
    {
        if (World == null) return MaterialSound.Dirt;
        var b = World.GetBlock(FloorV(GlobalPosition + new Vector3(0, -0.2f, 0)));
        return b.IsAir || b.IsLiquid ? MaterialSound.Dirt : b.Material;
    }

    private void SwimMove(float dt)
    {
        Sprinting = false;
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
            // Neutral buoyancy while fully submerged, so you can dive (Crouch), rise
            // (Jump) and hover to swim around and explore underwater rather than being
            // shoved back to the surface; once the head breaches, settle gently at the
            // waterline so an idle swimmer bobs there and can breathe.
            float targetY = HeadUnderwater ? 0f : -0.4f;
            vel.Y = Mathf.MoveToward(vel.Y, targetY, Gravity * 0.4f * dt);
        }

        // Let the player climb out onto a bank that's level with the surface.
        TrySwimClimb(dir, ref vel);

        Velocity = vel;
        MoveAndSlide();
        // no fall damage while swimming
        _airApexY = GlobalPosition.Y;
        _wasOnFloor = IsOnFloor();
    }

    /// <summary>When swimming and pushing toward a solid block whose top sits within
    /// about one cell of the feet (a bank), with clear space above it, drive the
    /// player upward. Combined with the forward input this lifts them out of the
    /// water and onto ground that is level with the surface.</summary>
    private void TrySwimClimb(Vector3 wishDir, ref Vector3 vel)
    {
        if (World == null) return;
        var flat = new Vector3(wishDir.X, 0, wishDir.Z);
        if (flat.LengthSquared() < 0.04f) return;            // need horizontal intent
        flat = flat.Normalized();
        Vector3 ahead = GlobalPosition + flat * (Radius + 0.25f);
        int ax = Mathf.FloorToInt(ahead.X), az = Mathf.FloorToInt(ahead.Z);
        int feetCell = Mathf.FloorToInt(GlobalPosition.Y + 0.1f);
        for (int y = feetCell; y <= feetCell + 1; y++)
        {
            var ledge = new Vector3I(ax, y, az);
            if (World.IsSolid(ledge)
                && !World.IsSolid(ledge + new Vector3I(0, 1, 0))
                && !World.IsSolid(ledge + new Vector3I(0, 2, 0)))
            {
                if (vel.Y < SwimClimbVel) vel.Y = SwimClimbVel;
                return;
            }
        }
    }

    private void FlyMove(float dt)
    {
        Sprinting = false;
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
            if (!InWater && fall > 0.4f)
                AudioManager.Play("land", 1f, Mathf.Lerp(-8f, 0f, Mathf.Min(fall / 6f, 1f)));
            // A small landing kick (even on a safe drop), scaling with the fall height.
            if (!InWater && fall > 1.2f)
                Fx.Shake(Mathf.Clamp((fall - 1.2f) / 8f, 0.06f, 0.5f));
            // A puff of material-tinted dust on landing, scaled by the fall height.
            if (!InWater && fall > 0.6f && World != null)
            {
                var lb = World.GetBlock(FloorV(GlobalPosition + new Vector3(0, -0.2f, 0)));
                if (!lb.IsAir && !lb.IsLiquid)
                    Fx.Burst(GlobalPosition, FxKind.Poof, DustTint(lb.Material),
                        Mathf.Clamp((int)(6 + fall * 2f), 6, 18));
            }
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
        float eye = _crouching ? EyeCrouch : EyeStand;
        // Sample low (just above the feet) so swimming engages as soon as the
        // lower body enters water and stays active until the player has climbed
        // almost fully out — which is what makes exiting onto a bank feel right.
        InWater = World.GetBlock(FloorV(p + new Vector3(0, 0.1f, 0))).IsLiquid;
        HeadUnderwater = World.GetBlock(FloorV(p + new Vector3(0, eye, 0))).IsLiquid;
        if (InWater && !_wasInWater)
        {
            AudioManager.Play("splash");
            Fx.Burst(GlobalPosition, FxKind.Splash, new Color(0.72f, 0.86f, 1f), 24);
            World.AddRipple(GlobalPosition, 1.0f); // B13 surface ripple (rendered in the water shader)
            Fx.Shake(0.08f);
        }
        if (_bubbles != null) _bubbles.Emitting = HeadUnderwater; // bubbles only while submerged
        _wasInWater = InWater;
    }

    /// <summary>A small stream of rising bubbles from the face, emitting only while the
    /// head is submerged. World-space (LocalCoords off) so they trail as you swim.</summary>
    private static GpuParticles3D MakeBubbles()
    {
        var mat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.22f,
            Direction = Vector3.Up,
            Spread = 22f,
            Gravity = new Vector3(0, 1.3f, 0), // bubbles rise
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.0f,
            ScaleMin = 0.4f,
            ScaleMax = 1.0f,
            Color = new Color(0.85f, 0.95f, 1f, 0.7f),
        };
        var mesh = new QuadMesh { Size = new Vector2(0.05f, 0.05f) };
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return new GpuParticles3D
        {
            Name = "Bubbles",
            Amount = 16,
            Lifetime = 1.6f,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = mat,
            DrawPass1 = mesh,
        };
    }

    private void UpdateCrouch()
    {
        bool want = InputEnabled && !Creative && !InWater && Input.IsActionPressed(GameInput.Actions.Crouch);
        // don't stand up into a solid block overhead
        if (!want && _crouching && World != null && World.IsSolid(FloorV(GlobalPosition + new Vector3(0, StandHeight, 0))))
            want = true;
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

    public bool IsAlive => !IsDead;
    public void TakeDamage(float amount, Node3D source) => Damage(amount, "hit");

    public void Damage(float amount, string cause = "")
    {
        if (IsDead || Creative || SafeMode || amount <= 0) return;
        Health = Mathf.Max(0, Health - amount);
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        EmitSignal(SignalName.Hurt, amount, cause);
        // Discrete hits/falls yelp; the tiny per-frame hazard ticks don't (and a
        // short cooldown keeps a flurry of hits from machine-gunning the sound).
        if (amount > 1.5f && _hurtCd <= 0f) { AudioManager.Play("hurt"); _hurtCd = 0.25f; }
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
        _drownTimer = 0;
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
