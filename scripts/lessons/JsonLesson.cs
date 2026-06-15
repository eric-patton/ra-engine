using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using RAEngine.Combat;
using RAEngine.Core;
using RAEngine.Dialogue;
using RAEngine.NpcSys;
using RAEngine.Quests;

namespace RAEngine.Lessons;

/// <summary>An <see cref="ILesson"/> driven entirely by a data file
/// (res://assets/lessons/&lt;id&gt;.json), so a non-coder can add a campaign chapter without
/// recompiling. Holds a parsed <see cref="LessonDoc"/> and interprets it against the existing
/// session/world/quest APIs in <see cref="Build"/>/<see cref="BuildQuest"/>. Every parse failure
/// degrades to a safe default plus a GD warning, never a throw — one typo never crashes a class.</summary>
public sealed class JsonLesson : ILesson
{
    private readonly LessonDoc _doc;
    private readonly List<(string Name, Enemy Enemy)> _enemies = new(); // every spawn, for "wake" (names can repeat)

    private JsonLesson(LessonDoc doc) => _doc = doc;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,   // teachers can leave // notes
        AllowTrailingCommas = true,
    };

    public static JsonLesson FromFile(string resPath)
    {
        if (!FileAccess.FileExists(resPath)) { GD.PushError($"[Lessons] missing {resPath}"); return null; }
        return FromJson(FileAccess.GetFileAsString(resPath), resPath);
    }

    /// <summary>Parse a lesson from raw JSON text (used by FromFile and by tests).</summary>
    public static JsonLesson FromJson(string text, string source)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<LessonDoc>(text, Opts);
            if (doc == null || string.IsNullOrEmpty(doc.Id))
            { GD.PushError($"[Lessons] {source}: missing required \"id\""); return null; }
            return new JsonLesson(doc);
        }
        catch (Exception e) { GD.PushError($"[Lessons] parse error in {source}: {e.Message}"); return null; }
    }

    // ---- ILesson ----------------------------------------------------------
    public string Id => _doc.Id;
    public string Title => string.IsNullOrEmpty(_doc.Title) ? _doc.Id : _doc.Title;
    public string Subtitle => _doc.Subtitle ?? "";
    public Vector3 Spawn => Vec(_doc.Spawn) ?? new Vector3(0, 3, 0);
    public float? TimeOfDay => ParseTime(_doc.Time);
    public Core.MusicMood Mood => ParseMood(_doc.Mood);

    // ---- campaign placement (read by Campaign.Chapters) -------------------
    public bool IsChapter => _doc.Chapter != null;
    public string[] ChapterRequires => _doc.Chapter?.Requires ?? System.Array.Empty<string>();
    public int ChapterOrder => _doc.Chapter?.Order ?? 0;

    public void Build(GameSession session)
    {
        var w = session.World;
        BuildTerrain(w, _doc.Terrain);
        if (_doc.Terrain.Count == 0) WorldGen.FlatGround(w, 0, 31, 0, 31, 0); // forgiving default canvas
        w.MarkAllDirty();
        w.RebuildAllNow();

        if (string.Equals(_doc.Mode, "build", StringComparison.OrdinalIgnoreCase))
            session.SetMode(GameSession.Mode.Build);
        if (_doc.Disarm) session.Weapons.Equip(null);

        if (_doc.Intro is { Length: > 0 }) session.Narrator.ShowMany(_doc.Intro);

        foreach (var n in _doc.Npcs) BuildNpc(w, n);
        foreach (var e in _doc.Enemies) BuildEnemy(session, e);
        foreach (var t in _doc.Narrations) BuildNarration(session, t);
    }

    public Quest BuildQuest(GameSession session)
    {
        if (_doc.Quest is not { Objectives.Count: > 0 }) return null;
        var objs = new List<Objective>();
        foreach (var od in _doc.Quest.Objectives)
        {
            var o = BuildObjective(od);
            if (o != null) objs.Add(o);
        }
        return objs.Count == 0 ? null
            : new Quest { Objectives = objs, OnComplete = MakeEffect(_doc.Quest.OnComplete) };
    }

    // ---- terrain ----------------------------------------------------------
    private static void BuildTerrain(VoxelWorld w, List<TerrainOp> ops)
    {
        foreach (var op in ops)
        {
            switch (op.Op?.Trim().ToLowerInvariant())
            {
                case "flat":
                    WorldGen.FlatGround(w, op.X0, op.X1, op.Z0, op.Z1, op.Y,
                        op.Top ?? "grass", op.Fill ?? "dirt", op.Base ?? "stone", op.Depth > 0 ? op.Depth : 4);
                    break;
                case "set":
                    if (TryVec3I(op.At, out var sp)) Place(w, sp, BlockId(op.Block));
                    else GD.PushWarning("[Lessons] 'set' op needs \"at\":[x,y,z]");
                    break;
                case "fill":
                    FillBox(w, op.From, op.To, BlockId(op.Block));
                    break;
                case "clear":
                    FillBox(w, op.From, op.To, 0);
                    break;
                case "line":
                    FillLine(w, op.From, op.To, BlockId(op.Block));
                    break;
                case "mound":
                    Mound(w, op.X, op.Z, op.R, op.H, op.Top ?? "grass", op.Fill ?? "dirt");
                    break;
                case "tree":
                    if (TryVec3I(op.At, out var tp)) WorldGen.Tree(w, tp, op.Height > 0 ? op.Height : 4);
                    else GD.PushWarning("[Lessons] 'tree' op needs \"at\"");
                    break;
                case "hut":
                    if (TryVec3I(op.At, out var hp))
                        WorldGen.BuildHut(w, hp, op.W > 0 ? op.W : 5, op.D > 0 ? op.D : 5, op.H > 0 ? op.H : 4,
                            op.Wall ?? "mud_brick", op.Roof ?? "thatch");
                    else GD.PushWarning("[Lessons] 'hut' op needs \"at\"");
                    break;
                case "tent":
                    if (TryVec3I(op.At, out var ep)) Tent(w, ep, op.Cloth ?? "cloth_cream");
                    else GD.PushWarning("[Lessons] 'tent' op needs \"at\"");
                    break;
                case "altar":
                    if (TryVec3I(op.At, out var ap)) Altar(w, ap);
                    else GD.PushWarning("[Lessons] 'altar' op needs \"at\"");
                    break;
                default:
                    GD.PushWarning($"[Lessons] unknown terrain op '{op.Op}'");
                    break;
            }
        }
    }

    // All terrain writes use LessonBuild so the armed quest tracker never counts them as progress.
    private static void Place(VoxelWorld w, Vector3I p, ushort id)
        => w.SetBlock(p, id, false, BlockChangeCause.LessonBuild);

    private static void FillBox(VoxelWorld w, float[] from, float[] to, ushort id)
    {
        if (!TryVec3I(from, out var a) || !TryVec3I(to, out var b))
        { GD.PushWarning("[Lessons] 'fill'/'clear' op needs \"from\" and \"to\""); return; }
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        int y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        int z0 = Math.Min(a.Z, b.Z), z1 = Math.Max(a.Z, b.Z);
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            w.SetBlock(x, y, z, id, false, BlockChangeCause.LessonBuild);
    }

    private static void FillLine(VoxelWorld w, float[] from, float[] to, ushort id)
    {
        if (!TryVec3I(from, out var a) || !TryVec3I(to, out var b))
        { GD.PushWarning("[Lessons] 'line' op needs \"from\" and \"to\""); return; }
        int dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        int steps = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
        if (steps == 0) { Place(w, a, id); return; }
        for (int i = 0; i <= steps; i++)
            Place(w, new Vector3I(
                a.X + (int)Math.Round((double)dx * i / steps),
                a.Y + (int)Math.Round((double)dy * i / steps),
                a.Z + (int)Math.Round((double)dz * i / steps)), id);
    }

    // Ports of the private helpers the C# lessons use, so JSON can express the same scenes.
    private static void Mound(VoxelWorld w, int cx, int cz, int radius, int height, string topB, string fillB)
    {
        if (radius <= 0 || height <= 0) { GD.PushWarning("[Lessons] 'mound' needs r>0 and h>0"); return; }
        ushort top = BlockId(topB), fill = BlockId(fillB);
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
        {
            float d = Mathf.Sqrt(x * x + z * z) / radius;
            if (d > 1f) continue;
            int h = (int)Mathf.Round((1f - d) * height);
            for (int y = 1; y <= h; y++) Place(w, new Vector3I(cx + x, y, cz + z), y == h ? top : fill);
        }
    }

    private static void Tent(VoxelWorld w, Vector3I o, string cloth)
    {
        ushort c = BlockId(cloth);
        for (int x = 0; x < 5; x++) for (int z = 0; z < 5; z++) Place(w, o + new Vector3I(x, 0, z), c);
        for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) Place(w, o + new Vector3I(1 + x, 1, 1 + z), c);
        Place(w, o + new Vector3I(2, 2, 2), c);
        Place(w, o + new Vector3I(2, 0, 0), 0); // doorway
    }

    private static void Altar(VoxelWorld w, Vector3I o)
    {
        ushort stone = BlockId("stone"), fire = BlockId("altar_fire");
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            Place(w, o + new Vector3I(x, 1, z), stone);
            Place(w, o + new Vector3I(x, 2, z), stone);
        }
        Place(w, o + new Vector3I(0, 3, 0), fire);
    }

    // ---- entities ---------------------------------------------------------
    private static void BuildNpc(VoxelWorld w, NpcDto n)
    {
        var npc = new Npc { NpcName = n.Name ?? "Villager", Beast = n.Beast, Dialogue = ResolveDialogue(n) };
        npc.Skin = Col(n.Skin, npc.Skin);
        npc.Robe = Col(n.Robe, npc.Robe);
        npc.Accent = Col(n.Accent, npc.Accent);
        w.AddChild(npc);
        npc.GlobalPosition = Vec(n.Pos) ?? Vector3.Zero;
    }

    private static DialogueData ResolveDialogue(NpcDto n)
    {
        if (n.Dialogue is { Nodes.Count: > 0 }) return n.Dialogue;             // inline branching
        if (n.Say is { Length: > 0 }) return DialogueData.Linear(n.Name ?? "", n.Say); // inline linear
        if (!string.IsNullOrEmpty(n.DialogueId)) return Dialogues.Load(n.DialogueId);   // file reference
        return null;                                                          // a silent prop
    }

    private void BuildEnemy(GameSession session, EnemyDto e)
    {
        var type = MakeEnemyType(e.Type);
        // The effective name is the Defeat key + wake target. Default it to the author's `type`
        // token (predictable) rather than the hidden factory name (e.g. "giant" -> "Goliath").
        type.Name = !string.IsNullOrEmpty(e.Name) ? e.Name
            : !string.IsNullOrWhiteSpace(e.Type) ? e.Type.Trim()
            : type.Name;
        if (e.Health is float hp) type.Health = hp;
        if (e.Scale is float sc) type.Scale = sc;
        var enemy = session.SpawnEnemy(type, Vec(e.Pos) ?? Vector3.Zero);
        if (e.Dormant) enemy.Target = null;
        _enemies.Add((type.Name, enemy));
    }

    private static EnemyType MakeEnemyType(string type)
    {
        switch (type?.Trim().ToLowerInvariant())
        {
            case "wolf": return EnemyType.Wolf();
            case "giant": return EnemyType.Giant();
            case "soldier": case null: case "": return EnemyType.Soldier();
            default:
                GD.PushWarning($"[Lessons] unknown enemy type '{type}', using Soldier");
                return EnemyType.Soldier();
        }
    }

    private static void BuildNarration(GameSession session, NarrationDto t)
    {
        Vector3 pos = Vec(t.Pos) ?? Vector3.Zero;
        Vector3 size = Vec(t.Size) ?? new Vector3(4, 4, 4);
        string[] lines = t.Lines ?? System.Array.Empty<string>();
        if (!string.IsNullOrEmpty(t.Id))
        {
            var trig = session.AddTrigger(pos, size, t.Id, lines); // wires Entered -> a Reach objective
            trig.Once = t.Once;
        }
        else
        {
            var trig = RAEngine.World.NarrationTrigger.Create(pos, size, session.Narrator, lines);
            trig.Once = t.Once;
            session.World.AddChild(trig);
        }
    }

    // ---- quest ------------------------------------------------------------
    private Objective BuildObjective(ObjectiveDto od)
    {
        var on = MakeEffect(od.OnComplete);
        int count = Math.Max(1, od.Count);
        // Treat an empty-string key like an omitted one (so talk with no key => "talk any").
        string key = string.IsNullOrEmpty(od.Key) ? null : od.Key;
        string blockKey = !string.IsNullOrEmpty(od.Block) ? od.Block : key;
        switch (od.Kind?.Trim().ToLowerInvariant())
        {
            case "talk":
                return key == null
                    ? Quest.TalkAny(count, od.Label, on, od.Optional)
                    : Quest.Talk(key, od.Label, on, od.Optional);
            case "defeat": // direct ctor so a "defeat N of a type" (Count>1) is expressible
                return new Objective(ObjectiveKind.Defeat, od.Label, key, count, on) { Optional = od.Optional };
            case "reach":
                return Quest.Reach(key, od.Label, on, od.Optional);
            case "break":
                return Quest.Break(blockKey, count, od.Label, on, od.Optional);
            case "place":
                return Quest.Place(blockKey, count, od.Label, on, od.Optional);
            case "collect":
                return Quest.Collect(blockKey, count, od.Label, on, od.Optional);
            default:
                GD.PushWarning($"[Lessons] unknown objective kind '{od.Kind}' ({od.Label})");
                return null;
        }
    }

    // ---- effect language --------------------------------------------------
    private Action<GameSession> MakeEffect(EffectDto e)
    {
        if (e == null) return null;
        return s =>
        {
            if (!string.IsNullOrEmpty(e.Wake)) WakeEnemy(s, e.Wake);          // gameplay state first
            if (e.Narrate is { Length: > 0 }) s.Narrator.ShowMany(e.Narrate); // story
            if (!string.IsNullOrEmpty(e.Center)) s.Hud.ShowCenter(e.Center, 0f);
            if (!string.IsNullOrEmpty(e.Banner)) s.Hud.ShowBanner(e.Banner, 3f);
            if (!string.IsNullOrEmpty(e.Mood)) AudioManager.SetMusicMood(ParseMood(e.Mood)); // audio
            if (!string.IsNullOrEmpty(e.Sound)) AudioManager.Play(e.Sound);
            if (e.Fx != null)                                                 // particles last
                Fx.Burst(Vec(e.Fx.At) ?? Vector3.Zero, ParseFx(e.Fx.Kind), Col(e.Fx.Tint, Colors.White), e.Fx.Count);
        };
    }

    private void WakeEnemy(GameSession s, string name)
    {
        bool any = false;
        foreach (var (n, en) in _enemies)
        {
            if (!GodotObject.IsInstanceValid(en)) continue;
            if (name == "*" || string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            { en.Target = s.Player; any = true; }
        }
        if (!any && name != "*") GD.PushWarning($"[Lessons] wake: no enemy named '{name}'");
    }

    // ---- forgiving scalar parsers -----------------------------------------
    private static Vector3? Vec(float[] a)
        => a is { Length: >= 3 } ? new Vector3(a[0], a[1], a[2]) : (Vector3?)null;

    private static bool TryVec3I(float[] a, out Vector3I v)
    {
        if (a is { Length: >= 3 })
        { v = new Vector3I((int)Mathf.Round(a[0]), (int)Mathf.Round(a[1]), (int)Mathf.Round(a[2])); return true; }
        v = default;
        return false;
    }

    private static Color Col(float[] a, Color fallback)
        => a is { Length: >= 3 } ? new Color(a[0], a[1], a[2], a.Length >= 4 ? a[3] : 1f) : fallback;

    private static ushort BlockId(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "air") return 0;
        if (BlockRegistry.TryId(name, out ushort id)) return id;
        GD.PushWarning($"[Lessons] unknown block '{name}'");
        return 0;
    }

    private static float? ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return EnvironmentController.Morning;
        switch (s.Trim().ToLowerInvariant())
        {
            case "dawn": return EnvironmentController.Dawn;
            case "morning": return EnvironmentController.Morning;
            case "noon": case "midday": return EnvironmentController.Noon;
            case "dusk": case "evening": return EnvironmentController.Dusk;
            case "night": case "midnight": return EnvironmentController.Night;
            case "cycle": case "none": return null; // let the day/night cycle run
        }
        if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f))
            return Mathf.Clamp(f, 0f, 1f);
        GD.PushWarning($"[Lessons] unknown time '{s}', using morning");
        return EnvironmentController.Morning;
    }

    private static Core.MusicMood ParseMood(string s)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "hope": return Core.MusicMood.Hope;
            case "solemn": return Core.MusicMood.Solemn;
            case "calm": case null: case "": return Core.MusicMood.Calm;
            default:
                GD.PushWarning($"[Lessons] unknown mood '{s}', using calm");
                return Core.MusicMood.Calm;
        }
    }

    private static FxKind ParseFx(string s) => (s?.Trim().ToLowerInvariant()) switch
    {
        "debris" => FxKind.Debris,
        "splash" => FxKind.Splash,
        "sparkle" => FxKind.Sparkle,
        "dust" => FxKind.Dust,
        _ => FxKind.Poof,
    };
}
