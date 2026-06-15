namespace RAEngine.Quests;

/// <summary>The kind of player action an <see cref="Objective"/> watches for.</summary>
public enum ObjectiveKind { Talk, Defeat, Reach, Break, Place, Collect }

/// <summary>One checklist line: immutable data describing WHAT to watch for. The
/// behaviour that fires when it is met (a narration line, a fanfare, waking an
/// enemy) lives in <see cref="OnComplete"/>, so the tracker never needs to know
/// anything lesson-specific. The data core (Kind/Label/Key/Count) is serialization
/// shaped, ready for a future JSON lesson loader.</summary>
public sealed record Objective(
    ObjectiveKind Kind,
    string Label,                                  // HUD text, WITHOUT any "(k/N)" suffix
    string Key = null,                             // npc name / enemy type / trigger id / block name; null = "any"
    int Count = 1,                                 // how many are needed (name 3 animals, mine 5 stone)
    System.Action<GameSession> OnComplete = null)  // optional flourish the moment this objective is met
{
    /// <summary>Optional objectives are shown as guidance but do not gate quest
    /// completion — <see cref="QuestTracker.AllDone"/> ignores them.</summary>
    public bool Optional { get; init; }

    /// <summary>True when the HUD line should show a "(k/N)" progress counter.</summary>
    public bool Counted => Count > 1 || (Kind == ObjectiveKind.Talk && Key == null);
}

/// <summary>A lesson's full objective set plus an optional whole-quest finale.</summary>
public sealed class Quest
{
    public required System.Collections.Generic.IReadOnlyList<Objective> Objectives { get; init; }

    /// <summary>Runs once, after every objective is done (the lesson's climax).</summary>
    public System.Action<GameSession> OnComplete { get; init; }

    // Fluent builders keep lesson code terse; a future JSON loader builds the same records.
    public static Objective Talk(string npcName, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Talk, label, npcName, 1, onComplete) { Optional = optional };

    /// <summary>Talk to any <paramref name="count"/> distinct NPCs (e.g. "name 3 animals").</summary>
    public static Objective TalkAny(int count, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Talk, label, null, count, onComplete) { Optional = optional };

    public static Objective Defeat(string enemyTypeName, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Defeat, label, enemyTypeName, 1, onComplete) { Optional = optional };

    /// <summary>Reach a zone marked by a <see cref="RAEngine.World.NarrationTrigger"/> with this id.</summary>
    public static Objective Reach(string triggerId, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Reach, label, triggerId, 1, onComplete) { Optional = optional };

    public static Objective Break(string block, int count, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Break, label, block, count, onComplete) { Optional = optional };

    public static Objective Place(string block, int count, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Place, label, block, count, onComplete) { Optional = optional };

    public static Objective Collect(string block, int count, string label, System.Action<GameSession> onComplete = null, bool optional = false)
        => new(ObjectiveKind.Collect, label, block, count, onComplete) { Optional = optional };
}
