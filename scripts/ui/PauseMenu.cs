using System;
using Godot;

namespace RAEngine.UI;

/// <summary>Esc-activated pause overlay. Freezes the tree while open. Offers
/// resume, settings, return to the main menu, and quit.</summary>
public partial class PauseMenu : CanvasLayer
{
    public Func<bool> CanPause;        // e.g. not while a dialogue is open
    public Action OnReturnToMenu;

    private Control _root;
    private SettingsPanel _settings;
    private bool _paused;
    private Input.MouseModeEnum _preMouse = Input.MouseModeEnum.Visible;

    public bool IsPaused => _paused;

    public override void _Ready()
    {
        Layer = 15;
        ProcessMode = ProcessModeEnum.Always;

        _root = UiKit.Dim(new Color(0.03f, 0.04f, 0.07f, 0.78f));
        _root.Visible = false;
        AddChild(_root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        box.AddChild(UiKit.Title("Paused", 48, UiKit.Gold));
        Add(box, "Resume", Resume);
        Add(box, "Settings", () => { _settings ??= MakeSettings(); _settings.Open(); });
        Add(box, "Main Menu", () => { SetPaused(false); OnReturnToMenu?.Invoke(); });
        Add(box, "Quit", () => GetTree().Quit());
    }

    private void Add(VBoxContainer box, string text, Action onPressed)
    {
        var b = UiKit.Button(text);
        b.Pressed += onPressed;
        box.AddChild(b);
    }

    private SettingsPanel MakeSettings()
    {
        var s = new SettingsPanel();
        AddChild(s);
        s.Visible = false;
        return s;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed(Core.GameInput.Actions.Pause)) return;
        if (_paused) Resume();
        else if (CanPause?.Invoke() ?? true) SetPaused(true);
        GetViewport().SetInputAsHandled();
    }

    private void Resume() => SetPaused(false);

    private void SetPaused(bool p)
    {
        _paused = p;
        GetTree().Paused = p;
        _root.Visible = p;
        // Free the cursor to use the menu, then restore exactly what it was — so
        // a player who was playing keyboard-only (free cursor) stays that way.
        if (p) { _preMouse = Input.MouseMode; Input.MouseMode = Input.MouseModeEnum.Visible; }
        else Input.MouseMode = _preMouse;
    }
}
