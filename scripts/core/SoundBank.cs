using System;
using System.Collections.Generic;
using Godot;

namespace RAEngine.Core;

/// <summary>Procedurally synthesizes every sound the game uses — short SFX plus a
/// looping music bed and outdoor ambience — as raw 16-bit PCM, so the project
/// ships with zero binary audio assets and the result is byte-for-byte
/// deterministic. <see cref="AudioManager"/> turns these clips into playable
/// streams; <c>--gen-audio</c> can also dump them to .wav for inspection.</summary>
public static class SoundBank
{
    public const int Rate = 22050;

    /// <summary>A finished mono waveform: float samples in [-1,1] plus whether it
    /// should loop (music/ambience) or play once (SFX).</summary>
    public sealed class Clip
    {
        public float[] Samples;
        public bool Loop;
    }

    /// <summary>Deterministic xorshift noise source (no Date/Random, so every build
    /// produces identical audio).</summary>
    private sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) => _s = seed == 0 ? 0x9E3779B9 : seed;
        public float White()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    // -----------------------------------------------------------------------
    //  Public build entry point
    // -----------------------------------------------------------------------

    /// <summary>The short one-shot SFX. Built eagerly (cheap); music/ambience are
    /// built lazily by their own methods so headless tests don't pay for them.</summary>
    public static Dictionary<string, Clip> BuildSfx()
    {
        var d = new Dictionary<string, Clip>
        {
            ["place"]  = Place(),
            ["break"]  = Break(),   // generic fallback
            ["step"]   = Step(),    // generic fallback
            ["jump"]   = Jump(),
            ["land"]   = Land(),
            ["splash"] = Splash(),
            ["hurt"]   = Hurt(),
            ["swing"]  = Swing(),
            ["shoot"]  = Shoot(),
            ["hit"]    = Hit(),
            ["defeat"] = Defeat(),
            ["click"]  = Blip(660, 0.045, 0.35),
            ["select"] = Blip(880, 0.032, 0.30),
            ["talk"]   = Blip(520, 0.040, 0.22),
            ["chime"]  = Chime(),
            ["fanfare"]= Fanfare(),
        };
        // Per-material footstep / break / mining variants, keyed e.g. "step_Grass",
        // "break_Wood", "mine_Stone" — callers build the id from the block's
        // MaterialSound. Unknown keys are a safe no-op in AudioManager.
        foreach (MaterialSound m in System.Enum.GetValues<MaterialSound>())
        {
            d[$"step_{m}"]  = StepFor(m);
            d[$"break_{m}"] = BreakFor(m);
            d[$"mine_{m}"]  = MineFor(m);
        }
        return d;
    }

    // -----------------------------------------------------------------------
    //  Per-material footstep / break / mining sounds
    // -----------------------------------------------------------------------

    private static Clip StepFor(MaterialSound m)
    {
        var b = Buf(0.08);
        var rng = new Rng(37 + (uint)m * 7u);
        switch (m)
        {
            case MaterialSound.Grass: Noise(b, 0, 0.06, 0.26, rng, 2400, 900, 0.020); break;
            case MaterialSound.Dirt:  Noise(b, 0, 0.06, 0.30, rng, 1300, 380, 0.025); break;
            case MaterialSound.Sand:  Noise(b, 0, 0.07, 0.28, rng, 3600, 1500, 0.030); break;
            case MaterialSound.Wood:  Perc(b, 0, 0.05, 220, 160, 0.22, Wave.Tri, 0.030);
                                      Noise(b, 0, 0.03, 0.12, rng, 2000, 800, 0.015); break;
            case MaterialSound.Snow:  Noise(b, 0, 0.075, 0.30, rng, 2700, 1000, 0.035); break;
            case MaterialSound.Metal: Perc(b, 0, 0.06, 1200, 950, 0.16, Wave.Sine, 0.040);
                                      Noise(b, 0, 0.02, 0.08, rng, 5000, 3000, 0.010); break;
            case MaterialSound.Cloth: Noise(b, 0, 0.06, 0.20, rng, 850, 300, 0.030); break;
            default:                  Noise(b, 0, 0.06, 0.30, rng, 1500, 380, 0.025); break; // Stone
        }
        return Done(b);
    }

    private static Clip BreakFor(MaterialSound m)
    {
        var b = Buf(0.18);
        var rng = new Rng(23 + (uint)m * 13u);
        switch (m)
        {
            case MaterialSound.Grass: Noise(b, 0, 0.14, 0.45, rng, 3600, 1200, 0.06); break;
            case MaterialSound.Dirt:  Noise(b, 0, 0.13, 0.50, rng, 3000, 500, 0.06);
                                      Perc(b, 0, 0.06, 90, 70, 0.25, Wave.Tri, 0.04); break;
            case MaterialSound.Sand:  Noise(b, 0, 0.16, 0.50, rng, 5000, 1500, 0.07); break;
            case MaterialSound.Wood:  Perc(b, 0, 0.06, 300, 120, 0.40, Wave.Tri, 0.03);
                                      Noise(b, 0, 0.12, 0.40, rng, 2500, 600, 0.05); break;
            case MaterialSound.Snow:  Noise(b, 0, 0.14, 0.42, rng, 2800, 800, 0.06); break;
            case MaterialSound.Metal: Perc(b, 0, 0.10, 900, 520, 0.40, Wave.Square, 0.07);
                                      Perc(b, 0, 0.13, 1450, 1350, 0.18, Wave.Sine, 0.09); break;
            case MaterialSound.Cloth: Noise(b, 0, 0.13, 0.38, rng, 1200, 400, 0.06); break;
            default:                  Noise(b, 0, 0.15, 0.60, rng, 4500, 700, 0.06);
                                      Perc(b, 0, 0.07, 95, 70, 0.32, Wave.Tri, 0.04); break; // Stone
        }
        return Done(b);
    }

    private static Clip MineFor(MaterialSound m)
    {
        var b = Buf(0.08);
        var rng = new Rng(53 + (uint)m * 17u);
        switch (m)
        {
            case MaterialSound.Grass: Noise(b, 0, 0.045, 0.24, rng, 2200, 900, 0.020); break;
            case MaterialSound.Dirt:  Noise(b, 0, 0.05, 0.28, rng, 1500, 500, 0.025); break;
            case MaterialSound.Sand:  Noise(b, 0, 0.05, 0.26, rng, 3400, 1400, 0.025); break;
            case MaterialSound.Wood:  Perc(b, 0, 0.05, 260, 180, 0.28, Wave.Tri, 0.030); break;
            case MaterialSound.Snow:  Noise(b, 0, 0.05, 0.26, rng, 2600, 900, 0.030); break;
            case MaterialSound.Metal: Perc(b, 0, 0.05, 1300, 1000, 0.20, Wave.Sine, 0.035); break;
            case MaterialSound.Cloth: Noise(b, 0, 0.05, 0.18, rng, 900, 320, 0.030); break;
            default:                  Perc(b, 0, 0.05, 420, 260, 0.28, Wave.Square, 0.030);
                                      Noise(b, 0, 0.03, 0.12, rng, 3000, 800, 0.015); break; // Stone
        }
        return Done(b);
    }

    /// <summary>Everything, including the looping beds — used by <c>--gen-audio</c>.</summary>
    public static Dictionary<string, Clip> BuildAll()
    {
        var d = BuildSfx();
        d["music"] = BuildMusic();
        d["ambience"] = BuildAmbience();
        return d;
    }

    // -----------------------------------------------------------------------
    //  Individual sounds
    // -----------------------------------------------------------------------

    private static Clip Place()
    {
        var b = Buf(0.10);
        Perc(b, 0.0, 0.10, 175, 150, 0.55, Wave.Tri, 0.030);
        Noise(b, 0.0, 0.02, 0.18, new Rng(11), 5000, 2500, 0.012); // tap transient
        return Done(b);
    }

    private static Clip Break()
    {
        var b = Buf(0.16);
        Noise(b, 0.0, 0.15, 0.6, new Rng(23), 4500, 700, 0.06);    // crunch
        Perc(b, 0.0, 0.07, 95, 70, 0.32, Wave.Tri, 0.04);          // low thump
        return Done(b);
    }

    private static Clip Step()
    {
        var b = Buf(0.07);
        Noise(b, 0.0, 0.06, 0.32, new Rng(37), 1500, 380, 0.025);
        return Done(b);
    }

    private static Clip Jump()
    {
        var b = Buf(0.12);
        Perc(b, 0.0, 0.11, 240, 380, 0.42, Wave.Sine, 0.06);
        return Done(b);
    }

    private static Clip Land()
    {
        var b = Buf(0.14);
        Noise(b, 0.0, 0.12, 0.42, new Rng(41), 900, 200, 0.05);
        Perc(b, 0.0, 0.08, 130, 90, 0.34, Wave.Tri, 0.04);
        return Done(b);
    }

    private static Clip Splash()
    {
        var b = Buf(0.30);
        Noise(b, 0.0, 0.28, 0.5, new Rng(53), 5500, 900, 0.10);
        Noise(b, 0.0, 0.10, 0.22, new Rng(59), 8000, 4000, 0.05);  // sparkle
        return Done(b);
    }

    private static Clip Hurt()
    {
        var b = Buf(0.17);
        Perc(b, 0.0, 0.16, 320, 150, 0.4, Wave.Saw, 0.07);
        return Done(b);
    }

    private static Clip Swing()
    {
        var b = Buf(0.16);
        // a whoosh: noise whose cutoff rises then is naturally damped by decay
        Noise(b, 0.0, 0.15, 0.4, new Rng(67), 700, 3500, 0.05);
        return Done(b);
    }

    private static Clip Shoot()
    {
        var b = Buf(0.14);
        Perc(b, 0.0, 0.05, 620, 300, 0.4, Wave.Tri, 0.03);         // string ping
        Noise(b, 0.0, 0.12, 0.3, new Rng(71), 5000, 1500, 0.05);   // whip
        return Done(b);
    }

    private static Clip Hit()
    {
        var b = Buf(0.11);
        Noise(b, 0.0, 0.09, 0.5, new Rng(83), 3200, 700, 0.04);
        Perc(b, 0.0, 0.06, 185, 120, 0.3, Wave.Square, 0.03);
        return Done(b);
    }

    private static Clip Defeat()
    {
        var b = Buf(0.36);
        Perc(b, 0.0, 0.34, 420, 120, 0.4, Wave.Tri, 0.14);
        Noise(b, 0.04, 0.30, 0.32, new Rng(97), 4200, 500, 0.13);  // poof
        return Done(b);
    }

    private static Clip Blip(double freq, double dur, double amp)
    {
        var b = Buf(dur);
        Perc(b, 0.0, dur, freq, freq, amp, Wave.Sine, dur * 0.6);
        return Done(b);
    }

    /// <summary>A short bright major arpeggio (C–E–G) — plays when an objective ticks.</summary>
    private static Clip Chime()
    {
        var b = Buf(0.55);
        int[] notes = { 72, 76, 79 }; // C5 E5 G5
        for (int i = 0; i < notes.Length; i++)
            Perc(b, i * 0.085, 0.34, Midi(notes[i]), Midi(notes[i]), 0.30, Wave.Sine, 0.16);
        return Done(b, peak: 0.8);
    }

    /// <summary>A rising five-note flourish with a sparkle tail — plays on lesson
    /// completion / a big celebratory beat.</summary>
    private static Clip Fanfare()
    {
        var b = Buf(1.2);
        int[] notes = { 67, 72, 76, 79, 84 };          // G4 C5 E5 G5 C6
        double[] at = { 0.0, 0.11, 0.22, 0.33, 0.50 };
        for (int i = 0; i < notes.Length; i++)
        {
            bool last = i == notes.Length - 1;
            Perc(b, at[i], last ? 0.55 : 0.22, Midi(notes[i]), Midi(notes[i]),
                 last ? 0.34 : 0.26, Wave.Tri, last ? 0.30 : 0.12);
        }
        Perc(b, 0.50, 0.55, Midi(91), Midi(91), 0.12, Wave.Sine, 0.24); // sparkle an octave up
        return Done(b, peak: 0.85);
    }

    // -----------------------------------------------------------------------
    //  Music: a calm I–V–vi–IV harp arpeggio over a soft low pad (D major).
    // -----------------------------------------------------------------------

    public static Clip BuildMusic()
    {
        const double bpm = 72;
        double beat = 60.0 / bpm;          // seconds per quarter note
        double loop = beat * 16;           // four bars of 4/4
        var b = Buf(loop);

        // Four chords, each a four-note ascending arpeggio (quarter notes), with a
        // sustained root pad an octave down. MIDI note numbers.
        int[][] chords =
        {
            new[] { 62, 66, 69, 74 }, // D  major  (I)
            new[] { 57, 61, 64, 69 }, // A  major  (V)
            new[] { 59, 62, 66, 71 }, // B  minor  (vi)
            new[] { 55, 59, 62, 67 }, // G  major  (IV)
        };

        for (int c = 0; c < 4; c++)
        {
            double t0 = c * beat * 4;
            // low pad (root, one octave below the arpeggio's root)
            Pad(b, t0, beat * 4, Midi(chords[c][0] - 12), 0.12, beat * 0.15, beat * 0.6);
            // arpeggio: one note per beat, each ringing into the next
            for (int n = 0; n < 4; n++)
                Perc(b, t0 + n * beat, beat * 1.6, Midi(chords[c][n]), Midi(chords[c][n]),
                     0.26, Wave.Tri, beat * 0.7);
        }

        var clip = Done(b, peak: 0.7);
        clip.Loop = true;
        return clip;
    }

    // -----------------------------------------------------------------------
    //  Ambience: gentle wind (gusting filtered brown noise) with sparse birdsong.
    // -----------------------------------------------------------------------

    public static Clip BuildAmbience()
    {
        const double loop = 16.0;
        var b = Buf(loop);
        int n = b.Length;
        var rng = new Rng(0xA11CE);

        // wind: brown noise, lowpassed, with a slow gust LFO whose cycles divide
        // the loop length evenly so it tiles seamlessly.
        double brown = 0, lp = 0;
        double gustCycles = 3; // whole cycles across the loop
        for (int i = 0; i < n; i++)
        {
            double w = rng.White();
            brown = brown * 0.985 + w * 0.03;
            double fc = 480;
            double a = 1 - Math.Exp(-2 * Math.PI * fc / Rate);
            lp += a * (brown - lp);
            double t = i / (double)Rate;
            double gust = 0.6 + 0.4 * Math.Sin(2 * Math.PI * gustCycles * t / loop);
            b[i] += (float)(lp * 4.0 * gust * 0.5);
        }

        // a few soft bird chirps scattered through the loop (deterministic times)
        double[] chirpAt = { 2.3, 5.1, 9.7, 13.2 };
        foreach (double ct in chirpAt)
        {
            Perc(b, ct, 0.07, 2600, 3200, 0.10, Wave.Sine, 0.04);
            Perc(b, ct + 0.09, 0.06, 3100, 2700, 0.08, Wave.Sine, 0.035);
        }

        var clip = Done(b, peak: 0.6);
        clip.Loop = true;
        return clip;
    }

    // -----------------------------------------------------------------------
    //  DSP primitives
    // -----------------------------------------------------------------------

    private enum Wave { Sine, Tri, Saw, Square }

    private static float[] Buf(double seconds) => new float[Math.Max(1, (int)(seconds * Rate))];

    private static double Midi(int note) => 440.0 * Math.Pow(2.0, (note - 69) / 12.0);

    /// <summary>Percussive tonal voice: instant-ish attack, exponential decay.</summary>
    private static void Perc(float[] buf, double t0, double dur, double f0, double f1,
                             double amp, Wave wave, double tau)
    {
        int start = (int)(t0 * Rate), len = (int)(dur * Rate);
        double phase = 0;
        for (int i = 0; i < len; i++)
        {
            int idx = start + i;
            if (idx < 0 || idx >= buf.Length) continue;
            double t = i / (double)Rate;
            double f = f0 + (f1 - f0) * (i / (double)len);
            phase += 2 * Math.PI * f / Rate;
            double atk = Math.Min(1.0, t / 0.003);
            double env = atk * Math.Exp(-t / tau);
            buf[idx] += (float)(Osc(wave, phase) * amp * env);
        }
    }

    /// <summary>Sustained pad voice with attack/release (no decay in the middle).</summary>
    private static void Pad(float[] buf, double t0, double dur, double freq,
                            double amp, double atk, double rel)
    {
        int start = (int)(t0 * Rate), len = (int)(dur * Rate);
        double phase = 0;
        for (int i = 0; i < len; i++)
        {
            int idx = start + i;
            if (idx < 0 || idx >= buf.Length) continue;
            double t = i / (double)Rate, tEnd = (len - i) / (double)Rate;
            phase += 2 * Math.PI * freq / Rate;
            double env = Math.Min(1.0, t / atk) * Math.Min(1.0, tEnd / rel);
            // soft pad: sine + a quiet fifth-ish overtone for warmth
            double s = Math.Sin(phase) + 0.25 * Math.Sin(phase * 2.0);
            buf[idx] += (float)(s * amp * env);
        }
    }

    /// <summary>Lowpassed white-noise burst with a swept cutoff and exp decay.</summary>
    private static void Noise(float[] buf, double t0, double dur, double amp, Rng rng,
                              double fcStart, double fcEnd, double tau)
    {
        int start = (int)(t0 * Rate), len = (int)(dur * Rate);
        double lp = 0;
        for (int i = 0; i < len; i++)
        {
            int idx = start + i;
            if (idx < 0 || idx >= buf.Length) continue;
            double t = i / (double)Rate;
            double fc = fcStart + (fcEnd - fcStart) * (i / (double)len);
            double a = 1 - Math.Exp(-2 * Math.PI * fc / Rate);
            lp += a * (rng.White() - lp);
            double atk = Math.Min(1.0, t / 0.002);
            double env = atk * Math.Exp(-t / tau);
            buf[idx] += (float)(lp * amp * env);
        }
    }

    private static double Osc(Wave w, double phase)
    {
        double p = phase % (2 * Math.PI);
        if (p < 0) p += 2 * Math.PI;
        return w switch
        {
            Wave.Sine => Math.Sin(p),
            Wave.Tri => 2.0 / Math.PI * Math.Asin(Math.Sin(p)),
            Wave.Saw => p / Math.PI - 1.0,
            Wave.Square => p < Math.PI ? 1.0 : -1.0,
            _ => Math.Sin(p),
        };
    }

    /// <summary>Normalize to a target peak (if it would clip) then soft-clip.</summary>
    private static Clip Done(float[] buf, double peak = 0.9)
    {
        float max = 1e-6f;
        foreach (float v in buf) max = Math.Max(max, Math.Abs(v));
        float g = (float)(max > peak ? peak / max : 1.0);
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (float)Math.Tanh(buf[i] * g * 1.05);
        return new Clip { Samples = buf, Loop = false };
    }

    // -----------------------------------------------------------------------
    //  Conversion helpers
    // -----------------------------------------------------------------------

    public static AudioStreamWav ToStream(Clip c)
    {
        var bytes = ToPcm16(c.Samples);
        var w = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
        };
        if (c.Loop)
        {
            w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            w.LoopBegin = 0;
            w.LoopEnd = c.Samples.Length;
        }
        return w;
    }

    public static byte[] ToPcm16(float[] s)
    {
        var bytes = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            int v = (int)Math.Round(Mathf.Clamp(s[i], -1f, 1f) * 32767f);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return bytes;
    }
}
