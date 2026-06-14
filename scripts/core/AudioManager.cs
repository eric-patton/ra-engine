using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Background music mood — lessons pick one; the sandbox and menu use Calm.</summary>
public enum MusicMood { Calm, Hope, Solemn }

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

    // Crossfading beds: several looping players whose volumes lerp toward target
    // weights, so music moods and time/weather ambience blend instead of cutting.
    private static readonly MusicMood[] Moods = { MusicMood.Calm, MusicMood.Hope, MusicMood.Solemn };
    private AudioStreamPlayer[] _music; // one per mood (index matches Moods)
    private AudioStreamPlayer[] _amb;   // [0]=day [1]=night [2]=rain
    private float[] _musW, _musT;       // music weights: current, target
    private float[] _ambW, _ambT;       // ambience weights: current, target
    private bool _musicOn, _ambOn;
    private const float FadeRate = 0.7f; // weight units per second
    private const float MinDb = -60f;    // treated as silent

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
        // Layered, crossfading music + ambience players (streams built lazily on the
        // first Start*, so headless logic tests never pay the synth cost).
        _music = new AudioStreamPlayer[Moods.Length];
        _musW = new float[Moods.Length];
        _musT = new float[Moods.Length];
        _musT[0] = 1f; // default to Calm
        for (int i = 0; i < _music.Length; i++)
        {
            _music[i] = new AudioStreamPlayer { Name = $"Music_{Moods[i]}", Bus = "Music", VolumeDb = MinDb };
            AddChild(_music[i]);
        }
        _amb = new AudioStreamPlayer[3];
        _ambW = new float[3];
        _ambT = new float[3];
        for (int i = 0; i < _amb.Length; i++)
        {
            _amb[i] = new AudioStreamPlayer { Name = $"Amb_{i}", Bus = "Music", VolumeDb = MinDb };
            AddChild(_amb[i]);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_musicOn) Crossfade(_music, _musW, _musT, dt);
        if (_ambOn) Crossfade(_amb, _ambW, _ambT, dt);
    }

    /// <summary>Lerp each player's amplitude toward its target weight (in dB).</summary>
    private static void Crossfade(AudioStreamPlayer[] players, float[] w, float[] t, float dt)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (!Mathf.IsEqualApprox(w[i], t[i]))
                w[i] = Mathf.MoveToward(w[i], t[i], FadeRate * dt);
            players[i].VolumeDb = w[i] <= 0.001f ? MinDb : Mathf.LinearToDb(w[i]);
        }
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

    public static void StartMusic() => Instance?.StartMusicImpl();
    public static void StartAmbience() => Instance?.StartAmbienceImpl();
    /// <summary>Crossfade to a music mood (lessons set this; sandbox/menu use Calm).</summary>
    public static void SetMusicMood(MusicMood mood) => Instance?.SetMusicMoodImpl(mood);
    /// <summary>Set the ambience blend (weights 0..1), driven by time of day + weather
    /// from <see cref="EnvironmentController"/>. Smoothly crossfaded each frame.</summary>
    public static void SetAmbienceMix(float day, float night, float rain) =>
        Instance?.SetAmbienceMixImpl(day, night, rain);
    public static void StopAmbience() => Instance?.StopAmbienceImpl();
    public static void StopBeds() => Instance?.StopBedsImpl();

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

    private void StartMusicImpl()
    {
        if (_musicOn) return;
        _musicOn = true;
        for (int i = 0; i < _music.Length; i++)
        {
            _music[i].Stream ??= SoundBank.ToStream(SoundBank.BuildMusic(Moods[i]));
            _musW[i] = 0f;
            _music[i].VolumeDb = MinDb;
            _music[i].Play();
        }
    }

    private void StartAmbienceImpl()
    {
        if (_ambOn) return;
        _ambOn = true;
        _amb[0].Stream ??= SoundBank.ToStream(SoundBank.BuildAmbienceDay());
        _amb[1].Stream ??= SoundBank.ToStream(SoundBank.BuildAmbienceNight());
        _amb[2].Stream ??= SoundBank.ToStream(SoundBank.BuildAmbienceRain());
        for (int i = 0; i < _amb.Length; i++)
        {
            _ambW[i] = 0f;
            _amb[i].VolumeDb = MinDb;
            _amb[i].Play();
        }
    }

    private void SetMusicMoodImpl(MusicMood mood)
    {
        for (int i = 0; i < _musT.Length; i++) _musT[i] = Moods[i] == mood ? 1f : 0f;
    }

    private void SetAmbienceMixImpl(float day, float night, float rain)
    {
        _ambT[0] = Mathf.Clamp(day, 0f, 1f);
        _ambT[1] = Mathf.Clamp(night, 0f, 1f);
        _ambT[2] = Mathf.Clamp(rain, 0f, 1f);
    }

    private void StopAmbienceImpl()
    {
        _ambOn = false;
        if (_amb != null) foreach (var p in _amb) p.Stop();
    }

    private void StopBedsImpl()
    {
        _musicOn = _ambOn = false;
        if (_music != null) foreach (var p in _music) p.Stop();
        if (_amb != null) foreach (var p in _amb) p.Stop();
    }
}
