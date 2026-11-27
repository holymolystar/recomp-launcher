// A film's or series' own page ("More info"): the wide picture, the title or its logo, what it is, the story, the
// cast; Play (or Resume, and Play from the start), My list; for a series its seasons and episodes, with where you
// are in each and which you've seen. Back goes back to where you were.
var Details = (function () {
  var el = function (t, c, x) { return Browse.el(t, c, x); };
  var root, it = null, zone = 'btns', btn = 0, cast = [], pidx = 0, season = 0, ep = 0, seasons = [], eps = [], btnActs = [];
  var EP_W = 384, EP_GAP = 24, shownArt = null, glowEl = null, firstT;

  function open(item) {
    it = item; zone = 'btns'; btn = 0; season = 0; ep = 0; seasons = []; eps = [];
    root = document.getElementById('details');
    shownArt = null; glowEl = null;
    if (!Look.calm()) { root.classList.add('first'); clearTimeout(firstT); firstT = setTimeout(function () { root.classList.remove('first'); }, 800); }
    draw();
    var a = App.acct;
    var after = function () { if (it !== item) return; if (item.kind === 'series') pickResumeSeason(); draw(); };
    var jobs = [Meta.enrich(item).catch(function () { })];
    if (item.kind === 'movie') { jobs.push(Api.movieInfo(a, item)); Best.warm(item); }
    if (item.kind === 'series') jobs.push(Api.seriesInfo(a, item));
    Promise.all(jobs).then(after, after);
  }
  // where the series was left: that season, that episode
  function pickResumeSeason() {
    seasons = []; (it.episodes || []).forEach(function (e) { if (seasons.indexOf(e.season) < 0) seasons.push(e.season); });
    var next = nextEpisode();
    season = next ? Math.max(0, seasons.indexOf(next.season)) : 0;
    eps = episodesOf(seasons[season]); ep = next ? Math.max(0, eps.indexOf(next)) : 0;
  }
  function episodesOf(s) { return (it.episodes || []).filter(function (e) { return e.season === s; }); }
  // the episode to play: the one you're in the middle of, else the one after the last one you finished, else the first
  function nextEpisode() {
    var l = it.episodes || []; if (!l.length) return null;
    var last = Store.lastEpisodeOf(it.id), i = -1;
    if (last) for (var j = 0; j < l.length; j++) if (l[j].key === last.ep) i = j;
    if (i >= 0 && !last.done && Store.resumeAt(l[i].key)) return l[i];
    if (i >= 0 && i + 1 < l.length) return l[i + 1];
    for (var k = 0; k < l.length; k++) if (!Store.isWatched(l[k].key)) return l[k];
    return l[0];
  }

  function draw() {
    root.innerHTML = '';
    var art = it.backdrop || it.image;
    if (art && (!glowEl || glowEl._src !== art)) glowEl = Look.glow(root, art); else if (glowEl) root.appendChild(glowEl);
    var bg = el('div', 'd-bg'); root.appendChild(bg);
    if (art) { if (!it.backdrop) bg.classList.add('poster'); Look.bgWhenLoaded(bg, art, shownArt === art); shownArt = art; }
    root.appendChild(el('div', 'd-fade'));
    if (glowEl) root.appendChild(glowEl);   // over the dark side, under the text
    var box = el('div', 'd-box'); root.appendChild(box);
    var name = el('div', 'd-title', Meta.cleanTitle(it.title).title); box.appendChild(name); Fmt.fitSoon(name, 2, 46);
    if (it.logo) { var lg = new Image(); lg.className = 'd-logo'; lg.onload = function () { if (name.parentNode) box.replaceChild(lg, name); }; lg.src = it.logo; }
    var meta = el('div', 'd-meta'); Browse.metaLine(meta, it); box.appendChild(meta);
    if (it.lang) meta.appendChild(el('span', 'lang', it.lang));
    box.appendChild(el('div', 'd-plot', it.plot || (it.infoDone || it.episodes ? '' : '…')));

    // the buttons
    var row = el('div', 'd-btns'); box.appendChild(row); btnActs = [];
    var add = function (text, main, act) { var b = el('div', 'hbtn' + (main ? ' play' : ''), text); row.appendChild(b); btnActs.push(act); };
    if (it.kind === 'movie') {
      var at = Store.resumeAt(it.key);
      add(at ? '▶  Resume from ' + clock(at) : '▶  Play', true, function () { App.play(it, at); });
      if (at) add('↺  From the start', false, function () { App.play(it, 0); });
    } else if (it.kind === 'series') {
      var nx = nextEpisode();
      if (nx) { var r = Store.resumeAt(nx.key); add((r ? '▶  Resume ' : '▶  Play ') + 'S' + nx.season + ':E' + nx.episode, true, function () { App.play(nx, r); }); }
      else add(it.episodes ? 'No episodes yet' : 'Getting the episodes…', true, function () { });
    }
    add(Store.isFav(it.key) ? '✓  In My list' : '+  My list', false, function () { App.toggleList(it); draw(); });
    var vers = Extras.versionsOf(it);
    if (vers.length) add('⇄  Versions (' + vers.length + ')', false, function () { Extras.openVersions(it); });
    add('⋯  More', false, function () { App.more(it, [{ name: '✎  Your own name, picture or story', act: function () { setTimeout(editDetails, 0); } }]); });
    btn = Math.min(btn, btnActs.length - 1);

    // the cast, as buttons: OK opens everything they're in (the launcher's People)
    cast = Rows.castOf(it).slice(0, 5); pidx = Math.min(pidx, Math.max(0, cast.length - 1));
    if (cast.length || it.director) {
      var pp = el('div', 'd-cast'); box.appendChild(pp);
      if (cast.length) pp.appendChild(el('span', 'd-cast-t', 'Starring'));
      cast.forEach(function (n, i) { pp.appendChild(el('span', 'chip small' + (zone === 'people' && i === pidx ? ' focus' : ''), n)); });
      if (it.director) pp.appendChild(el('div', 'd-dir', 'Directed by ' + it.director));
    }

    // a series' seasons and episodes
    if (it.kind === 'series' && it.episodes && it.episodes.length) {
      if (!seasons.length) pickResumeSeason();
      var sw = el('div', 'd-seasons'); root.appendChild(sw);
      seasons.forEach(function (s, i) { sw.appendChild(el('div', 'chip' + (i === season ? ' sel' : '') + (zone === 'seasons' && i === season ? ' focus' : ''), 'Season ' + s)); });
      var strip = el('div', 'd-eps'); root.appendChild(strip);
      var inner = el('div', 'strip'); strip.appendChild(inner);
      eps.forEach(function (e, i) {
        var c = el('div', 'ep' + (zone === 'eps' && i === ep ? ' focus' : '')); c.style.left = (i * (EP_W + EP_GAP)) + 'px';
        var pic = el('div', 'ep-pic'); c.appendChild(pic);
        var src = e.image || it.backdrop || it.image; if (src) pic.style.backgroundImage = 'url("' + src + '")';
        var pr = Store.progress(e.key);
        if (pr && pr.dur) { var bar = el('div', 'tbar'); var f = el('i'); f.style.width = Math.min(100, 100 * pr.pos / pr.dur) + '%'; bar.appendChild(f); pic.appendChild(bar); }
        if (Store.isWatched(e.key)) pic.appendChild(el('div', 'ep-seen', '✓ Watched'));
        c.appendChild(el('div', 'ep-name', e.episode + '.  ' + (e.title || 'Episode ' + e.episode).replace(/^.*?S\d+\s*E\d+\s*[-:]?\s*/i, '') ));
        var sub = [e.duration ? String(e.duration).replace(/^0?(\d+):(\d\d):\d\d$/, function (m, h, mm) { return (Number(h) ? h + 'h ' : '') + Number(mm) + 'm'; }) : '', ''].join('');
        if (sub) c.appendChild(el('div', 'ep-sub', sub));
        if (e.plot) c.appendChild(el('div', 'ep-plot', e.plot));
        inner.appendChild(c);
      });
      var first = Math.max(0, ep - 1);
      inner.style.transform = 'translateX(' + (-first * (EP_W + EP_GAP)) + 'px)';
    }
    [].forEach.call(row.children, function (b, i) { b.classList.toggle('focus', zone === 'btns' && i === btn); });
  }
  function clock(s) { s = Math.round(s); var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + (m < 10 ? '0' : '') : '') + m + ':' + (x < 10 ? '0' : '') + x; }

  // your own details: a different name, picture or story (kept on the TV, for every profile), and back again
  function editDetails() {
    var x = it, changed = !!Store.meta(x.key);
    var items = [
      { name: 'Change its name', act: function () { App.ask('A name for it', x.title, function (v) { App.setMeta(x, 'title', v); draw(); }); } },
      { name: "Change its picture (a picture's link)", act: function () { App.ask("A picture's link (https://...)", '', function (v) { App.setMeta(x, 'image', v); draw(); }); } },
      { name: 'Change its story', act: function () { App.ask('The story', x.plot || '', function (v) { App.setMeta(x, 'plot', v); draw(); }); } }];
    if (changed) items.push({ name: 'Back to the original details', act: function () { App.resetMeta(x); draw(); } });
    App.menu('Your own details', items);
  }
  function key(k) {
    var hasEps = it.kind === 'series' && eps.length;
    if (k === 'red') { editDetails(); return true; }
    if (zone === 'people') {
      if (k === 'left') pidx = Math.max(0, pidx - 1);
      else if (k === 'right') pidx = Math.min(cast.length - 1, pidx + 1);
      else if (k === 'up') zone = 'btns';
      else if (k === 'down') { if (hasEps) zone = 'seasons'; }
      else if (k === 'ok') { App.openPerson(cast[pidx]); return true; }
      else if (k === 'back') { App.back(); return true; }
      else return false;
      draw(); return true;
    }
    if (k === 'back') { App.back(); return true; }
    if (k === 'yellow') { App.toggleList(it); draw(); return true; }
    if (zone === 'btns') {
      if (k === 'left') btn = Math.max(0, btn - 1);
      else if (k === 'right') btn = Math.min(btnActs.length - 1, btn + 1);
      else if (k === 'down' && cast.length) zone = 'people';
      else if (k === 'down' && hasEps) zone = 'seasons';
      else if (k === 'ok') { btnActs[btn](); return true; }
      else if (k === 'play' || k === 'playpause') { btnActs[0](); return true; }
      else return k === 'up';
    } else if (zone === 'seasons') {
      if (k === 'left' || k === 'right') { season = Math.max(0, Math.min(seasons.length - 1, season + (k === 'left' ? -1 : 1))); eps = episodesOf(seasons[season]); ep = 0; }
      else if (k === 'up') zone = cast.length ? 'people' : 'btns';
      else if (k === 'down' || k === 'ok') zone = 'eps';
      else return false;
    } else if (zone === 'eps') {
      if (k === 'left') ep = Math.max(0, ep - 1);
      else if (k === 'right') ep = Math.min(eps.length - 1, ep + 1);
      else if (k === 'up') zone = 'seasons';
      else if (k === 'ok' || k === 'play' || k === 'playpause') { var e = eps[ep]; App.play(e, Store.resumeAt(e.key)); return true; }
      else if (k === 'green') { var w = eps[ep]; Store.setWatched(w.key, !Store.isWatched(w.key)); if (!Store.isWatched(w.key)) Store.clearProgress(w.key); }
      else return false;
    }
    draw(); return true;
  }

  return { open: open, key: key, redraw: function () { if (it) draw(); }, get item() { return it; }, nextEpisode: function (s) { var keep = it; it = s; var n = nextEpisode(); it = keep; return n; } };
})();
