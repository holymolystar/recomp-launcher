// The creator's jingle: an easy-going bossa-lounge loop written for the launcher (16 bars in F major at 108 beats a
// minute, about 36 seconds). A vibraphone-like melody over soft electric-piano chords with jazzy sevenths, a round
// bass that walks root and fifth, a side-stick on the bossa clave and a quiet shaker. Drawn once into a WAV in the
// temp folder and looped with a MediaPlayer, so the interface sounds still play over it. Plays while the character
// creator is open, a little under the Sounds volume; Off keeps it quiet.
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

static class Jingle {
    const double Bpm = 108;
    const int Rate = Sound.Rate;
    static MediaPlayer player;
    static DispatcherTimer fade;
    static string file;

    // melody: (beat, MIDI note, length in beats)
    static readonly double[][] Melody = {
        new double[] { 0.5, 69, .5 }, new double[] { 1, 72, .5 }, new double[] { 1.5, 76, 1 }, new double[] { 3, 74, .5 }, new double[] { 3.5, 72, .5 },
        new double[] { 4, 69, 1.5 }, new double[] { 5.5, 65, .5 }, new double[] { 6, 67, .5 }, new double[] { 6.5, 69, .5 }, new double[] { 7, 72, 1 },
        new double[] { 8.5, 70, .5 }, new double[] { 9, 74, .5 }, new double[] { 9.5, 77, 1 }, new double[] { 11, 76, .5 }, new double[] { 11.5, 74, .5 },
        new double[] { 12, 72, 1 }, new double[] { 13, 70, .5 }, new double[] { 13.5, 67, .5 }, new double[] { 14, 74, 1.5 },
        new double[] { 16.5, 76, .5 }, new double[] { 17, 79, .5 }, new double[] { 17.5, 76, .5 }, new double[] { 18, 72, 1 }, new double[] { 19.5, 71, .5 },
        new double[] { 20, 69, .5 }, new double[] { 20.5, 72, .5 }, new double[] { 21, 78, 1 }, new double[] { 22, 76, .5 }, new double[] { 22.5, 74, .5 }, new double[] { 23, 72, 1 },
        new double[] { 24, 70, 1.5 }, new double[] { 25.5, 74, .5 }, new double[] { 26, 77, 1 }, new double[] { 27, 81, .5 }, new double[] { 27.5, 79, .5 },
        new double[] { 28, 77, 1 }, new double[] { 29, 76, .5 }, new double[] { 29.5, 72, .5 }, new double[] { 31, 67, .5 }, new double[] { 31.5, 69, .5 },
        new double[] { 32, 70, .5 }, new double[] { 32.5, 74, .5 }, new double[] { 33, 77, .5 }, new double[] { 33.5, 81, 1 }, new double[] { 35, 79, .5 }, new double[] { 35.5, 77, .5 },
        new double[] { 36, 76, 1.5 }, new double[] { 37.5, 73, .5 }, new double[] { 38, 76, .5 }, new double[] { 38.5, 79, 1 },
        new double[] { 40, 77, 1 }, new double[] { 41, 76, .5 }, new double[] { 41.5, 74, .5 }, new double[] { 42, 69, 1.5 },
        new double[] { 44, 71, .5 }, new double[] { 44.5, 74, .5 }, new double[] { 45, 77, 1 }, new double[] { 46, 76, .5 }, new double[] { 46.5, 74, .5 }, new double[] { 47, 71, 1 },
        new double[] { 48, 70, .5 }, new double[] { 48.5, 74, .5 }, new double[] { 49, 79, 1 }, new double[] { 50, 77, .5 }, new double[] { 50.5, 74, .5 }, new double[] { 51, 70, 1 },
        new double[] { 52, 72, .5 }, new double[] { 52.5, 76, .5 }, new double[] { 53, 79, .5 }, new double[] { 53.5, 82, 1 }, new double[] { 55, 81, .5 }, new double[] { 55.5, 79, .5 },
        new double[] { 56, 77, 2 }, new double[] { 58, 76, .5 }, new double[] { 58.5, 72, .5 }, new double[] { 59, 69, 1 },
        new double[] { 60, 67, 1 }, new double[] { 62, 72, .5 },
    };
    // one chord a bar: the bass root, then the notes the piano plays (rootless, jazzy voicings)
    static readonly int[][] Chords = {
        new[] { 41, 64, 67, 69, 72 },   // Fmaj9
        new[] { 38, 60, 64, 65, 69 },   // Dm9
        new[] { 43, 62, 65, 69, 70 },   // Gm9
        new[] { 36, 62, 64, 67, 70 },   // C9
        new[] { 45, 64, 67, 71, 72 },   // Am9
        new[] { 38, 60, 64, 66, 69 },   // D9
        new[] { 43, 62, 65, 69, 70 },   // Gm9
        new[] { 36, 62, 65, 67, 70 },   // C9sus
        new[] { 46, 62, 65, 69, 72 },   // Bbmaj9
        new[] { 45, 61, 64, 67, 71 },   // A7(9)
        new[] { 38, 60, 64, 65, 69 },   // Dm9
        new[] { 43, 59, 62, 65, 69 },   // G9
        new[] { 43, 62, 65, 69, 70 },   // Gm9
        new[] { 36, 62, 64, 67, 70 },   // C9
        new[] { 41, 64, 67, 69, 72 },   // Fmaj9
        new[] { 36, 62, 65, 67, 70 },   // C9sus
    };

