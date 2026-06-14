using System;
using System.Collections.Generic;
using Godot;

namespace RAEngine.UI;

/// <summary>Teacher tools (opened with F1): toggle a no-combat "safe mode", enter
/// present mode (hide the HUD for clean screenshots), drop a scripture signpost at
/// the spot you're looking at, and set / teleport between waypoints.</summary>
public partial class TeacherPanel : CanvasLayer
{
    public Func<bool> GetSafe;
    public Action<bool> SetSafe;
    public Action OnPresent;
    public Action<string> OnPlaceSignpost;
    public Action OnAddWaypoint;
    public Func<IReadOnlyList<(string name, Vector3 pos)>> GetWaypoints;
    public Action<int> OnTeleport;
    public Action OnClose;

    private Button _safeBtn;
    private LineEdit _signText;
    private VBoxContainer _wpList;

    public override void _Ready()
    {
        Layer = 19;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;

        var root = UiKit.Dim(new Color(0.04f, 0.05f, 0.08f, 0.93f));
        AddChild(root);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        box.AddThemeConstantOverride("separation", 10);
        center.AddChild(box);

        box.AddChild(UiKit.Title("Teacher Tools", 40, UiKit.Gold));

        _safeBtn = UiKit.Button("Safe mode: OFF");
        _safeBtn.Pressed += () => { SetSafe?.Invoke(!(GetSafe?.Invoke() ?? false)); RefreshSafe(); };
        box.AddChild(_safeBtn);

        var present = UiKit.Button("Present mode  (hide HUD · F2 screenshot)");
        present.Pressed += () => { OnPresent?.Invoke(); Close(); };
        box.AddChild(present);

        box.AddChild(Heading("Scripture signpost"));
        _signText = new LineEdit { PlaceholderText = "Type a verse, then place…", CustomMinimumSize = new Vector2(520, 40) };
        box.AddChild(_signText);
        var place = UiKit.Button("Place signpost where I'm looking");
        place.Pressed += () =>
        {
            OnPlaceSignpost?.Invoke(_signText.Text);
            _signText.Text = "";
            Close();
        };
        box.AddChild(place);

        box.AddChild(Heading("Waypoints"));
        var addWp = UiKit.Button("Set waypoint here");
        addWp.Pressed += () => { OnAddWaypoint?.Invoke(); RebuildWaypoints(); };
        box.AddChild(addWp);

        _wpList = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        _wpList.AddThemeConstantOverride("separation", 6);
        box.AddChild(_wpList);

        var close = UiKit.Button("Close  (F1)");
        close.Pressed += Close;
        box.AddChild(close);
    }

    private static Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 18);
        l.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        return l;
    }

    public void Open()
    {
        Visible = true;
        RefreshSafe();
        RebuildWaypoints();
    }

    public void Close()
    {
        Visible = false;
        OnClose?.Invoke();
    }

    private void RefreshSafe()
    {
        bool on = GetSafe?.Invoke() ?? false;
        _safeBtn.Text = on ? "Safe mode: ON" : "Safe mode: OFF";
    }

    private void RebuildWaypoints()
    {
        foreach (Node c in _wpList.GetChildren()) c.QueueFree();
        var list = GetWaypoints?.Invoke();
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var (name, pos) = list[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var label = new Label
            {
                Text = $"{name}  ({pos.X:F0}, {pos.Y:F0}, {pos.Z:F0})",
                CustomMinimumSize = new Vector2(360, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.AddThemeFontSizeOverride("font_size", 16);
            row.AddChild(label);
            int idx = i;
            var go = UiKit.Button("Go", 16);
            go.CustomMinimumSize = new Vector2(120, 42);
            go.Pressed += () => { OnTeleport?.Invoke(idx); Close(); };
            row.AddChild(go);
            _wpList.AddChild(row);
        }
    }
}
