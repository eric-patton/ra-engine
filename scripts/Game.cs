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

    public override void _Ready()
    {
        var version = (string)Engine.GetVersionInfo()["string"];
        GD.Print($"[RA] Game ready on Godot {version}");
        Core.Settings.Load();
        AddChild(new Core.AudioManager { Name = "Audio" }); // persistent, app-wide sound

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
            case "--test-daynight":
                RunDayNightTest();
                break;
            case "--test-sky":
                RunSkyTest();
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
        _menu = new UI.MainMenu { Name = "MainMenu" };
        _menu.OnPlayLesson = id => StartLesson(Lessons.LessonCatalog.Get(id));
        _menu.OnSandbox = StartSandbox;
        _menu.OnQuit = () => GetTree().Quit();
        AddChild(_menu);
        Core.AudioManager.StartMusic();   // calm bed under the menu
        Core.AudioManager.StopAmbience(); // outdoor ambience belongs to play, not menu
    }

    private void ClearMenu()
    {
        if (_menu != null) { _menu.QueueFree(); _menu = null; }
    }

    /// <summary>An endless, procedurally generated creative world that streams
    /// chunks in around the player as they explore.</summary>
    private void StartSandbox()
    {
        ClearMenu();
        _session = new GameSession { Name = "Session", ReturnToMenuRequested = ShowMainMenu };
        AddChild(_session);

        var gen = new Core.TerrainGenerator(seed: 1337);
        int sx = 24, sz = 24;
        int surface = gen.SurfaceHeight(sx, sz);
        var spawn = new Vector3(sx + 0.5f, surface + 3f, sz + 0.5f);

        _session.Setup(spawn, creative: true);
        var world = _session.World;
        world.StartStreaming(gen, _session.Player, renderDistance: 6, minChunkY: -1, maxChunkY: 3);
        world.EnsureSpawnArea(spawn, radius: 2); // immediate ground under the player

        _session.Env.SetWeatherFollow(_session.Player);
        _session.AddChild(new Core.WeatherDirector
        {
            Name = "Weather", Generator = gen, Player = _session.Player, Env = _session.Env,
        });

        _session.Hud.ShowBanner("Sandbox — endless world!  (WASD move · arrows/numpad or mouse look · +/- place/break · G fly · B mode)", 6f);
        Core.AudioManager.StartMusic();
        Core.AudioManager.StartAmbience();
    }

    private GameSession StartLesson(Lessons.ILesson lesson)
    {
        ClearMenu();
        _session = new GameSession { Name = "Session", ReturnToMenuRequested = ShowMainMenu };
        AddChild(_session);
        _session.Setup(lesson.Spawn, creative: false);
        if (lesson.TimeOfDay is float tod) _session.Env.SetFixedTime(tod);
        else _session.Env.SetCycle(true);
        lesson.Build(_session);
        _session.Hud.ShowBanner($"{lesson.Title}", 4f);
        Core.AudioManager.StartMusic();
        Core.AudioManager.StartAmbience();
        return _session;
    }

    private async void RunLessonTest()
    {
        var lesson = Lessons.LessonCatalog.Get("david");
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(lesson.Spawn, creative: false, captureMouse: false);
        lesson.Build(session);
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
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(lesson.Spawn, creative: false, captureMouse: false);
        lesson.Build(session);
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

        // keyboard BREAK -> removes whatever is now under the crosshair
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        Vector3I breakCell = session.Interactor.CurrentTarget.Block;
        Input.ActionPress(Core.GameInput.Actions.KbBreak);
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease(Core.GameInput.Actions.KbBreak);
        bool broke = session.World.GetBlockId(breakCell) == 0;

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
        session.Player.Head.Rotation = new Vector3(-0.05f, 0, 0);

        session.Env.SetFixedTime(Core.EnvironmentController.Noon);
        await Capture("res://_sky_noon.png", 0.6);
        session.Env.SetFixedTime(Core.EnvironmentController.Dusk);
        await Capture("res://_sky_dusk.png", 0.6);
        session.Env.SetFixedTime(Core.EnvironmentController.Night);
        await Capture("res://_sky_night.png", 0.6);
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
