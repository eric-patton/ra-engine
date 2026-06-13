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
        }
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
