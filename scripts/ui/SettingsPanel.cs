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
        box.AddChild(Slider("Mouse sensitivity", 0.2f, 3.0f, Settings.MouseSensitivity,
            v => { Settings.MouseSensitivity = v; Settings.Save(); }));
        box.AddChild(Slider("Master volume", 0f, 1f, Settings.MasterVolume,
            v => { Settings.MasterVolume = v; Settings.Save(); }));

        var back = UiKit.Button("Back");
        back.Pressed += () => { Visible = false; OnBack?.Invoke(); };
        box.AddChild(back);
    }

    private Control Slider(string label, float min, float max, float value, Action<float> onChange)
    {
        var row = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        var valueLabel = new Label { Text = $"{label}:  {value:0.00}" };
        valueLabel.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(valueLabel);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(420, 24) };
        slider.ValueChanged += v =>
        {
            valueLabel.Text = $"{label}:  {v:0.00}";
            onChange((float)v);
        };
        row.AddChild(slider);
        return row;
    }

    public void Open() => Visible = true;
}
