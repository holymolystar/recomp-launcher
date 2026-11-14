// Bluetooth with a controller: your paired devices (connected or not) and the ones nearby that can pair, as a list
// you move through with the d-pad. A pairs a device nearby (put the controller or headset in pairing mode first);
// A on a paired one asks, then removes it. Windows' own Bluetooth interfaces (Windows.Devices.Enumeration, described
// in System32\WinMetadata on Windows 10 and 11); a launcher built without them opens Windows' Bluetooth page instead.
// Shown as a card (Settings > Controller > Pair a controller) and as a page of the in-game menu (Quick settings).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
#if WINRT
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
#endif

namespace RecompLauncher {

class BtDevice { public string Id, Name; public bool Paired, Connected; public object Info; }

static class Bt {
    public static bool Available {
#if WINRT
        get { return true; }
#else
        get { return false; }
#endif
    }
#if WINRT
    // wait for one of Windows' own background jobs (this is always called off the window's thread)
    static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op) {
        while (op.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(30);
        return op.GetResults();
    }
#endif
    // paired devices first (connected ones on top), then the ones nearby that can pair; blocks for a few seconds
    public static List<BtDevice> List() {
        var l = new List<BtDevice>();
#if WINRT
        var props = new[] { "System.Devices.Aep.IsConnected" };
        foreach (bool paired in new[] { true, false })
            foreach (var sel in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(paired), BluetoothLEDevice.GetDeviceSelectorFromPairingState(paired) }) {
                DeviceInformationCollection found;
                try { found = Wait(DeviceInformation.FindAllAsync(sel, props)); } catch { continue; }
                foreach (var d in found) {
                    if (string.IsNullOrWhiteSpace(d.Name) || l.Any(x => x.Id == d.Id || (x.Name == d.Name && x.Paired == paired))) continue;
                    if (!paired && !d.Pairing.CanPair) continue;
                    object c; d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out c);
                    l.Add(new BtDevice { Id = d.Id, Name = d.Name, Paired = paired, Connected = c is bool && (bool)c, Info = d });
                }
            }
#endif
        return l.OrderBy(x => x.Paired ? 0 : 1).ThenBy(x => x.Connected ? 0 : 1).ThenBy(x => x.Name).ToList();
    }
    public static string Pair(BtDevice dev) {
#if WINRT
        try {
            var d = (DeviceInformation)dev.Info;
            var custom = d.Pairing.Custom;
            Windows.Foundation.TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> accept = (s, e) => e.Accept();
            // the "accept" answer to Windows' pairing question; this compiler can't attach to Windows' own events, so
            // it's attached at run time (else the plain pairing, which works for devices that ask nothing)
            DevicePairingResult r;
            var ev = typeof(DeviceInformationCustomPairing).GetEvent("PairingRequested");
            object token = null;
            try { token = ev.GetAddMethod().Invoke(custom, new object[] { accept }); } catch { }
            if (token != null) {
                r = Wait(custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin));
                try { ev.GetRemoveMethod().Invoke(custom, new[] { token }); } catch { }
            } else r = Wait(d.Pairing.PairAsync());
            return r.Status == DevicePairingResultStatus.Paired || r.Status == DevicePairingResultStatus.AlreadyPaired ? null : Explain(r.Status);
        } catch (Exception e) { return e.InnerException != null ? e.InnerException.Message : e.Message; }
#else
        return "This launcher was built without Bluetooth.";
#endif
    }
#if WINRT
    static string Explain(DevicePairingResultStatus s) {
        switch (s) {
            case DevicePairingResultStatus.ConnectionRejected: case DevicePairingResultStatus.RejectedByHandler: return "the device said no";
            case DevicePairingResultStatus.AuthenticationTimeout: return "it took too long: put the device in pairing mode and try again";
            case DevicePairingResultStatus.NoSupportedProfiles: return "Windows doesn't know what kind of device it is";
            default: return s.ToString();
        }
    }
#endif
    public static string Remove(BtDevice dev) {
#if WINRT
        try {
            var r = Wait(((DeviceInformation)dev.Info).Pairing.UnpairAsync());
            return r.Status == DeviceUnpairingResultStatus.Unpaired || r.Status == DeviceUnpairingResultStatus.AlreadyUnpaired ? null : r.Status.ToString();
        } catch (Exception e) { return e.Message; }
#else
        return "This launcher was built without Bluetooth.";
#endif
    }
}

