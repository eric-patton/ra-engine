using System.Collections.Generic;
using Godot;
using RAEngine.Core;

namespace RAEngine.UI;

/// <summary>Heads-up display: crosshair, hotbar, health and air bars, plus a
/// transient status/objective banner. Designed large and high-contrast for a
/// mixed-age audience.</summary>
public partial class GameHud : CanvasLayer
{
    public Hotbar Hotbar { get; private set; }
    private ProgressBar _health;
    private ProgressBar _air;
    private Label _banner;
    private Timer _bannerTimer;
    private Control _crosshair;
    private Label _weaponLabel;

    public override void _Ready()
    {
        BuildCrosshair();
        BuildHotbar();
        BuildBars();
        BuildBanner();
        BuildWeaponLabel();
        BuildInteractPrompt();
        BuildObjectives();
    }

    private void BuildWeaponLabel()
    {
        _weaponLabel = new Label { Visible = false, Text = "" };
        _weaponLabel.AddThemeFontSizeOverride("font_size", 20);
        _weaponLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _weaponLabel.AddThemeConstantOverride("outline_size", 4);
        _weaponLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _weaponLabel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        AddChild(_weaponLabel);
        GetViewport().SizeChanged += RelayoutWeapon;
        RelayoutWeapon();
    }

    private void RelayoutWeapon()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _weaponLabel.Size = new Vector2(220, 28);
        _weaponLabel.Position = new Vector2(vp.X - 240, vp.Y - 44);
        _weaponLabel.HorizontalAlignment = HorizontalAlignment.Right;
    }

    public void SetWeapon(string name) => _weaponLabel.Text = string.IsNullOrEmpty(name) ? "" : $"⚔ {name}";
    public void SetWeaponVisible(bool on) => _weaponLabel.Visible = on;
    public void SetHotbarVisible(bool on) => Hotbar.Visible = on;

    private Label _interactPrompt;

    private void BuildInteractPrompt()
    {
        _interactPrompt = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _interactPrompt.AddThemeFontSizeOverride("font_size", 22);
        _interactPrompt.AddThemeColorOverride("font_outline_color", Colors.Black);
        _interactPrompt.AddThemeConstantOverride("outline_size", 5);
        _interactPrompt.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_interactPrompt);
        GetViewport().SizeChanged += RelayoutInteract;
        RelayoutInteract();
    }

    private void RelayoutInteract()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _interactPrompt.Size = new Vector2(vp.X, 28);
        _interactPrompt.Position = new Vector2(0, vp.Y * 0.62f);
    }

    public void SetInteractPrompt(string text)
    {
        if (_interactPrompt == null) return;
        _interactPrompt.Text = text;
        _interactPrompt.Visible = !string.IsNullOrEmpty(text);
    }

    // ---- objectives + lesson completion ----------------------------------

    private VBoxContainer _objectives;
    private readonly System.Collections.Generic.List<Label> _objLabels = new();
    private readonly System.Collections.Generic.List<string> _objText = new();
    private Label _center;

    private void BuildObjectives()
    {
        _objectives = new VBoxContainer { Name = "Objectives" };
        _objectives.AddThemeConstantOverride("separation", 4);
        AddChild(_objectives);

        _center = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _center.AddThemeFontSizeOverride("font_size", 40);
        _center.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.55f));
        _center.AddThemeColorOverride("font_outline_color", Colors.Black);
        _center.AddThemeConstantOverride("outline_size", 8);
        _center.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_center);

        GetViewport().SizeChanged += RelayoutObjectives;
        RelayoutObjectives();
    }

    private void RelayoutObjectives()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _objectives.Position = new Vector2(vp.X - 320, 20);
        _objectives.Size = new Vector2(300, 200);
        _center.Position = new Vector2(vp.X * 0.15f, vp.Y * 0.35f);
        _center.Size = new Vector2(vp.X * 0.7f, vp.Y * 0.3f);
    }

    public void SetObjectives(System.Collections.Generic.IEnumerable<string> items)
    {
        foreach (Node c in _objectives.GetChildren()) c.QueueFree();
        _objLabels.Clear();
        _objText.Clear();
        var title = new Label { Text = "Objectives" };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 4);
        _objectives.AddChild(title);
        foreach (string s in items)
        {
            var l = new Label { Text = "☐  " + s };
            l.AddThemeFontSizeOverride("font_size", 16);
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            l.AddThemeConstantOverride("outline_size", 4);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(300, 0);
            _objectives.AddChild(l);
            _objLabels.Add(l);
            _objText.Add(s);
        }
    }

    public void CompleteObjective(int i)
    {
        if (i < 0 || i >= _objLabels.Count) return;
        _objLabels[i].Text = "☑  " + _objText[i];
        _objLabels[i].Modulate = new Color(0.6f, 1f, 0.6f, 0.85f);
    }

    public void ShowCenter(string text, float seconds = 0f)
    {
        _center.Text = text;
        _center.Visible = !string.IsNullOrEmpty(text);
        if (seconds > 0)
            GetTree().CreateTimer(seconds).Timeout += () => _center.Visible = false;
    }

    public void Configure(BlockTextures tex, IEnumerable<ushort> palette)
    {
        Hotbar.Init(tex, palette);
    }

    private void BuildCrosshair()
    {
        _crosshair = new Control { Name = "Crosshair", MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_crosshair);
        _crosshair.Draw += () =>
        {
            Vector2 c = _crosshair.Size / 2f;
            var col = new Color(1, 1, 1, 0.85f);
            _crosshair.DrawLine(c + new Vector2(-9, 0), c + new Vector2(9, 0), col, 2f);
            _crosshair.DrawLine(c + new Vector2(0, -9), c + new Vector2(0, 9), col, 2f);
        };
        _crosshair.Resized += () => _crosshair.QueueRedraw();
    }

    private void BuildHotbar()
    {
        Hotbar = new Hotbar { Name = "Hotbar" };
        Hotbar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Hotbar);
    }

    private void BuildBars()
    {
        var box = new VBoxContainer { Name = "Stats" };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        box.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        box.Position = new Vector2(20, 20);

        _health = MakeBar(new Color(0.85f, 0.22f, 0.22f), "Health");
        _air = MakeBar(new Color(0.35f, 0.7f, 0.95f), "Air");
        box.AddChild(WithLabel("❤", _health)); // heart
        box.AddChild(WithLabel("○", _air));     // air bubble
        _air.GetParent<Control>().Visible = false;   // shown only while swimming
    }

    private static ProgressBar MakeBar(Color fill, string name)
    {
        var bar = new ProgressBar
        {
            Name = name,
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(220, 22),
        };
        var bg = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.5f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        var fg = new StyleBoxFlat { BgColor = fill, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        bar.AddThemeStyleboxOverride("background", bg);
        bar.AddThemeStyleboxOverride("fill", fg);
        return bar;
    }

    private static HBoxContainer WithLabel(string glyph, Control bar)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var lbl = new Label { Text = glyph };
        lbl.AddThemeFontSizeOverride("font_size", 20);
        lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
        lbl.AddThemeConstantOverride("outline_size", 4);
        row.AddChild(lbl);
        row.AddChild(bar);
        return row;
    }

    private void BuildBanner()
    {
        _banner = new Label
        {
            Name = "Banner",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        _banner.AddThemeFontSizeOverride("font_size", 30);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 6);
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_banner);
        GetViewport().SizeChanged += RelayoutBanner;
        RelayoutBanner();

        _bannerTimer = new Timer { OneShot = true };
        AddChild(_bannerTimer);
        _bannerTimer.Timeout += () => _banner.Visible = false;
    }

    private void RelayoutBanner()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _banner.Position = new Vector2(0, 70);
        _banner.Size = new Vector2(vp.X, 50);
    }

    public void SetHealth(float current, float max)
    {
        _health.MaxValue = max;
        _health.Value = current;
    }

    public void SetAir(float current, float max)
    {
        _air.MaxValue = max;
        _air.Value = current;
        _air.GetParent<Control>().Visible = current < max - 0.01f;
    }

    public void ShowBanner(string text, float seconds = 3f)
    {
        _banner.Text = text;
        _banner.Visible = true;
        _bannerTimer.Start(seconds);
    }
}
