using Godot;
using RAEngine.Core;

namespace RAEngine.Combat;

/// <summary>A thrown/launched projectile (sling stone, arrow). Moves under
/// gravity and raycasts each step against world and mobs; damages the first
/// <see cref="IDamageable"/> it hits, otherwise stops on terrain.</summary>
public partial class Projectile : Node3D
{
    public Vector3 Velocity;
    public float Damage = 10f;
    public float Gravity = 16f;
    public float Life = 6f;
    public Node3D Shooter;
    public VoxelWorld World; // optional: lets the stone splash when it plops into water (B9)

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Life -= dt;
        if (Life <= 0) { QueueFree(); return; }

        Velocity += Vector3.Down * Gravity * dt;
        Vector3 from = GlobalPosition;
        Vector3 to = from + Velocity * dt;

        // Water has no collider, so the raycast passes through it — detect entry directly
        // and plop: a splash burst + a surface ripple ring, then stop.
        if (World != null &&
            World.GetBlock(new Vector3I(Mathf.FloorToInt(to.X), Mathf.FloorToInt(to.Y), Mathf.FloorToInt(to.Z))).IsLiquid)
        {
            GlobalPosition = to;
            Fx.Burst(to, FxKind.Splash, new Color(0.74f, 0.87f, 1f), 16);
            Fx.Ring(to, new Color(0.85f, 0.92f, 1f, 0.85f), 1.2f, 0.6f);
            AudioManager.Play("splash");
            QueueFree();
            return;
        }

        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        if (Shooter is CollisionObject3D co) query.Exclude = new Godot.Collections.Array<Rid> { co.GetRid() };
        var hit = space.IntersectRay(query);

        if (hit.Count > 0)
        {
            GlobalPosition = (Vector3)hit["position"];
            var collider = hit["collider"].As<GodotObject>();
            if (collider is IDamageable dmg && (Node)collider != Shooter)
                dmg.TakeDamage(Damage, this);
            QueueFree();
            return;
        }

        GlobalPosition = to;
        // Only orient when the velocity isn't (near) parallel to Up, or LookAt errors.
        if (Velocity.LengthSquared() > 0.0001f && Mathf.Abs(Velocity.Normalized().Y) < 0.985f)
            LookAt(to + Velocity, Vector3.Up);
    }

    public static Projectile Spawn(Node parent, Vector3 origin, Vector3 velocity, float damage, Node3D shooter, Texture2D icon = null, VoxelWorld world = null)
    {
        var p = new Projectile { Velocity = velocity, Damage = damage, Shooter = shooter, World = world };
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.6f, 0.6f, 0.62f),
                AlbedoTexture = icon,
                Roughness = 0.9f,
            },
        };
        p.AddChild(mesh);
        parent.AddChild(p);
        p.GlobalPosition = origin;
        return p;
    }
}
