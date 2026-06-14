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
    private int _bannerToken;
    private Control _crosshair;
    private Label _weaponLabel;

    private ColorRect _underwater;

    public override void _Ready()
    {
        BuildUnderwater();
        BuildDamage();
        BuildCrosshair();
        BuildHotbar();
        BuildBars();
        BuildBanner();
        BuildWeaponLabel();
        BuildInteractPrompt();
        BuildMouseHint();
        BuildObjectives();
        BuildCompass();
        BuildClock();
        BuildDebug();
        BuildFade(); // last: a scene-transition curtain drawn over everything
    }

    // ---- debug HUD (F3) ---------------------------------------------------

    private Label _debug;

    public bool DebugVisible => _debug != null && _debug.Visible;

    private void BuildDebug()
    {
        _debug = new Label { Name = "Debug", Visible = false };
        _debug.AddThemeFontSizeOverride("font_size", 15);
        _debug.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
        _debug.AddThemeColorOverride("font_outline_color", Colors.Black);
        _debug.AddThemeConstantOverride("outline_size", 4);
        _debug.MouseFilter = Control.MouseFilterEnum.Ignore;
        _debug.Position = new Vector2(20, 96); // below the health/air bars
        AddChild(_debug);
    }

    public void ToggleDebug() { if (_debug != null) _debug.Visible = !_debug.Visible; }
    public void SetDebug(string text) { if (_debug != null && _debug.Text != text) _debug.Text = text; }

    // ---- clock ------------------------------------------------------------

    private Label _clock;

    private void BuildClock()
    {
        _clock = new Label { Name = "Clock", Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        _clock.AddThemeFontSizeOverride("font_size", 20);
        _clock.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.72f));
        _clock.AddThemeColorOverride("font_outline_color", Colors.Black);
        _clock.AddThemeConstantOverride("outline_size", 4);
        _clock.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_clock);
        GetViewport().SizeChanged += RelayoutClock;
        RelayoutClock();
    }

    private void RelayoutClock()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _clock.Size = new Vector2(vp.X, 26);
        _clock.Position = new Vector2(0, 60); // centred, just below the compass strip
    }

    public void SetClock(string text)
    {
        if (_clock != null && _clock.Text != text) _clock.Text = text;
    }

    // ---- compass ----------------------------------------------------------

    private Control _compass;
    private float _heading; // degrees; 0 = North (-Z), 90 = East (+X)
    private bool _compassOn;

    private void BuildCompass()
    {
        _compass = new Control { Name = "Compass", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _compass.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        AddChild(_compass);
        _compass.Draw += DrawCompass;
        GetViewport().SizeChanged += () => _compass.QueueRedraw();
    }

    public void SetCompassEnabled(bool on)
    {
        _compassOn = on;
        _compass.Visible = on;
    }

    public void SetHeading(float headingDegrees)
    {
        if (!_compassOn) return;
        _heading = headingDegrees;
        _compass.QueueRedraw();
    }

    private void DrawCompass()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float cx = vp.X / 2f, y = 22f, halfW = 230f, halfFov = 70f;
        var dim = new Color(1, 1, 1, 0.35f);
        var bright = new Color(1f, 0.92f, 0.55f);
        _compass.DrawLine(new Vector2(cx - halfW, y + 26), new Vector2(cx + halfW, y + 26), dim, 2f);
        _compass.DrawLine(new Vector2(cx, y + 12), new Vector2(cx, y + 40), bright, 2f); // centre tick

        (float ang, string s)[] marks =
        {
            (0, "N"), (45, "NE"), (90, "E"), (135, "SE"),
            (180, "S"), (225, "SW"), (270, "W"), (315, "NW"),
        };
        var font = ThemeDB.FallbackFont;
        foreach (var (ang, s) in marks)
        {
            float delta = Mathf.Wrap(ang - _heading, -180f, 180f);
            if (Mathf.Abs(delta) > halfFov) continue;
            float x = cx + (delta / halfFov) * halfW;
            bool card = s.Length == 1;
            int size = card ? 22 : 15;
            Vector2 ts = font.GetStringSize(s, HorizontalAlignment.Left, -1, size);
            _compass.DrawString(font, new Vector2(x - ts.X / 2f, y + 8), s, HorizontalAlignment.Left, -1, size,
                card ? bright : new Color(1, 1, 1, 0.7f));
        }
    }

    private ShaderMaterial _underwaterMat;
    private float _uwStrength;   // current, smoothed
    private float _uwTarget;     // 0 = dry, ~0.35 = at the waterline, 1 = submerged

    private void BuildUnderwater()
    {
        // A full-screen overlay running the underwater post-process shader. As the
        // FIRST HUD child it samples only the 3D scene, so the world warps/murks but
        // the HUD on top stays crisp. Hidden (no cost) while dry.
        _underwater = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _underwater.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shader = GD.Load<Shader>("res://assets/shaders/underwater.gdshader");
        if (shader != null)
        {
            _underwaterMat = new ShaderMaterial { Shader = shader };
            _underwater.Material = _underwaterMat;
        }
        else
        {
            // Fallback: the old flat blue tint if the shader is missing.
            _underwater.Color = new Color(0.12f, 0.36f, 0.55f, 0f);
        }
        AddChild(_underwater);
    }

    /// <summary>Target underwater intensity: 0 dry, ~0.35 at the waterline, 1 fully
    /// submerged. The effect eases toward this each frame.</summary>
    public void SetUnderwater(float target) => _uwTarget = Mathf.Clamp(target, 0f, 1f);

    private float _airDarken; // 0 = full air, 1 = out of breath (deepens the underwater murk)

    /// <summary>How starved of air the swimmer is (0..1). Deepens the underwater
    /// vignette as a gentle "come up for air" cue. Only visible while submerged.</summary>
    public void SetAirDarken(float v) => _airDarken = Mathf.Clamp(v, 0f, 1f);

    // ---- damage / low-health overlay --------------------------------------

    private ColorRect _damage;
    private ShaderMaterial _damageMat;
    private float _dmgFlash;   // transient hit flash, decays to 0
    private float _lowHealth;  // sustained 0..1 low-health pulse strength

    private void BuildDamage()
    {
        _damage = new ColorRect { Name = "Damage", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _damage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shader = GD.Load<Shader>("res://assets/shaders/damage.gdshader");
        if (shader != null)
        {
            _damageMat = new ShaderMaterial { Shader = shader };
            _damage.Material = _damageMat;
        }
        AddChild(_damage);
    }

    /// <summary>A coloured edge flash (amount 0..1) that fades out — used for hits.</summary>
    public void Flash(Color color, float amount)
    {
        if (_damageMat == null) return;
        _damageMat.SetShaderParameter("flash_color", color);
        _dmgFlash = Mathf.Max(_dmgFlash, Mathf.Clamp(amount, 0f, 1f));
    }

    /// <summary>Red hurt flash (the common case).</summary>
    public void FlashHurt(float amount = 0.7f) => Flash(new Color(0.85f, 0.06f, 0.05f), amount);

    /// <summary>Drive the low-health pulse from a health fraction (0..1). Pulses only
    /// when badly hurt (under ~30%); clears at full health or on death-screen.</summary>
    public void SetLowHealth(float healthFraction)
    {
        const float thresh = 0.3f;
        _lowHealth = healthFraction < thresh && healthFraction > 0.001f
            ? (thresh - healthFraction) / thresh
            : 0f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Underwater post-process: ease strength toward the target, deepen by low air.
        if (_underwater != null)
        {
            if (!Mathf.IsEqualApprox(_uwStrength, _uwTarget))
                _uwStrength = Mathf.MoveToward(_uwStrength, _uwTarget, dt * 3.5f);
            bool show = _uwStrength > 0.001f;
            _underwater.Visible = show;
            if (show)
            {
                if (_underwaterMat != null)
                {
                    _underwaterMat.SetShaderParameter("strength", _uwStrength);
                    _underwaterMat.SetShaderParameter("low_air", _airDarken);
                }
                else _underwater.Color = new Color(0.12f, 0.36f, 0.55f, _uwStrength * 0.34f); // fallback tint
            }
        }

        // Damage / low-health overlay: decay the transient flash, push uniforms.
        if (_damageMat != null)
        {
            if (_dmgFlash > 0f) _dmgFlash = Mathf.MoveToward(_dmgFlash, 0f, dt * 2.5f);
            _damageMat.SetShaderParameter("flash_amount", _dmgFlash);
            _damageMat.SetShaderParameter("low_health", _lowHealth);
            bool dshow = _dmgFlash > 0.001f || _lowHealth > 0.001f;
            if (_damage.Visible != dshow) _damage.Visible = dshow;
        }
    }

    // ---- scene-transition fade --------------------------------------------

    private ColorRect _fade;

    private void BuildFade()
    {
        _fade = new ColorRect
        {
            Name = "Fade",
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fade);
    }

    /// <summary>Fade the screen to black over <paramref name="seconds"/> (for lesson
    /// beats / scene transitions). Awaitable so a lesson can sequence on it.</summary>
    public async System.Threading.Tasks.Task FadeToBlack(float seconds = 0.6f)
    {
        if (_fade == null) return;
        _fade.Visible = true;
        var tween = CreateTween();
        tween.TweenProperty(_fade, "color", new Color(0, 0, 0, 1), seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>Fade back in from black over <paramref name="seconds"/>.</summary>
    public async System.Threading.Tasks.Task FadeIn(float seconds = 0.6f)
    {
        if (_fade == null) return;
        _fade.Visible = true;
        var tween = CreateTween();
        tween.TweenProperty(_fade, "color", new Color(0, 0, 0, 0), seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
        _fade.Visible = false;
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

    private Label _mouseHint;

    private void BuildMouseHint()
    {
        _mouseHint = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _mouseHint.AddThemeFontSizeOverride("font_size", 18);
        _mouseHint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        _mouseHint.AddThemeColorOverride("font_outline_color", Colors.Black);
        _mouseHint.AddThemeConstantOverride("outline_size", 5);
        _mouseHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_mouseHint);
        GetViewport().SizeChanged += RelayoutMouseHint;
        RelayoutMouseHint();
    }

    private void RelayoutMouseHint()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _mouseHint.Size = new Vector2(vp.X, 24);
        _mouseHint.Position = new Vector2(0, vp.Y - 40);
    }

    public void SetMouseHint(string text)
    {
        if (_mouseHint == null) return;
        if (_mouseHint.Text != text) _mouseHint.Text = text;
        _mouseHint.Visible = !string.IsNullOrEmpty(text);
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
        _objectives.Position = new Vector2(vp.X - 296, 20);
        _objectives.Size = new Vector2(280, 200);
        _center.Position = new Vector2(vp.X * 0.15f, vp.Y * 0.34f);
        _center.Size = new Vector2(vp.X * 0.7f, vp.Y * 0.32f);
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
            l.CustomMinimumSize = new Vector2(272, 0);
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
        AudioManager.Play("chime"); // a warm reward at the moment progress is earned
    }

    private int _centerToken;

    public void ShowCenter(string text, float seconds = 0f)
    {
        _center.Text = text;
        _center.Visible = !string.IsNullOrEmpty(text);
        int token = ++_centerToken; // a newer ShowCenter invalidates older auto-hide timers
        if (seconds > 0)
            GetTree().CreateTimer(seconds).Timeout += () => { if (token == _centerToken) _center.Visible = false; };
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
    }

    public override void _ExitTree()
    {
        var vp = GetViewport();
        if (vp == null) return;
        vp.SizeChanged -= RelayoutWeapon;
        vp.SizeChanged -= RelayoutInteract;
        vp.SizeChanged -= RelayoutObjectives;
        vp.SizeChanged -= RelayoutBanner;
        vp.SizeChanged -= RelayoutMouseHint;
        vp.SizeChanged -= RelayoutClock;
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
        int token = ++_bannerToken; // a newer banner cancels an older auto-hide
        GetTree().CreateTimer(seconds).Timeout += () => { if (token == _bannerToken) _banner.Visible = false; };
    }
}
