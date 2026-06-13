using Godot;
using RAEngine.Core;
using RAEngine.PlayerSys;

namespace RAEngine.Combat;

/// <summary>Drives the player's equipped weapon. Melee weapons hit along a short
/// camera ray; ranged weapons launch a <see cref="Projectile"/>. Disabled in
/// build mode so it never competes with block editing for the primary button.</summary>
public partial class WeaponController : Node3D
{
    public Player Player;
    public Node ProjectileParent;
    public bool Enabled;
    public Weapon Current { get; private set; } = Weapon.Fist();

    [Signal] public delegate void WeaponChangedEventHandler(string name);

    private float _cooldown;
    private MeshInstance3D _viewmodel;

    public override void _Ready()
    {
        _viewmodel = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.5f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.36f, 0.2f) },
            Visible = false,
        };
    }

    public void AttachViewmodel()
    {
        if (Player == null || _viewmodel.GetParent() != null) return;
        Player.Head.AddChild(_viewmodel);
        _viewmodel.Position = new Vector3(0.32f, -0.26f, -0.55f);
        _viewmodel.RotationDegrees = new Vector3(-8, 6, 0);
    }

    public void Equip(Weapon w)
    {
        Current = w;
        _viewmodel.Visible = Enabled && w != null;
        if (_viewmodel.MaterialOverride is StandardMaterial3D m)
            m.AlbedoColor = w.Ranged ? new Color(0.55f, 0.5f, 0.45f) : new Color(0.7f, 0.7f, 0.75f);
        EmitSignal(SignalName.WeaponChanged, w?.Name ?? "");
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        _viewmodel.Visible = on && Current != null;
    }

    public override void _Process(double delta)
    {
        if (_cooldown > 0) _cooldown -= (float)delta;
        if (!Enabled || Player == null || Current == null) return;
        if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

        if (Input.IsActionPressed(GameInput.Actions.Primary) && _cooldown <= 0f)
            PrimaryAttack();
    }

    /// <summary>Perform the equipped weapon's attack now (used by input, scripts,
    /// and tests). Respects nothing but the equipped weapon — caller gates timing.</summary>
    public void PrimaryAttack()
    {
        if (Current == null) return;
        _cooldown = Current.Cooldown;
        if (Current.Ranged) FireRanged();
        else SwingMelee();
    }

    private void SwingMelee()
    {
        AnimateSwing();
        Camera3D cam = Player.Camera;
        Vector3 from = cam.GlobalPosition;
        Vector3 to = from + -cam.GlobalTransform.Basis.Z * Current.Range;
        var space = Player.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit["collider"].As<GodotObject>() is IDamageable dmg && dmg.IsAlive)
            dmg.TakeDamage(Current.Damage, Player);
    }

    private void FireRanged()
    {
        AnimateRecoil();
        Camera3D cam = Player.Camera;
        Vector3 fwd = -cam.GlobalTransform.Basis.Z;
        Vector3 vel = (fwd + Vector3.Up * Current.ProjectileArc).Normalized() * Current.ProjectileSpeed;
        Vector3 origin = cam.GlobalPosition + fwd * 0.6f;
        Projectile.Spawn(ProjectileParent ?? GetTree().Root, origin, vel, Current.Damage, Player);
    }

    private void AnimateSwing()
    {
        if (!_viewmodel.Visible) return;
        var t = CreateTween();
        t.TweenProperty(_viewmodel, "rotation_degrees", new Vector3(-55, 6, 0), 0.06);
        t.TweenProperty(_viewmodel, "rotation_degrees", new Vector3(-8, 6, 0), 0.16);
    }

    private void AnimateRecoil()
    {
        if (!_viewmodel.Visible) return;
        var t = CreateTween();
        t.TweenProperty(_viewmodel, "position", new Vector3(0.32f, -0.26f, -0.35f), 0.05);
        t.TweenProperty(_viewmodel, "position", new Vector3(0.32f, -0.26f, -0.55f), 0.15);
    }
}
