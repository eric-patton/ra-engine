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
