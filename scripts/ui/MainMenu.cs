using System;
using Godot;
using RAEngine.Core;
using RAEngine.Lessons;

namespace RAEngine.UI;

/// <summary>Title screen: launch a lesson, open the build sandbox, change
/// settings, or quit.</summary>
public partial class MainMenu : CanvasLayer
{
    public Action<string> OnPlayLesson;
    public Action OnSandbox;
    public Action OnQuit;
    /// <summary>Launch a showcase world by id ("fx", "blocks"), from the Showcases submenu.</summary>
    public Action<string> OnShowcase;

    /// <summary>Campaign completion, used to mark finished chapters with a ✓. Set by
    /// the host before the menu enters the tree; null is treated as nothing-complete.</summary>
    public CampaignProgress Progress;

    private SettingsPanel _settings;
    private VBoxContainer _mainBox, _showcaseBox;

    public override void _Ready()
    {
        Layer = 10;
        var bg = UiKit.Dim(new Color(0.06f, 0.08f, 0.13f));
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        // Two pages share the centre; only one is visible at a time (the container skips
        // hidden children when it lays out, so the visible page stays centred).
        _mainBox = BuildMainBox();
        _showcaseBox = BuildShowcaseBox();
        center.AddChild(_mainBox);
        center.AddChild(_showcaseBox);
        _showcaseBox.Visible = false;

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private VBoxContainer BuildMainBox()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);

        box.AddChild(UiKit.Title("RA ENGINE", 72, UiKit.Gold));
        box.AddChild(UiKit.Title("Block Worlds for Stories & Lessons", 22, new Color(0.85f, 0.88f, 0.95f)));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        // Campaign chapters, in order, with a ✓ on completed ones. Everything stays
        // playable so a teacher can jump straight to any lesson in class.
        foreach (var chapter in Campaign.Chapters)
        {
            string id = chapter.Lesson.Id;
            bool done = Progress?.IsComplete(id) ?? false;
            var b = UiKit.Button($"{(done ? "✓" : "▶")}   {chapter.Lesson.Title}");
            if (done) b.Modulate = new Color(0.72f, 1f, 0.72f);
            b.Pressed += () => OnPlayLesson?.Invoke(id);
            box.AddChild(b);
        }

        var sandbox = UiKit.Button("🔨   Build Sandbox");
        sandbox.Pressed += () => OnSandbox?.Invoke();
        box.AddChild(sandbox);

        var showcases = UiKit.Button("✨   Showcases");
        showcases.Pressed += () => { _mainBox.Visible = false; _showcaseBox.Visible = true; };
        box.AddChild(showcases);

        var settings = UiKit.Button("⚙   Settings");
        settings.Pressed += OpenSettings;
        box.AddChild(settings);

        var quit = UiKit.Button("✖   Quit");
        quit.Pressed += () => OnQuit?.Invoke();
        box.AddChild(quit);

        return box;
    }

    private VBoxContainer BuildShowcaseBox()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);

        box.AddChild(UiKit.Title("SHOWCASES", 52, UiKit.Gold));
        box.AddChild(UiKit.Title("Walk-through demos of the engine's effects", 20, new Color(0.85f, 0.88f, 0.95f)));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        (string id, string label)[] shows =
        {
            ("fx", "✨   Effects Showcase"),
            ("blocks", "🧱   Block Gallery"),
        };
        foreach (var (id, label) in shows)
        {
            var b = UiKit.Button(label);
            b.Pressed += () => OnShowcase?.Invoke(id);
            box.AddChild(b);
        }

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var back = UiKit.Button("←   Back");
        back.Pressed += () => { _showcaseBox.Visible = false; _mainBox.Visible = true; };
        box.AddChild(back);

        return box;
    }

    private void OpenSettings()
    {
        _settings ??= CreateSettings();
        _settings.Open();
    }

    private SettingsPanel CreateSettings()
    {
        var s = new SettingsPanel();
        AddChild(s);
        s.OnBack = () => { };
        return s;
    }
}
