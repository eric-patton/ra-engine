using Godot;
using RAEngine.Core;

namespace RAEngine.PlayerSys;

/// <summary>A bodiless free-fly "photo mode" camera for framing screenshots and
/// cinematic angles. Toggled with P (see <see cref="GameSession"/>): while active
/// it is the current camera and the player is frozen.
///
/// Two control modes (T toggles), both shown in the on-screen readout:
///  • Continuous (default, best by hand) — WASD fly relative to facing, Space/Ctrl
///    up/down, Shift boost, mouse + arrow/numpad look, mouse wheel adjusts speed.
///  • Step (best for tooling via the godot-ai bridge) — every WASD/Space/Ctrl press
///    nudges an exact distance and every arrow press turns an exact angle, with
///    Shift for a finer step. Because each press is one discrete increment it is
///    immune to input round-trip timing, so the camera can't overshoot — unlike a
///    held key, which keeps moving for the whole (unpredictable) hold.
///
/// The bridge can also read the transform back with get_node_info (the native
/// position/rotation/fov, plus the exported <see cref="AimAt"/>) to close the loop.
/// Orientation is always read from the live <see cref="Node3D.Rotation"/>, so an
/// externally-set rotation is honoured rather than snapped back.</summary>
public partial class FreeCam : Camera3D
{
    public float MouseSensitivity = 0.0026f;

    private const float MinSpeed = 0.5f, MaxSpeed = 80f, PitchLimit = 1.55f; // ~89°
    private const float MoveStepCoarse = 2f, MoveStepFine = 0.5f;
    private static readonly float RotStepCoarse = Mathf.DegToRad(15f);
    private static readonly float RotStepFine = Mathf.DegToRad(5f);

    private float _speed = 8f;
    private Vector2 _mouseLook;   // relative mouse accumulated since the last frame

    public bool Active { get; private set; }
    /// <summary>When true, WASD/arrow presses move/turn in exact fixed increments
    /// (precise, timing-independent) instead of flowing continuously.</summary>
    public bool StepMode { get; private set; }

    public override void _Ready() => SetActive(false);

    /// <summary>Enter/leave photo mode: become (or stop being) the current camera and
    /// run (or pause) the fly controls. The session seeds the transform first.</summary>
    public void SetActive(bool on)
    {
        Active = on;
        Current = on;
        _mouseLook = Vector2.Zero;
        SetProcess(on);
        SetProcessUnhandledInput(on);
    }

