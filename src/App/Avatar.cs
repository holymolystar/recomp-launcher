// A character for the creator (Creator.cs) and the profile pictures: every part, colour and slider is listed once in
// AvatarSpec (which tab it's on, its choices, its range), and an AvatarDef holds one value per key. Stored in the
// profile as "v2|key=value;key=value": keys it doesn't know are ignored and missing keys get their default, so
// characters keep working as parts are added. Characters from the first, simpler maker ("0,1,1,...") are read too.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RecompLauncher {

enum SpecKind { Part, Colour, Slider }

class AvatarItem {
    public string Key, Label; public SpecKind Kind;
    public string[] Names;       // parts
    public string[] Palette;     // colours
    public double Min, Max, Def; // sliders (and the default part)
    public string DefColour;
    public int Thumb;            // parts: 0 the head, 1 the whole body, 2 the picture with its background, 3 the middle of the face
    public bool Percent;         // sliders: shown as a percentage (else as an offset)
}
class AvatarTab { public string Name, Glyph; public bool Zoom; public List<AvatarItem> Items = new List<AvatarItem>(); }

static class AvatarSpec {
    public static readonly string[] Skin = { "#ffe3cc", "#f9d4b4", "#f5c9a3", "#eebb8e", "#e3a87c", "#d49465", "#c68456", "#a96f45", "#8d5a3b", "#744830", "#5c3a26", "#45291a", "#a8e0a0", "#a9cdf5", "#d6b4f0", "#f5a9c8" };
    public static readonly string[] Hair = { "#111111", "#2b2118", "#3b2a20", "#5a3825", "#7a4a2a", "#a0652f", "#c27c3e", "#d9a15b", "#e6c36a", "#f2dc9b", "#9a9a9a", "#dcdcdc", "#ffffff", "#b0322a", "#e0503c", "#ff8a3d", "#e06ab8", "#ff9ad5", "#8a57d6", "#6b3fa0", "#4a7be0", "#1f3f8a", "#2ac0c8", "#47b26b" };
    public static readonly string[] Eye = { "#2a2230", "#3b2a20", "#6b4a2e", "#8a6a3a", "#b07850", "#2f7d4a", "#5aa36b", "#18a3a3", "#2f6fe0", "#5fb3f0", "#7f8c99", "#4a4a4a", "#8a57d6", "#c43c8a", "#d6455b", "#e6a23c" };
    public static readonly string[] Lip = { "#8c2f3c", "#6e2a3a", "#b2404f", "#d65a6a", "#e88a94", "#c96f6f", "#9b4a3f", "#4a2a2a", "#e06ab8", "#8a57d6" };
    public static readonly string[] Colour = {
        "#f0453a", "#b0322a", "#ff7f50", "#f4a312", "#c98f12", "#f2d24b", "#fff3b8", "#47b26b", "#6b8e23", "#1e7a5a", "#d5f2de",
        "#0ab4dc", "#18a3a3", "#cff3fb", "#2f6fe0", "#1f3f8a", "#d6e3ff", "#8a57d6", "#5c2e91", "#e6dbff", "#e06ab8", "#a0526d",
        "#ffd9ee", "#ffd6d1", "#ffe8c2", "#8b5a2b", "#c2b280", "#f7f7f7", "#eeeeee", "#7f8c99", "#4a4a4a", "#2d2d2d", "#14213d", "#111111" };

    public static readonly string[] Moods = { "Your face", "Happy", "Laughing", "Surprised", "Wink", "Cool", "Sleepy", "Angry", "Sad" };
    public static readonly string[] Poses = { "Stand", "Wave", "Cheer", "Hands on hips", "Point", "Thinking", "Shrug" };

    public static readonly List<AvatarTab> Tabs = new List<AvatarTab>();
    public static readonly Dictionary<string, AvatarItem> ByKey = new Dictionary<string, AvatarItem>();

    static AvatarTab tab;
    static void Tab(string name, string glyph, bool zoom) { tab = new AvatarTab { Name = name, Glyph = glyph, Zoom = zoom }; Tabs.Add(tab); }
    static void Add(AvatarItem it) { tab.Items.Add(it); if (!ByKey.ContainsKey(it.Key)) ByKey[it.Key] = it; }
    static void Part(string key, string label, int thumb, int def, params string[] names) { Add(new AvatarItem { Key = key, Label = label, Kind = SpecKind.Part, Names = names, Def = def, Thumb = thumb }); }
    static void Col(string key, string label, string[] pal, string def) { Add(new AvatarItem { Key = key, Label = label, Kind = SpecKind.Colour, Palette = pal, DefColour = def }); }
    static void Slide(string key, string label, double min, double max, double def, bool pct) { Add(new AvatarItem { Key = key, Label = label, Kind = SpecKind.Slider, Min = min, Max = max, Def = def, Percent = pct }); }

