using Godot;

namespace RAEngine.Combat;

/// <summary>A drivable character visual — either the procedural box <see cref="MobRig"/>
/// or an imported rigged glTF (<see cref="RiggedModel"/>). Enemies and NPCs talk to this
/// interface, so either kind of art works behind the same gameplay code. Implementations
/// are always also a <see cref="Node3D"/> (added to the scene, scaled, defeat-tweened).</summary>
public interface ICharacterModel
{
    void Animate(float speed, float dt); // walk/idle chosen by movement speed
    void Attack();                       // a one-shot attack motion
    void Squash();                       // a brief hit reaction
    void SetFlash(bool on);              // red hit-flash across the whole model
}

/// <summary>Builds a character visual: an imported rigged glTF when <paramref name="modelScene"/>
/// points at a loadable scene, otherwise the procedural blocky <see cref="MobRig"/>. The
/// fallback means a null/mistyped/missing asset path degrades to the box model instead of
/// crashing — so art can be dropped in per archetype without touching gameplay code.</summary>
public static class CharacterModel
{
    public static Node3D BuildHumanoid(Color skin, Color cloth, Color accent,
                                       string modelScene = null, float modelYaw = 0f)
    {
        Node3D rigged = RiggedModel.TryLoad(modelScene, modelYaw);
        return rigged ?? MobModel.BuildHumanoid(skin, cloth, accent);
    }

    public static Node3D BuildBeast(Color fur, Color belly,
                                    string modelScene = null, float modelYaw = 0f)
    {
        Node3D rigged = RiggedModel.TryLoad(modelScene, modelYaw);
        return rigged ?? MobModel.BuildBeast(fur, belly);
    }
}
