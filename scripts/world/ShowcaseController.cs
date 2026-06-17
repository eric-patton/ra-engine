using Godot;
using RAEngine.Core;
using RAEngine.UI;

namespace RAEngine.World;

/// <summary>Hotkey driver for the FX showcase world (built by <see cref="WorldGen.FxShowcase"/>):
/// cycle weather, time-of-day, the bloom/glow preset and ambient-life density so a viewer can see every
/// effect without waiting for the day cycle or for weather to roll in. Added only by the
/// <c>--showcase</c> entry, so it never affects normal play.</summary>
public sealed partial class ShowcaseController : Node
{
    public EnvironmentController Env;
    public GameHud Hud;
    public FireController Fire;
    public AmbientLifeDirector Ambient;

    private int _weather, _time, _glow, _palette;
    private int _ambient = 2; // Off / Sparse / Normal / Lush; starts at Normal

    private static readonly (string label, float scale)[] Ambients =
    {
        ("Off", 0f), ("Sparse", 0.5f), ("Normal", 1f), ("Lush", 1.6f),
    };

    private static readonly Weather[] Weathers = { Weather.Clear, Weather.Rain, Weather.Snow };

    private static readonly (string label, float tod)[] Times =
    {
        ("Morning", EnvironmentController.Morning),
        ("Noon", EnvironmentController.Noon),
        ("Dusk", EnvironmentController.Dusk),
        ("Night", EnvironmentController.Night),
    };

    private static readonly (string label, EnvironmentController.GlowPreset preset)[] Glows =
    {
        ("Normal", EnvironmentController.GlowPreset.Normal),
        ("Divine", EnvironmentController.GlowPreset.Divine),
        ("Plague", EnvironmentController.GlowPreset.Plague),
        ("Cave", EnvironmentController.GlowPreset.Cave),
    };

    private static readonly (string label, FirePalette pal)[] Palettes =
    {
        ("Normal", FirePalette.Normal),
        ("Holy", FirePalette.Holy),
        ("Forge", FirePalette.Forge),
    };

    public override void _UnhandledInput(InputEvent e)
    {
        if (Env == null || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.F5:
                _weather = (_weather + 1) % Weathers.Length;
                Env.SetWeather(Weathers[_weather]);
                Hud?.ShowBanner($"Weather: {Weathers[_weather]}", 1.6f);
                break;
            case Key.F6:
                _time = (_time + 1) % (Times.Length + 1); // last step = resume auto cycle
                if (_time == Times.Length) { Env.SetCycle(true, 240f); Hud?.ShowBanner("Time: auto cycle", 1.6f); }
                else { Env.SetFixedTime(Times[_time].tod); Hud?.ShowBanner($"Time: {Times[_time].label}", 1.6f); }
                break;
            case Key.F7:
                _glow = (_glow + 1) % Glows.Length;
                Env.SetGlowPreset(Glows[_glow].preset);
                Hud?.ShowBanner($"Glow: {Glows[_glow].label}", 1.6f);
                break;
            case Key.L:
                _ambient = (_ambient + 1) % Ambients.Length;
                Ambient?.SetDensityScale(Ambients[_ambient].scale);
                Hud?.ShowBanner($"Ambient life: {Ambients[_ambient].label}", 1.6f);
                break;
            // H, not an F-key: F8 is the Godot editor's Stop shortcut, so it would kill
            // the game during editor playtesting. H ("hallow") is free in-game and out.
            case Key.H:
                if (Fire != null)
                {
                    _palette = (_palette + 1) % Palettes.Length;
                    Fire.SetAllPalette(Palettes[_palette].pal);
                    Hud?.ShowBanner($"Flames: {Palettes[_palette].label}", 1.6f);
                }
                break;
        }
    }
}