    /// <summary>Tooling convenience (readable via get_node_info): assign a world point to
    /// aim the camera at it, keeping its current position. Looking straight up/down is
    /// handled explicitly (a plain LookAt(up) is undefined there). Reads back as the
    /// camera position — there is no separately stored value.</summary>
    [Export]
    public Vector3 AimAt
    {
        get => GlobalPosition;
        set
        {
            if (!IsInsideTree()) return;
            Vector3 dir = value - GlobalPosition;
            if (dir.LengthSquared() < 1e-6f) return;
            dir = dir.Normalized();
            if (Mathf.Abs(dir.Y) > 0.9995f) // near-vertical: set pitch directly, keep yaw
            {
                Vector3 r = Rotation;
                r.X = Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f));
                r.Z = 0f;
                Rotation = r;
            }
            else LookAt(value, Vector3.Up);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Active) return;
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
            _mouseLook += mm.Relative;
        else if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _speed = Mathf.Min(_speed * 1.12f, MaxSpeed);
            else if (mb.ButtonIndex == MouseButton.WheelDown) _speed = Mathf.Max(_speed / 1.12f, MinSpeed);
        }
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        float dt = (float)delta;
        if (Input.IsActionJustPressed(GameInput.Actions.FreeCamStep)) StepMode = !StepMode;
        ApplyMouseLook();
        if (StepMode) ApplyStep();
        else { ApplyContinuousLook(dt); ApplyContinuousMove(dt); }
    }

    /// <summary>Turn yaw/pitch by deltas onto the camera's live rotation, clamping pitch
    /// and keeping roll at zero. Working from the current <see cref="Node3D.Rotation"/>
    /// (not a cached yaw/pitch) means a rotation set externally is respected.</summary>
    private void RotateBy(float dyaw, float dpitch)
    {
        Vector3 rot = Rotation;
        rot.Y += dyaw;
        rot.X = Mathf.Clamp(rot.X + dpitch, -PitchLimit, PitchLimit);
        rot.Z = 0f;
        Rotation = rot;
    }

    private void ApplyMouseLook()
    {
        if (_mouseLook == Vector2.Zero) return;
        float sens = MouseSensitivity * Settings.MouseSensitivity;
        RotateBy(-_mouseLook.X * sens, -_mouseLook.Y * sens);
        _mouseLook = Vector2.Zero;
    }

    private void ApplyContinuousLook(float dt)
    {
        float rate = Mathf.DegToRad(Settings.KeyboardLookSpeed) * dt;
        float dyaw = (Input.GetActionStrength(GameInput.Actions.LookLeft)
                    - Input.GetActionStrength(GameInput.Actions.LookRight)) * rate;
        float dpitch = (Input.GetActionStrength(GameInput.Actions.LookUp)
                      - Input.GetActionStrength(GameInput.Actions.LookDown)) * rate;
        if (!Mathf.IsZeroApprox(dyaw) || !Mathf.IsZeroApprox(dpitch)) RotateBy(dyaw, dpitch);
    }

    /// <summary>Fly relative to facing. No inertia — movement is direct for predictable
    /// framing. Shift triples the speed; the mouse wheel sets the base speed.</summary>
    private void ApplyContinuousMove(float dt)
    {
        Vector2 inp = Input.GetVector(GameInput.Actions.Left, GameInput.Actions.Right,
                                      GameInput.Actions.Forward, GameInput.Actions.Back);
        Vector3 dir = GlobalTransform.Basis * new Vector3(inp.X, 0, inp.Y);
        if (Input.IsActionPressed(GameInput.Actions.Jump)) dir.Y += 1f;
        if (Input.IsActionPressed(GameInput.Actions.Crouch)) dir.Y -= 1f;
        if (dir.LengthSquared() < 1e-6f) return;
        float speed = Input.IsActionPressed(GameInput.Actions.Sprint) ? _speed * 3f : _speed;
        GlobalPosition += dir.Normalized() * speed * dt;
    }

    /// <summary>Discrete, fixed-increment control: one press = one exact step (Shift =
    /// finer). Each key is read with IsActionJustPressed, so holding it — for however
    /// long an input round-trip takes — still applies a single step, never a drift.</summary>
    private void ApplyStep()
    {
        bool fine = Input.IsActionPressed(GameInput.Actions.Sprint);
        float rs = fine ? RotStepFine : RotStepCoarse;
        float ms = fine ? MoveStepFine : MoveStepCoarse;

        float dyaw = 0f, dpitch = 0f;
        if (Input.IsActionJustPressed(GameInput.Actions.LookLeft)) dyaw += rs;
        if (Input.IsActionJustPressed(GameInput.Actions.LookRight)) dyaw -= rs;
        if (Input.IsActionJustPressed(GameInput.Actions.LookUp)) dpitch += rs;
        if (Input.IsActionJustPressed(GameInput.Actions.LookDown)) dpitch -= rs;
        if (dyaw != 0f || dpitch != 0f) RotateBy(dyaw, dpitch);

        Vector3 local = Vector3.Zero;
        if (Input.IsActionJustPressed(GameInput.Actions.Forward)) local.Z -= 1f;
        if (Input.IsActionJustPressed(GameInput.Actions.Back)) local.Z += 1f;
        if (Input.IsActionJustPressed(GameInput.Actions.Left)) local.X -= 1f;
        if (Input.IsActionJustPressed(GameInput.Actions.Right)) local.X += 1f;
        if (local != Vector3.Zero)
            GlobalPosition += (GlobalTransform.Basis * local).Normalized() * ms;
        if (Input.IsActionJustPressed(GameInput.Actions.Jump)) GlobalPosition += Vector3.Up * ms;
        if (Input.IsActionJustPressed(GameInput.Actions.Crouch)) GlobalPosition += Vector3.Down * ms;
    }

    /// <summary>A compact, screenshot-readable summary of where the camera sits and
    /// looks (the session paints this onto the HUD while photo mode is on). Yaw/pitch
    /// are wrapped to a readable ±180°.</summary>
    public string StatusLine()
    {
        Vector3 p = GlobalPosition;
        float yaw = Mathf.Wrap(Mathf.RadToDeg(Rotation.Y), -180f, 180f);
        float pitch = Mathf.Wrap(Mathf.RadToDeg(Rotation.X), -180f, 180f);
        string mode = StepMode
            ? $"STEP {MoveStepCoarse:0.#}m / 15° (Shift: fine)"
            : $"fly  speed {_speed:0.#}";
        return $"pos {p.X:F1}, {p.Y:F1}, {p.Z:F1}    fov {Fov:F0}\n"
             + $"yaw {yaw:F0}°  pitch {pitch:F0}°    {mode}";
    }
}
