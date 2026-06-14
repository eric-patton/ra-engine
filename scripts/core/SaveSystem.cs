using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>A saved sandbox world. Because the world is an infinite streamed,
/// seeded one, we persist only the seed plus the player's edit-deltas (every block
/// they changed from the generated baseline) — not the chunks themselves — along
/// with the player position, time of day and inventory.</summary>
public sealed class SaveData
{
    public string Name = "World";
    public int Seed;
    public long SavedUnix;
    public Vector3 PlayerPos;
    public float TimeOfDay = 0.4f;
    public readonly Dictionary<string, int> Inventory = new();
    public readonly List<(int x, int y, int z, string block)> Edits = new();
    public readonly List<(Vector3 pos, string text)> Signposts = new();
    public readonly List<(string name, Vector3 pos)> Waypoints = new();
}

/// <summary>Reads and writes <see cref="SaveData"/> to <c>user://saves/*.rsave</c>
/// using Godot's variant serialization.</summary>
public static class SaveSystem
{
    private const string Dir = "user://saves";
    private const int Version = 1;

    private static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        string s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "world" : s;
    }

    public static string PathFor(string name) => $"{Dir}/{Sanitize(name)}.rsave";
    public static bool Exists(string name) => FileAccess.FileExists(PathFor(name));

    public static void Save(SaveData d)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Dir));
        string finalPath = PathFor(d.Name);
        string tmpPath = finalPath + ".tmp";

        // 1. Serialize to a temp file first, so a crash or power loss mid-write can
        //    never corrupt the existing save (uniquely demoralizing for this audience).
        using (var f = FileAccess.Open(tmpPath, FileAccess.ModeFlags.Write))
        {
            if (f == null) { GD.PushError($"[Save] cannot write {tmpPath}: {FileAccess.GetOpenError()}"); return; }

            var inv = new Godot.Collections.Dictionary();
            foreach (var (block, count) in d.Inventory) inv[block] = count;

            var edits = new Godot.Collections.Array();
            foreach (var (x, y, z, block) in d.Edits)
                edits.Add(new Godot.Collections.Array { x, y, z, block });

            var signs = new Godot.Collections.Array();
            foreach (var (pos, text) in d.Signposts)
                signs.Add(new Godot.Collections.Array { pos, text });

            var waypoints = new Godot.Collections.Array();
            foreach (var (name, pos) in d.Waypoints)
                waypoints.Add(new Godot.Collections.Array { name, pos });

            var dict = new Godot.Collections.Dictionary
            {
                { "version", Version },
                { "name", d.Name },
                { "seed", d.Seed },
                { "saved", d.SavedUnix },
                { "player", d.PlayerPos },
                { "time", d.TimeOfDay },
                { "inventory", inv },
                { "edits", edits },
                { "signposts", signs },
                { "waypoints", waypoints },
            };
            f.StoreVar(dict);
        } // closed before the swap below

        // 2. Atomically swap the temp file into place, keeping the previous save as a
        //    single rolling .bak that Load() falls back on if the main file is bad.
        string absFinal = ProjectSettings.GlobalizePath(finalPath);
        string absTmp = ProjectSettings.GlobalizePath(tmpPath);
        string absBak = absFinal + ".bak";
        try
        {
            if (System.IO.File.Exists(absFinal))
                System.IO.File.Replace(absTmp, absFinal, absBak); // prev save -> .bak
            else
                System.IO.File.Move(absTmp, absFinal);
        }
        catch (System.Exception e)
        {
            // Replace can fail across some filesystems; fall back to a direct overwrite.
            GD.PushWarning($"[Save] atomic swap failed ({e.Message}); writing directly.");
            try { System.IO.File.Copy(absTmp, absFinal, true); System.IO.File.Delete(absTmp); }
            catch (System.Exception e2) { GD.PushError($"[Save] direct write failed: {e2.Message}"); }
        }
        GD.Print($"[Save] wrote '{d.Name}' (seed {d.Seed}, {d.Edits.Count} edits)");
    }

    public static SaveData Load(string name)
    {
        string path = PathFor(name);
        var d = LoadFrom(path, name);
        if (d != null) return d;
        // The main file is missing or corrupt — recover from the rolling backup.
        var bak = LoadFrom(path + ".bak", name);
        if (bak != null) GD.Print($"[Save] '{name}' main file unreadable; recovered from .bak");
        return bak;
    }

    private static SaveData LoadFrom(string path, string fallbackName)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var dict = f.GetVar().AsGodotDictionary();
        if (dict == null || dict.Count == 0) return null;
        string name = fallbackName;

        var d = new SaveData
        {
            Name = dict.TryGetValue("name", out var n) ? n.AsString() : name,
            Seed = dict.TryGetValue("seed", out var s) ? s.AsInt32() : 0,
            SavedUnix = dict.TryGetValue("saved", out var sv) ? sv.AsInt64() : 0,
            PlayerPos = dict.TryGetValue("player", out var p) ? p.AsVector3() : Vector3.Zero,
            TimeOfDay = dict.TryGetValue("time", out var t) ? t.AsSingle() : 0.4f,
        };
        if (dict.TryGetValue("inventory", out var invV))
            foreach (var kv in invV.AsGodotDictionary())
                d.Inventory[kv.Key.AsString()] = kv.Value.AsInt32();
        if (dict.TryGetValue("edits", out var editsV))
            foreach (var e in editsV.AsGodotArray())
            {
                var a = e.AsGodotArray();
                if (a.Count >= 4)
                    d.Edits.Add((a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsString()));
            }
        if (dict.TryGetValue("signposts", out var signsV))
            foreach (var e in signsV.AsGodotArray())
            {
                var a = e.AsGodotArray();
                if (a.Count >= 2) d.Signposts.Add((a[0].AsVector3(), a[1].AsString()));
            }
        if (dict.TryGetValue("waypoints", out var wpV))
            foreach (var e in wpV.AsGodotArray())
            {
                var a = e.AsGodotArray();
                if (a.Count >= 2) d.Waypoints.Add((a[0].AsString(), a[1].AsVector3()));
            }
        return d;
    }

    /// <summary>Headers of all saved worlds, newest first (name, seed, saved time).</summary>
    public static List<SaveData> List()
    {
        var list = new List<SaveData>();
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(Dir))) return list;
        using var dir = DirAccess.Open(Dir);
        if (dir == null) return list;
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".rsave")) continue;
            var d = Load(file.Substring(0, file.Length - ".rsave".Length));
            if (d != null) list.Add(d);
        }
        list.Sort((a, b) => b.SavedUnix.CompareTo(a.SavedUnix));
        return list;
    }

    public static void Delete(string name)
    {
        string abs = ProjectSettings.GlobalizePath(PathFor(name));
        if (System.IO.File.Exists(abs)) System.IO.File.Delete(abs);
    }
}
