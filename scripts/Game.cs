using Godot;

namespace RAEngine;

/// <summary>
/// Root node of the running game. For now it only confirms the C# assembly
/// loads and supports a headless smoke test; real systems are wired in later
/// milestones.
/// </summary>
public partial class Game : Node3D
{
    private GameSession _session;
    private UI.MainMenu _menu;
    private UI.SaveMenu _saveMenu;
    private string _saveName;

    private static readonly (string block, int count)[] DefaultKit =
    {
        ("grass", 64), ("dirt", 64), ("stone", 64), ("cobblestone", 64),
        ("planks", 64), ("oak_log", 32), ("mud_brick", 64), ("leaves", 32), ("lamp", 8),
    };

    public override void _Ready()
    {
        var version = (string)Engine.GetVersionInfo()["string"];
        GD.Print($"[RA] Game ready on Godot {version}");
        Core.Settings.Load();
        AddChild(new Core.AudioManager { Name = "Audio" }); // persistent, app-wide sound
        AddChild(new Core.Fx { Name = "Fx" });               // persistent, app-wide particles + screen effects

        string mode = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--")) { mode = arg; break; } // first flag wins, deterministically

        switch (mode)
        {
            case "--smoke":
                GD.Print("[RA] smoke: C# assembly loaded OK");
                QuitSoon();
                break;
            case "--gen-textures":
                Tools.TextureForge.GenerateAll();
                QuitSoon();
                break;
            case "--gen-audio":
                GenAudio();
                QuitSoon();
                break;
            case "--test-blocks":
                Core.BlockRegistry.EnsureInit();
                var tex = Core.BlockTextures.Build();
                GD.Print($"[RA] test-blocks: blocks={Core.BlockRegistry.Count} layers={tex.LayerCount} " +
                         $"grass.top.layer={Core.BlockRegistry.Get("grass").FaceLayer[(int)Core.Face.PosY]}");
                QuitSoon();
                break;
            case "--test-world":
                RunWorldTest();
                break;
            case "--test-player":
                RunPlayerTest();
                break;
            case "--test-swim-exit":
                RunSwimExitTest();
                break;
            case "--test-controls":
                RunControlsTest();
                break;
            case "--test-stream":
                RunStreamTest();
                break;
            case "--test-greedy":
                RunGreedyTest();
                break;
            case "--test-biomes":
                RunBiomeTest();
                break;
            case "--test-underground":
                RunUndergroundTest();
                break;
            case "--test-daynight":
                RunDayNightTest();
                break;
            case "--test-sky":
                RunSkyTest();
                break;
            case "--test-water":
                RunWaterTest();
                break;
            case "--test-mining":
                RunMiningTest();
                break;
            case "--test-strata":
                RunStrataTest();
                break;
            case "--test-waterfill":
                RunWaterFillTest();
                break;
            case "--test-swim":
                RunSwimTest();
                break;
            case "--test-underwater":
                RunUnderwaterTest();
                break;
            case "--test-fx":
                RunFxTest();
                break;
            case "--test-ambient":
                RunAmbientTest();
                break;
            case "--test-craft":
                RunCraftTest();
                break;
            case "--test-persist":
                RunPersistTest();
                break;
            case "--test-teacher":
                RunTeacherTest();
                break;
            case "--test-hud":
                RunHudTest();
                break;
            case "--test-combat":
                RunCombatTest();
                break;
            case "--test-npc":
                RunNpcTest();
                break;
            case "--test-save":
                RunSaveTest();
                break;
            case "--lesson-david":
                StartLesson(Lessons.LessonCatalog.Get("david"));
                break;
            case "--lesson-creation":
                StartLesson(Lessons.LessonCatalog.Get("creation"));
                break;
            case "--sandbox":
                StartSandbox();
                break;
            case "--test-creation":
                RunCreationTest();
                break;
            case "--test-lesson":
                RunLessonTest();
                break;
            case "--test-menu":
                RunMenuTest();
                break;
            case "--test-quest":
                RunQuestTest();
                break;
            case "--test-campaign":
                RunCampaignTest();
                break;
            case "--test-jsonlesson":
                RunJsonLessonTest();
                break;
            case "--lesson-jericho":
                StartLesson(Lessons.LessonCatalog.Get("jericho"));
                break;
            case "--menu":
            default:
                ShowMainMenu();
                break;
        }
    }

    private void QuitSoon() => GetTree().CreateTimer(0.1).Timeout += () => GetTree().Quit(0);

    /// <summary>Dump every synthesized sound to assets/audio/*.wav for inspection
    /// (the game itself generates these at runtime; the files are a dev artifact).</summary>
    private void GenAudio()
    {
        const string dir = "res://assets/audio";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        int n = 0;
        foreach (var kv in Core.SoundBank.BuildAll())
        {
            byte[] pcm = Core.SoundBank.ToPcm16(kv.Value.Samples);
            using var f = Godot.FileAccess.Open($"{dir}/{kv.Key}.wav", Godot.FileAccess.ModeFlags.Write);
            if (f == null) continue;
            f.StoreBuffer(WavFile(pcm, Core.SoundBank.Rate));
            n++;
        }
        GD.Print($"[RA] gen-audio: wrote {n} wav files to {dir}");
    }

