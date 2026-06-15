using System.Collections.Generic;
using Godot;
using RAEngine.Lessons;

namespace RAEngine.Core;

/// <summary>One campaign chapter: a lesson plus the chapter ids that must be
/// completed before it unlocks.</summary>
public sealed record Chapter(ILesson Lesson, string[] Requires);

/// <summary>The ordered campaign — the sequence a player works through. Order here
/// is deliberately independent of <see cref="LessonCatalog"/> (which the CLI flags
/// use directly), and the first chapter must have no prerequisites so a fresh save
/// locks nothing.</summary>
public static class Campaign
{
    public static readonly IReadOnlyList<Chapter> Chapters = new[]
    {
        new Chapter(LessonCatalog.Get("creation"), System.Array.Empty<string>()),
        new Chapter(LessonCatalog.Get("david"), new[] { "creation" }),
    };

    public static Chapter For(string lessonId)
    {
        foreach (var c in Chapters)
            if (c.Lesson.Id == lessonId) return c;
        return null;
    }

    /// <summary>The next chapter's lesson id after <paramref name="lessonId"/>, or null.</summary>
    public static string NextAfter(string lessonId)
    {
        for (int i = 0; i < Chapters.Count - 1; i++)
            if (Chapters[i].Lesson.Id == lessonId) return Chapters[i + 1].Lesson.Id;
        return null;
    }
}

/// <summary>Which chapters the player has completed. Drives the menu's ✓ marks and
/// the (currently advisory) unlock logic.</summary>
public sealed class CampaignProgress
{
    public readonly HashSet<string> Completed = new();

    public bool IsComplete(string id) => Completed.Contains(id);

    public void MarkComplete(string id)
    {
        if (!string.IsNullOrEmpty(id)) Completed.Add(id);
    }

    /// <summary>A chapter is unlocked when every id in its Requires is complete.
    /// Ids outside the campaign are treated as unlocked.</summary>
    public bool IsUnlocked(string id)
    {
        var ch = Campaign.For(id);
        if (ch == null) return true;
        foreach (string req in ch.Requires)
            if (!Completed.Contains(req)) return false;
        return true;
    }
}

/// <summary>Reads/writes <see cref="CampaignProgress"/> to <c>user://campaign.rprog</c>
/// using the same atomic tmp+swap+.bak pattern as <see cref="SaveSystem"/>, so a crash
/// mid-write never corrupts a child's hard-won progress.</summary>
public static class CampaignStore
{
    private const string FinalPath = "user://campaign.rprog";
    private const int Version = 1;

    public static CampaignProgress Load()
    {
        var p = new CampaignProgress();
        var dict = ReadDict(FinalPath) ?? ReadDict(FinalPath + ".bak");
        if (dict != null && dict.TryGetValue("completed", out var c))
            foreach (var v in c.AsGodotArray()) p.Completed.Add(v.AsString());
        return p;
    }

    public static void Save(CampaignProgress p)
    {
        string tmp = FinalPath + ".tmp";
        using (var f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (f == null) { GD.PushError($"[Campaign] cannot write {tmp}: {FileAccess.GetOpenError()}"); return; }
            var completed = new Godot.Collections.Array();
            foreach (string id in p.Completed) completed.Add(id);
            f.StoreVar(new Godot.Collections.Dictionary { { "version", Version }, { "completed", completed } });
        }

        string absFinal = ProjectSettings.GlobalizePath(FinalPath);
        string absTmp = ProjectSettings.GlobalizePath(tmp);
        string absBak = absFinal + ".bak";
        try
        {
            if (System.IO.File.Exists(absFinal)) System.IO.File.Replace(absTmp, absFinal, absBak);
            else System.IO.File.Move(absTmp, absFinal);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[Campaign] atomic swap failed ({e.Message}); writing directly.");
            try { System.IO.File.Copy(absTmp, absFinal, true); System.IO.File.Delete(absTmp); }
            catch (System.Exception e2) { GD.PushError($"[Campaign] direct write failed: {e2.Message}"); }
        }
    }

    /// <summary>Load, mark one chapter complete, and save.</summary>
    public static void MarkComplete(string id)
    {
        var p = Load();
        p.MarkComplete(id);
        Save(p);
    }

    private static Godot.Collections.Dictionary ReadDict(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var dict = f.GetVar().AsGodotDictionary();
        return (dict == null || dict.Count == 0) ? null : dict;
    }

    /// <summary>Remove the campaign progress files (used by the headless test).</summary>
    public static void DeleteAll()
    {
        foreach (string path in new[] { FinalPath, FinalPath + ".bak" })
        {
            string abs = ProjectSettings.GlobalizePath(path);
            if (System.IO.File.Exists(abs)) System.IO.File.Delete(abs);
        }
    }
}
