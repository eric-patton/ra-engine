using System.Collections.Generic;
using Godot;

namespace RAEngine.Lessons;

/// <summary>A self-contained, playable lesson. <see cref="Build"/> populates the
/// session's world with terrain, NPCs, enemies, narration and objectives, and
/// wires up the victory condition.</summary>
public interface ILesson
{
    string Id { get; }
    string Title { get; }
    string Subtitle { get; }
    Vector3 Spawn { get; }
    void Build(GameSession session);

    /// <summary>The time of day to pin this lesson to (0..1), or null to let the
    /// day/night cycle run. Default: a bright mid-morning.</summary>
    float? TimeOfDay => Core.EnvironmentController.Morning;
}

/// <summary>Catalogue of available lessons (used by the menu and CLI).</summary>
public static class LessonCatalog
{
    private static readonly List<ILesson> All = new()
    {
        new DavidAndGoliath(),
        new CreationGarden(),
    };

    public static IReadOnlyList<ILesson> List => All;

    public static ILesson Get(string id)
    {
        foreach (var l in All)
            if (l.Id == id) return l;
        return All.Count > 0 ? All[0] : null;
    }
}