    static AvatarSpec() {
        Tab("Face", "🙂", true);
        Part("face", "Face shape", 0, 0, "Round", "Oval", "Heart", "Square", "Long", "Wide", "Pear", "Diamond");
        Col("skin", "Skin", Skin, "#f5c9a3");
        Slide("headSize", "Head size", 0.85, 1.15, 1, true);
        Part("ears", "Ears", 0, 0, "Normal", "Small", "Pointy", "Big");
        Part("extra", "Face details", 0, 0, "None", "Freckles", "Rosy cheeks", "Beauty mark", "Bandage", "Face paint", "Star sticker", "Smile lines");

        Tab("Hair", "💇", true);
        Part("hair", "Style", 0, 2, "Bald", "Buzz", "Short", "Spiky", "Swept", "Side part", "Curtains", "Curly", "Afro", "Mohawk", "Pixie", "Bob", "Long", "Wavy",
             "Ponytail", "High ponytail", "Twin tails", "Bun", "Space buns", "Braids", "Messy", "Quiff");
        Col("hairCol", "Colour", Hair, "#5a3825");
        Part("hairFlip", "Parting", 0, 0, "Normal", "Mirrored");

        Tab("Eyes", "👁", true);
        Part("eyes", "Style", 3, 1, "Dots", "Round", "Almond", "Big", "Sparkle", "Sleepy", "Cat", "Wide", "Narrow", "Happy", "Star", "Lashes", "Droopy", "Sharp", "Button", "Heart");
        Col("eyeCol", "Colour", Eye, "#3b2a20");
        Slide("eyeSize", "Size", 0.6, 1.5, 1, true);
        Slide("eyeSpace", "Spacing", -18, 18, 0, false);
        Slide("eyeY", "Height", -20, 20, 0, false);
        Slide("eyeTilt", "Tilt", -20, 20, 0, false);

        Tab("Eyebrows", "🤨", true);
        Part("brows", "Style", 3, 2, "None", "Thin", "Soft", "Thick", "Bushy", "Arched", "Straight", "Angled", "Worried", "Short", "Dots", "Unibrow");
        Col("browCol", "Colour", Hair, "#5a3825");
        Slide("browSize", "Size", 0.6, 1.5, 1, true);
        Slide("browSpace", "Spacing", -15, 15, 0, false);
        Slide("browY", "Height", -15, 15, 0, false);
        Slide("browTilt", "Tilt", -25, 25, 0, false);

        Tab("Nose", "👃", true);
        Part("nose", "Style", 3, 3, "None", "Dot", "Button", "Curve", "Triangle", "Round", "Long", "Pointy", "Wide", "Bump");
        Slide("noseSize", "Size", 0.5, 1.6, 1, true);
        Slide("noseY", "Height", -15, 15, 0, false);

        Tab("Mouth", "👄", true);
        Part("mouth", "Style", 3, 0, "Smile", "Big smile", "Grin", "Laugh", "Small", "Oh", "Flat", "Smirk", "Kitty", "Tongue", "Frown", "Pout", "Fang", "Kiss");
        Col("lipCol", "Lip colour", Lip, "#8c2f3c");
        Slide("mouthSize", "Size", 0.6, 1.5, 1, true);
        Slide("mouthY", "Height", -15, 15, 0, false);

        Tab("Beard", "🧔", true);
        Part("beard", "Style", 0, 0, "None", "Stubble", "Moustache", "Handlebar", "Goatee", "Short beard", "Full beard", "Chin strap", "Soul patch");
        Col("beardCol", "Colour", Hair, "#5a3825");

        Tab("Body", "🧍", false);
        Part("style", "Style", 1, 0, "Simple", "Detailed");
        Slide("height", "Height", 0.85, 1.2, 1, true);
        Slide("build", "Build", 0.8, 1.3, 1, true);
        Col("skin", "Skin", Skin, "#f5c9a3");

        Tab("Top", "👕", false);
        Part("top", "Style", 1, 0, "T-shirt", "Long sleeves", "Hoodie", "Jacket", "Sweater", "Tank top", "Shirt", "Jersey", "Dress", "Overalls");
        Col("topCol", "Colour", Colour, "#2f6fe0");
        Col("topCol2", "Second colour", Colour, "#f7f7f7");
        Part("pattern", "Pattern", 1, 0, "Plain", "Stripes", "Dots", "Star", "Heart", "Lightning");

        Tab("Bottoms", "👖", false);
        Part("bottom", "Style", 1, 0, "Jeans", "Shorts", "Skirt", "Joggers", "Cargo", "Leggings", "Short skirt");
        Col("bottomCol", "Colour", Colour, "#1f3f8a");

        Tab("Shoes", "👟", false);
        Part("shoes", "Style", 1, 0, "Sneakers", "High-tops", "Boots", "Sandals", "Slip-ons", "Rain boots", "Barefoot");
        Col("shoeCol", "Colour", Colour, "#f0453a");

        Tab("Hat", "🎩", true);
        Part("hat", "Style", 0, 0, "None", "Cap", "Backwards cap", "Beanie", "Crown", "Headband", "Bow", "Beret", "Cowboy hat", "Top hat", "Wizard hat", "Bunny ears", "Cat ears", "Headphones", "Flower");
        Col("hatCol", "Colour", Colour, "#f0453a");

        Tab("Glasses", "👓", true);
        Part("glasses", "Style", 3, 0, "None", "Round", "Square", "Thin", "Cat-eye", "Shades", "Aviators", "Star", "Goggles", "Monocle");
        Col("glassCol", "Frame colour", Colour, "#2d2d2d");
        Slide("glassSize", "Size", 0.8, 1.3, 1, true);
        Slide("glassY", "Height", -12, 12, 0, false);

        Tab("Extras", "🎒", false);
        Part("neck", "Neck", 1, 0, "None", "Scarf", "Bow tie", "Tie", "Necklace", "Bandana");
        Col("neckCol", "Neck colour", Colour, "#f0453a");
        Part("back", "Back", 1, 0, "None", "Backpack", "Cape", "Wings");
        Col("backCol", "Back colour", Colour, "#f4a312");
        Part("earrings", "Earrings", 0, 0, "None", "Studs", "Hoops", "Stars");
        Col("earCol", "Earring colour", Colour, "#f2d24b");

        Tab("Pose", "💃", false);
        Part("pose", "Pose", 1, 0, Poses);
        Part("mood", "Mood (for your picture)", 2, 0, Moods);

        Tab("Backdrop", "🖼", false);
        Col("bgCol", "Colour", Colour, "#cff3fb");
        Part("bgPattern", "Pattern", 2, 0, "Plain", "Dots", "Stripes", "Stars", "Sunburst", "Checks");
    }
}