    private static byte[] WavFile(byte[] pcm, int rate)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        const int channels = 1, bits = 16;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                       // fmt chunk size
        w.Write((short)1);                 // PCM
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * bits / 8); // byte rate
        w.Write((short)(channels * bits / 8)); // block align
        w.Write((short)bits);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        return ms.ToArray();
    }

    // ---- menu / session flow ---------------------------------------------

    private void ShowMainMenu()
    {
        GetTree().Paused = false;
        if (_session != null) { _session.QueueFree(); _session = null; }
        _saveName = null;
        ClearMenu();
        _menu = new UI.MainMenu { Name = "MainMenu", Progress = Core.CampaignStore.Load() };
        _menu.OnPlayLesson = id => StartLesson(Lessons.LessonCatalog.Get(id));
        _menu.OnSandbox = ShowSaveMenu;
        _menu.OnQuit = () => GetTree().Quit();
        AddChild(_menu);
        Core.AudioManager.StartMusic();   // calm bed under the menu
        Core.AudioManager.SetMusicMood(Core.MusicMood.Calm);
        Core.AudioManager.StopAmbience(); // outdoor ambience belongs to play, not menu
    }

    private void ClearMenu()
    {
        if (_menu != null) { _menu.QueueFree(); _menu = null; }
        if (_saveMenu != null) { _saveMenu.QueueFree(); _saveMenu = null; }
    }

    /// <summary>The "Build Sandbox" world picker: new world or continue a saved one.</summary>
    private void ShowSaveMenu()
    {
        ClearMenu();
        _saveMenu = new UI.SaveMenu { Name = "SaveMenu" };
        _saveMenu.OnBack = ShowMainMenu;
        _saveMenu.OnNewWorld = () => StartSandbox(NewWorldSave());
        _saveMenu.OnLoad = name =>
        {
            var save = Core.SaveSystem.Load(name);
            if (save != null) StartSandbox(save);
            else StartSandbox(NewWorldSave());
        };
        AddChild(_saveMenu);
    }

    private Core.SaveData NewWorldSave()
    {
        long now = (long)Time.GetUnixTimeFromSystem();
        var save = new Core.SaveData
        {
            Name = UniqueWorldName(),
            Seed = (int)(now & 0x7FFFFFFF),
            SavedUnix = now,
            TimeOfDay = Core.EnvironmentController.Morning,
        };
        foreach (var (block, count) in DefaultKit) save.Inventory[block] = count;
        return save;
    }

    private static string UniqueWorldName()
    {
        var taken = new System.Collections.Generic.HashSet<string>();
        foreach (var s in Core.SaveSystem.List()) taken.Add(s.Name);
        if (!taken.Contains("World")) return "World";
        for (int i = 2; ; i++)
            if (!taken.Contains($"World {i}")) return $"World {i}";
    }

    private void SaveCurrent()
    {
        if (_session == null || string.IsNullOrEmpty(_saveName)) return;
        Core.SaveSystem.Save(_session.CaptureSave(_saveName, (long)Time.GetUnixTimeFromSystem()));
    }

    /// <summary>Save the sandbox when the player closes the window, so a stray click on
    /// the X never loses an hour of building. (Headless tests quit programmatically and
    /// never raise this notification, so they are unaffected.)</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveCurrent();
    }

    /// <summary>A fresh sandbox (used by the --sandbox CLI flag).</summary>
    private void StartSandbox() => StartSandbox(NewWorldSave());

    /// <summary>Build (or resume) an endless, procedurally generated creative world
    /// from a save: the seed regenerates the terrain, edit-deltas are re-applied as
    /// chunks stream in, and player position, time and inventory are restored. The
    /// world auto-saves periodically and on returning to the menu.</summary>
    private void StartSandbox(Core.SaveData save)
    {
        ClearMenu();
        _saveName = save.Name;
        _session = new GameSession { Name = "Session" };
        _session.ReturnToMenuRequested = () => { SaveCurrent(); ShowMainMenu(); };
        AddChild(_session);

        var gen = new Core.TerrainGenerator(save.Seed);
        Vector3 spawn = save.PlayerPos == Vector3.Zero
            ? new Vector3(24.5f, gen.SurfaceHeight(24, 24) + 3f, 24.5f)
            : save.PlayerPos;

        _session.Setup(spawn, creative: true);
        _session.Env.TimeOfDay = save.TimeOfDay;
        _session.Env.SetCycle(true);

        var world = _session.World;
        var edits = new System.Collections.Generic.List<(int, int, int, ushort)>();
        foreach (var (x, y, z, block) in save.Edits)
            edits.Add((x, y, z, Core.BlockRegistry.IdOf(block)));
        world.PreloadEdits(edits);
        world.StartStreaming(gen, _session.Player, renderDistance: 6, minChunkY: -1, maxChunkY: 3);
        world.EnsureSpawnArea(spawn, radius: 2);

        _session.Env.SetWeatherFollow(_session.Player);
        _session.AddChild(new Core.WeatherDirector
        {
            Name = "Weather", Generator = gen, Player = _session.Player, Env = _session.Env,
        });

        var kit = new System.Collections.Generic.List<(string, int)>();
        foreach (var (block, count) in save.Inventory) kit.Add((block, count));
        _session.EnableSurvival(kit.ToArray());
        _session.RestoreTeacherState(save.Signposts, save.Waypoints);

        var autosave = new Timer { Name = "AutoSave", WaitTime = 60, Autostart = true, OneShot = false };
        _session.AddChild(autosave);
        autosave.Timeout += SaveCurrent;

        _session.Hud.ShowBanner($"{save.Name} — endless world!  (WASD · +/- place/break · G fly · Tab craft)", 6f);
        Core.AudioManager.StartMusic();
        Core.AudioManager.StartAmbience();
        Core.AudioManager.SetMusicMood(Core.MusicMood.Calm);
        SaveCurrent(); // register the world in the save list right away
    }

    private GameSession StartLesson(Lessons.ILesson lesson)
    {
        ClearMenu();
        _session = new GameSession { Name = "Session", ReturnToMenuRequested = ShowMainMenu, LessonId = lesson.Id };
        AddChild(_session);
        _session.Setup(lesson.Spawn, creative: false);
        if (lesson.TimeOfDay is float tod) _session.Env.SetFixedTime(tod);
        else _session.Env.SetCycle(true);
        lesson.Build(_session);
        var quest = lesson.BuildQuest(_session);
        if (quest != null) _session.StartQuest(quest);
        _session.QuestCompleted += id => Core.CampaignStore.MarkComplete(id);
        _session.Hud.ShowBanner($"{lesson.Title}", 4f);
        Core.AudioManager.StartMusic();
        Core.AudioManager.StartAmbience();
        Core.AudioManager.SetMusicMood(lesson.Mood);
        return _session;
    }

    private async void RunLessonTest()
    {
        var lesson = Lessons.LessonCatalog.Get("david");
        var session = new GameSession { Name = "Session", LessonId = "david" };
        AddChild(session);
        session.Setup(lesson.Spawn, creative: false, captureMouse: false);
        lesson.Build(session);
        var quest = lesson.BuildQuest(session);
        if (quest != null) session.StartQuest(quest);
        session.Player.InputEnabled = false;

        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_lesson.png", 0.3);

        // 1. talk to Jesse
        NpcSys.Npc jesse = null;
        foreach (Node n in GetTree().GetNodesInGroup("npc"))
            if (n is NpcSys.Npc npc && npc.NpcName == "Jesse") jesse = npc;
        bool talked = false;
        if (jesse != null) jesse.Talked += () => talked = true;
        session.StartDialogueWith(jesse);
        int g = 0;
        while (session.InDialogue && g++ < 20)
        {
            if (session.Dialogue.HasChoices) session.Dialogue.Choose(0);
            else session.Dialogue.Advance();
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        }

        // 2. cross the battle line -> wakes Goliath
        Combat.Enemy goliath = null;
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
            if (n is Combat.Enemy e && e.Type.Name == "Goliath") goliath = e;
        bool defeated = false;
        if (goliath != null) goliath.Defeated += () => defeated = true;
        session.Player.GlobalPosition = new Vector3(32, 2, 26);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        bool woke = goliath != null && goliath.Target != null;

        // 3. defeat Goliath
        goliath?.TakeDamage(9999, session.Player);
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);

        GD.Print($"[RA] lesson-test: jesseTalked={talked} goliathWoke={woke} goliathDefeated={defeated}");
        await Capture("res://_lesson_victory.png", 0.3);
        GetTree().Quit(0);
    }

    private async void RunCreationTest()
    {
        var lesson = Lessons.LessonCatalog.Get("creation");
        var session = new GameSession { Name = "Session", LessonId = "creation" };
        AddChild(session);
        session.Setup(lesson.Spawn, creative: false, captureMouse: false);
        lesson.Build(session);
        var quest = lesson.BuildQuest(session);
        if (quest != null) session.StartQuest(quest);
        session.Player.InputEnabled = false;

        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_creation.png", 0.3);

        var animals = new System.Collections.Generic.List<NpcSys.Npc>();
        foreach (Node n in GetTree().GetNodesInGroup("npc"))
            if (n is NpcSys.Npc a) animals.Add(a);
        int talked = 0;
        foreach (var a in animals) a.Talked += () => talked++;

        foreach (var a in animals)
        {
            session.StartDialogueWith(a);
            int g = 0;
            while (session.InDialogue && g++ < 12)
            {
                if (session.Dialogue.HasChoices) session.Dialogue.Choose(0);
                else session.Dialogue.Advance();
                await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            }
        }

        session.Player.GlobalPosition = new Vector3(32, 2, 12); // the Tree of Life
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[RA] creation-test: animals={animals.Count} named={talked}");
        await Capture("res://_creation2.png", 0.3);
        GetTree().Quit(0);
    }

    /// <summary>Headless: the quest tracker advances on each event kind, dedupes repeat
    /// talks, ignores terrain / non-player block writes, and fires QuestCompleted once.</summary>
    private void RunQuestTest()
    {
        Core.BlockRegistry.EnsureInit();
        var session = new GameSession { Name = "Session", LessonId = "quest-test" };
        AddChild(session);
        session.Setup(new Vector3(8, 5, 8), creative: false, captureMouse: false);
        Core.WorldGen.FlatGround(session.World, 0, 16, 0, 16, 0);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Player.InputEnabled = false;

        // Terrain written BEFORE the quest arms must never surface as progress.
        int preArmEmits = 0;
        session.World.BlockChanged += (p, o, n, c) => preArmEmits++;
        ushort stone = Core.BlockRegistry.IdOf("stone");
        session.World.SetBlock(2, 1, 2, stone); // not armed yet -> silent
        bool preArmSilent = preArmEmits == 0;

        var quest = new Quests.Quest
        {
            Objectives = new[]
            {
                Quests.Quest.Talk("Jesse", "Talk to Jesse"),
                Quests.Quest.TalkAny(3, "Name the animals"),
                Quests.Quest.Defeat("Goliath", "Defeat Goliath"),
                Quests.Quest.Reach("zone", "Reach the zone"),
                Quests.Quest.Break("stone", 2, "Mine two stone"),
                Quests.Quest.Place("definitely_not_a_block", 1, "Bad block key", optional: true),
            },
        };
        int completedFires = 0;
        session.QuestCompleted += _ => completedFires++;
        session.StartQuest(quest);

        session.Quest.OnTalk("Jesse");
        bool talkOk = session.Quest.IsDone(0);

        session.Quest.OnTalk("Lion");
        session.Quest.OnTalk("Lion");   // duplicate name -> ignored
        session.Quest.OnTalk("Ox");
        session.Quest.OnTalk("Dove");
        bool talkAnyOk = session.Quest.IsDone(1) && session.Quest.Progress(1) == 3;

        session.Quest.OnDefeat("Soldier"); // wrong type -> ignored
        session.Quest.OnDefeat("Goliath");
        bool defeatOk = session.Quest.IsDone(2);

        session.Quest.OnReach("elsewhere"); // wrong id -> ignored
        session.Quest.OnReach("zone");
        bool reachOk = session.Quest.IsDone(3);

        // Armed player edits: a LessonBuild write does not count; breaking a stone does.
        session.World.SetBlock(3, 1, 3, stone, cause: Core.BlockChangeCause.LessonBuild);
        session.World.SetBlock(3, 1, 3, 0); // break #1
        session.World.SetBlock(4, 1, 4, stone, cause: Core.BlockChangeCause.LessonBuild);
        session.World.SetBlock(4, 1, 4, 0); // break #2
        bool breakOk = session.Quest.IsDone(4) && session.Quest.Progress(4) >= 2;

        // Breaking a non-stone block must NOT auto-complete the bad-key Place objective.
        ushort dirt = Core.BlockRegistry.IdOf("dirt");
        session.World.SetBlock(5, 1, 5, dirt, cause: Core.BlockChangeCause.LessonBuild);
        session.World.SetBlock(5, 1, 5, 0);
        bool badKeyGuarded = !session.Quest.IsDone(5);

        bool allDone = session.Quest.AllDone; // true: the only incomplete objective (5) is optional

        // Second quest: a deduped repeat key must fall through to a later same-key
        // objective, and an incomplete OPTIONAL objective must not gate completion.
        var session2 = new GameSession { Name = "Session2", LessonId = "quest-test-2" };
        AddChild(session2);
        session2.Setup(new Vector3(8, 5, 8), creative: false, captureMouse: false);
        Core.WorldGen.FlatGround(session2.World, 0, 16, 0, 16, 0);
        session2.World.MarkAllDirty();
        session2.World.RebuildAllNow();
        session2.Player.InputEnabled = false;

        int completedFires2 = 0;
        session2.QuestCompleted += _ => completedFires2++;
        session2.StartQuest(new Quests.Quest
        {
            Objectives = new[]
            {
                Quests.Quest.TalkAny(2, "Name two"),
                Quests.Quest.Talk("Jesse", "Talk to Jesse"),
                Quests.Quest.Defeat("Goliath", "Optional foe", optional: true),
            },
        });
        session2.Quest.OnTalk("Jesse"); // counts toward TalkAny (idx0)
        session2.Quest.OnTalk("Jesse"); // deduped at idx0 -> must fall through to complete idx1
        bool continueFix = session2.Quest.IsDone(1);
        session2.Quest.OnTalk("Lion");  // completes TalkAny (idx0)
        bool optionalOk = session2.Quest.AllDone && !session2.Quest.IsDone(2); // optional foe never defeated

        GD.Print($"[RA] quest-test: preArmSilent={preArmSilent} talk={talkOk} talkAny={talkAnyOk} " +
                 $"defeat={defeatOk} reach={reachOk} break={breakOk} badKeyGuarded={badKeyGuarded} " +
                 $"allDone={allDone} completedFires={completedFires} continueFix={continueFix} " +
                 $"optionalOk={optionalOk} completedFires2={completedFires2}");
        QuitSoon();
    }

    /// <summary>Headless: campaign progress persists + reloads, and unlock walks Requires.</summary>
    private void RunCampaignTest()
    {
        Core.CampaignStore.DeleteAll();
        var fresh = Core.CampaignStore.Load();
        bool creationUnlocked = fresh.IsUnlocked("creation"); // no prereqs
        bool davidLocked = !fresh.IsUnlocked("david");        // requires creation

        fresh.MarkComplete("creation");
        Core.CampaignStore.Save(fresh);

        var reloaded = Core.CampaignStore.Load();
        bool creationDone = reloaded.IsComplete("creation");
        bool davidUnlocked = reloaded.IsUnlocked("david");
        bool nextOk = Core.Campaign.NextAfter("creation") == "david";

        Core.CampaignStore.DeleteAll();

        GD.Print($"[RA] campaign-test: creationUnlocked={creationUnlocked} davidLocked={davidLocked} " +
                 $"creationDone={creationDone} davidUnlocked={davidUnlocked} nextOk={nextOk}");
        QuitSoon();
    }

    /// <summary>Headless: the JSON lesson loader builds Jericho's world, wires its quest from data,
    /// and the quest drives to completion (talk, collect the trumpets, reach the gate).</summary>
    private void RunJsonLessonTest()
    {
        Core.BlockRegistry.EnsureInit();
        var lesson = Lessons.LessonCatalog.Get("jericho");
        bool inCatalog = lesson is Lessons.JsonLesson && lesson.Id == "jericho";

        var session = new GameSession { Name = "Session", LessonId = "jericho" };
        AddChild(session);
        session.Setup(lesson.Spawn, creative: false, captureMouse: false);
        lesson.Build(session);
        var quest = lesson.BuildQuest(session);
        if (quest != null) session.StartQuest(quest);
        session.Player.InputEnabled = false;

        // terrain interpreted from JSON: a wall cell is mud_brick, the gate is carved to air
        ushort mud = Core.BlockRegistry.IdOf("mud_brick");
        bool wallBuilt = session.World.GetBlockId(22, 3, 6) == mud;
        bool gateOpen = session.World.GetBlockId(32, 1, 18) == 0;

        // an NPC was spawned from JSON
        bool joshua = false;
        foreach (Node n in GetTree().GetNodesInGroup("npc"))
            if (n is NpcSys.Npc npc && npc.NpcName == "Joshua") joshua = true;

        bool completed = false;
        session.QuestCompleted += _ => completed = true;

        // drive the data-defined quest: talk (optional), collect 3 trumpets, reach the gate
        session.Quest.OnTalk("Joshua");
        ushort gold = Core.BlockRegistry.IdOf("gold_block");
        foreach (var p in new[] { new Vector3I(28, 1, 40), new Vector3I(32, 1, 38), new Vector3I(36, 1, 40) })
            if (session.World.GetBlockId(p) == gold) session.World.SetBlock(p, 0); // break = collect
        bool collectOk = session.Quest.IsDone(1);
        session.Quest.OnReach("the-gate");
        bool allDone = session.Quest.AllDone;

        GD.Print($"[RA] jsonlesson-test: inCatalog={inCatalog} wall={wallBuilt} gate={gateOpen} " +
                 $"npc={joshua} collect={collectOk} allDone={allDone} completed={completed}");

        // Coverage for the enemy/wake/defeat paths Jericho (peaceful) never exercises: two same-type
        // UNNAMED dormant soldiers + a wake-all + a case-mismatched Defeat key, all from inline JSON.
        const string inline = """
            {
              "id": "_enemytest", "title": "Enemy Test", "spawn": [8, 3, 8],
              "terrain": [ { "op": "flat", "x0": 0, "x1": 16, "z0": 0, "z1": 16, "y": 0 } ],
              "enemies": [
                { "type": "soldier", "pos": [4, 1, 4], "dormant": true },
                { "type": "soldier", "pos": [6, 1, 4], "dormant": true }
              ],
              "narrations": [ { "id": "edge", "pos": [8, 3, 8], "size": [6, 6, 6], "lines": ["Here they come."] } ],
              "quest": {
                "objectives": [
                  { "kind": "reach", "key": "edge", "label": "Hold the line", "onComplete": { "wake": "*" } },
                  { "kind": "defeat", "key": "Soldier", "count": 2, "label": "Rout the guards" }
                ]
              }
            }
            """;
        var lesson2 = Lessons.JsonLesson.FromJson(inline, "inline-enemytest");
        var s2 = new GameSession { Name = "Session2", LessonId = "_enemytest" };
        AddChild(s2);
        s2.Setup(lesson2.Spawn, creative: false, captureMouse: false);
        lesson2.Build(s2);
        var q2 = lesson2.BuildQuest(s2);
        if (q2 != null) s2.StartQuest(q2);
        s2.Player.InputEnabled = false;

        var foes = new System.Collections.Generic.List<Combat.Enemy>();
        foreach (Node n in s2.World.GetChildren())
            if (n is Combat.Enemy en) foes.Add(en);
        bool twoFoes = foes.Count == 2;
        bool dormant = twoFoes && foes.TrueForAll(f => f.Target == null);

        s2.Quest.OnReach("edge");                              // reach -> wake "*" (both soldiers)
        bool wokeAll = twoFoes && foes.TrueForAll(f => f.Target != null);

        foreach (var f in foes) f.TakeDamage(9999, s2.Player); // defeat both (key "Soldier" vs name "soldier")
        bool defeatedAll = s2.Quest.IsDone(1) && s2.Quest.AllDone;

        GD.Print($"[RA] jsonlesson-enemies: twoFoes={twoFoes} dormant={dormant} wokeAll={wokeAll} defeatedAll={defeatedAll}");
        QuitSoon();
    }

    private async void RunMenuTest()
    {
        ShowMainMenu();
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_menu.png", 0.2);

        // simulate clicking "Play: David and Goliath"
        _menu.OnPlayLesson("david");
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_menu_lesson.png", 0.2);
        GD.Print($"[RA] menu-test: sessionStarted={_session != null} menuCleared={_menu == null}");
        GetTree().Quit(0);
    }

    private void RunSaveTest()
    {
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        Core.WorldGen.Showcase(world);
        world.MarkAllDirty();
        world.RebuildAllNow();

        // snapshot a region
        var snap = new System.Collections.Generic.Dictionary<Vector3I, ushort>();
        for (int x = -2; x <= 24; x++)
        for (int y = -3; y <= 6; y++)
        for (int z = -2; z <= 18; z++)
        {
            ushort id = world.GetBlockId(x, y, z);
            if (id != 0) snap[new Vector3I(x, y, z)] = id;
        }

        const string path = "user://worlds/test.rworld";
        Core.WorldIO.SaveWorld(world, path);

        // capture a prefab, then clear and reload
        var prefab = Core.WorldIO.Capture(world, new Vector3I(0, 0, 0), new Vector3I(2, 2, 2));
        world.Clear();
        int afterClear = world.GetBlockId(0, 0, 0);
        Core.WorldIO.LoadWorld(world, path);

        int mismatches = 0, chec_d = 0;
        foreach (var kv in snap)
        {
            chec_d++;
            if (world.GetBlockId(kv.Key) != kv.Value) mismatches++;
        }

        // stamp the prefab into empty space and verify its corner block matches
        Core.WorldIO.Stamp(world, prefab, new Vector3I(60, 1, 60));
        ushort expectedCorner = Core.BlockRegistry.IdOf(prefab.Palette[prefab.Cells[prefab.Index(0, 0, 0)]]);
        bool stampOk = world.GetBlockId(60, 1, 60) == expectedCorner;

        GD.Print($"[RA] save-test: snapshot={chec_d} mismatches={mismatches} clearedTo={afterClear} " +
                 $"prefab={prefab.Size.X}x{prefab.Size.Y}x{prefab.Size.Z} stampOk={stampOk}");
        QuitSoon();
    }

    private async void RunNpcTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: false);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Player.InputEnabled = false;

        var npc = new NpcSys.Npc
        {
            NpcName = "Jesse",
            Robe = new Color(0.5f, 0.35f, 0.55f),
            Dialogue = Dialogue.DialogueData.Linear("Jesse",
                "Welcome, young shepherd.",
                "The Philistines have gathered for battle in the Valley of Elah.",
                "Will you carry bread to your brothers at the camp?"),
        };
        session.World.AddChild(npc);
        npc.GlobalPosition = new Vector3(24, 1, 19);

        // a narration trigger right where the player stands
        var trig = World.NarrationTrigger.Create(new Vector3(24, 1.5f, 24), new Vector3(3, 3, 3),
            session.Narrator, "And David rose early in the morning...", "...and went, as Jesse had commanded him.");
        session.World.AddChild(trig);

        session.Player.Head.Rotation = new Vector3(-0.1f, 0, 0);
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_npc.png", 0.2);

        // start + drive the conversation
        session.StartDialogue(npc.Dialogue);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        bool boxShown = session.Dialogue.Active;
        await Capture("res://_dialogue.png", 0.2);

        int guard = 0;
        while (session.InDialogue && guard++ < 12)
        {
            session.Dialogue.Advance();
            await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print($"[RA] npc-test: dialogueShown={boxShown} finished={!session.InDialogue} steps={guard} " +
                 $"inputRestored={session.Player.InputEnabled}");

        GetTree().Quit(0);
    }

    private async void RunCombatTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: false);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Player.InputEnabled = false; // physics only; we trigger combat by script

        var soldier = session.SpawnEnemy(Combat.EnemyType.Soldier(), new Vector3(24, 1, 16));
        var giant = session.SpawnEnemy(Combat.EnemyType.Giant(), new Vector3(29, 1, 14));
        var wolf = session.SpawnEnemy(Combat.EnemyType.Wolf(), new Vector3(34, 1, 24)); // clear +X lane
        wolf.Target = null; // stand still so the projectile test isn't a moving-lead problem
        session.Hud.ShowBanner("Combat test", 5f);

        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_combat.png", 0.2);

        // fire a projectile straight at the wolf's centre (tests projectile -> damage)
        float wolfBefore = wolf.Health;
        Vector3 camPos = session.Player.Camera.GlobalPosition;
        Vector3 wolfCenter = wolf.GlobalPosition + Vector3.Up * 1.0f;
        Vector3 dir = (wolfCenter - camPos).Normalized();
        Combat.Projectile.Spawn(session, camPos, dir * 45f, 22f, session.Player);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[RA] combat sling: wolf {wolfBefore:F0} -> {(GodotObject.IsInstanceValid(wolf) ? wolf.Health : 0):F0}");

        // let the soldier close in and hit the player
        await ToSignal(GetTree().CreateTimer(2.6), SceneTreeTimer.SignalName.Timeout);
        float sdist = GodotObject.IsInstanceValid(soldier)
            ? (soldier.GlobalPosition - session.Player.GlobalPosition).Length() : -1;
        GD.Print($"[RA] combat chase: soldierDist={sdist:F1} playerHp={session.Player.Health:F0}");

        // defeat the giant outright -> should poof and free
        giant.TakeDamage(9999, session.Player);
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[RA] combat defeat: giantValid={GodotObject.IsInstanceValid(giant)}");

        GetTree().Quit(0);
    }

    private async void RunHudTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: true);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        // a pillar to target straight ahead (-Z)
        for (int y = 1; y <= 3; y++)
            session.World.SetBlock(24, y, 18, Core.BlockRegistry.IdOf("stone"), false);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();

        // look slightly down toward the pillar so the selection outline shows
        session.Player.Head.Rotation = new Vector3(-0.25f, 0, 0);
        session.Hud.ShowBanner("Block interaction test", 5f);

        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);

        // exercise place/break logic directly (deterministic, no input needed)
        bool placed = session.Interactor.PlaceAt(new Vector3I(24, 4, 18), Core.BlockRegistry.IdOf("gold_block"));
        bool broke = session.Interactor.BreakAt(new Vector3I(24, 1, 18));
        var hit = Core.VoxelRay.Cast(session.World, session.Player.Camera.GlobalPosition,
            -session.Player.Camera.GlobalTransform.Basis.Z, 6f);
        GD.Print($"[RA] hud-test: placed={placed} broke={broke} rayHit={hit.Ok} hitBlock={hit.Block} " +
                 $"afterPlace={Core.BlockRegistry.Get(session.World.GetBlockId(24, 4, 18)).Name} " +
                 $"afterBreak={Core.BlockRegistry.Get(session.World.GetBlockId(24, 1, 18)).Name}");

        await Capture("res://_hud.png", 0.4);
        GetTree().Quit(0);
    }

    private async void RunPlayerTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        Core.WorldGen.FlatGround(world, 0, 32, 0, 32, 0);
        // stone tub with ~7-deep water for the swim test
        for (int x = 4; x <= 10; x++)
        for (int z = 4; z <= 10; z++)
        for (int y = -8; y <= 0; y++)
            world.SetBlock(x, y, z, Core.BlockRegistry.IdOf("stone"), false);
        for (int x = 5; x <= 9; x++)
        for (int z = 5; z <= 9; z++)
        for (int y = -7; y <= 0; y++)
            world.SetBlock(x, y, z, Core.BlockRegistry.IdOf("water"), false);
        world.MarkAllDirty();
        world.RebuildAllNow();

        var player = new PlayerSys.Player { Name = "Player", World = world, InputEnabled = false };
        AddChild(player);
        player.GlobalPosition = new Vector3(16, 8, 16);
        player.Camera.Current = true;

        // Phase A: fall onto grass from ~7 blocks -> expect landing + fall damage
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[RA] player ground: y={player.GlobalPosition.Y:F2} onFloor={player.IsOnFloor()} " +
                 $"hp={player.Health:F1} inWater={player.InWater}");
        await Capture("res://_player_ground.png", 0.3);

        // Phase B: drop into the deep water -> expect float near surface, no fall damage
        player.Respawn(new Vector3(7, 6, 7));
        await ToSignal(GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"[RA] player water: y={player.GlobalPosition.Y:F2} inWater={player.InWater} " +
                 $"headUnder={player.HeadUnderwater} air={player.Air:F1} hp={player.Health:F1}");
        await Capture("res://_player_water.png", 0.3);

        GetTree().Quit(0);
    }

    /// <summary>Regression test for the reported "can't jump out of water when the
    /// ground is level with it" bug: drive the player toward a bank whose top is
    /// flush with the water surface and verify they climb out onto it.</summary>
    private async void RunSwimExitTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        Core.WorldGen.FlatGround(world, 0, 32, 0, 32, 0); // grass top surface at y=1

        // A pool whose surface is LEVEL with the surrounding grass: water fills
        // cells y=0..-2 (so the water top face is at y=1, same as the grass top).
        ushort water = Core.BlockRegistry.IdOf("water");
        ushort stone = Core.BlockRegistry.IdOf("stone");
        for (int x = 8; x <= 24; x++)
        for (int z = 8; z <= 16; z++)
        {
            for (int y = 0; y >= -2; y--) world.SetBlock(x, y, z, water, false);
            world.SetBlock(x, -3, z, stone, false); // pool floor
        }
        world.MarkAllDirty();
        world.RebuildAllNow();

        var player = new PlayerSys.Player { Name = "Player", World = world };
        AddChild(player);
        player.GlobalPosition = new Vector3(16, 3, 12); // above the pool centre
        player.Camera.Current = true;
        // The body faces -Z by default, so "forward" drives toward the z=7 bank.

        // Let them fall in and settle at the surface.
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        float floatY = player.GlobalPosition.Y;
        bool floatIn = player.InWater;

        // Swim forward toward the bank; stop the moment we've climbed out (so we
        // don't then march across the grass and off the edge of the test world).
        Input.ActionPress(Core.GameInput.Actions.Forward);
        bool exited = false;
        Vector3 exitPos = player.GlobalPosition;
        for (int i = 0; i < 50 && !exited; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            Vector3 q = player.GlobalPosition;
            // out of the pool (z < 8), standing on the bank, head above the waterline
            if (!player.InWater && player.IsOnFloor() && q.Y > 0.9f && q.Z < 7.8f)
            {
                exited = true;
                exitPos = q;
            }
        }
        Input.ActionRelease(Core.GameInput.Actions.Forward);

        GD.Print($"[RA] swim-exit: floated y={floatY:F2} inWater={floatIn} -> " +
                 $"exitPos=({exitPos.X:F1},{exitPos.Y:F2},{exitPos.Z:F1}) EXITED_ONTO_BANK={exited}");
        GetTree().Quit(0);
    }

    /// <summary>Phase-1 controls test: verifies the game is playable without a
    /// mouse — the cursor stays free by default, arrow/numpad keys turn the camera,
    /// and the +/- keys place and break blocks at the crosshair while uncaptured.</summary>
    private async void RunControlsTest()
    {
        Core.Settings.CaptureMode = Core.Settings.MouseCapture.ClickToCapture; // deterministic
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 22), creative: true); // Build mode, free cursor

        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        for (int y = 1; y <= 3; y++)
            session.World.SetBlock(24, y, 18, Core.BlockRegistry.IdOf("stone"), false);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();

        bool initFree = Input.MouseMode != Input.MouseModeEnum.Captured;

        // settle a frame so the interactor casts at the pillar straight ahead (-Z)
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        var tgt = session.Interactor.CurrentTarget;
        Vector3I placeCell = tgt.Prev;

        // keyboard PLACE (mouse not captured) -> a block appears at the near face
        Input.ActionPress(Core.GameInput.Actions.KbPlace);
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease(Core.GameInput.Actions.KbPlace);
        bool placed = tgt.Ok && session.World.GetBlockId(placeCell) != 0;

        // keyboard BREAK -> gradual mining: hold the key until the block is chipped
        // away (no longer an instant one-frame break).
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        Vector3I breakCell = session.Interactor.CurrentTarget.Block;
        Input.ActionPress(Core.GameInput.Actions.KbBreak);
        bool broke = false;
        for (int i = 0; i < 40 && !broke; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            if (session.World.GetBlockId(breakCell) == 0) broke = true;
        }
        Input.ActionRelease(Core.GameInput.Actions.KbBreak);

        // keyboard LOOK: arrows/numpad turn the body (yaw) and head (pitch)
        float yaw0 = session.Player.Rotation.Y, pitch0 = session.Player.Head.Rotation.X;
        Input.ActionPress(Core.GameInput.Actions.LookLeft);
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease(Core.GameInput.Actions.LookLeft);
        Input.ActionPress(Core.GameInput.Actions.LookUp);
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease(Core.GameInput.Actions.LookUp);
        float yawDelta = Mathf.Abs(session.Player.Rotation.Y - yaw0);
        float pitchDelta = session.Player.Head.Rotation.X - pitch0;

        // M toggles the cursor grab on demand
        Input.ActionPress(Core.GameInput.Actions.ToggleCapture);
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease(Core.GameInput.Actions.ToggleCapture);
        bool toggledCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;

        GD.Print($"[RA] controls-test: initFree={initFree} placed={placed} broke={broke} " +
                 $"yawDelta={yawDelta:F2} pitchDelta={pitchDelta:F2} mToggleCaptured={toggledCaptured}");
        GetTree().Quit(0);
    }

    /// <summary>Phase-8 teacher-tools test: safe mode blocks damage, and signposts
    /// and waypoints round-trip through a save.</summary>
    private void RunTeacherTest()
    {
        Core.BlockRegistry.EnsureInit();
        var player = new PlayerSys.Player { Name = "P", InputEnabled = false };
        AddChild(player);

        player.SafeMode = true;
        float hp0 = player.Health;
        player.Damage(50, "hit");
        bool safeBlocks = Mathf.IsEqualApprox(player.Health, hp0);
        player.SafeMode = false;
        player.Damage(10, "hit");
        bool unsafeHurts = player.Health < hp0;

        var d = new Core.SaveData { Name = "__teacher_test__", Seed = 1 };
        d.Signposts.Add((new Vector3(1, 2, 3), "John 3:16"));
        d.Waypoints.Add(("Camp", new Vector3(4, 5, 6)));
        Core.SaveSystem.Save(d);
        var loaded = Core.SaveSystem.Load("__teacher_test__");
        bool signOk = loaded != null && loaded.Signposts.Count == 1
            && loaded.Signposts[0].text == "John 3:16" && loaded.Signposts[0].pos == new Vector3(1, 2, 3);
        bool wpOk = loaded != null && loaded.Waypoints.Count == 1
            && loaded.Waypoints[0].name == "Camp" && loaded.Waypoints[0].pos == new Vector3(4, 5, 6);
        Core.SaveSystem.Delete("__teacher_test__");

        GD.Print($"[RA] teacher-test: safeBlocks={safeBlocks} unsafeHurts={unsafeHurts} signOk={signOk} wpOk={wpOk}");
        QuitSoon();
    }

    /// <summary>Phase-7 persistence test: a save round-trips through disk, and a
    /// player edit re-applies over the regenerated terrain on reload.</summary>
    private void RunPersistTest()
    {
        Core.BlockRegistry.EnsureInit();
        var d = new Core.SaveData
        {
            Name = "__persist_test__", Seed = 4321, SavedUnix = 123,
            PlayerPos = new Vector3(10, 20, 30), TimeOfDay = 0.6f,
        };
        d.Inventory["stone"] = 12;
        d.Inventory["planks"] = 5;
        d.Edits.Add((1, 2, 3, "gold_block"));
        Core.SaveSystem.Save(d);

        var loaded = Core.SaveSystem.Load("__persist_test__");
        bool basicsOk = loaded != null && loaded.Seed == 4321
            && loaded.PlayerPos == new Vector3(10, 20, 30) && Mathf.IsEqualApprox(loaded.TimeOfDay, 0.6f);
        bool invOk = loaded != null && loaded.Inventory.Count == 2
            && loaded.Inventory.TryGetValue("stone", out int sc) && sc == 12;
        bool editsOk = loaded != null && loaded.Edits.Count == 1 && loaded.Edits[0].block == "gold_block";
        Core.SaveSystem.Delete("__persist_test__");
        bool deleted = !Core.SaveSystem.Exists("__persist_test__");

        // A preloaded edit must survive terrain regeneration.
        var world = new Core.VoxelWorld { Name = "W" };
        AddChild(world);
        var gen = new Core.TerrainGenerator(4321);
        int sh = gen.SurfaceHeight(5, 5);
        ushort gold = Core.BlockRegistry.IdOf("gold_block");
        world.PreloadEdits(new (int, int, int, ushort)[] { (5, sh + 1, 5, gold) });
        var target = new Node3D { Name = "T" };
        AddChild(target);
        target.GlobalPosition = new Vector3(5, sh + 2, 5);
        world.StartStreaming(gen, target, 3, -1, 3);
        world.EnsureSpawnArea(new Vector3(5, 0, 5), 1);
        bool editApplied = world.GetBlockId(5, sh + 1, 5) == gold;

        GD.Print($"[RA] persist-test: basicsOk={basicsOk} invOk={invOk} editsOk={editsOk} " +
                 $"deleted={deleted} editApplied={editApplied}");
        QuitSoon();
    }

    /// <summary>Phase-6 crafting/inventory test: stacks add and consume, recipes
    /// convert ingredients into output, and unaffordable recipes are blocked.</summary>
    private void RunCraftTest()
    {
        Core.BlockRegistry.EnsureInit();
        var inv = new Core.Inventory();
        ushort log = Core.BlockRegistry.IdOf("oak_log");
        ushort planks = Core.BlockRegistry.IdOf("planks");
        ushort stone = Core.BlockRegistry.IdOf("stone");

        inv.Add(log, 2);
        var planksRecipe = System.Array.Find(Core.CraftBook.All, r => r.Name == "Wood Planks");
        bool crafted = inv.Craft(planksRecipe);
        int logsAfter = inv.Count(log), planksAfter = inv.Count(planks);

        var sandstone = System.Array.Find(Core.CraftBook.All, r => r.Name == "Sandstone");
        bool cantAfford = !inv.CanAfford(sandstone);

        inv.Add(stone, 1);
        bool consumed = inv.TryConsume(stone, 1);
        bool emptyDropped = inv.Count(stone) == 0 && !new System.Collections.Generic.List<ushort>(inv.Order).Contains(stone);

        GD.Print($"[RA] craft-test: crafted={crafted} logsAfter={logsAfter} planks={planksAfter} " +
                 $"cantAfford={cantAfford} consumed={consumed} emptyDropped={emptyDropped}");
        QuitSoon();
    }

    /// <summary>Phase-5 day/night logic test: the sun should dominate at noon, the
    /// moon at midnight, and they should swap with the time of day.</summary>
    private void RunDayNightTest()
    {
        var env = new Core.EnvironmentController { Name = "Env" };
        AddChild(env); // _Ready builds sun/moon synchronously

        env.SetFixedTime(Core.EnvironmentController.Noon);
        bool noonOk = env.Sun.Visible && env.Sun.LightEnergy > 1.0f && !env.Moon.Visible;
        float noonSun = env.Sun.LightEnergy;

        env.SetFixedTime(Core.EnvironmentController.Night);
        bool nightOk = !env.Sun.Visible && env.Moon.Visible && env.Moon.LightEnergy > 0.05f;
        float midnightMoon = env.Moon.LightEnergy;

        env.SetFixedTime(Core.EnvironmentController.Dawn);
        bool dawnOk = env.Sun.Visible && env.Sun.LightEnergy < noonSun;

        GD.Print($"[RA] daynight-test: noonOk={noonOk} nightOk={nightOk} dawnOk={dawnOk} " +
                 $"noonSun={noonSun:F2} midnightMoon={midnightMoon:F2}");
        QuitSoon();
    }

    /// <summary>Water-fill test: in a streamed world, a single water source dropped
    /// into an enclosed below-sea-level basin should flood the whole interior up to
    /// sea level (and never rise above it).</summary>
    private async void RunWaterFillTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        var gen = new Core.TerrainGenerator(2024);
        var target = new Node3D { Name = "T" };
        AddChild(target);
        target.GlobalPosition = new Vector3(8, 40, 8);
        world.StartStreaming(gen, target, 3, -1, 3);
        world.EnsureSpawnArea(new Vector3(8, 0, 8), 2);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);

        ushort stone = Core.BlockRegistry.IdOf("stone");
        ushort water = Core.BlockRegistry.IdOf("water");
        int sea = Core.TerrainGenerator.SeaLevel; // 26
        const int x0 = 6, x1 = 10, z0 = 6, z1 = 10;
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            world.SetBlock(x, 23, z, stone);          // floor
            for (int y = 24; y <= 27; y++)
            {
                bool wall = x == x0 || x == x1 || z == z0 || z == z1;
                world.SetBlock(x, y, z, wall ? stone : (ushort)0); // walls solid, interior air
            }
        }
        world.SetBlock(7, sea, 7, water);             // one source drop in a corner

        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout); // let it flood

        int filled = 0, interior = 0;
        for (int x = 7; x <= 9; x++)
        for (int z = 7; z <= 9; z++)
        for (int y = 24; y <= sea; y++)
        {
            interior++;
            if (world.GetBlockId(x, y, z) == water) filled++;
        }
        bool noLeak = world.GetBlockId(8, sea + 1, 8) != water;
        GD.Print($"[RA] waterfill-test: interior={interior} filled={filled} " +
                 $"allFilled={filled == interior} noLeakAboveSea={noLeak}");
        GetTree().Quit(0);
    }

    /// <summary>Underwater post-process test: submerge the camera in a water box and
    /// look at a colourful wall through several metres of water, so the murk + blue
    /// shift + refraction wobble + vignette are visible (HUD stays crisp on top).</summary>
    private async void RunUnderwaterTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(8, 5, 8), creative: true, captureMouse: false);
        ushort water = Core.BlockRegistry.IdOf("water");
        ushort stone = Core.BlockRegistry.IdOf("stone");
        ushort gold = Core.BlockRegistry.IdOf("gold_block");
        ushort grass = Core.BlockRegistry.IdOf("grass");
        ushort brick = Core.BlockRegistry.IdOf("brick");
        for (int x = 0; x <= 16; x++)
        for (int z = 0; z <= 16; z++)
        {
            session.World.SetBlock(x, 0, z, stone, false); // floor
            for (int y = 1; y <= 9; y++)
            {
                bool wall = x == 0 || x == 16 || z == 0 || z == 16;
                session.World.SetBlock(x, y, z, wall ? stone : water, false);
            }
        }
        // A colourful wall at z=3 to view through the water.
        for (int x = 4; x <= 12; x++)
        for (int y = 1; y <= 7; y++)
        {
            ushort b = ((x + y) % 3 == 0) ? gold : ((x + y) % 3 == 1) ? grass : brick;
            session.World.SetBlock(x, y, 3, b, false);
        }
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);

        session.Player.GlobalPosition = new Vector3(8, 5, 10); // submerged, facing -Z toward the wall
        session.Player.Head.Rotation = new Vector3(-0.12f, 0, 0);
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        bool headUnder = session.Player.HeadUnderwater;
        await Capture("res://_underwater.png", 0.4);
        GD.Print($"[RA] underwater-test: headUnder={headUnder}");
        GetTree().Quit(0);
    }

    /// <summary>FX test: smash a block to throw tinted debris (captured mid-flight),
    /// then take a hit to show the red hurt-flash + camera shake. Run WINDOWED — the
    /// screenshot path waits on FramePostDraw, which never fires under --headless.</summary>
    private async void RunFxTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: true, captureMouse: false);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        ushort dirt = Core.BlockRegistry.IdOf("dirt");
        for (int y = 1; y <= 3; y++) session.World.SetBlock(24, y, 20, dirt, false); // a little pillar to smash
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        session.Player.Head.Rotation = new Vector3(-0.12f, 0, 0); // look at the pillar

        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        // Dirt is soft (0.5s), so a held break smashes it quickly and throws debris.
        Input.ActionPress(Core.GameInput.Actions.KbBreak);
        await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout); // just after a break
        await Capture("res://_fx_debris.png", 0.0);
        Input.ActionRelease(Core.GameInput.Actions.KbBreak);

        // Take a hit (creative suppresses damage, so drop it first) to trigger the
        // red hurt-flash overlay + camera shake, captured while both are strong.
        session.Player.SetCreative(false);
        session.Player.Damage(20f, "test");
        await Capture("res://_fx_hurt.png", 0.05);

        GD.Print($"[RA] fx-test: captured debris + hurt flash (health={session.Player.Health:F0})");
        GetTree().Quit(0);
    }

    /// <summary>Ambient-particle test: glowing fireflies at night, then pale drifting
    /// motes at noon, both following the player. Run WINDOWED (screenshot path).</summary>
    private async void RunAmbientTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: true, captureMouse: false);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Player.Head.Rotation = new Vector3(-0.1f, 0, 0);

        // Night: fireflies twinkle around the player.
        session.Env.SetFixedTime(Core.EnvironmentController.Night);
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout); // let them populate
        await Capture("res://_ambient_night.png", 0.0);

        // Day: pale motes drift in the sunlight.
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        await Capture("res://_ambient_day.png", 0.0);

        GD.Print("[RA] ambient-test: captured night fireflies + day motes");
        GetTree().Quit(0);
    }

    /// <summary>Swim test: fully submerged, the player should hover (neutral buoyancy),
    /// dive while crouch is held, and rise while jump is held.</summary>
    private async void RunSwimTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        ushort stone = Core.BlockRegistry.IdOf("stone");
        ushort water = Core.BlockRegistry.IdOf("water");
        for (int x = 4; x <= 10; x++)
        for (int z = 4; z <= 10; z++)
        for (int y = -11; y <= 1; y++)
            world.SetBlock(x, y, z, stone, false);
        for (int x = 5; x <= 9; x++)
        for (int z = 5; z <= 9; z++)
        for (int y = -10; y <= 0; y++)
            world.SetBlock(x, y, z, water, false);
        world.MarkAllDirty();
        world.RebuildAllNow();

        var player = new PlayerSys.Player { Name = "Player", World = world, InputEnabled = true };
        AddChild(player);
        player.GlobalPosition = new Vector3(7, -1, 7); // submerged
        player.Camera.Current = true;
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        bool inWater = player.InWater, headUnder = player.HeadUnderwater;
        float yStart = player.GlobalPosition.Y;

        Input.ActionPress(Core.GameInput.Actions.Crouch);
        await ToSignal(GetTree().CreateTimer(0.9), SceneTreeTimer.SignalName.Timeout);
        float yDive = player.GlobalPosition.Y;
        Input.ActionRelease(Core.GameInput.Actions.Crouch);

        Input.ActionPress(Core.GameInput.Actions.Jump);
        await ToSignal(GetTree().CreateTimer(0.9), SceneTreeTimer.SignalName.Timeout);
        float yRise = player.GlobalPosition.Y;
        Input.ActionRelease(Core.GameInput.Actions.Jump);

        bool dived = yDive < yStart - 0.5f;
        bool rose = yRise > yDive + 0.5f;
        GD.Print($"[RA] swim-test: inWater={inWater} headUnder={headUnder} " +
                 $"yStart={yStart:F1} yDive={yDive:F1} yRise={yRise:F1} dived={dived} rose={rose}");
        GetTree().Quit(0);
    }

    /// <summary>Mining test: hold the break action on a stone block and screenshot
    /// mid-mine, so the progressive crack overlay is visible (stone's 1.6s hardness
    /// gives a wide window to catch a mid-stage crack).</summary>
    private async void RunMiningTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 1, 24), creative: true, captureMouse: false);
        Core.WorldGen.FlatGround(session.World, 0, 48, 0, 48, 0);
        for (int y = 1; y <= 3; y++)
            session.World.SetBlock(24, y, 20, Core.BlockRegistry.IdOf("stone"), false);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        session.Player.Head.Rotation = new Vector3(-0.12f, 0, 0); // look at the pillar

        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        // Hold the keyboard break action (works without a captured mouse) to mine.
        Input.ActionPress(Core.GameInput.Actions.KbBreak);
        await ToSignal(GetTree().CreateTimer(0.95), SceneTreeTimer.SignalName.Timeout); // ~60% into a 1.6s mine
        await Capture("res://_mining.png", 0.0);
        Input.ActionRelease(Core.GameInput.Actions.KbBreak);
        GD.Print("[RA] mining-test: captured mid-mine");
        GetTree().Quit(0);
    }

    /// <summary>Strata test: generate real terrain, carve away one half to expose a
    /// vertical cross-section, and screenshot it so the underground layering — top
    /// soil, stone, ore deposits, cave air and the bedrock floor — is visible.</summary>
    private async void RunStrataTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        var gen = new Core.TerrainGenerator(1337);
        var buf = new ushort[Core.Chunk.Volume];
        for (int cx = 0; cx < 3; cx++)
        for (int cy = -1; cy <= 3; cy++)
        for (int cz = 0; cz < 3; cz++)
        {
            gen.Generate(new Vector3I(cx, cy, cz), buf);
            world.LoadChunk(new Vector3I(cx, cy, cz), buf);
        }
        world.RebuildAllNow();

        // Carve away the +X half to reveal the cross-section face at x = 24.
        for (int x = 24; x < 48; x++)
        for (int y = -16; y < 48; y++)
        for (int z = 0; z < 48; z++)
            world.SetBlock(x, y, z, 0, false);
        world.MarkAllDirty();
        world.RebuildAllNow();

        var cam = new Camera3D { Name = "Cam", Fov = 60f };
        AddChild(cam);
        cam.Current = true;
        cam.Position = new Vector3(31, 9, 24);                 // close to the x=24 cut face
        cam.LookAt(new Vector3(23, 5, 24), Vector3.Up);
        await Capture("res://_strata.png", 0.8);
        GD.Print("[RA] strata-test: captured cross-section");
        GetTree().Quit(0);
    }

    /// <summary>Water-seam test: a wide, multi-chunk sheet of water (so the greedy
    /// mesher produces big merged top quads and chunk-border edges — the exact case
    /// that used to show a faint grid) viewed at a grazing angle, screenshotted for
    /// a visual check that the per-pixel wave normal removed the seams.</summary>
    private async void RunWaterTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 6, 44), creative: true, captureMouse: false);
        ushort sand = Core.BlockRegistry.IdOf("sand");
        ushort water = Core.BlockRegistry.IdOf("water");
        for (int x = -8; x <= 56; x++)
        for (int z = -8; z <= 56; z++)
        {
            session.World.SetBlock(x, -2, z, sand, false);
            session.World.SetBlock(x, -1, z, water, false);
            session.World.SetBlock(x, 0, z, water, false);
        }
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        session.Player.Head.Rotation = new Vector3(-0.16f, 0, 0); // grazing look across the water
        await Capture("res://_water.png", 0.8);
        GetTree().Quit(0);
    }

    /// <summary>Phase-5 sky render test: screenshot the world at noon, dusk and night
    /// to eyeball the sky shader (sun, clouds, stars) and the lighting swing.</summary>
    private async void RunSkyTest()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 8, 24), creative: true, captureMouse: false);
        Core.WorldGen.FlatGround(session.World, -8, 56, -8, 56, 0);
        for (int i = 0; i < 6; i++)
            Core.WorldGen.Tree(session.World, new Vector3I(12 + i * 6, 1, 30));
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        // SUN: mid-morning, face north (-Z, where the sun sits) and pitch up to it.
        session.Player.Rotation = new Vector3(0, 0, 0);
        session.Player.Head.Rotation = new Vector3(0.55f, 0, 0);
        session.Env.SetFixedTime(Core.EnvironmentController.Morning);
        await Capture("res://_sky_sun.png", 0.6);

        // CLOUDS: noon, look high up.
        session.Player.Head.Rotation = new Vector3(0.95f, 0, 0);
        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        await Capture("res://_sky_clouds.png", 0.6);

        // MOON: pre-dawn, face south (+Z, where the moon sits) and pitch up to it.
        session.Player.Rotation = new Vector3(0, Mathf.Pi, 0);
        session.Player.Head.Rotation = new Vector3(0.55f, 0, 0);
        session.Env.SetFixedTime(0.85f);
        await Capture("res://_sky_moon.png", 0.6);

        GD.Print("[RA] sky-test: done");
        GetTree().Quit(0);
    }

    /// <summary>Phase-4 biome test: a wide sample of the seeded world should contain
    /// several distinct biomes, and generating a band of chunks should place trees
    /// (logs + leaves) in the wooded ones.</summary>
    private void RunBiomeTest()
    {
        var gen = new Core.TerrainGenerator(1337);
        var counts = new System.Collections.Generic.Dictionary<Core.Biome, int>();
        const int step = 8, range = 2400;
        for (int x = -range; x <= range; x += step)
        for (int z = -range; z <= range; z += step)
        {
            var b = gen.BiomeAt(x, z, gen.SurfaceHeight(x, z));
            counts.TryGetValue(b, out int c);
            counts[b] = c + 1;
        }

        ushort log = Core.BlockRegistry.IdOf("oak_log");
        ushort leaves = Core.BlockRegistry.IdOf("leaves");
        int logCount = 0, leafCount = 0;
        var buf = new ushort[Core.Chunk.Volume];
        for (int cx = 0; cx < 40; cx++)
        for (int cy = 0; cy <= 3; cy++)
        {
            gen.Generate(new Vector3I(cx, cy, 0), buf);
            foreach (var id in buf)
            {
                if (id == log) logCount++;
                else if (id == leaves) leafCount++;
            }
        }

        var summary = new System.Text.StringBuilder();
        foreach (var kv in counts) summary.Append($"{kv.Key}:{kv.Value} ");
        GD.Print($"[RA] biome-test: distinct={counts.Count} logs={logCount} leaves={leafCount} | {summary}");
        QuitSoon();
    }

    /// <summary>Underground test: below the surface the world should be layered —
    /// a bedrock floor at the bottom, mostly stone, scattered ore deposits and some
    /// carved cave air — and the new 3D noise must be deterministic.</summary>
    private void RunUndergroundTest()
    {
        var gen = new Core.TerrainGenerator(1337);
        ushort stone = Core.BlockRegistry.IdOf("stone");
        ushort bedrock = Core.BlockRegistry.IdOf("bedrock");
        ushort coal = Core.BlockRegistry.IdOf("coal_ore");
        ushort copper = Core.BlockRegistry.IdOf("copper_ore");
        ushort iron = Core.BlockRegistry.IdOf("iron_ore");
        ushort gold = Core.BlockRegistry.IdOf("gold_ore");

        int stoneN = 0, bedrockN = 0, oreN = 0, caveN = 0, bottomCells = 0, bottomBedrock = 0;
        var buf = new ushort[Core.Chunk.Volume];
        for (int cx = 0; cx < 16; cx++)
        for (int cz = 0; cz < 3; cz++)
        for (int cy = -1; cy <= 3; cy++)
        {
            gen.Generate(new Vector3I(cx, cy, cz), buf);
            for (int ly = 0; ly < Core.Chunk.Size; ly++)
            for (int lz = 0; lz < Core.Chunk.Size; lz++)
            for (int lx = 0; lx < Core.Chunk.Size; lx++)
            {
                ushort id = buf[Core.Chunk.Index(lx, ly, lz)];
                int wy = cy * Core.Chunk.Size + ly;
                if (id == stone) stoneN++;
                else if (id == bedrock) bedrockN++;
                else if (id == coal || id == copper || id == iron || id == gold) oreN++;
                else if (id == 0 && wy >= -13 && wy <= 8) caveN++; // air this deep == a cave
                if (wy == -16) { bottomCells++; if (id == bedrock) bottomBedrock++; }
            }
        }

        var n = new Core.ValueNoise2D(99);
        bool det = Mathf.IsEqualApprox(n.Noise3(1.5f, 2.5f, 3.5f), n.Noise3(1.5f, 2.5f, 3.5f))
            && n.Fractal3(4f, 5f, 6f, 3) >= 0f && n.Fractal3(4f, 5f, 6f, 3) <= 1f;

        GD.Print($"[RA] underground-test: stone={stoneN} ores={oreN} bedrock={bedrockN} caves={caveN} " +
                 $"bottomAllBedrock={bottomBedrock == bottomCells && bottomCells > 0} noise3Det={det}");
        QuitSoon();
    }

    /// <summary>Phase-3 greedy-meshing test: a flat 16×16 grass slab (one chunk
    /// layer) should collapse to a handful of merged quads — six faces, all with
    /// uniform AO — instead of the ~576 unmerged faces it would otherwise be.</summary>
    private void RunGreedyTest()
    {
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);
        ushort grass = Core.BlockRegistry.IdOf("grass");
        for (int x = 0; x < Core.Chunk.Size; x++)
        for (int z = 0; z < Core.Chunk.Size; z++)
            world.SetBlock(x, 0, z, grass, false);

        var chunk = world.Chunks[new Vector3I(0, 0, 0)];
        var snap = Core.ChunkMesher.Capture(world, chunk);
        var data = Core.ChunkMesher.BuildData(snap);
        int verts = data.VertexCount;
        int tris = (data.Opaque.Indices.Count + data.Water.Indices.Count) / 3;
        // 6 faces (top, bottom, four side strips) -> 6 quads -> 24 verts, 12 tris.
        bool merged = verts <= 32;
        GD.Print($"[RA] greedy-test: verts={verts} tris={tris} collisionVerts={data.Collision.Count} merged={merged}");
        QuitSoon();
    }

    /// <summary>Phase-3 streaming test: a procedural world should load chunks around
    /// a moving target, mesh them, provide solid ground, and unload chunks the
    /// target leaves far behind.</summary>
    private async void RunStreamTest()
    {
        Core.Scenery.AddDaylight(this);
        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world);

        var gen = new Core.TerrainGenerator(1337);
        var target = new Node3D { Name = "Target" };
        AddChild(target);
        int surf0 = gen.SurfaceHeight(8, 8);
        target.GlobalPosition = new Vector3(8, surf0 + 2, 8);
        world.StartStreaming(gen, target, renderDistance: 4, minChunkY: -1, maxChunkY: 3);

        // Let the area around the origin stream in.
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        int genNear = world.GeneratedChunkCount;
        int chunksNear = world.ChunkCount;
        int sh = gen.SurfaceHeight(8, 8);
        bool groundExists = world.GetBlockId(8, sh, 8) != 0;
        bool airAbove = world.GetBlockId(8, sh + 6, 8) == 0;
        bool holdClear = !world.StreamingHold(new Vector3(8, sh + 1, 8));
        int meshedNear = 0;
        foreach (var kv in world.Chunks) if (kv.Value.Meshed) meshedNear++;
        var originChunk = Core.VoxelWorld.ChunkCoord(8, sh, 8);

        // Teleport far away: the origin region should unload, a new region load.
        int fx = 8 + 16 * 48;
        target.GlobalPosition = new Vector3(fx, gen.SurfaceHeight(fx, 8) + 2, 8);
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        bool originUnloaded = !world.Chunks.ContainsKey(originChunk);
        bool farGround = world.GetBlockId(fx, gen.SurfaceHeight(fx, 8), 8) != 0;

        GD.Print($"[RA] stream-test: genNear={genNear} chunksNear={chunksNear} meshedNear={meshedNear} " +
                 $"groundExists={groundExists} airAbove={airAbove} holdClear={holdClear} " +
                 $"originUnloaded={originUnloaded} farGround={farGround}");
        GetTree().Quit(0);
    }

    private void RunWorldTest()
    {
        Core.Scenery.AddDaylight(this);

        var world = new Core.VoxelWorld { Name = "World" };
        AddChild(world); // _Ready builds textures + material
        Core.WorldGen.Showcase(world);
        world.MarkAllDirty();
        world.RebuildAllNow();
        GD.Print($"[RA] test-world: chunks={world.ChunkCount}");

        var cam = new Camera3D { Name = "TestCam", Fov = 70f };
        AddChild(cam);
        CaptureSequence(cam);
    }

    private async void CaptureSequence(Camera3D cam)
    {
        cam.Current = true;
        // wide overview
        cam.Position = new Vector3(26, 16, 40);
        cam.LookAt(new Vector3(24, 2, 6), Vector3.Up);
        await Capture("res://_world_test.png", 1.0);
        // close-up on the pillars + hut to inspect texture / normal / AO detail
        cam.Position = new Vector3(10, 5, 12);
        cam.LookAt(new Vector3(18, 2.5f, 6), Vector3.Up);
        await Capture("res://_world_close.png", 0.4);
        GetTree().Quit(0);
    }

    private async System.Threading.Tasks.Task Capture(string path, double afterSeconds)
    {
        await ToSignal(GetTree().CreateTimer(afterSeconds), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(path);
        GD.Print($"[RA] screenshot {path} -> {e}");
    }
}