partial class Shell {
    // ---------- the card (from Settings) ----------
    Grid btLayer; StackPanel btRows; TextBlock btSub; readonly List<Button> btStops = new List<Button>(); int btIdx; List<BtDevice> btList = new List<BtDevice>();
    string btArmed; bool btBusy;
    bool BtOpen { get { return btLayer != null && btLayer.Visibility == Visibility.Visible; } }
    void OpenBluetooth() {
        if (!Bt.Available) { try { System.Diagnostics.Process.Start("ms-settings:bluetooth"); } catch { } return; }
        if (btLayer == null) {
            Border card; StackPanel body;
            btLayer = Card("Bt", 560, VerticalAlignment.Center, out card, out body);
            body.Children.Add(new TextBlock { Text = "BLUETOOTH", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Ui.Brush(Ui.Teal) });
            body.Children.Add(new TextBlock { Text = "Pair a controller or headset", FontSize = 22, FontFamily = Ui.Semi, Margin = new Thickness(0, 2, 0, 2) });
            btSub = new TextBlock { FontSize = 12, Foreground = Ui.Brush(Ui.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            body.Children.Add(btSub);
            btRows = new StackPanel();
            body.Children.Add(new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = btRows });
            ((Rectangle)btLayer.Children[0]).MouseLeftButtonUp += (s, e) => CloseBluetooth();
        }
        Sound.Play(Sfx.Open);
        ShowCard("BtLayer", "BtCard");
        BtRefresh();
    }
    void CloseBluetooth() { if (!BtOpen) return; Sound.Play(Sfx.Back); HideCard("BtLayer", "BtCard"); }
    // look for devices (in the background), then show them
    void BtRefresh() {
        if (btBusy) return;
        btBusy = true; btArmed = null;
        btSub.Text = "Looking for devices... Put a controller in pairing mode now (Xbox: hold the pair button on top; PlayStation: hold Share / Create and PS; Switch Pro: hold the sync button).";
        FillBt(null);
        ThreadPool.QueueUserWorkItem(_ => {
            var l = Bt.List();
            W.Dispatcher.BeginInvoke(new Action(() => { btBusy = false; btList = l; btSub.Text = l.Count == 0 ? "Nothing found. Put the device in pairing mode, then Search again." : "A pairs a device nearby; A on a paired one removes it."; FillBt(l); }));
        });
    }
    void FillBt(List<BtDevice> l) {
        btRows.Children.Clear(); btStops.Clear();
        foreach (var d in l ?? new List<BtDevice>()) {
            var dev = d;
            string state = d.Paired ? (d.Connected ? "Connected" : "Paired") : "Nearby: A to pair";
            if (btArmed == d.Id) state = "Press again to remove";
            var b = BtButton(d.Name, state, d.Paired && d.Connected);
            b.Click += (s, e) => BtChoose(dev);
            btStops.Add(b); btRows.Children.Add(b);
        }
        var again = MakeButton(btBusy ? "Searching..." : "Search again", 40); again.Margin = new Thickness(0, 6, 8, 0);
        again.Click += (s, e) => BtRefresh();
        var done = MakeButton("Done", 40); done.Background = Ui.Brush(Ui.Teal); done.Foreground = Ui.Brush("#0A0E11"); done.Margin = new Thickness(0, 6, 0, 0);
        done.Click += (s, e) => CloseBluetooth();
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(again); bar.Children.Add(done);
        btRows.Children.Add(bar); btStops.Add(again); btStops.Add(done);
        btIdx = Math.Min(btIdx, btStops.Count - 1); MarkBt();
    }
    Button BtButton(string name, string state, bool on) {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = name, FontSize = 14.5, FontFamily = Ui.Semi, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var st = new TextBlock { Text = state, FontSize = 12, Foreground = Ui.Brush(on ? "#8fc93a" : Ui.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(st, 1); g.Children.Add(st);
        return new Button { Style = (Style)W.FindResource("Btn"), Content = g, Height = 50, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 0, 8) };
    }
    void MarkBt() { for (int i = 0; i < btStops.Count; i++) btStops[i].Effect = i == btIdx && padUsed ? SetGlow : null; }
    void BtChoose(BtDevice d) {
        if (btBusy) return;
        if (d.Paired && btArmed != d.Id) { btArmed = d.Id; Sound.Play(Sfx.Bump); FillBt(btList); return; }
        btBusy = true; Sound.Play(Sfx.Select);
        btSub.Text = (d.Paired ? "Removing " : "Pairing with ") + d.Name + "...";
        ThreadPool.QueueUserWorkItem(_ => {
            string err = d.Paired ? Bt.Remove(d) : Bt.Pair(d);
            W.Dispatcher.BeginInvoke(new Action(() => {
                btBusy = false;
                if (err != null) { btSub.Text = d.Name + ": " + err + "."; Sound.Play(Sfx.Bump); return; }
                Sound.Play(Sfx.Launch); BtRefresh();
                SetStep(d.Name + (d.Paired ? " is removed." : " is paired."), Ui.Teal);
            }));
        });
    }
    void BtPad(int bit) {
        if (bit == Pad.B) { CloseBluetooth(); return; }
        if (bit == Pad.A) { if (btIdx >= 0 && btIdx < btStops.Count) Press(btStops[btIdx]); return; }
        int was = btIdx;
        if (bit == Pad.Up) btIdx = Math.Max(0, btIdx - 1);
        if (bit == Pad.Down || bit == Pad.Right) btIdx = Math.Min(btStops.Count - 1, btIdx + 1);
        if (bit == Pad.Left) btIdx = Math.Max(0, btIdx - 1);
        if (btIdx != was) { Sound.Play(Sfx.Move); MarkBt(); btStops[btIdx].BringIntoView(); }
    }
}
}