    static double Hz(double midi) { return 440 * Math.Pow(2, (midi - 69) / 12); }
    static int At(double beat) { return (int)Math.Round(beat * 60.0 / Bpm * Rate); }

    // ---------- the instruments ----------
    // vibraphone: a pure tone with a bright strike and a slow tremolo
    static void Vibe(float[] b, double beat, double f, double amp, double len) {
        int s0 = At(beat); double tau = 0.35 + len * 0.25;
        for (int i = s0; i < b.Length; i++) {
            double t = (i - s0) / (double)Rate;
            double env = Math.Min(1, t / 0.004) * Math.Exp(-t / tau);
            if (env < 0.0004 && t > 0.01) break;
            double w = Math.Sin(2 * Math.PI * f * t) + 0.22 * Math.Sin(2 * Math.PI * 4 * f * t) * Math.Exp(-t / 0.05) + 0.08 * Math.Sin(2 * Math.PI * 3 * f * t) * Math.Exp(-t / 0.2);
            b[i] += (float)(amp * env * w * (1 + 0.22 * Math.Sin(2 * Math.PI * 5.2 * t)));
        }
    }
    // electric piano: a soft bell with a warm second harmonic
    static void Keys(float[] b, double beat, double f, double amp, double len) {
        int s0 = At(beat), off = At(beat + len) - s0;
        for (int i = s0; i < b.Length; i++) {
            double t = (i - s0) / (double)Rate;
            double env = Math.Min(1, t / 0.006) * Math.Exp(-t / 0.9);
            if (i - s0 > off) env *= Math.Exp(-((i - s0 - off) / (double)Rate) / 0.06);
            if (env < 0.0004 && t > 0.01) break;
            double w = Math.Sin(2 * Math.PI * f * t) + 0.45 * Math.Sin(2 * Math.PI * 2 * f * t) * Math.Exp(-t / 0.3) + 0.12 * Math.Sin(2 * Math.PI * 7 * f * t) * Math.Exp(-t / 0.03);
            b[i] += (float)(amp * env * w);
        }
    }
    // bass: round, with a soft pluck
    static void Bass(float[] b, double beat, double f, double amp, double len) {
        int s0 = At(beat), off = At(beat + len) - s0;
        for (int i = s0; i < b.Length; i++) {
            double t = (i - s0) / (double)Rate;
            double env = Math.Min(1, t / 0.008) * Math.Exp(-t / 0.7);
            if (i - s0 > off) env *= Math.Exp(-((i - s0 - off) / (double)Rate) / 0.05);
            if (env < 0.0004 && t > 0.02) break;
            double w = Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(2 * Math.PI * 2 * f * t) * Math.Exp(-t / 0.15);
            b[i] += (float)(amp * env * w);
        }
    }
    static readonly Random noise = new Random(11);
    static void Shaker(float[] b, double beat, double amp) {
        int s0 = At(beat); double prev = 0;
        for (int i = s0; i < b.Length && i < s0 + Rate / 12; i++) {
            double t = (i - s0) / (double)Rate, n = noise.NextDouble() * 2 - 1;
            b[i] += (float)(amp * Math.Min(1, t / 0.006) * Math.Exp(-t / 0.025) * (n - prev)); prev = n;
        }
    }
    static void Rim(float[] b, double beat, double amp) {
        int s0 = At(beat);
        for (int i = s0; i < b.Length && i < s0 + Rate / 10; i++) {
            double t = (i - s0) / (double)Rate, e = Math.Exp(-t / 0.012);
            b[i] += (float)(amp * e * (Math.Sin(2 * Math.PI * 1750 * t) + 0.6 * Math.Sin(2 * Math.PI * 2650 * t) + 0.4 * Math.Sin(2 * Math.PI * 480 * t)));
        }
    }

