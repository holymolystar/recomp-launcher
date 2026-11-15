// Little interface sounds, console style: a soft tick when the selection moves, a two-note chime to pick, the same
// going down to go back, a short rising run to open a page, a bright arpeggio when a game starts. No music, and no
// sound files: every sound is drawn here as a few soft bell tones and played from memory. The volume (Settings:
// SoundVolume, 0 to 100) scales them; 0 is off.
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace RecompLauncher {

enum Sfx { Move, Bump, Select, Back, Open, ToggleOn, ToggleOff, Launch, Boot }

static class Sound {
    internal const int Rate = 44100;
    static int volume = 60;
    static readonly Dictionary<string, SoundPlayer> cache = new Dictionary<string, SoundPlayer>();
    static DateTime last = DateTime.MinValue;
    // when the sound playing now ends (the launcher waits for the start sound before it closes)
    public static DateTime BusyUntil = DateTime.MinValue;

    public static int Volume {
        get { return volume; }
        set { volume = Math.Max(0, Math.Min(100, value)); }
    }
    public static readonly int[] Levels = { 0, 25, 50, 75, 100 };

    public static void Play(Sfx s) { Play(s, volume); }
    // one sound at a time; a second one asked for in the same moment (a button and the page it opens) is skipped
    public static void Play(Sfx s, int vol) {
        if (vol <= 0) return;
        var now = DateTime.Now;
        if ((now - last).TotalMilliseconds < 45) return;
        last = now;
        try {
            string key = s + "@" + vol;
            SoundPlayer p;
            if (!cache.TryGetValue(key, out p)) {
                var wav = Wave(Draw(s), Math.Pow(vol / 100.0, 1.3));
                p = new SoundPlayer(new MemoryStream(wav)); p.Load();
                cache[key] = p;
            }
            p.Play();
            BusyUntil = now.AddMilliseconds(Length(s));
        } catch { }
    }
    static int Length(Sfx s) { return (int)(Draw(s).Length * 1000L / Rate); }

    // ---------- the sounds ----------
    static readonly Dictionary<Sfx, float[]> drawn = new Dictionary<Sfx, float[]>();
    static float[] Draw(Sfx s) {
        float[] b;
        if (drawn.TryGetValue(s, out b)) return b;
        switch (s) {
            case Sfx.Move:      b = Buf(60);  Bell(b, 0, 2093, 0.36, 9, 0.35); break;                                  // C7 tick
            case Sfx.Bump:      b = Buf(110); Bell(b, 0, 220, 0.40, 28, 0.15); Bell(b, 0, 330, 0.12, 20, 0.1); break;    // soft low knock
            case Sfx.Select:    b = Buf(240); Bell(b, 0, 1318.5, 0.30, 45, 0.3); Bell(b, 55, 1975.5, 0.30, 70, 0.3); break;   // E6, B6
            case Sfx.Back:      b = Buf(240); Bell(b, 0, 1318.5, 0.28, 45, 0.3); Bell(b, 55, 880, 0.28, 70, 0.3); break;      // E6, A5
            case Sfx.Open:      b = Buf(300); Bell(b, 0, 784, 0.20, 40, 0.25); Bell(b, 40, 1046.5, 0.22, 40, 0.25); Bell(b, 80, 1568, 0.24, 90, 0.25); break;
            case Sfx.ToggleOn:  b = Buf(120); Bell(b, 0, 1760, 0.24, 30, 0.2); break;
            case Sfx.ToggleOff: b = Buf(120); Bell(b, 0, 1174.7, 0.24, 30, 0.2); break;
            case Sfx.Boot:      // start-up: a soft low C, then G5 C6 E6 G6 rising and ringing out together
                b = Buf(1500);
                Bell(b, 0, 261.6, 0.20, 600, 0.15); Bell(b, 0, 392, 0.10, 500, 0.1);
                double[] up = { 783.99, 1046.5, 1318.5, 1568 };
                for (int i = 0; i < up.Length; i++) { Bell(b, 120 + i * 85, up[i], 0.17, 520 - i * 40, 0.25); Bell(b, 120 + i * 85, up[i] * 1.004, 0.06, 420, 0.15); }
                break;
            default: // Launch: C6 E6 G6 C7, the last one rings out
                b = Buf(800);
                double[] notes = { 1046.5, 1318.5, 1568, 2093 };
                for (int i = 0; i < notes.Length; i++) {
                    bool lastNote = i == notes.Length - 1;
                    Bell(b, i * 70, notes[i], 0.26, lastNote ? 220 : 70, 0.3);
                    Bell(b, i * 70, notes[i] * 1.003, 0.10, lastNote ? 220 : 70, 0.2);   // a slight chorus
                }
                break;
        }
        drawn[s] = b;
        return b;
    }
    static float[] Buf(int ms) { return new float[Rate * ms / 1000]; }
    // a soft bell: a sine with a little octave and fifth above it, a 3 ms rise and an exponential fade (tau in ms)
    internal static void Bell(float[] b, int atMs, double f, double amp, double tauMs, double bright) {
        int start = Rate * atMs / 1000;
        double tau = tauMs / 1000.0, rise = 0.003;
        for (int i = start; i < b.Length; i++) {
            double t = (i - start) / (double)Rate;
            double env = Math.Min(1, t / rise) * Math.Exp(-t / tau);
            if (env < 0.0005 && t > rise) break;
            double w = Math.Sin(2 * Math.PI * f * t) + bright * Math.Sin(4 * Math.PI * f * t) * Math.Exp(-t / (tau * 0.5)) + bright * 0.4 * Math.Sin(6 * Math.PI * f * t) * Math.Exp(-t / (tau * 0.3));
            b[i] += (float)(amp * env * w / (1 + 1.4 * bright));
        }
    }
    // 16-bit mono WAV, with a short fade at the very end and a soft limit
    internal static byte[] Wave(float[] b, double gain) {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int n = b.Length, fade = Rate / 200;
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
        for (int i = 0; i < n; i++) {
            double v = Math.Tanh(b[i] * 1.2) * gain;
            if (i > n - fade) v *= (n - i) / (double)fade;
            w.Write((short)Math.Round(v * 30000));
        }
        w.Flush();
        return ms.ToArray();
    }
}
}
