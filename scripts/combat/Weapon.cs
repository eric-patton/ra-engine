namespace RAEngine.Combat;

/// <summary>A weapon definition. Melee weapons hit along a short camera ray;
/// ranged weapons launch a <see cref="Projectile"/>.</summary>
public sealed class Weapon
{
    public string Name;
    public bool Ranged;
    public float Damage;
    public float Range = 3.2f;        // melee reach
    public float Cooldown = 0.5f;
    public float ProjectileSpeed = 28f;
    public float ProjectileArc = 0.12f; // upward bias for thrown weapons
    public string ProjectileBlock = "cobblestone"; // texture flavour for the projectile

    public static Weapon Fist() => new() { Name = "Fist", Damage = 4f, Range = 2.8f, Cooldown = 0.4f };
    public static Weapon Sword() => new() { Name = "Sword", Damage = 14f, Range = 3.4f, Cooldown = 0.45f };
    public static Weapon Staff() => new() { Name = "Staff", Damage = 9f, Range = 3.6f, Cooldown = 0.5f };

    public static Weapon Sling() => new()
    {
        Name = "Sling", Ranged = true, Damage = 22f, Cooldown = 0.85f,
        ProjectileSpeed = 32f, ProjectileArc = 0.15f, ProjectileBlock = "cobblestone",
    };

    public static Weapon Bow() => new()
    {
        Name = "Bow", Ranged = true, Damage = 16f, Cooldown = 0.7f,
        ProjectileSpeed = 42f, ProjectileArc = 0.05f, ProjectileBlock = "oak_log",
    };
}
