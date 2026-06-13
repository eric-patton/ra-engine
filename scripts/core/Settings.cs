using Godot;

namespace RAEngine.Core;

/// <summary>Player-facing settings, persisted to user://settings.cfg.</summary>
public static class Settings
{
    public static float MouseSensitivity = 1.0f; // multiplier on the base look speed
    public static float MasterVolume = 0.8f;      // 0..1
    public static bool Loaded;

    private const string Path = "user://settings.cfg";

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            MouseSensitivity = (float)(double)cfg.GetValue("input", "mouse_sensitivity", 1.0);
            MasterVolume = (float)(double)cfg.GetValue("audio", "master_volume", 0.8);
        }
        Loaded = true;
        Apply();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("input", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("audio", "master_volume", MasterVolume);
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
