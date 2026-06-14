using System;
using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>Mouse sensitivity + master volume sliders. Reusable from the main
/// menu and the pause menu; changes save immediately.</summary>
public partial class SettingsPanel : CanvasLayer
{
    public Action OnBack;
    private Control _root;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiKit.Dim(new Color(0.04f, 0.05f, 0.08f, 0.96f));
        AddChild(_root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        center.AddChild(box);

        box.AddChild(UiKit.Title("Settings", 44, UiKit.Gold));
        box.AddChild(MouseModeRow());
        box.AddChild(Slider("Mouse sensitivity", 0.2f, 3.0f, Settings.MouseSensitivity,
            v => { Settings.MouseSensitivity = v; Settings.Save(); }));
        box.AddChild(Slider("Keyboard look speed", 40f, 220f, Settings.KeyboardLookSpeed,
            v => { Settings.KeyboardLookSpeed = v; Settings.Save(); }, "0"));
        box.AddChild(Slider("Master volume", 0f, 1f, Settings.MasterVolume,
            v => { Settings.MasterVolume = v; Settings.Save(); }));

        var back = UiKit.Button("Back");
        back.Pressed += () => { Visible = false; OnBack?.Invoke(); };
        box.AddChild(back);
    }

    private Control Slider(string label, float min, float max, float value, Action<float> onChange, string fmt = "0.00")
    {
        var row = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        var valueLabel = new Label { Text = $"{label}:  {value.ToString(fmt)}" };
        valueLabel.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(valueLabel);
        double step = fmt == "0" ? 5 : 0.05;
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(420, 24) };
        slider.ValueChanged += v =>
        {
            valueLabel.Text = $"{label}:  {((float)v).ToString(fmt)}";
            onChange((float)v);
        };
        row.AddChild(slider);
        return row;
    }

    private Control MouseModeRow()
    {
        var row = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        var label = new Label { Text = "Mouse" };
        label.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(label);

        var opt = new OptionButton { CustomMinimumSize = new Vector2(420, 30) };
        opt.AddItem("Free cursor — click to look", (int)Settings.MouseCapture.ClickToCapture);
        opt.AddItem("Keyboard only — never grab mouse", (int)Settings.MouseCapture.Off);
        opt.AddItem("Always capture (classic FPS)", (int)Settings.MouseCapture.Always);
        opt.Selected = opt.GetItemIndex((int)Settings.CaptureMode);
        opt.ItemSelected += idx =>
        {
            Settings.CaptureMode = (Settings.MouseCapture)opt.GetItemId((int)idx);
            Settings.Save();
        };
        row.AddChild(opt);
        return row;
    }

    public void Open() => Visible = true;
}
