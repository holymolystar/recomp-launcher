// Profiles: several players, each with a picture, a name and their own saves. Kept in profiles\profiles.json.
//
// Saves: a game's save folders (the folders in its catalog "Saves" list; the files there, like .ini and .json, are
// its settings and stay shared) hold one profile's saves at a time. Live says whose; a game with no entry holds the
// saves of the Original profile (the saves that were there before profiles existed). Before a game starts for
// another profile, PrepareSaves:
//   1. copies each live save folder into the owner's store (profiles\<owner>\saves\<game>\<folder>), replacing the
//      older copy there only once the new copy is complete;
//   2. moves the live folder aside, copies the player's own saves (or nothing, for a fresh start) into its place,
//      and only then deletes the folder that was moved aside. If anything fails, the folder moved aside goes back.
// A deleted profile only leaves the list: its folder stays in profiles\, so no save is ever lost.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RecompLauncher {

class PlayerProfile {
    public string Id, Name, Avatar, Frame, Character;
    public List<string> Favs = new List<string>();                                       // favourite games, by id
    public List<KeyValuePair<string, List<string>>> Collections = new List<KeyValuePair<string, List<string>>>();   // name, game ids
}

static class Profiles {
    public static readonly List<PlayerProfile> All = new List<PlayerProfile>();
    public static PlayerProfile Current;
    static string original;
    static readonly Dictionary<string, string> live = new Dictionary<string, string>();
    public static string Dir { get { return Path.Combine(Paths.Root, "profiles"); } }
    static string FilePath { get { return Path.Combine(Dir, "profiles.json"); } }

