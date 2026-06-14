using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>App-wide sound. A single persistent instance (created at the game
/// root) owns a small pool of one-shot SFX players plus looping music and
/// ambience players, routed through dedicated "Sfx" and "Music" buses under
/// "Master". Gameplay code triggers sounds through the static helpers, which are
/// safe no-ops if no instance exists (e.g. headless logic that never set audio
/// up). All waveforms come from <see cref="SoundBank"/> — no asset files.</summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private const int PoolSize = 8;
    private readonly Dictionary<string, AudioStream> _sfx = new();
    private AudioStreamPlayer[] _pool;
    private int _next;
    private AudioStreamPlayer _music, _ambience;
    private AudioStream _musicStream, _ambienceStream;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // UI clicks etc. still sound while paused

        EnsureBus("Sfx");
        EnsureBus("Music");
        Settings.Apply(); // push saved bus volumes now that the buses exist

        foreach (var kv in SoundBank.BuildSfx())
            _sfx[kv.Key] = SoundBank.ToStream(kv.Value);

        _pool = new AudioStreamPlayer[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer { Name = $"Sfx{i}", Bus = "Sfx" };
            AddChild(p);
            _pool[i] = p;
        }
        _music = new AudioStreamPlayer { Name = "MusicPlayer", Bus = "Music" };
        _ambience = new AudioStreamPlayer { Name = "AmbiencePlayer", Bus = "Music" };
        AddChild(_music);
        AddChild(_ambience);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
    }

    // ---- static facade (no-ops when there is no instance) ----------------

    public static void Play(string id, float pitch = 1f, float volumeDb = 0f) =>
        Instance?.PlaySfx(id, pitch, volumeDb);

    public static void StartMusic() => Instance?.PlayMusic();
    public static void StartAmbience() => Instance?.PlayAmbience();
    public static void StopAmbience() => Instance?._ambience.Stop();
    public static void StopBeds()
    {
        if (Instance == null) return;
        Instance._music.Stop();
        Instance._ambience.Stop();
    }

    private void PlaySfx(string id, float pitch, float volumeDb)
    {
        if (!_sfx.TryGetValue(id, out AudioStream s)) return;
        AudioStreamPlayer p = _pool[_next];
        _next = (_next + 1) % PoolSize;
        p.Stream = s;
        p.PitchScale = Mathf.Clamp(pitch, 0.2f, 4f);
        p.VolumeDb = volumeDb;
        p.Play();
    }

    private void PlayMusic()
    {
        _musicStream ??= SoundBank.ToStream(SoundBank.BuildMusic());
        if (_music.Stream != _musicStream) _music.Stream = _musicStream;
        if (!_music.Playing) _music.Play();
    }

    private void PlayAmbience()
    {
        _ambienceStream ??= SoundBank.ToStream(SoundBank.BuildAmbience());
        if (_ambience.Stream != _ambienceStream) _ambience.Stream = _ambienceStream;
        if (!_ambience.Playing) _ambience.Play();
    }
}
