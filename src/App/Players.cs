// Who's playing? The profiles, as big round pictures with their names, and a New profile card. Picking one plays as
// it (its own saves, its picture on Home); Edit opens Personalise for it; Delete (pressed twice) takes it off the
// list, and its saves stay in the profiles folder. Opens from the picture at the top left of Home, LB on a
// controller, and at start when there is more than one profile.
// Controller: left / right pick, A plays as (or makes a new one), X edits, down goes to the buttons, B closes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace RecompLauncher {

partial class Shell {
    Grid plLayer; Border plCard; WrapPanel plCards; TextBlock plNote; Button plPlay, plEdit, plDelete;
    readonly List<FrameworkElement> plStops = new List<FrameworkElement>();
    int plIdx, plCardIdx; bool plConfirm;   // plIdx: the controller's place; plCardIdx: the card picked (what the buttons act on)
    readonly Random plRandom = new Random();
    bool PlayersOpen { get { return plLayer != null && plLayer.Visibility == Visibility.Visible; } }

    void BuildPlayers() {
        if (plLayer != null) return;
        plLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
        var dim = new Rectangle { Fill = Ui.Brush("#C0050709") };
        dim.MouseLeftButtonUp += (s, e) => ClosePlayers();
        plLayer.Children.Add(dim);
        plCard = new Border { Width = 780, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(18),
            Background = Ui.Brush("#11181D"), BorderBrush = Ui.Brush("#26333C"), BorderThickness = new Thickness(1), Padding = new Thickness(34, 30, 34, 28),
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black } };
        var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform()); tg.Children.Add(new TranslateTransform());
        plCard.RenderTransform = tg;
        var stack = new StackPanel();
        var head = new Grid();
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "PROFILES", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
        titles.Children.Add(new TextBlock { Text = "Who's playing?", FontSize = 24, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(titles);
        var close = new Button { Style = (Style)W.FindResource("Chrome"), Content = "", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, -14, 0) };
        close.Click += (s, e) => ClosePlayers();
        head.Children.Add(close);
        stack.Children.Add(head);

        plCards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 6) };
        stack.Children.Add(new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = plCards });
        plNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(plNote);

        var bar = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        plDelete = MakeButton("Delete profile", 44); plDelete.Background = Brushes.Transparent; plDelete.Foreground = Ui.Brush(Ui.Muted); plDelete.Margin = new Thickness(0); plDelete.Padding = new Thickness(4, 0, 4, 0);
        plDelete.HorizontalAlignment = HorizontalAlignment.Left;
        plDelete.Click += (s, e) => DeletePicked();
        bar.Children.Add(plDelete);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        plEdit = MakeButton("Edit", 44); plEdit.Click += (s, e) => EditPicked();
        plPlay = MakeButton("Play", 44); plPlay.Background = Ui.Brush(Ui.Teal); plPlay.Foreground = Ui.Brush("#0A0E11"); plPlay.Padding = new Thickness(28, 0, 28, 0); plPlay.Margin = new Thickness(10, 0, 0, 0);
        plPlay.Click += (s, e) => PlayPicked();
        right.Children.Add(plEdit); right.Children.Add(plPlay);
        bar.Children.Add(right);
        stack.Children.Add(bar);

        plCard.Child = stack;
        plLayer.Children.Add(plCard);
        plLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(plLayer);
        W.RegisterName("PlayersLayer", plLayer);
        W.RegisterName("PlayersCard", plCard);
    }
    void FillPlayers() {
        plCards.Children.Clear(); plStops.Clear();
        foreach (var p in Profiles.All) plCards.Children.Add(PlayerCard(p));
        plCards.Children.Add(PlayerCard(null));
        // the buttons are stops too, after the cards
        plStops.Add(plDelete); plStops.Add(plEdit); plStops.Add(plPlay);
        int shared = Games.Count(g => g.Inst != null && !Profiles.Separate(g));
        plNote.Text = "Each profile has its own picture and its own saves: when a game starts, the launcher puts that profile's saves in place. Game settings stay shared."
            + (shared > 0 ? " " + shared + (shared == 1 ? " installed game keeps" : " installed games keep") + " one save for everyone (the launcher doesn't know where they save)." : "");
    }
    FrameworkElement PlayerCard(PlayerProfile p) {
        var card = new StackPanel { Width = 132, Margin = new Thickness(6, 0, 6, 14), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        bool body = p != null && p.Avatar == "char";
        var pic = new Grid { Width = 112, Height = body ? 160 : 112, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        if (body) {
            // a made character stands there whole, in its pose and mood
            pic.Children.Add(new Rectangle { StrokeThickness = 4, RadiusX = 20, RadiusY = 20 });
            var img = new Image { Source = AvatarArt.Render(AvatarDef.Parse(p.Character), new AvatarState { Backdrop = true }, AvatarArt.Crop.Full, 200, 290), Stretch = Stretch.UniformToFill };
            pic.Children.Add(new Border { Margin = new Thickness(7), CornerRadius = new CornerRadius(14), Child = img, ClipToBounds = true,
                Clip = new RectangleGeometry(new Rect(0, 0, 98, 146), 14, 14) });
        } else pic.Children.Add(new Ellipse { StrokeThickness = 4 });
        if (p != null && !body) pic.Children.Add(new Ellipse { Margin = new Thickness(8), Fill = AvatarBrush(p) });
        else if (p == null) {
            pic.Children.Add(new Ellipse { Margin = new Thickness(8), Fill = Ui.Brush("#1C252C"), Stroke = Ui.Brush("#26333C"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } });
            pic.Children.Add(new TextBlock { Text = "", FontFamily = Ui.Mdl, FontSize = 26, Foreground = Ui.Brush(Ui.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        card.Children.Add(pic);
        card.Children.Add(new TextBlock { Text = p != null ? Profiles.DisplayName(p) : "New profile", FontSize = 14, FontFamily = Ui.Semi, HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) });
        card.Children.Add(new TextBlock { Text = p == Profiles.Current ? "Playing now" : "", FontSize = 11, Foreground = Ui.Brush(Ui.Teal), HorizontalAlignment = HorizontalAlignment.Center });
        if (p != null) {
            string top; double secs = Playtime.TotalIn(Profiles.PlaytimeFile(p), out top);
            var topGame = top != null ? Catalog.Find(top) : null;
            card.Children.Add(new TextBlock { Text = secs > 60 ? Playtime.Format(secs) + (topGame != null ? "\n" + (topGame.Short ?? topGame.Name) + " most" : "") : "", FontSize = 11, Foreground = Ui.Brush(Ui.Muted),
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        }
        int idx = plStops.Count;
        card.MouseLeftButtonUp += (s, e) => { if (plIdx == idx) PlayPicked(); else { Sound.Play(Sfx.Move); PickPlayer(idx); } };
        plStops.Add(card);
        return card;
    }
    void PickPlayer(int i) {
        plIdx = Math.Max(0, Math.Min(plStops.Count - 1, i));
        plConfirm = false;
        int cards = Profiles.All.Count + 1;
        if (plIdx < cards) plCardIdx = plIdx;
        for (int k = 0; k < cards; k++) {
            var ring = (Shape)((Grid)((StackPanel)plStops[k]).Children[0]).Children[0];
            ring.Stroke = k == plCardIdx ? Ui.Brush(Ui.Teal) : Brushes.Transparent;
        }
        for (int k = 0; k < plStops.Count; k++) plStops[k].Effect = padUsed && k == plIdx && k >= cards ? SetGlow : null;
        bool isNew = plCardIdx == cards - 1;
        plPlay.Content = isNew ? "New profile" : "Play as " + Profiles.DisplayName(PlTarget);
        plEdit.IsEnabled = !isNew;
        plDelete.Content = "Delete profile"; plDelete.Foreground = Ui.Brush(Ui.Muted);
        plDelete.Visibility = Profiles.All.Count > 1 && !isNew ? Visibility.Visible : Visibility.Hidden;
        plStops[plIdx].BringIntoView();
    }
    PlayerProfile PlTarget { get { return plCardIdx < Profiles.All.Count ? Profiles.All[plCardIdx] : null; } }

    void OpenPlayers() {
        if (PlayersOpen) return;
        if (ProfileOpen) CloseProfile();
        BuildPlayers();
        FillPlayers();
        PickPlayer(Profiles.All.IndexOf(Profiles.Current));
        ShowCard("PlayersLayer", "PlayersCard");
    }
    void ClosePlayers() {
        if (!PlayersOpen) return;
        HideCard("PlayersLayer", "PlayersCard");
    }
    void PlayPicked() {
        int cards = Profiles.All.Count + 1;
        if (plCardIdx == cards - 1) { NewPlayer(); return; }
        var p = PlTarget;
        Sound.Play(Sfx.Select);
        Profiles.Switch(p);
        ApplyAvatar();
        RefreshForProfile();
        SetStep("Playing as " + Profiles.DisplayName(p) + ".", Ui.Teal);
        ClosePlayers();
    }
    void EditPicked() {
        var p = PlTarget; if (p == null) return;
        Profiles.Switch(p); ApplyAvatar(); RefreshForProfile();
        HideCard("PlayersLayer", "PlayersCard");
        OpenProfile();
    }
    void NewPlayer() {
        var p = Profiles.Add(plRandom);
        Profiles.Switch(p); ApplyAvatar(); RefreshForProfile();
        HideCard("PlayersLayer", "PlayersCard");
        OpenProfile();
    }
    // pressed twice: the first press asks
    void DeletePicked() {
        var p = PlTarget;
        if (p == null || Profiles.All.Count < 2) return;
        if (!plConfirm) { plConfirm = true; plDelete.Content = "Delete " + Profiles.DisplayName(p) + "? Press again"; plDelete.Foreground = Ui.Brush(Ui.Red); Sound.Play(Sfx.Bump); return; }
        string name = Profiles.DisplayName(p);
        Profiles.Remove(p);
        Sound.Play(Sfx.Back);
        plDelete.Foreground = Ui.Brush(Ui.Muted);
        ApplyAvatar();
        FillPlayers();
        PickPlayer(Profiles.All.IndexOf(Profiles.Current));
        plNote.Text = name + " is off the list. Their saves are kept in the profiles folder (profiles\\" + p.Id + ").";
    }
    void PlayersPad(int bit) {
        int cards = Profiles.All.Count + 1;
        bool onCard = plIdx < cards;
        if (bit == Pad.B || bit == Pad.Y) { ClosePlayers(); return; }
        if (bit == Pad.X) { if (plCardIdx != cards - 1) EditPicked(); return; }
        if (bit == Pad.A) {
            if (onCard) PlayPicked();
            else if (plStops[plIdx] == plDelete) DeletePicked();
            else if (plStops[plIdx] == plEdit) { if (plEdit.IsEnabled) EditPicked(); }
            else PlayPicked();
            return;
        }
        int was = plIdx;
        if (bit == Pad.Left) plIdx = onCard ? Math.Max(0, plIdx - 1) : Math.Max(cards + (plDelete.Visibility == Visibility.Visible ? 0 : 1), plIdx - 1);
        else if (bit == Pad.Right) plIdx = onCard ? Math.Min(cards - 1, plIdx + 1) : Math.Min(plStops.Count - 1, plIdx + 1);
        else if (bit == Pad.Down) { if (onCard) plIdx = plStops.Count - 1; }   // to Play
        else if (bit == Pad.Up) { if (!onCard) plIdx = plCardIdx; }
        else return;
        Sound.Play(plIdx != was ? Sfx.Move : Sfx.Bump);
        if (plIdx != was) PickPlayer(plIdx);
    }
}
}
