using Godot;
using RAEngine.Core;
using RAEngine.UI;

namespace RAEngine.PlayerSys;

/// <summary>The "pre-build" level editor. Active in build mode. Lets you mark a
/// region (Z/X), fill/clear it (F/R), copy/paste prefabs (C/V), and save/load the
/// whole world (F5/F9). Cells are taken from the block under the crosshair.</summary>
public partial class BuildEditor : Node3D
{
    public VoxelWorld World;
    public BlockInteractor Interactor;
    public Hotbar Hotbar;
    public GameHud Hud;
    public bool Enabled;
    public string WorldPath = "user://worlds/quicksave.rworld";

    private Vector3I? _a, _b;
    private Structure _clipboard;
    private MeshInstance3D _markA, _markB;

    public override void _Ready()
    {
        _markA = MakeMarker(new Color(0.3f, 1f, 0.4f, 0.35f));
        _markB = MakeMarker(new Color(1f, 0.4f, 0.35f, 0.35f));
        AddChild(_markA);
        AddChild(_markB);
    }

    private static MeshInstance3D MakeMarker(Color c)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One * 1.04f },
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = c,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        return mi;
    }

    public override void _Process(double delta)
    {
        if (!Enabled || World == null || Interactor == null) return;
        if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

        var t = Interactor.CurrentTarget;

        if (Input.IsActionJustPressed(GameInput.Actions.EditorSave))
        {
            bool ok = WorldIO.SaveWorld(World, WorldPath);
            Hud?.ShowBanner(ok ? $"World saved → {WorldPath}" : "Save failed", 2.5f);
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.EditorLoad))
        {
            bool ok = WorldIO.LoadWorld(World, WorldPath);
            Hud?.ShowBanner(ok ? "World loaded" : "No saved world found", 2.5f);
        }

        if (!t.Ok) return;

        if (Input.IsActionJustPressed(GameInput.Actions.MarkA))
        {
            _a = t.Block; _markA.GlobalPosition = (Vector3)t.Block + Vector3.One * 0.5f; _markA.Visible = true;
            Hud?.ShowBanner($"Corner A {t.Block}", 1.2f);
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.MarkB))
        {
            _b = t.Block; _markB.GlobalPosition = (Vector3)t.Block + Vector3.One * 0.5f; _markB.Visible = true;
            Hud?.ShowBanner($"Corner B {t.Block}", 1.2f);
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.FillRegion))
        {
            FillRegion(Hotbar?.SelectedBlockId ?? 0);
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.ClearRegion))
        {
            FillRegion(0);
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.Capture))
        {
            if (_a is { } a && _b is { } b)
            {
                _clipboard = WorldIO.Capture(World, a, b);
                Hud?.ShowBanner($"Captured prefab {_clipboard.Size.X}×{_clipboard.Size.Y}×{_clipboard.Size.Z}", 2f);
            }
        }
        else if (Input.IsActionJustPressed(GameInput.Actions.Stamp))
        {
            if (_clipboard != null)
            {
                WorldIO.Stamp(World, _clipboard, t.Prev);
                Hud?.ShowBanner("Stamped prefab", 1.5f);
            }
        }
    }

    private void FillRegion(ushort id)
    {
        if (_a is not { } a || _b is not { } b) { Hud?.ShowBanner("Mark corners A (Z) and B (X) first", 2f); return; }
        Vector3I min = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
        Vector3I max = new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));
        int n = 0;
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        { World.SetBlock(x, y, z, id, remesh: false); n++; }
        World.MarkAllDirty();
        Hud?.ShowBanner(id == 0 ? $"Cleared {n} blocks" : $"Filled {n} blocks", 1.5f);
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        if (!on) { _markA.Visible = false; _markB.Visible = false; }
    }
}
