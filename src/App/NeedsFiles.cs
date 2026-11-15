// "Needs files": an installed game that still lacks what only the player can give it (a ROM, a disc, a dump, a build
// made from them) gets a red badge on its tile. Each game says what to look for in games.json "Ready" (paths in its
// folder, ";" between choices, wildcards allowed, a folder counts too); a game whose ROM the launcher copies in
// (Rom.CopyAs) or whose folders it copies (Rom.CopyIn) needs no Ready. Games that keep your files elsewhere have no
// badge, since the launcher can't see them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RecompLauncher {

partial class Shell {
    // checked at most every few seconds per game: the tiles are refreshed often
    readonly Dictionary<string, Tuple<DateTime, bool>> needsSeen = new Dictionary<string, Tuple<DateTime, bool>>();
    bool NeedsFiles(Game g) {
        if (g.App != null || g.ByStore || g.Inst == null || g.IsBusy || string.IsNullOrEmpty(g.Dir)) return false;
        Tuple<DateTime, bool> seen;
        if (needsSeen.TryGetValue(g.Id, out seen) && (DateTime.Now - seen.Item1).TotalSeconds < 5) return seen.Item2;
        bool needs = false;
        var want = new List<string>();
        if (!string.IsNullOrEmpty(g.Ready)) want.AddRange(g.Ready.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0));
        else if (!string.IsNullOrEmpty(g.Rom.CopyAs)) want.Add(g.Rom.CopyAs);
        else if (g.Rom.CopyIn.Length > 0) want.Add(g.Rom.CopyIn[0]);
        if (want.Count > 0) needs = !want.Any(w => Found(g.Dir, w));
        needsSeen[g.Id] = Tuple.Create(DateTime.Now, needs);
        return needs;
    }
    static bool Found(string dir, string rel) {
        try {
            string p = Path.Combine(dir, Paths.Expand(rel));
            if (p.IndexOfAny(new[] { '*', '?' }) < 0) return File.Exists(p) || Directory.Exists(p);
            if (Engine.Glob(p) != null) return true;
            // a wildcard folder: any folder that matches
            string parent = Path.GetDirectoryName(p), name = Path.GetFileName(p);
            return Directory.Exists(parent) && Directory.GetDirectories(parent, name).Length > 0;
        } catch { return false; }
    }
}
}
