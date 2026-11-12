// Home music: a soft background tune while the launcher is on screen, as on a console's home menu. Two original
// tunes, drawn here from a few soft tones like the interface sounds (Sfx.cs): "calm" (warm seventh chords, a slow
// arpeggio) and "bright" (a light major tune). Settings > Sound > Home music: by theme (dark themes calm, light ones
// bright), Calm or Bright; it can be turned off, and its volume is a meter of ten bars (10% each). It fades out when
// a game starts or the launcher is put away, and in again after. While Settings is open, changes are heard at once.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    MediaPlayer music; string musicStyle; DispatcherTimer musicTimer; double musicLevel;
    string MusicStyle() {
        string v = Settings.Get("HomeMusic");
        if (v == "calm" || v == "bright") return v;
        return Theme.Current.Light ? "bright" : "calm";
    }
    void StartHomeMusic() {
        if (musicTimer != null) return;
        musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        musicTimer.Tick += (s, e) => TickHomeMusic();
        musicTimer.Start();
    }
    void TickHomeMusic() {
        string style = MusicStyle();
        bool want = MusicOn && MusicVolume > 0 && W.IsVisible && W.IsActive && !inTray && playing == null && !BrowserViewOpen && !SaverOn;
        double target = want ? 0.5 * Math.Pow(MusicVolume / 100.0, 1.6) : 0;
        if (want && (music == null || musicStyle != style)) {
            string file = MusicFile(style);
            if (file == null) { BuildMusicLater(style); return; }
            if (music != null) music.Close();
            music = new MediaPlayer { Volume = 0 };
            music.MediaEnded += (s, e) => { music.Position = TimeSpan.Zero; music.Play(); };
            music.Open(new Uri(file)); musicStyle = style; musicLevel = 0;
            music.Play();
        }
        if (music == null) return;
        // a slow fade: in over about two seconds, out over one
        double step = target > musicLevel ? 0.03 : 0.06;
        musicLevel = Math.Abs(target - musicLevel) <= step ? target : musicLevel + Math.Sign(target - musicLevel) * step;
        music.Volume = musicLevel;
        if (musicLevel <= 0 && !want) { music.Pause(); } else if (want) music.Play();
    }
    // ---------- its settings: on or off, the tune, and a volume meter ----------
    bool SettingsShown { get { var l = W.FindName("SettingsLayer") as FrameworkElement; return l != null && l.Visibility == Visibility.Visible; } }
    bool MusicOnSaved { get { string v = Settings.Get("HomeMusicOn"); return v == "on" || (v != "off" && Settings.Get("HomeMusic") != "off"); } }
    int MusicVolumeSaved { get { int v; return int.TryParse(Settings.Get("HomeMusicVolume"), out v) ? Math.Max(10, Math.Min(100, v / 10 * 10)) : 50; } }
    bool MusicOn { get { return SettingsShown && optMusic != null ? optMusic.On : MusicOnSaved; } }
    int MusicVolume { get { return SettingsShown && optMusic != null ? pickedMusicVolume : MusicVolumeSaved; } }
    Toggle optMusic; int pickedMusicVolume = 50; readonly List<Button> musicBars = new List<Button>(); TextBlock musicVolText;
    void MusicRows(StackPanel rows) {
        optMusic = NewToggle("Home music", "A soft tune while the launcher is on screen; it stops while you play.");
        rows.Children.Add(optMusic.Root); extraStops.Add(optMusic.Root);
        rows.Children.Add(ChoiceRow("HomeMusic", "Tune", "By theme: calm with a dark theme, bright with a light one.", 0, new[] { "theme", "calm", "bright" }, new[] { "By theme", "Calm", "Bright" }));
        rows.Children.Add(MeterRow("Music volume", musicBars, out musicVolText, PickMusicVolume));
    }
    // a row with a volume meter: ten bars (10% to 100%) rising left to right
    FrameworkElement MeterRow(string title, List<Button> bars, out TextBlock valueText, Action<int> pick) {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontFamily = Ui.Semi });
        text.Children.Add(new TextBlock { Text = "Click a bar or drag across them; on a controller, left and right then A.", FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap });
        var meter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 40, Background = Brushes.Transparent };
        bars.Clear();
        for (int i = 0; i < 10; i++) {
            int level = (i + 1) * 10;
            var b = new Button { Template = BarTemplate(), Width = 16, Height = 40, Padding = new Thickness(0, 28 - i * 3, 0, 0), Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Bottom, Tag = level, ToolTip = level + "%", Background = Ui.Brush(Raised), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2) };
            b.Click += (s, e) => pick(level);
            b.MouseEnter += (s, e) => { if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) pick(level); };
            bars.Add(b); meter.Children.Add(b); extraStops.Add(b);
        }
        valueText = new TextBlock { FontSize = 13, FontFamily = Ui.Semi, Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        meter.Children.Add(valueText);
        Grid.SetColumn(meter, 1);
        row.Children.Add(text); row.Children.Add(meter);
        return row;
    }
    static void PaintMeter(List<Button> bars, TextBlock valueText, int v) {
        foreach (var b in bars) b.Background = Ui.Brush((int)b.Tag <= v ? Ui.Teal : Raised);
        if (valueText != null) valueText.Text = v + "%";
    }
    // a bar: a plain rectangle with slightly rounded corners (the outline shows where the controller is)
    static ControlTemplate barTemplate;
    static ControlTemplate BarTemplate() {
        if (barTemplate != null) return barTemplate;
        // the button is as tall as the meter (so the controller moves straight across); the bar sits at its bottom
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        f.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        root.AppendChild(f);
        f.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        f.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        f.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        barTemplate = new ControlTemplate(typeof(Button)) { VisualTree = root };
        return barTemplate;
    }
    void PickMusicVolume(int v) {
        pickedMusicVolume = Math.Max(10, Math.Min(100, v));
        PaintMeter(musicBars, musicVolText, pickedMusicVolume);
    }

    // the tune as a .wav in the cache folder, drawn once, in the background (it takes a moment)
    static string MusicPath(string style) { return Path.Combine(Paths.Root, "cache", "home-music-" + style + "-1.wav"); }
    static string MusicFile(string style) { string f = MusicPath(style); return File.Exists(f) ? f : null; }
    static volatile bool musicBuilding;
    static void BuildMusicLater(string style) {
        if (musicBuilding) return;
        musicBuilding = true;
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
            try {
                string f = MusicPath(style); Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f + ".part", Tune(style));
                if (File.Exists(f)) File.Delete(f);
                File.Move(f + ".part", f);
            } catch { }
            musicBuilding = false;
        });
    }

    // ---------- the tunes ----------
    const int MRate = 32000;
    static double Freq(int midi) { return 440 * Math.Pow(2, (midi - 69) / 12.0); }
    static byte[] Tune(string style) {
        bool bright = style == "bright";
        double bpm = bright ? 108 : 84, beat = 60 / bpm;
        // four chords, each two bars (8 beats), played twice; the second time the tune rises a little
        int[][] chords = bright
            ? new[] { new[] { 60, 64, 67, 72 }, new[] { 57, 60, 64, 69 }, new[] { 53, 57, 60, 65 }, new[] { 55, 59, 62, 67 } }   // C, Am, F, G
            : new[] { new[] { 53, 57, 60, 64 }, new[] { 52, 55, 59, 62 }, new[] { 50, 53, 57, 60 }, new[] { 48, 52, 55, 59 } };  // Fmaj7, Em7, Dm7, Cmaj7
        // the tune: scale steps over the chords, one number per beat (-1 = rest), 64 beats
        int[] scale = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16 };
        int[] tune = bright
            ? new[] { 4, -1, 5, 4, 2, -1, 0, -1,  2, -1, 4, 2, 1, -1, -1, -1,  0, -1, 2, 0, 3, -1, 2, -1,  1, -1, 2, 1, 4, -1, -1, -1,
                      4, -1, 5, 7, 6, -1, 5, -1,  4, -1, 5, 4, 2, -1, -1, -1,  3, -1, 2, 3, 5, -1, 4, -1,  2, -1, 1, -1, 0, -1, -1, -1 }
            : new[] { 2, -1, -1, 4, 3, -1, 2, -1,  1, -1, -1, -1, 2, -1, -1, -1,  0, -1, -1, 2, 1, -1, 0, -1,  -1, -1, -1, -1, 4, -1, -1, -1,
                      5, -1, -1, 4, 3, -1, 4, -1,  2, -1, -1, -1, 3, -1, -1, -1,  1, -1, -1, 2, 0, -1, 1, -1,  -1, -1, -1, -1, -1, -1, -1, -1 };
        int beats = 64;
        int n = (int)(beats * beat * MRate);
        var mix = new double[n];
        int key = bright ? 72 : 72;
        for (int b = 0; b < beats; b++) {
            var ch = chords[(b / 8) % 4];
            double t0 = b * beat;
            // the pad: the chord, soft and slow, renewed each bar
            if (b % 4 == 0) foreach (int m in ch) Tone(mix, t0, beat * 4.4, Freq(m), bright ? 0.045 : 0.05, 0.6, 1.2, 0.0, 0.003);
            // the bass on the first and third beat
            if (b % 2 == 0) Tone(mix, t0, beat * 1.8, Freq(ch[0] - 12), 0.09, 0.02, 0.6, 0.15, 0);
            // the arpeggio, in eighths
            for (int k = 0; k < 2; k++) {
                int[] order = bright ? new[] { 0, 2, 1, 3, 2, 1, 3, 2 } : new[] { 0, 1, 2, 3, 2, 1, 2, 3 };
                int note = ch[order[(b * 2 + k) % 8]] + 12;
                Tone(mix, t0 + k * beat / 2, beat * 1.2, Freq(note), bright ? 0.035 : 0.03, 0.005, 0.5, 0.25, 0);
            }
            // the tune, a bell
            int st = tune[b];
            if (st >= 0) Tone(mix, t0, beat * 2.2, Freq(key + scale[st] - (bright ? 0 : 5)), 0.075, 0.008, 1.0, 0.35, 0.004);
        }
        // a soft room: two short echoes
        var outp = new double[n];
        for (int i = 0; i < n; i++) {
            double v = mix[i];
            int d1 = (int)(0.23 * MRate), d2 = (int)(0.41 * MRate);
            v += 0.22 * mix[(i - d1 + n) % n] + 0.12 * mix[(i - d2 + n) % n];
            outp[i] = v;
        }
        double peak = 0.0001; foreach (var v in outp) peak = Math.Max(peak, Math.Abs(v));
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(MRate); w.Write(MRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(outp[i] / peak * 0.8 * 32767));
        w.Flush();
        return ms.ToArray();
    }
    // a soft tone: attack, then a decay to a level held until the end, with a short release; a little second
    // harmonic makes it bell-like; it wraps round the end of the loop so the loop has no seam
    static void Tone(double[] mix, double start, double len, double f, double amp, double attack, double decay, double harm, double vibrato) {
        int n = mix.Length, s0 = (int)(start * MRate), count = (int)(len * MRate);
        for (int i = 0; i < count; i++) {
            double t = i / (double)MRate;
            double env = t < attack ? t / attack : Math.Exp(-(t - attack) / Math.Max(0.05, decay));
            double rel = Math.Min(1, (len - t) / 0.08); env *= Math.Max(0, rel);
            double ph = 2 * Math.PI * f * t + (vibrato > 0 ? vibrato * 20 * Math.Sin(2 * Math.PI * 5 * t) : 0);
            double v = Math.Sin(ph) + harm * Math.Sin(2 * ph) * Math.Exp(-t * 3);
            mix[(s0 + i) % n] += amp * env * v;
        }
    }
}
}
