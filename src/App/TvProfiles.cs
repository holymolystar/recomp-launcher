// TV & Movies: who's watching. The screen TV opens on: the free profile (free channels, the world list and your own
// library) is always there; each IPTV login is a profile of its own, with its provider's Movies, Shows and TV Live.
// Every profile has a picture: one of twelve made here, or a picture from this PC (copied into tvavatars\).
// Controller: left / right pick, A opens, X changes the picked profile, down to "Manage profiles", B closes TV.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    Grid tvProfiles; StackPanel tvProfRow; Border tvManageBtn; TextBlock tvProfSub;
    readonly List<Border> tvProfTiles = new List<Border>(); readonly List<TextBlock> tvProfNames = new List<TextBlock>();
    int tvProfIdx, tvProfLast; bool tvManage, tvPinOk;
    bool TvProfilesOpen { get { return tvProfiles != null && tvProfiles.Visibility == Visibility.Visible && !tvProfLeaving; } }

    // the built-in pictures: two colours and a symbol
    static readonly string[][] TvAvatars = {
        new[] { "#FF6A2B", "#D7141E", "" }, new[] { "#3FB8A0", "#1B5F7A", "" }, new[] { "#8B6CFF", "#3B1E9E", "" }, new[] { "#FF5FA8", "#9E1E63", "" },
        new[] { "#3D9BF0", "#123A7A", "" }, new[] { "#F7C948", "#C4581A", "" }, new[] { "#34D17A", "#14532D", "" }, new[] { "#22B8E6", "#0B3B5C", "" },
        new[] { "#B36BFF", "#4C1D95", "" }, new[] { "#FF8A3D", "#7C2D12", "" }, new[] { "#7D8BA3", "#1E293B", "" }, new[] { "#F25555", "#7F1D1D", "" } };
    static readonly Dictionary<string, ImageSource> tvAvatarCache = new Dictionary<string, ImageSource>();

    static ImageSource TvAvatarImage(string av) {
        av = av ?? "a0";
        ImageSource got; if (tvAvatarCache.TryGetValue(av, out got)) return got;
        int n;
        if (!(av.Length <= 3 && av.StartsWith("a") && int.TryParse(av.Substring(1), out n))) {
            try {
                if (File.Exists(av)) {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(av); bi.DecodePixelWidth = 320; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                    tvAvatarCache[av] = bi; return bi;
                }
            } catch { }
            n = 0;
        }
        var a = TvAvatars[Math.Abs(n) % TvAvatars.Length];
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var bg = new LinearGradientBrush(Ui.Col(a[0]), Ui.Col(a[1]), 50); bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, 300, 300));
            var glow = new RadialGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.3, 0.2), GradientOrigin = new Point(0.3, 0.2), RadiusX = 0.7, RadiusY = 0.7 }; glow.Freeze();
            dc.DrawRectangle(glow, null, new Rect(0, 0, 300, 300));
            var ft = new FormattedText(a[2], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Mdl, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 132, Brushes.White, 1);
            dc.DrawText(ft, new Point(150 - ft.Width / 2, 150 - ft.Height / 2));
        }
        dg.Freeze(); var img = new DrawingImage(dg); img.Freeze();
        tvAvatarCache[av] = img; return img;
    }
    static Ellipse TvAvatar(string av, double size) {
        return new Ellipse { Width = size, Height = size, Fill = new ImageBrush(TvAvatarImage(av)) { Stretch = Stretch.UniformToFill } };
    }

    // ---------- the screen ----------
    void BuildTvProfiles() {
        tvProfiles = new Grid { Visibility = Visibility.Collapsed, Background = TvTheme.Brush(TvBg) };
        Grid.SetColumnSpan(tvProfiles, 2);
        var glow = TvTheme.Live(new RadialGradientBrush { Center = new Point(0.5, 0.05), GradientOrigin = new Point(0.5, 0.05), RadiusX = 0.75, RadiusY = 0.75 }, g => {
            var c = TvTheme.Col(TvAccent); g.GradientStops.Clear();
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, c.R, c.G, c.B), 0)); g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        });
        tvProfiles.Children.Add(new Border { Background = glow, IsHitTestVisible = false });
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
        var mark = TvMark(58); mark.HorizontalAlignment = HorizontalAlignment.Center; mark.Margin = new Thickness(0, 0, 0, 30);
        mark.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7, Color = Color.FromRgb(0xFF, 0x3D, 0x1F) };
        col.Children.Add(mark);
        col.Children.Add(new TextBlock { Text = "Who's watching?", FontSize = 46, FontFamily = TvFont, FontWeight = FontWeights.Black, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        tvProfSub = new TextBlock { FontSize = 16.5, Foreground = TvTheme.Brush("#8CFFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 48) };
        col.Children.Add(tvProfSub);
        tvProfRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(tvProfRow);
        var mt = new TextBlock { Text = "Manage profiles", FontSize = 16.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center };
        tvManageBtn = new Border { Child = mt, Padding = new Thickness(26, 10, 26, 11), CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 50, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        tvManageBtn.MouseLeftButtonUp += (s, e) => { tvProfIdx = -1; TvToggleManage(); };
        tvManageBtn.MouseEnter += (s, e) => { if (MouseMode) { if (tvProfIdx >= 0) tvProfLast = tvProfIdx; tvProfIdx = -1; MarkTvProfiles(); } };
        col.Children.Add(tvManageBtn);
        tvProfiles.Children.Add(col);
        tvLayer.Children.Add(tvProfiles);
    }
    void OpenTvProfiles() {
        if (tvProfiles == null) BuildTvProfiles();
        tvManage = false;
        tvProfLeaving = false; if (tvProfiles.RenderTransform is ScaleTransform) { var pst = (ScaleTransform)tvProfiles.RenderTransform; pst.BeginAnimation(ScaleTransform.ScaleXProperty, null); pst.BeginAnimation(ScaleTransform.ScaleYProperty, null); pst.ScaleX = pst.ScaleY = 1; }
        tvProfiles.Visibility = Visibility.Visible; tvProfiles.BeginAnimation(UIElement.OpacityProperty, Anim(1, 240, 0));
        tvZone = "profiles";
        tvProfIdx = tvAcc == null ? 0 : TvStore.Accounts.IndexOf(tvAcc) + 1; if (tvProfIdx < 0) tvProfIdx = 0;
        FillTvProfiles();
        SetHints();
    }
    void FillTvProfiles() {
        tvProfRow.Children.Clear(); tvProfTiles.Clear(); tvProfNames.Clear();
        tvProfSub.Text = tvManage ? "Pick a profile to change its name, picture or login" : "Pick a profile";
        var list = new List<string[]> { new[] { TvStore.FreeName, TvStore.FreeAvatar, "Free films, series and TV" } };
        foreach (var a in TvStore.Accounts) list.Add(new[] { a.Name, a.Avatar ?? "a" + ((TvStore.Accounts.IndexOf(a) + 1) % TvAvatars.Length), a.Type == "m3u" ? "Playlist" : "IPTV login" });
        list.Add(new[] { "Add profile", null, "Your IPTV login" });
        double size = list.Count > 6 ? 118 : 146;
        for (int i = 0; i < list.Count; i++) {
            var d = list[i]; bool add = i == list.Count - 1; int idx = i;
            var face = new Grid { Width = size, Height = size };
            if (add) {
                face.Children.Add(new Ellipse { Fill = TvTheme.Brush("#1FFFFFFF"), Stroke = TvTheme.Brush("#40FFFFFF"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
                face.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = size * 0.32, Foreground = TvTheme.Brush("#D9FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            } else {
                face.Children.Add(TvAvatar(d[1], size));
                if (tvManage) {
                    face.Children.Add(new Ellipse { Fill = TvTheme.Brush("#8C000000") });
                    face.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = size * 0.26, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                }
            }
            var ring = new Border { Child = face, CornerRadius = new CornerRadius(size / 2 + 6), Padding = new Thickness(4), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock { Text = d[0], FontSize = 19, FontFamily = Ui.Semi, Foreground = TvTheme.Brush("#A6FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size + 30 };
            var sub = new TextBlock { Text = d[2], FontSize = 13.5, Foreground = TvTheme.Brush("#59FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            var st = new StackPanel { Width = size + 44 }; st.Children.Add(ring); st.Children.Add(name); st.Children.Add(sub);
            var tile = new Border { Child = st, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(10, 0, 10, 0), RenderTransformOrigin = new Point(0.5, 0.35), RenderTransform = new ScaleTransform(1, 1) };
            TvOnHover(tile, () => { if (tvProfIdx != idx) { tvProfIdx = idx; MarkTvProfiles(); } });
            tile.MouseLeftButtonUp += (s, e) => { tvProfIdx = idx; TvProfileAct(idx, false); };
            tile.MouseRightButtonUp += (s, e) => { tvProfIdx = idx; TvProfileAct(idx, true); e.Handled = true; };
            tvProfRow.Children.Add(tile); tvProfTiles.Add(tile); tvProfNames.Add(name);
        }
        if (tvProfIdx >= tvProfTiles.Count) tvProfIdx = tvProfTiles.Count - 1;
        MarkTvProfiles();
    }
    void MarkTvProfiles() {
        for (int i = 0; i < tvProfTiles.Count; i++) {
            bool on = i == tvProfIdx;
            var ring = (Border)((StackPanel)tvProfTiles[i].Child).Children[0];
            ring.BorderBrush = on ? Brushes.White : Brushes.Transparent;
            tvProfNames[i].Foreground = on ? Brushes.White : TvTheme.Brush("#A6FFFFFF");
            var st = (ScaleTransform)tvProfTiles[i].RenderTransform; double k = on ? 1.08 : 1;
            if (Math.Abs(st.ScaleX - k) > 0.001) { st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(k, 150)); st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(k, 150)); }
        }
        bool m = tvProfIdx < 0;
        tvManageBtn.Background = m ? Brushes.White : tvManage ? TvTheme.Brush("#33FFFFFF") : Brushes.Transparent;
        tvManageBtn.BorderBrush = m ? Brushes.White : TvTheme.Brush("#66FFFFFF");
        ((TextBlock)tvManageBtn.Child).Text = tvManage ? "Done" : "Manage profiles";
        ((TextBlock)tvManageBtn.Child).Foreground = m ? Brushes.Black : Brushes.White;
        var ms = (ScaleTransform)tvManageBtn.RenderTransform; ms.ScaleX = ms.ScaleY = m ? 1.06 : 1;
        SetHints();
    }
    void TvToggleManage() { tvManage = !tvManage; Sound.Play(Sfx.Select); FillTvProfiles(); }
    // open a profile, or (managing, or X) change it; the last tile adds one
    void TvProfileAct(int i, bool edit) {
        if (tvProfWait) return;
        int n = TvStore.Accounts.Count;
        if (i == n + 1) { Sound.Play(Sfx.Select); TvWizardStart(); return; }
        var a = i == 0 ? null : TvStore.Accounts[i - 1];
        // a profile with a PIN asks for it first
        if (!tvPinOk) { TvWithPin(TvMine.Profile(a), a == null ? TvStore.FreeName : a.Name, () => { tvPinOk = true; try { TvProfileAct(i, edit); } finally { tvPinOk = false; } }); return; }
        if (edit || tvManage) { Sound.Play(Sfx.Select); if (a == null) TvFreeForm(); else TvAccountForm(a); return; }
        Sound.Play(Sfx.Launch);
        tvAcc = a;
        TvTheme.Use(TvTheme.Of(TvMine.Profile(a)));
        if (TvMine.LastProfile != TvMine.Profile(a)) { TvMine.LastProfile = TvMine.Profile(a); TvMine.Save(); }
        // everything is loaded here, with the pill, before the profile opens
        tvProfWait = true;
        TvPrefetchThen(a, () => {
            tvProfWait = false;
            if (!TvOpen || tvAcc != a || tvProfiles.Visibility != Visibility.Visible) return;
            TvProfilesAway();   // they grow towards you and fade while the page comes in (TvLook.cs)
            TvApplyProfile();
            TvShowPage(TvStartPage());
            TvAskLangsOnce(a);
        });
    }
    bool tvProfWait;   // a profile is loading: the others wait
    // the provider's sections only show with a provider's profile; the round picture top right is the profile's
    void TvApplyProfile() {
        int pi = tvRailIds.IndexOf("profiles");
        if (pi >= 0) { var g = (Grid)tvRailItems[pi].Child; g.Children.Clear(); g.Children.Add(TvAvatar(tvAcc != null ? tvAcc.Avatar ?? "a" + ((TvStore.Accounts.IndexOf(tvAcc) + 1) % TvAvatars.Length) : TvStore.FreeAvatar, 38)); }
    }
    bool TvRailShown(int i) { return i >= 0 && i < tvRailItems.Count && tvRailItems[i].Visibility == Visibility.Visible; }
    void TvProfilesPad(int bit) {
        int count = tvProfTiles.Count;
        if (bit == Pad.B) { if (tvManage) TvToggleManage(); else CloseTv(); return; }
        if (tvProfIdx < 0) {
            if (bit == Pad.Up) { tvProfIdx = Math.Min(tvProfLast, count - 1); Sound.Play(Sfx.Move); MarkTvProfiles(); }
            else if (bit == Pad.A) TvToggleManage();
            else Sound.Play(Sfx.Bump);
            return;
        }
        if (bit == Pad.Left || bit == Pad.Right) {
            int j = tvProfIdx + (bit == Pad.Left ? -1 : 1);
            if (j < 0 || j >= count) { Sound.Play(Sfx.Bump); return; }
            tvProfIdx = j; Sound.Play(Sfx.Move); MarkTvProfiles();
        } else if (bit == Pad.Down) { tvProfLast = tvProfIdx; tvProfIdx = -1; Sound.Play(Sfx.Move); MarkTvProfiles(); }
        else if (bit == Pad.A) TvProfileAct(tvProfIdx, false);
        else if (bit == Pad.X) TvProfileAct(tvProfIdx, true);
        else Sound.Play(Sfx.Bump);
    }

    // ---------- the free profile's name and picture ----------
    bool tvAccFree, tvAccPicking;
    void TvFreeForm() {
        tvAccFree = true; tvAccNew = false; tvAccPicking = false;
        tvAccEdit = new TvAccount { Id = "free", Name = TvStore.FreeName, Avatar = TvStore.FreeAvatar };
        TvOpenAccountPage();
    }
    // the pictures to pick from, in the same card as the profile
    void TvFillAvatarPicker() {
        var a = tvAccEdit; var p = tvAccPage;
        p.Title.Text = "Pick a picture"; p.Sub.Text = "For " + a.Name + ". Or use a picture of your own from this PC.";
        var rows = new List<ListRow>();
        for (int i = 0; i < TvAvatars.Length; i++) {
            string id = "a" + i;
            rows.Add(new ListRow { Pic = TvAvatarImage(id), C1 = TvAvatars[i][0], C2 = TvAvatars[i][1], Title = "Picture " + (i + 1), Text = (a.Avatar ?? "") == id ? "In use" : null,
                Do = () => { a.Avatar = id; tvAccPicking = false; TvFillAccountForm(); } });
        }
        rows.Add(new ListRow { Glyph = "", C1 = "#5b6670", C2 = "#5b6670", Title = "A picture from this PC", Text = "PNG or JPG; it's copied into the launcher's tvavatars folder", Do = () => {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "A profile picture" };
            if (d.ShowDialog() != true) return;
            try {
                string dir = Path.Combine(Paths.Root, "tvavatars"); Directory.CreateDirectory(dir);
                string to = Path.Combine(dir, (a.Id ?? "p") + "-" + DateTime.Now.Ticks + Path.GetExtension(d.FileName).ToLowerInvariant());
                File.Copy(d.FileName, to, true); a.Avatar = to;
            } catch (Exception e) { SetStep("TV: that picture couldn't be used: " + e.Message, Ui.Red); }
            tvAccPicking = false; TvFillAccountForm(); } });
        rows.Add(new ListRow { Glyph = "", C1 = "#33424d", C2 = "#33424d", Title = "Back", Do = () => { tvAccPicking = false; TvFillAccountForm(); } });
        FillRows(p, rows, "");
    }
    ListRow TvPictureRow() {
        var a = tvAccEdit;
        return new ListRow { Pic = TvAvatarImage(a.Avatar ?? "a1"), C1 = "#33424d", C2 = "#33424d", Title = "Profile picture", Text = "Pick to change it", Do = () => { tvAccPicking = true; TvFillAccountForm(); } };
    }
    void TvSaveFree() {
        TvStore.FreeName = string.IsNullOrWhiteSpace(tvAccEdit.Name) ? "Free TV" : tvAccEdit.Name.Trim();
        TvStore.FreeAvatar = tvAccEdit.Avatar ?? "a0";
        TvStore.Save();
        if (ListPageOpen && lpOpen == tvAccPage) CloseListPage();
        TvAfterProfileChange();
    }
    void TvAfterProfileChange() {
        if (TvProfilesOpen) FillTvProfiles(); else TvApplyProfile();
    }
}
}
