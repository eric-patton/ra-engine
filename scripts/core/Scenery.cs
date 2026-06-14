using Godot;

namespace RAEngine.Core;

/// <summary>Sky, sun and ambient lighting setup shared by gameplay and tests. Now a
/// thin wrapper that drops in an <see cref="EnvironmentController"/> (day/night sky,
/// sun, moon, clouds, stars); callers keep working unchanged.</summary>
public static class Scenery
{
    public static EnvironmentController AddDaylight(Node parent)
    {
        var env = new EnvironmentController { Name = "Environment" };
        parent.AddChild(env);
        return env;
    }
}
