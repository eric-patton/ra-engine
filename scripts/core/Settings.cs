using Godot;

namespace RAEngine.Core;

/// <summary>Player-facing settings, persisted to user://settings.cfg.</summary>
public static class Settings
{
    /// <summary>How the game treats the mouse cursor during play.
    /// <list type="bullet">
    /// <item><c>Off</c> — never grab the cursor; play entirely with the keyboard
    /// (arrows/numpad to look, +/- to place/break). The M key can still grab it.</item>
    /// <item><c>ClickToCapture</c> — the cursor stays free; click in the window to
    /// look with the mouse, press M (or Esc) to release it. The default, so the
    /// game never silently swallows the pointer.</item>
    /// <item><c>Always</c> — grab the cursor on start and re-grab on focus, the
    /// classic FPS behaviour.</item>
    /// </list></summary>
    public enum MouseCapture { Off, ClickToCapture, Always }

    public static float MouseSensitivity = 1.0f; // multiplier on the base look speed
    public static float MasterVolume = 0.8f;      // 0..1
    public static MouseCapture CaptureMode = MouseCapture.ClickToCapture;
    public static float KeyboardLookSpeed = 110f; // degrees/sec for arrow/numpad look
    public static bool Loaded;

    private const string Path = "user://settings.cfg";

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            MouseSensitivity = (float)(double)cfg.GetValue("input", "mouse_sensitivity", 1.0);
            MasterVolume = (float)(double)cfg.GetValue("audio", "master_volume", 0.8);
            CaptureMode = (MouseCapture)cfg.GetValue("input", "mouse_capture_mode", (int)MouseCapture.ClickToCapture).AsInt32();
            KeyboardLookSpeed = (float)(double)cfg.GetValue("input", "keyboard_look_speed", 110.0);
        }
        Loaded = true;
        Apply();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("input", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("audio", "master_volume", MasterVolume);
        cfg.SetValue("input", "mouse_capture_mode", (int)CaptureMode);
        cfg.SetValue("input", "keyboard_look_speed", KeyboardLookSpeed);
        cfg.Save(Path);
        Apply();
    }

    public static void Apply()
    {
        int bus = AudioServer.GetBusIndex("Master");
        if (bus >= 0)
            AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Clamp(MasterVolume, 0.0001f, 1f)));
    }
}
