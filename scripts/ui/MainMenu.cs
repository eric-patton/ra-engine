using System;
using Godot;
using RAEngine.Lessons;

namespace RAEngine.UI;

/// <summary>Title screen: launch a lesson, open the build sandbox, change
/// settings, or quit.</summary>
public partial class MainMenu : CanvasLayer
{
    public Action<string> OnPlayLesson;
    public Action OnSandbox;
    public Action OnQuit;

    private SettingsPanel _settings;

    public override void _Ready()
    {
        Layer = 10;
        var bg = UiKit.Dim(new Color(0.06f, 0.08f, 0.13f));
        AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        box.AddChild(UiKit.Title("RA ENGINE", 72, UiKit.Gold));
        box.AddChild(UiKit.Title("Block Worlds for Stories & Lessons", 22, new Color(0.85f, 0.88f, 0.95f)));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        foreach (var lesson in LessonCatalog.List)
        {
            var b = UiKit.Button($"▶   {lesson.Title}");
            string id = lesson.Id;
            b.Pressed += () => OnPlayLesson?.Invoke(id);
            box.AddChild(b);
        }

        var sandbox = UiKit.Button("🔨   Build Sandbox");
        sandbox.Pressed += () => OnSandbox?.Invoke();
        box.AddChild(sandbox);

        var settings = UiKit.Button("⚙   Settings");
        settings.Pressed += OpenSettings;
        box.AddChild(settings);

        var quit = UiKit.Button("✖   Quit");
        quit.Pressed += () => OnQuit?.Invoke();
        box.AddChild(quit);

        Input.MouseMode = Input.MouseModeEnum.Visible;
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