class AvatarDef {
    readonly Dictionary<string, double> nums = new Dictionary<string, double>();
    readonly Dictionary<string, string> cols = new Dictionary<string, string>();

    public AvatarDef() {
        foreach (var it in AvatarSpec.ByKey.Values) {
            if (it.Kind == SpecKind.Colour) cols[it.Key] = it.DefColour; else nums[it.Key] = it.Def;
        }
    }
    public int I(string key) {
        var it = AvatarSpec.ByKey[key];
        int n = it.Names.Length, v = (int)Math.Round(nums[key]);
        return ((v % n) + n) % n;
    }
    public double D(string key) { return nums[key]; }
    public string C(string key) { return cols[key]; }
    public void Set(string key, double v) {
        var it = AvatarSpec.ByKey[key];
        if (it.Kind == SpecKind.Slider) v = Math.Max(it.Min, Math.Min(it.Max, v));
        nums[key] = v;
    }
    public void SetColour(string key, string hex) { cols[key] = hex; }
    public AvatarDef Clone() { return Parse(ToString()); }
    public bool Same(AvatarDef o) { return o != null && o.ToString() == ToString(); }

    public override string ToString() {
        var parts = AvatarSpec.ByKey.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k =>
            k + "=" + (cols.ContainsKey(k) ? cols[k] : nums[k].ToString("0.###", CultureInfo.InvariantCulture)));
        return "v2|" + string.Join(";", parts.ToArray());
    }
    public static AvatarDef Parse(string s) {
        var d = new AvatarDef();
        if (string.IsNullOrEmpty(s)) return d;
        if (!s.StartsWith("v2|")) return FromOld(s);
        foreach (var kv in s.Substring(3).Split(';')) {
            int eq = kv.IndexOf('='); if (eq <= 0) continue;
            string k = kv.Substring(0, eq), v = kv.Substring(eq + 1);
            AvatarItem it; if (!AvatarSpec.ByKey.TryGetValue(k, out it)) continue;
            if (it.Kind == SpecKind.Colour) { if (v.StartsWith("#") && v.Length == 7) d.cols[k] = v.ToLowerInvariant(); }
            else { double x; if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) d.Set(k, x); }
        }
        return d;
    }
    // the first maker's thirteen numbers: face, skin, hair, hair colour, eyebrows, eyes, mouth, glasses, facial hair,
    // hat, hat colour, shirt, background
    static AvatarDef FromOld(string s) {
        var d = new AvatarDef();
        var v = s.Split(',').Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).ToArray();
        Func<int, int> at = i => i < v.Length ? v[i] : 0;
        string[] skin = { "#ffe3cc", "#f5c9a3", "#e3a87c", "#c68456", "#8d5a3b", "#5c3a26", "#a8e0a0", "#a9cdf5" };
        string[] hair = { "#2b2118", "#5a3825", "#a0652f", "#e6c36a", "#dcdcdc", "#e0503c", "#4a7be0", "#e06ab8", "#47b26b", "#8a57d6" };
        string[] cloth = { "#f0453a", "#f4a312", "#f2d24b", "#47b26b", "#0ab4dc", "#2f6fe0", "#8a57d6", "#e06ab8", "#f7f7f7", "#2d2d2d" };
        string[] bg = { "#cff3fb", "#ffd6d1", "#ffe8c2", "#fff3b8", "#d5f2de", "#d6e3ff", "#e6dbff", "#ffd9ee", "#eeeeee", "#2d2d2d", "#14213d" };
        Func<string[], int, string> pick = (a, i) => a[((i % a.Length) + a.Length) % a.Length];
        d.Set("face", new[] { 0, 1, 3, 5 }[((at(0) % 4) + 4) % 4]);
        d.SetColour("skin", pick(skin, at(1)));
        d.Set("hair", new[] { 0, 2, 3, 12, 11, 17, 7, 9, 5 }[((at(2) % 9) + 9) % 9]);
        d.SetColour("hairCol", pick(hair, at(3))); d.SetColour("browCol", pick(hair, at(3))); d.SetColour("beardCol", pick(hair, at(3)));
        d.Set("brows", new[] { 0, 1, 3, 7, 8 }[((at(4) % 5) + 5) % 5]);
        int eye = ((at(5) % 7) + 7) % 7;
        d.Set("eyes", new[] { 0, 1, 9, 5, 1, 3, 11 }[eye]);
        if (eye == 4) d.Set("mood", 4);
        d.Set("mouth", new[] { 0, 2, 5, 6, 9, 7, 8 }[((at(6) % 7) + 7) % 7]);
        d.Set("glasses", new[] { 0, 1, 2, 5 }[((at(7) % 4) + 4) % 4]);
        d.Set("beard", new[] { 0, 2, 6, 1 }[((at(8) % 4) + 4) % 4]);
        d.Set("hat", new[] { 0, 1, 3, 4, 5 }[((at(9) % 5) + 5) % 5]);
        d.SetColour("hatCol", pick(cloth, at(10)));
        d.SetColour("topCol", pick(cloth, at(11)));
        d.SetColour("bgCol", pick(bg, at(12)));
        return d;
    }

    // a random character that looks put together: matching brows and beard, extras now and then
    public static AvatarDef Random(Random r) {
        var d = new AvatarDef();
        Func<string[], string> any = a => a[r.Next(a.Length)];
        foreach (var it in AvatarSpec.ByKey.Values) {
            if (it.Kind == SpecKind.Part) d.Set(it.Key, r.Next(it.Names.Length));
            else if (it.Kind == SpecKind.Colour) d.SetColour(it.Key, any(it.Palette));
            else d.Set(it.Key, it.Def + (r.NextDouble() - 0.5) * (it.Max - it.Min) * 0.35);
        }
        d.SetColour("skin", AvatarSpec.Skin[r.Next(r.Next(5) == 0 ? 16 : 12)]);
        d.SetColour("browCol", d.C("hairCol")); d.SetColour("beardCol", d.C("hairCol"));
        if (r.Next(3) > 0) d.Set("beard", 0);
        if (r.Next(3) > 0) d.Set("glasses", 0);
        if (r.Next(2) > 0) d.Set("hat", 0);
        if (r.Next(3) > 0) d.Set("neck", 0);
        if (r.Next(3) > 0) d.Set("back", 0);
        if (r.Next(3) > 0) d.Set("earrings", 0);
        if (r.Next(2) > 0) d.Set("extra", 0);
        if (r.Next(2) > 0) d.Set("pattern", 0);
        if (r.Next(2) > 0) d.Set("bgPattern", 0);
        d.Set("hairFlip", r.Next(2));
        d.Set("mood", r.Next(3) == 0 ? r.Next(AvatarSpec.Moods.Length) : 0);
        d.SetColour("eyeCol", AvatarSpec.Eye[r.Next(r.Next(3) == 0 ? 16 : 7)]);
        d.SetColour("lipCol", AvatarSpec.Lip[r.Next(5)]);
        return d;
    }
}
}