    public static void Load() {
        All.Clear(); live.Clear();
        var d = Json.ReadFile(FilePath);
        if (d != null) {
            var list = d.ContainsKey("Profiles") ? d["Profiles"] as System.Collections.IEnumerable : null;
            if (list != null)
                foreach (var o in list) {
                    var p = o as Dictionary<string, object>; if (p == null) continue;
                    var pp = new PlayerProfile { Id = Json.Str(p, "Id"), Name = Json.Str(p, "Name") ?? "", Avatar = Json.Str(p, "Avatar") ?? "", Frame = Json.Str(p, "Frame") ?? "", Character = Json.Str(p, "Character") ?? "" };
                    pp.Favs.AddRange(Json.Strings(p, "Favs"));
                    object cols;
                    if (p.TryGetValue("Collections", out cols) && cols is System.Collections.IEnumerable)
                        foreach (var c in ((System.Collections.IEnumerable)cols).OfType<Dictionary<string, object>>())
                            if (Json.Str(c, "Name") != null) pp.Collections.Add(new KeyValuePair<string, List<string>>(Json.Str(c, "Name"), Json.Strings(c, "Games").ToList()));
                    All.Add(pp);
                }
            original = Json.Str(d, "Original");
            var l = Json.Obj(d, "Live");
            if (l != null) foreach (var kv in l) live[kv.Key] = Convert.ToString(kv.Value);
            Current = All.FirstOrDefault(p => p.Id == Json.Str(d, "Current"));
        }
        if (All.Count == 0) {
            // the first time: one profile from the picture and name set before profiles existed; it owns the saves
            All.Add(new PlayerProfile { Id = "p1", Name = Settings.Get("PlayerName"), Avatar = Settings.Get("Avatar"), Frame = Settings.Get("AvatarFrame"), Character = Settings.Get("Character") });
            original = "p1";
        }
        if (original == null) original = All[0].Id;
        if (Current == null) Current = All[0];
        Playtime.Use(PlaytimeFile(Current));
        Save();
    }
    // play time is kept per profile: the first profile keeps playtime.json (the time played before profiles), the
    // others profiles\<id>\playtime.json
    public static string PlaytimeFile(PlayerProfile p) {
        if (p.Id == original) return Path.Combine(Paths.Root, "playtime.json");
        string dir = Path.Combine(Dir, p.Id);
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "playtime.json");
    }
    public static bool IsFav(string id) { return Current.Favs.Contains(id); }
    public static void ToggleFav(string id) { if (!Current.Favs.Remove(id)) Current.Favs.Add(id); Save(); }
    public static List<string> CollectionNames() { return Current.Collections.Select(c => c.Key).ToList(); }
    public static List<string> InCollection(string name) { var c = Current.Collections.FirstOrDefault(x => x.Key == name); return c.Value ?? new List<string>(); }
    // puts a game in a collection or takes it out; a collection with no games left goes away
    public static void SetInCollection(string name, string id, bool on) {
        int i = Current.Collections.FindIndex(x => x.Key == name);
        if (i < 0) { if (!on) return; Current.Collections.Add(new KeyValuePair<string, List<string>>(name, new List<string>())); i = Current.Collections.Count - 1; }
        var games = Current.Collections[i].Value;
        if (on) { if (!games.Contains(id)) games.Add(id); } else games.Remove(id);
        if (games.Count == 0) Current.Collections.RemoveAt(i);
        Save();
    }
    public static void Save() {
        try {
            Directory.CreateDirectory(Dir);
            Json.WriteFile(FilePath, new Dictionary<string, object> {
                { "Profiles", All.Select(p => new Dictionary<string, object> { { "Id", p.Id }, { "Name", p.Name }, { "Avatar", p.Avatar }, { "Frame", p.Frame }, { "Character", p.Character },
                    { "Favs", p.Favs.ToList() }, { "Collections", p.Collections.Select(c => (object)new Dictionary<string, object> { { "Name", c.Key }, { "Games", c.Value.ToList() } }).ToList() } }).ToList() },
                { "Current", Current.Id }, { "Original", original }, { "Live", live.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
            });
        } catch { }
    }

    // the current profile's picture settings, by their old Settings names
    public static string Get(string key) {
        var p = Current;
        switch (key) {
            case "PlayerName": return p.Name ?? "";
            case "Avatar": return p.Avatar ?? "";
            case "AvatarFrame": return p.Frame ?? "";
            case "Character": return p.Character ?? "";
        }
        return "";
    }
    public static void Set(string key, string value) {
        value = value ?? "";
        switch (key) {
            case "PlayerName": Current.Name = value; break;
            case "Avatar": Current.Avatar = value; break;
            case "AvatarFrame": Current.Frame = value; break;
            case "Character": Current.Character = value; break;
        }
    }
    public static string DisplayName(PlayerProfile p) { return string.IsNullOrEmpty(p.Name) ? "Player " + (All.IndexOf(p) + 1) : p.Name; }

    public static PlayerProfile Add(Random r) {
        int n = 1; while (All.Any(p => p.Id == "p" + n) || Directory.Exists(Path.Combine(Dir, "p" + n))) n++;
        var np = new PlayerProfile { Id = "p" + n, Name = "", Avatar = "char", Frame = "", Character = AvatarDef.Random(r).ToString() };
        All.Add(np); Save();
        return np;
    }
    public static void Remove(PlayerProfile p) {
        if (All.Count < 2) return;
        All.Remove(p);
        if (Current == p) Current = All[0];
        Save();
    }
    public static void Switch(PlayerProfile p) { Current = p; Playtime.Use(PlaytimeFile(p)); Save(); }

    // ---------- saves ----------
    // (catalog entry, full path) of each save folder of a game
    static List<string[]> SaveDirs(Game g) {
        var r = new List<string[]>();
        if (string.IsNullOrEmpty(g.Saves) || string.IsNullOrEmpty(g.Dir)) return r;
        foreach (var e in g.Saves.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0)) {
            string full = Paths.Expand(e);
            if (!Path.IsPathRooted(full)) full = Path.Combine(g.Dir, full);
            // folders are saves; files (.ini, .json...) are the game's settings and stay shared
            bool isDir = Directory.Exists(full) || (!File.Exists(full) && Path.GetExtension(full).Length == 0);
            if (isDir) r.Add(new[] { e, full });
        }
        return r;
    }
    public static bool Separate(Game g) { return SaveDirs(g).Count > 0; }
    static string Store(string profile, Game g, string entry) {
        string safe = new string(entry.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '%' ? '_' : c).ToArray());
        return Path.Combine(Dir, profile, "saves", g.Id, safe);
    }
    // puts the current profile's saves in place for a game; throws (with everything as it was) if it can't
    public static void PrepareSaves(Game g) {
        string owner;
        if (!live.TryGetValue(g.Id, out owner)) owner = original;
        if (owner == Current.Id) return;
        var dirs = SaveDirs(g);
        if (dirs.Count == 0) return;
        // 1. the live saves go to their owner's store
        foreach (var d in dirs) {
            if (!Directory.Exists(d[1])) continue;
            string store = Store(owner, g, d[0]), fresh = store + ".new", old = store + ".old";
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            CopyDir(d[1], fresh);
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(store)) Directory.Move(store, old);
            Directory.Move(fresh, store);
            if (Directory.Exists(old)) Directory.Delete(old, true);
        }
        // 2. the player's own saves go in their place
        // all or nothing: every folder is moved aside first, and they all come back if any copy fails
        var moved = new List<string[]>();
        try {
            foreach (var d in dirs) {
                string aside = d[1] + ".recomp-swap";
                if (Directory.Exists(aside)) Directory.Delete(aside, true);
                if (Directory.Exists(d[1])) { Directory.Move(d[1], aside); moved.Add(new[] { d[1], aside }); }
            }
            foreach (var d in dirs) {
                string mine = Store(Current.Id, g, d[0]);
                if (Directory.Exists(mine)) CopyDir(mine, d[1]); else Directory.CreateDirectory(d[1]);
            }
        } catch {
            // what was copied in goes (folders that weren't there before included), what was moved aside comes back
            foreach (var d in dirs) try { if (Directory.Exists(d[1])) Directory.Delete(d[1], true); } catch { }
            foreach (var m in moved) try { if (!Directory.Exists(m[0])) Directory.Move(m[1], m[0]); } catch { }
            throw;
        }
        foreach (var m in moved) try { Directory.Delete(m[1], true); } catch { }
        live[g.Id] = Current.Id;
        Save();
    }
    static void CopyDir(string from, string to) {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var sub in Directory.GetDirectories(from)) CopyDir(sub, Path.Combine(to, Path.GetFileName(sub)));
    }
}
}
