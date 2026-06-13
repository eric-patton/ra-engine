using Godot;

namespace RAEngine.Core;

/// <summary>Anything that can take damage from weapons or projectiles.</summary>
public interface IDamageable
{
    void TakeDamage(float amount, Node3D source);
    bool IsAlive { get; }
}
