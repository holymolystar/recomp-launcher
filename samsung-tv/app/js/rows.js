// Which rows a page has, the way the launcher builds them (TvApp.cs, TvBrowse.cs, TvGenres.cs, TvDiscover.cs):
//   For You  Continue watching, My list, last watched channels, Top 10 new films, New, Recommended for you,
//            Because you watched..., Explore by decade, Series, Live TV, the biggest categories
//   Movies / Shows  Continue watching, My list, the categories, Top 10, Because you watched..., Recently added,
//            a row per genre, then every category of the provider
// A row: {title, items, wide (channels), top10, chips (category buttons: {name, open})}.
var Rows = (function () {
  var GENRES = [
    ['action', 'Action films', 'Action series'], ['comedy', 'Comedies', 'Comedy series'], ['thriller', 'Thrillers', 'Thriller series'],
    ['scifi', 'Sci-fi & fantasy', 'Sci-fi & fantasy series'], ['horror', 'Horror films', 'Horror series'], ['drama', 'Dramas', 'Drama series'],
    ['crime', 'Crime films', 'Crime series'], ['romance', 'Romantic films', 'Romantic series'], ['animation', 'Animated films', 'Animated series'],
    ['family', 'Family films', 'Kids & family'], ['adventure', 'Adventures', 'Adventure series'], ['documentary', 'Documentaries', 'Docuseries'],
    ['mystery', 'Mysteries', 'Mystery series'], ['reality', 'Reality films', 'Reality TV'], ['war', 'War films', 'War series'],
    ['history', 'History films', 'History series'], ['western', 'Westerns', 'Western series'], ['music', 'Music films', 'Music series']];

  // a genre's name as Cinemeta or a provider writes it, as the ones above
  function canon(g) {
    g = (g || '').trim().toLowerCase(); var r = [];
    if (!g) return r;
    if (g.indexOf('action') >= 0) r.push('action');
    if (g.indexOf('adventure') >= 0 || g.indexOf('avontuur') >= 0) r.push('adventure');
    if (/comed|komedie|sitcom/.test(g)) r.push('comedy');
    if (g.indexOf('thrill') >= 0) r.push('thriller');
    if (/sci|fantas/.test(g)) r.push('scifi');
    if (g.indexOf('horror') >= 0) r.push('horror');
    if (g.indexOf('drama') >= 0) r.push('drama');
    if (/crime|misdaad/.test(g)) r.push('crime');
    if (/romanc|romant/.test(g)) r.push('romance');
    if (/anim|cartoon|tekenfilm/.test(g)) r.push('animation');
    if (/famil|kids|kinder|children|disney/.test(g)) r.push('family');
    if (g.indexOf('docu') >= 0) r.push('documentary');
    if (g.indexOf('myster') >= 0) r.push('mystery');
    if (g.indexOf('reality') >= 0) r.push('reality');
    if (/\bwar\b|oorlog/.test(g)) r.push('war');
    if (/histor|biograph/.test(g)) r.push('history');
    if (g.indexOf('western') >= 0) r.push('western');
    if (/music|muziek/.test(g)) r.push('music');
    return r;
  }
  // a title's genres: the provider's, else Cinemeta's (what's been looked up), else its category's name
  function genresOf(x) {
    if (x._g && (x._g.length || x._gv === Meta.version)) return x._g;
    if (!x.genre) Meta.apply(x);
    var l = [];
    (x.genre || '').split(/[,\/;|&]/).forEach(function (g) { l = l.concat(canon(g)); });
    if (!l.length) l = canon(x.cat);
    var u = []; l.forEach(function (g) { if (u.indexOf(g) < 0) u.push(g); });
    x._gv = Meta.version; return (x._g = u);
  }
  // the same film or series, however the provider names it (HD, 4K, another language)
  function same(x) { return x._same || (x._same = Meta.squash(Meta.cleanTitle(x.title).title)); }
  function decade(x) { var m = /(19|20)\d\d/.exec(x.year || '') || /\b(19[2-9]\d|20[0-4]\d)\b/.exec(x.title || ''); return m ? (Math.floor(Number(m[0]) / 10) * 10) + 's' : null; }
  function uniq(l) { var seen = {}; return l.filter(function (x) { var k = same(x); if (seen[k]) return false; seen[k] = 1; return true; }); }

  function row(title, items, opt) { var r = { title: title, items: items }; if (opt) for (var k in opt) r[k] = opt[k]; return r; }

  // a row per genre (at least 8 titles, up to 60)
  function genreRows(list, series) {
    var by = {};
    list.forEach(function (x) { genresOf(x).forEach(function (g) { var l = by[g] = by[g] || { items: [], seen: {} }; var k = same(x); if (l.items.length < 60 && !l.seen[k]) { l.seen[k] = 1; l.items.push(x); } }); });
    return GENRES.filter(function (d) { return by[d[0]] && by[d[0]].items.length >= 8; }).map(function (d) { return row(series ? d[2] : d[1], by[d[0]].items); });
  }

  // "Because you watched ...": titles like what you watched last (and then like what's in My list: "More like ...")
  function becauseRows(pool, kindOk, max) {
    var rows = [], byKey = {}, index = new Map();
    pool.forEach(function (x, i) { if (!byKey[x.key]) byKey[x.key] = x; if (!index.has(x)) index.set(x, i); });
    // an episode counts as its series
    var asSeries = function (k) { var it = Store.remembered(k); return it && it.kind === 'episode' ? 's:' + it.seriesId : k; };
    var cont = Store.continuing().map(function (c) { return asSeries(c.key); });
    var watched = cont.concat(Store.watched.slice().reverse().map(asSeries));
    var seeds = watched.map(function (k) { return [k, true]; }).concat(Store.favs.map(function (k) { return [k, false]; }));
    var skip = {}; watched.concat(Store.favs).forEach(function (k) { skip[k] = 1; });
    var used = {};
    for (var s = 0; s < seeds.length && rows.length < max; s++) {
      var seed = byKey[seeds[s][0]];
      if (!seed || !kindOk(seed) || used[seed.key]) continue;
      used[seed.key] = 1;
      var sg = genresOf(seed), dec = decade(seed), seedSame = same(seed);
      var like = pool.filter(function (x) { return kindOk(x) && !skip[x.key] && !used[x.key] && x.key !== seed.key; }).map(function (x) {
        var shared = sg.length ? genresOf(x).filter(function (g) { return sg.indexOf(g) >= 0; }).length : 0;
        return { x: x, shared: shared, score: shared * 3 + (x.cat && x.cat === seed.cat ? 1 : 0) + (dec && decade(x) === dec ? 1 : 0) };
      }).filter(function (z) { return sg.length ? z.shared > 0 : z.score > 0; })
        .sort(function (a, b) { return b.score - a.score || index.get(a.x) - index.get(b.x); })
        .map(function (z) { return z.x; });
      like = uniq(like).filter(function (x) { return same(x) !== seedSame; }).slice(0, 20);
      if (like.length < 6) continue;
      like.forEach(function (x) { used[x.key] = 1; });
      rows.push(row((seeds[s][1] ? 'Because you watched ' : 'More like ') + Meta.cleanTitle(seed.title).title, like));
    }
    return rows;
  }

  // "Recommended for you": titles sharing the most genres with what you watched and liked
  function recommended(pool) {
    var keys = {}; Store.favs.concat(Store.watched).concat(Store.continuing().map(function (c) { return c.key; })).forEach(function (k) { keys[k] = 1; });
    var weight = {}, any = false;
    pool.forEach(function (x) { if (keys[x.key]) { any = true; genresOf(x).forEach(function (g) { weight[g] = (weight[g] || 0) + 1; }); } });
    if (!any) return [];
    return uniq(pool.filter(function (x) { return !keys[x.key]; }).map(function (x) { return { x: x, s: genresOf(x).reduce(function (n, g) { return n + (weight[g] || 0); }, 0) }; })
      .filter(function (z) { return z.s > 0; }).sort(function (a, b) { return b.s - a.s; }).map(function (z) { return z.x; })).slice(0, 30);
  }

  // Continue watching: films and episodes you're in the middle of (an episode shows as itself, with its series' picture)
  function continuing(kinds) {
    var D = App.data;
    return Store.continuing().map(function (c) {
      var it = D.byKey[c.key] || Store.remembered(c.key);
      if (!it) return null;
      if (!it.key) it.key = c.key;
      return it;
    }).filter(function (it) { return it && (!kinds || kinds.indexOf(it.kind) >= 0) && Tidy.ok(it); }).slice(0, 30);
  }
  function mine(kinds) {
    var D = App.data;
    return Store.favs.map(function (k) { var it = D.byKey[k] || Store.remembered(k); if (it && !it.key) it.key = k; return it; })
      .filter(function (it) { return it && (!kinds || kinds.indexOf(it.kind) >= 0) && Tidy.ok(it); });
  }
  function groups(list) {
    var by = {}, order = [];
    list.forEach(function (x) { var g = x.cat || 'Other'; if (!by[g]) { by[g] = []; order.push(g); } by[g].push(x); });
    return order.map(function (g) { return { name: g, items: by[g] }; });
  }

  function home() {
    var D = App.data, rows = [], films = Tidy.filter(D.movies), shows = Tidy.filter(D.series);
    var cont = continuing(); if (cont.length) rows.push(row('Continue watching', cont));
    var my = mine(); if (my.length) rows.push(row('My list', my.slice(0, 60), { wide: my.every(function (x) { return x.kind === 'live'; }) }));
    var lastCh = Store.recent.map(function (k) { return D.live.byKey[k]; }).filter(Boolean).filter(Tidy.ok);
    if (lastCh.length) rows.push(row('Last watched channels', lastCh, { wide: true }));
    if (films.length >= 10) rows.push(row('Top 10 new films', films.slice(0, 10), { top10: true }));
    if (films.length > 10) rows.push(row('New on ' + App.providerName(), films.slice(10, 50)));
    var wk = uniq(Extras.newThisWeek(films.concat(shows))); if (wk.length >= 4) rows.push(row('New this week', wk));
    var cols = Extras.collections(films, 20); if (cols.length >= 2) rows.push(row('Collections', cols));
    var rec = recommended(films.concat(shows)); if (rec.length) rows.push(row('Recommended for you', rec));
    rows = rows.concat(becauseRows(films.concat(shows), function (x) { return x.kind === 'movie' || x.kind === 'series'; }, 2));
    var decs = {}; films.forEach(function (x) { var d = decade(x); if (d) (decs[d] = decs[d] || []).push(x); });
    var dk = Object.keys(decs).filter(function (d) { return decs[d].length >= 3; }).sort().reverse();
    if (dk.length) rows.push(row('Explore by decade', [], { chips: dk.map(function (d) { return { name: d, open: function () { App.openCategory(d + ' films', decs[d]); } }; }) }));
    if (shows.length) rows.push(row('Series', shows.slice(0, 40)));
    var live = Tidy.filter(D.live.chans); if (live.length) rows.push(row('Live TV', live.slice(0, 30), { wide: true }));
    groups(films).sort(function (a, b) { return b.items.length - a.items.length; }).slice(0, 6).forEach(function (g) { rows.push(row(g.name, g.items.slice(0, 60))); });
    groups(shows).sort(function (a, b) { return b.items.length - a.items.length; }).slice(0, 3).forEach(function (g) { rows.push(row(g.name, g.items.slice(0, 60))); });
    return rows;
  }
  function vod(shows) {
    var D = App.data, list = Tidy.filter(shows ? D.series : D.movies), rows = [];
    var cont = continuing(shows ? ['episode', 'series'] : ['movie']); if (cont.length) rows.push(row('Continue watching', cont));
    var my = mine([shows ? 'series' : 'movie']); if (my.length) rows.push(row('My list', my));
    var gs = groups(list);
    if (!shows) {
      var decs = {}; list.forEach(function (x) { var d = decade(x); if (d) (decs[d] = decs[d] || []).push(x); });
      var dk = Object.keys(decs).filter(function (d) { return decs[d].length >= 3; }).sort().reverse();
      var decRow = dk.length ? row('Explore by decade', [], { chips: dk.map(function (d) { return { name: d, open: function () { App.openCategory(d + ' films', decs[d]); } }; }) }) : null;
    }
    if (gs.length) rows.push(row('Categories', [], { chips: gs.map(function (g) { return { name: g.name, open: function () { App.openCategory(g.name, g.items); } }; }) }));
    if (list.length >= 10) rows.push(row(shows ? 'Top 10 new series' : 'Top 10 new films', list.slice(0, 10), { top10: true }));
    rows = rows.concat(becauseRows(list, function (x) { return x.kind === (shows ? 'series' : 'movie'); }, 2));
    if (list.length > 10) rows.push(row(shows ? 'New series' : 'Recently added', list.slice(10, 50)));
    var wk = uniq(Extras.newThisWeek(list)); if (wk.length >= 4) rows.push(row('New this week', wk));
    if (!shows) { var cols = Extras.collections(list, 30); if (cols.length >= 2) rows.push(row('Collections', cols)); }
    if (decRow) rows.push(decRow);
    rows = rows.concat(genreRows(list, shows));
    gs.forEach(function (g) { rows.push(row(g.name, g.items)); });
    return rows;
  }
  // ---------- people ----------
  // the cast: the provider's or Cinemeta's list, or the names after "starring" in the story
  function castOf(it) {
    var r = [];
    if (it.cast && it.cast.indexOf('Directed by') !== 0) it.cast.split(',').forEach(function (x) { x = x.trim(); if (x.length > 1 && r.indexOf(x) < 0) r.push(x); });
    var m = /\b(?:starring|stars|starred|featuring)\s+([^.;]+?)(?:[.;]| as | with | in (?:the|a|his|her)\b|$)/i.exec(it.plot || '');
    if (m) m[1].split(/,\s*|\s+and\s+/).forEach(function (n) { n = n.trim(); if (/^[A-Z][\w'.-]+(\s+[A-Z][\w'.-]+){1,3}$/.test(n) && r.indexOf(n) < 0) r.push(n); });
    return r.slice(0, 8);
  }
  function has(x, who) { who = who.toLowerCase(); if (!x.cast) Meta.apply(x); return (x.cast || '').toLowerCase().indexOf(who) >= 0 || (x.director || '').toLowerCase().indexOf(who) >= 0 || (x.plot || '').toLowerCase().indexOf(who) >= 0; }
  // everything with someone in it
  function person(who) {
    var D = App.data, rows = [];
    var f = uniq(Tidy.filter(D.movies).filter(function (x) { return has(x, who); })), s = uniq(Tidy.filter(D.series).filter(function (x) { return has(x, who); }));
    if (f.length) rows.push(row('Films with ' + who, f));
    if (s.length) rows.push(row('Series with ' + who, s));
    return rows;
  }

  // a category (or decade) on its own page: its titles, seven to a row
  function grid(items, per) {
    var rows = []; per = per || 7;
    for (var i = 0; i < items.length; i += per) rows.push(row('', items.slice(i, i + per), { grid: true }));
    return rows;
  }
  // search: what has all the typed words in its name
  function search(text) {
    var fold = function (s) { s = (s || '').toLowerCase(); try { s = s.normalize('NFD').replace(/[\u0300-\u036f]/g, ''); } catch (e) { } return s; };
    var D = App.data, words = fold(text).split(/\s+/).map(function (w) { return w.replace(/[^a-z0-9]/g, ''); }).filter(Boolean);
    if (!words.length) return [];
    var hit = function (x) { var t = x._fold || (x._fold = fold(x.title)); return words.every(function (w) { return t.indexOf(w) >= 0; }); };
    var rows = [];
    var f = Tidy.filter(D.movies).filter(hit).slice(0, 60), s = Tidy.filter(D.series).filter(hit).slice(0, 60), c = Tidy.filter(D.live.chans).filter(hit).slice(0, 60);
    if (f.length) rows.push(row('Films', f)); if (s.length) rows.push(row('Series', s)); if (c.length) rows.push(row('Channels', c, { wide: true }));
    // people: someone's name finds what they're in (search finds people too, as in the launcher)
    if (text.trim().length >= 4 && text.indexOf(' ') > 0) {
      var who = text.trim(), pf = Tidy.filter(D.movies).concat(Tidy.filter(D.series)).filter(function (x) { return has(x, who); });
      if (pf.length) rows.push(row('With ' + who.replace(/\b\w/g, function (c) { return c.toUpperCase(); }), uniq(pf).slice(0, 60)));
    }
    return rows;
  }

  return { home: home, vod: vod, grid: grid, search: search, person: person, castOf: castOf, genresOf: genresOf, decade: decade };
})();