    static float[] Draw() {
        int loop = At(64);
        var b = new float[loop + Rate * 3];   // room for the last notes to ring; folded back onto the start
        foreach (var n in Melody) Vibe(b, n[0], Hz(n[1]), 0.17, n[2]);
        for (int bar = 0; bar < 16; bar++) {
            var c = Chords[bar]; double at = bar * 4;
            // bossa bass: root, the root again just before beat 2, then the fifth
            Bass(b, at, Hz(c[0]), 0.34, 1.4);
            Bass(b, at + 1.5, Hz(c[0]), 0.2, 0.45);
            Bass(b, at + 2, Hz(c[0] + 7), 0.3, 1.4);
            Bass(b, at + 3.5, Hz(c[0] + 7), 0.18, 0.45);
            // the piano on the off-beats, a little different in every other bar
            var hits = bar % 2 == 0 ? new[] { 0.0, 1.5, 2.5 } : new[] { 0.5, 2.0, 3.5 };
            foreach (var h in hits) for (int k = 1; k < c.Length; k++) Keys(b, at + h, Hz(c[k]), 0.032, h == 0 ? 1.2 : 0.6);
            // the clave on the rim: three hits, then two
            foreach (var h in bar % 2 == 0 ? new[] { 0.0, 1.5, 3.0 } : new[] { 1.0, 2.5 }) Rim(b, at + h, 0.05);
            for (int e = 0; e < 16; e++) Shaker(b, at + e * 0.25, e % 2 == 1 ? 0.035 : 0.018);
        }
        var o = new float[loop];
        for (int i = 0; i < b.Length; i++) o[i % loop] += b[i];
        return o;
    }

    public static void Start() {
        if (Sound.Volume <= 0) return;
        try {
            if (file == null || !File.Exists(file)) {
                file = Path.Combine(Path.GetTempPath(), "RecompLauncher-lounge.wav");
                File.WriteAllBytes(file, Sound.Wave(Draw(), 0.95));
            }
            if (fade != null) fade.Stop();
            if (player == null) {
                player = new MediaPlayer();
                player.MediaEnded += (s, e) => { player.Position = TimeSpan.Zero; player.Play(); };
                player.Open(new Uri(file));
            }
            player.Volume = Math.Pow(Sound.Volume / 100.0, 1.3) * 0.6;
            player.Position = TimeSpan.Zero;
            player.Play();
        } catch { }
    }
    // a short fade, then quiet
    public static void Stop() {
        if (player == null) return;
        if (fade != null) fade.Stop();
        double from = player.Volume; int step = 0;
        fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        fade.Tick += (s, e) => {
            step++;
            player.Volume = from * Math.Max(0, 1 - step / 15.0);
            if (step >= 15) { fade.Stop(); player.Pause(); }
        };
        fade.Start();
    }
}
}
