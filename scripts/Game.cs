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

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--smoke")
            {
                GD.Print("[RA] smoke: C# assembly loaded OK");
                GetTree().CreateTimer(0.1).Timeout += () => GetTree().Quit(0);
            }
            else if (arg == "--gen-textures")
            {
                Tools.TextureForge.GenerateAll();
                GetTree().CreateTimer(0.1).Timeout += () => GetTree().Quit(0);
            }
            else if (arg == "--test-blocks")
            {
                Core.BlockRegistry.EnsureInit();
                var tex = Core.BlockTextures.Build();
                GD.Print($"[RA] test-blocks: blocks={Core.BlockRegistry.Count} layers={tex.LayerCount} " +
                         $"grass.top.layer={Core.BlockRegistry.Get("grass").FaceLayer[(int)Core.Face.PosY]}");
                GetTree().CreateTimer(0.1).Timeout += () => GetTree().Quit(0);
            }
            else if (arg == "--test-world")
            {
                RunWorldTest();
            }
            else if (arg == "--test-player")
            {
                RunPlayerTest();
            }
        }
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
