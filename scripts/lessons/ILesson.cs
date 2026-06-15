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

    /// <summary>Background music mood for this lesson. Default: Calm.</summary>
    Core.MusicMood Mood => Core.MusicMood.Calm;

    /// <summary>The lesson's objectives, or null for free exploration. Built after
    /// <see cref="Build"/> (and started by the host), so it can reference the NPCs,
    /// enemies and triggers the lesson just spawned.</summary>
    Quests.Quest BuildQuest(GameSession session) => null;
}

/// <summary>Catalogue of available lessons (used by the menu and CLI). The two hand-written C#
/// lessons always lead; any JSON lesson under <c>res://assets/lessons/*.json</c> is appended
/// (sorted by id), so a teacher can add a chapter by dropping in a file.</summary>
public static class LessonCatalog
{
    private const string Dir = "res://assets/lessons";
    private static List<ILesson> _all;

    public static IReadOnlyList<ILesson> List => _all ??= Build();

    public static ILesson Get(string id)
    {
        foreach (var l in List)
            if (l.Id == id) return l;
        // A genuine miss (typo, or a JSON lesson that failed to load) shouldn't silently
        // launch the wrong chapter — surface it before falling back.
        GD.PushError($"[Lessons] no lesson with id '{id}'; falling back to '{(List.Count > 0 ? List[0].Id : "<none>")}'");
        return List.Count > 0 ? List[0] : null;
    }

    private static List<ILesson> Build()
    {
        var list = new List<ILesson> { new DavidAndGoliath(), new CreationGarden() }; // C# first, stable
        foreach (string path in ScanJson())
        {
            var l = JsonLesson.FromFile(path);
            if (l == null || string.IsNullOrEmpty(l.Id)) continue;          // bad file -> skip (already warned)
            if (list.Exists(x => x.Id == l.Id))                              // a JSON id can't shadow a built-in
            { GD.PushWarning($"[Lessons] duplicate id '{l.Id}' in {path}; skipped"); continue; }
            list.Add(l);
        }
        return list;
    }

    private static List<string> ScanJson()
    {
        var paths = new List<string>();
        using var dir = DirAccess.Open(Dir);
        if (dir == null) return paths;                                       // folder absent -> just the C# lessons
        dir.ListDirBegin();
        for (string f = dir.GetNext(); f != ""; f = dir.GetNext())
            if (!dir.CurrentIsDir() && f.EndsWith(".json")) paths.Add($"{Dir}/{f}");
        dir.ListDirEnd();
        paths.Sort(System.StringComparer.OrdinalIgnoreCase);                 // deterministic order
        return paths;
    }
}
