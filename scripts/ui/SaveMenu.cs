using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>The "Build Sandbox" world picker: start a new world or continue a saved
/// one. Lists saved worlds with their last-played time, and lets you load or delete
/// each. Plain and large for a mixed-age audience.</summary>
public partial class SaveMenu : CanvasLayer
{
    public System.Action OnBack;
    public System.Action OnNewWorld;
    public System.Action<string> OnLoad;

    private VBoxContainer _list;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        var root = UiKit.Dim(new Color(0.04f, 0.05f, 0.08f, 0.96f));
        AddChild(root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        center.AddChild(box);

        box.AddChild(UiKit.Title("Worlds", 44, UiKit.Gold));

        var newBtn = UiKit.Button("✦  New World");
        newBtn.Pressed += () => OnNewWorld?.Invoke();
        box.AddChild(newBtn);

        _list = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        _list.AddThemeConstantOverride("separation", 8);
        box.AddChild(_list);

        var back = UiKit.Button("Back");
        back.Pressed += () => OnBack?.Invoke();
        box.AddChild(back);

        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        foreach (var save in SaveSystem.List())
            _list.AddChild(BuildRow(save));
    }

    private Control BuildRow(SaveData save)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(560, 54) };
        row.AddThemeConstantOverride("separation", 8);

        string when = save.SavedUnix > 0
            ? Time.GetDatetimeStringFromUnixTime(save.SavedUnix).Replace("T", "  ")
            : "";
        var label = new Label
        {
            Text = $"{save.Name}\n{when}",
            CustomMinimumSize = new Vector2(320, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(label);

        var load = UiKit.Button("Play", 18);
        load.CustomMinimumSize = new Vector2(110, 48);
        load.Pressed += () => OnLoad?.Invoke(save.Name);
        row.AddChild(load);

        var del = UiKit.Button("Delete", 16);
        del.CustomMinimumSize = new Vector2(110, 48);
        del.Pressed += () => { SaveSystem.Delete(save.Name); Rebuild(); };
        row.AddChild(del);

        return row;
    }
}
