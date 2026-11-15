// Weather by the clock on Home: the temperature and a little sign for the weather now, with today's high and low
// when you point at it. Settings > General > Weather: your town (empty: no weather, nothing is looked up) and °C or
// °F. From Open-Meteo (free, no account): the town is looked up once, then the weather every 30 minutes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RecompLauncher {

partial class Shell {
    TextBlock homeWeather; DispatcherTimer weatherTimer; TextBox weatherBox;
    static string WeatherSign(int code) {
        if (code == 0) return "☀";                      // clear
        if (code <= 2) return "⛅";                      // a few clouds
        if (code == 3) return "☁";                      // cloudy
        if (code == 45 || code == 48) return "\U0001F32B";   // fog
        if (code >= 95) return "⛈";                     // thunder
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "❄";   // snow
        return "\U0001F327";                                 // rain and drizzle
    }
    static string WeatherWords(int code) {
        if (code == 0) return "Clear"; if (code <= 2) return "Partly cloudy"; if (code == 3) return "Cloudy"; if (code == 45 || code == 48) return "Fog";
        if (code >= 95) return "Thunderstorms"; if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "Snow"; if (code >= 80) return "Showers"; return "Rain";
    }
    void StartWeather() {
        if (homeWeather == null) return;
        if (weatherTimer == null) {
            weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            weatherTimer.Tick += (s, e) => FetchWeather();
            weatherTimer.Start();
        }
        FetchWeather();
    }
    void FetchWeather() {
        string town = Settings.Get("WeatherCity").Trim(); bool f = Settings.Get("WeatherUnit") == "f";
        if (town.Length == 0) { homeWeather.Visibility = Visibility.Collapsed; return; }
        ThreadPool.QueueUserWorkItem(_ => {
            string text = null, tip = null;
            try {
                using (var wc = new WebClient()) {
                    wc.Encoding = System.Text.Encoding.UTF8;
                    var geo = Json.Parse(wc.DownloadString("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + Uri.EscapeDataString(town))) as Dictionary<string, object>;
                    object arr;
                    if (geo != null && geo.TryGetValue("results", out arr) && arr is object[] && ((object[])arr).Length > 0) {
                        var place = (Dictionary<string, object>)((object[])arr)[0];
                        string lat = Json.Num(place, "latitude").ToString(CultureInfo.InvariantCulture), lon = Json.Num(place, "longitude").ToString(CultureInfo.InvariantCulture);
                        var w = Json.Parse(wc.DownloadString("https://api.open-meteo.com/v1/forecast?current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto"
                            + (f ? "&temperature_unit=fahrenheit" : "") + "&latitude=" + lat + "&longitude=" + lon)) as Dictionary<string, object>;
                        var cur = w != null ? Json.Obj(w, "current") : null;
                        if (cur != null) {
                            int code = (int)Json.Num(cur, "weather_code");
                            text = WeatherSign(code) + "  " + Math.Round(Json.Num(cur, "temperature_2m")) + "°";
                            var daily = Json.Obj(w, "daily"); string hi = "", lo = "";
                            object a1, a2;
                            if (daily != null && daily.TryGetValue("temperature_2m_max", out a1) && a1 is object[] && daily.TryGetValue("temperature_2m_min", out a2) && a2 is object[]) {
                                hi = Math.Round(Convert.ToDouble(((object[])a1)[0], CultureInfo.InvariantCulture)) + "°"; lo = Math.Round(Convert.ToDouble(((object[])a2)[0], CultureInfo.InvariantCulture)) + "°";
                            }
                            tip = Json.Str(place, "name") + ": " + WeatherWords(code) + (hi.Length > 0 ? ", today " + lo + " to " + hi : "");
                        }
                    }
                }
            } catch { }
            W.Dispatcher.BeginInvoke(new Action(() => {
                homeWeather.Visibility = text != null ? Visibility.Visible : Visibility.Collapsed;
                if (text != null) { homeWeather.Text = text; homeWeather.ToolTip = tip; }
            }));
        });
    }
}
}
