using Godot;

namespace RAEngine;

/// <summary>
/// Root node of the running game. For now it only confirms the C# assembly
/// loads and supports a headless smoke test; real systems are wired in later
/// milestones.
/// </summary>
public partial class Game : Node3D
{
    public override void _Ready()
    {
        var version = (string)Engine.GetVersionInfo()["string"];
        GD.Print($"[RA] Game ready on Godot {version}");

        string mode = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--")) mode = arg;

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
            default:
                StartSandbox();
                break;
        }
    }

    private void QuitSoon() => GetTree().CreateTimer(0.1).Timeout += () => GetTree().Quit(0);

    /// <summary>Default run: a flat creative world for free building.</summary>
    private void StartSandbox()
    {
        var session = new GameSession { Name = "Session" };
        AddChild(session);
        session.Setup(new Vector3(24, 3, 24), creative: true);
        Core.WorldGen.FlatGround(session.World, -8, 56, -8, 56, 0);
        session.World.MarkAllDirty();
        session.World.RebuildAllNow();
        session.Hud.ShowBanner("Sandbox — build freely! (G toggles fly)", 4f);
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
