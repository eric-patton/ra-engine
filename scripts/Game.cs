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
        }
    }
}
