// The browser inside the launcher: the Browser card's shortcuts open here, in the launcher's own window, through
// Microsoft's WebView2 (the Edge engine Windows already has; src\lib\webview2). A bar on top shows the page, with
// Back, Forward, an address button and Close. Logins and cookies are kept between visits
// (%LOCALAPPDATA%\RecompLauncher\browser).
// The controller is its mouse and keys (BrowserPad, Browser.cs): left stick points, A clicks, X right-clicks, right
// stick scrolls, B goes back, Y opens the touch keyboard, LB / RB back / forward, Menu types an address or a search,
// View is full screen. The Xbox / PS / Home button (or View + Menu) opens the launcher's menu over it: Back to the
// browser, Close the browser (it closes completely), Quick settings, Power. Without WebView2 on the PC the shortcuts
// open in the browser installed instead (Browser.cs).
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Path = System.IO.Path;

namespace RecompLauncher {

partial class Shell {
    Grid bvLayer; Border bvHost; WebView2 bv; TextBlock bvTitle, bvUrl; static bool? webViewOk;
    bool BrowserViewOpen { get { return bvLayer != null && bvLayer.Visibility == Visibility.Visible; } }
    // is WebView2 on this PC? (it comes with Windows 11 and with Edge)
    static bool WebViewAvailable() {
        if (webViewOk == null) { try { webViewOk = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); } catch { webViewOk = false; } }
        return webViewOk.Value;
    }
    void BuildBrowserView() {
        bvLayer = new Grid { Visibility = Visibility.Collapsed, Background = Ui.Brush("#0A0E11") };
        bvLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bvLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // the bar on top
        var bar = new Grid { Background = Ui.Brush("#11181D"), Height = 58 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
        Func<string, string, Action, Button> tool = (glyph, tip, act) => {
            var b = new Button { Style = (Style)W.FindResource("Btn"), Width = 40, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip,
                Content = new TextBlock { Text = glyph, FontFamily = Ui.Mdl, FontSize = 15 } };
            b.Click += (s, e) => act();
            return b;
        };
        nav.Children.Add(tool("", "Back (B or LB)", () => { if (bv != null && bv.CanGoBack) bv.GoBack(); }));
        nav.Children.Add(tool("", "Forward (RB)", () => { if (bv != null && bv.CanGoForward) bv.GoForward(); }));
        nav.Children.Add(tool("", "Reload", () => { if (bv != null) bv.Reload(); }));
        bar.Children.Add(nav);
        var where = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        bvTitle = new TextBlock { FontSize = 14, FontFamily = Ui.Semi, Foreground = Ui.Brush(Ui.Text), TextTrimming = TextTrimming.CharacterEllipsis };
        bvUrl = new TextBlock { FontSize = 11.5, Foreground = Ui.Brush(Ui.Muted), TextTrimming = TextTrimming.CharacterEllipsis };
        where.Children.Add(bvTitle); where.Children.Add(bvUrl);
        where.MouseLeftButtonUp += (s, e) => AskAddress();
        Grid.SetColumn(where, 1); bar.Children.Add(where);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 14, 0) };
        var go = MakeButton("Address", 40); go.Margin = new Thickness(0, 0, 8, 0); go.Click += (s, e) => AskAddress();
        var close = MakeButton("Close", 40); close.Click += (s, e) => CloseBrowserView();
        right.Children.Add(go); right.Children.Add(close);
        Grid.SetColumn(right, 2); bar.Children.Add(right);
        bvLayer.Children.Add(bar);
        bvHost = new Border();
        Grid.SetRow(bvHost, 1); bvLayer.Children.Add(bvHost);
        bvLayer.LayoutTransform = N<Grid>("Main").LayoutTransform;
        N<Grid>("Clipped").Children.Add(bvLayer);
    }
    void OpenBrowserView(string url) {
        if (bvLayer == null) BuildBrowserView();
        if (bv == null) {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"RecompLauncher\browser");
            bv = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = data }, DefaultBackgroundColor = System.Drawing.Color.FromArgb(10, 14, 17) };
            bv.CoreWebView2InitializationCompleted += (s, e) => {
                if (!e.IsSuccess) { SetStep("The browser didn't start: " + (e.InitializationException != null ? e.InitializationException.Message : "?"), Ui.Red); return; }
                bv.CoreWebView2.DocumentTitleChanged += (s2, e2) => ShowBrowserPlace();
                bv.CoreWebView2.SourceChanged += (s2, e2) => ShowBrowserPlace();
                // links that would open a new window open here instead
                bv.CoreWebView2.NewWindowRequested += (s2, e2) => { e2.Handled = true; bv.CoreWebView2.Navigate(e2.Uri); };
            };
            bvHost.Child = bv;
        }
        bvTitle.Text = "Loading..."; bvUrl.Text = url;
        bvLayer.Visibility = Visibility.Visible;
        bv.Source = new Uri(url);
        bv.Focus();
        Sound.Play(Sfx.Launch);
        HideRing(); SetHints();
        SetStep("Browser: left stick points, A clicks, B goes back, Menu types an address. The Xbox / PS / Home button opens the menu.", Ui.Teal);
    }
    void ShowBrowserPlace() {
        if (bv == null || bv.CoreWebView2 == null) return;
        bvTitle.Text = string.IsNullOrEmpty(bv.CoreWebView2.DocumentTitle) ? "Browser" : bv.CoreWebView2.DocumentTitle;
        bvUrl.Text = bv.CoreWebView2.Source;
    }
    // closed completely: the page, its sound and its video stop
    void CloseBrowserView() {
        if (!BrowserViewOpen) return;
        bvLayer.Visibility = Visibility.Collapsed;
        if (bv != null) { bvHost.Child = null; try { bv.Dispose(); } catch { } bv = null; }
        Sound.Play(Sfx.Back);
        SetStep("Closed the browser.", Ui.Muted);
        SetHints();
    }
    // an address or a search, typed (the on-screen keyboard works with the controller)
    void AskAddress() {
        if (!BrowserViewOpen) return;
        // the page is its own window inside the launcher's and would cover the keyboard: it steps aside meanwhile
        bvHost.Visibility = Visibility.Hidden;
        Action back = () => { bvHost.Visibility = Visibility.Visible; if (bv != null) bv.Focus(); };
        OpenOsk("Address or search", "", null, text => {
            back();
            text = (text ?? "").Trim();
            if (text.Length == 0 || bv == null) return;
            string url = text.Contains("://") ? text
                : !text.Contains(" ") && text.Contains(".") ? "https://" + text
                : "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
            bv.Source = new Uri(url);
        }, back);
    }
    void BrowserBack() { if (bv != null && bv.CanGoBack) bv.GoBack(); }
    void BrowserForward() { if (bv != null && bv.CanGoForward) bv.GoForward(); }
    void FocusBrowserView() { if (bv != null) bv.Focus(); }
}
}
