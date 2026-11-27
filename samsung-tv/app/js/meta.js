// Cinemeta (the free film and series database the launcher uses, TvCinemeta.cs): what a provider doesn't say about a
// film or series, its genres, a wide picture, its logo, its rating and length, its story and its trailer (YouTube).
// Asked once per title and kept on the TV; after the lists load, the newest titles are looked up quietly in the
// background (as the launcher does when a profile loads), so genre rows and "Because you watched" have more to go on.
var Meta = (function () {
  var BASE = 'https://v3-cinemeta.strem.io/', KEY = 'recomp-tv-meta', MAX = 3000;
  var cache = {}, order = [];
  try { var saved = JSON.parse(localStorage.getItem(KEY) || '{}'); cache = saved.c || {}; order = saved.o || Object.keys(cache); } catch (e) { cache = {}; order = []; }
  var dirty = false, saveTimer = null;
  function save() { dirty = true; if (saveTimer) return; saveTimer = setTimeout(function () { saveTimer = null; if (!dirty) return; dirty = false; try { localStorage.setItem(KEY, JSON.stringify({ c: cache, o: order })); } catch (e) { while (order.length > 500) delete cache[order.shift()]; try { localStorage.setItem(KEY, JSON.stringify({ c: cache, o: order })); } catch (e2) { } } }, 3000); }
  var version = 0;   // goes up whenever something new is learned (genre rows are worked out again then)

  // the title as Cinemeta would have it: no year, quality or language tags
  function cleanTitle(t) {
    t = t || ''; var year = '';
    var y = /[\(\[\s-](19[2-9]\d|20[0-4]\d)[\)\]\s]*$|\((19[2-9]\d|20[0-4]\d)\)/.exec(t);
    if (y) year = y[1] || y[2];
    t = t.replace(/^\s*(\|[^|]{1,8}\|\s*|[A-Z]{2,4}\s*[-:|]\s+|\[[^\]]{1,10}\]\s*)/, '');
    t = t.replace(/\s*[\(\[]\s*(19|20)\d\d\s*[\)\]]/g, '');
    t = t.replace(/\s*[\[\(]?\b(4K|UHD|FHD|HD|SD|1080p|720p|2160p|HEVC|x265|x264|MULTI|VOSTFR|DUBBED|SUBBED|NL|EN)\b[\]\)]?/gi, '');
    t = t.replace(/\s+-\s*(19|20)\d\d\s*$/, '');
    return { title: t.replace(/^[\s\-:|.]+|[\s\-:|.]+$/g, ''), year: year };
  }
  function squash(s) { return (s || '').toLowerCase().replace(/[^a-z0-9]/g, ''); }
  function keyOf(it) {
    var c = cleanTitle(it.title), year = c.year || ((/(19|20)\d\d/.exec(it.year || '') || [])[0] || '');
    if (c.title.length < 2) return null;
    return (it.kind === 'series' ? 'series' : 'movie') + '|' + squash(c.title) + '|' + year;
  }
  function cached(it) { var k = it && keyOf(it); var h = k && cache[k]; return h && !h.none ? h : null; }
  function asked(it) { var k = keyOf(it); return !k || !!cache[k]; }

  var waiting = {};
  // what Cinemeta knows about it (null when nothing); asks only once per title
  function find(it) {
    var k = keyOf(it); if (!k) return Promise.resolve(null);
    if (cache[k]) return Promise.resolve(cache[k].none ? null : cache[k]);
    if (waiting[k]) return waiting[k];
    var type = it.kind === 'series' ? 'series' : 'movie', c = cleanTitle(it.title), year = c.year || ((/(19|20)\d\d/.exec(it.year || '') || [])[0] || '');
    var want = squash(c.title);
    var p = Api.json(BASE + 'catalog/' + type + '/top/search=' + encodeURIComponent(c.title) + '.json', 15000).then(function (res) {
      var pick = ((res && res.metas) || []).slice(0, 10).filter(function (m) {
        if (squash(m.name) !== want) return false;
        if (!year) return true;
        var ry = /(19|20)\d\d/.exec(m.releaseInfo || ''); return !ry || Math.abs(Number(ry[0]) - Number(year)) <= 1;
      })[0];
      if (!pick) return null;
      return Api.json(BASE + 'meta/' + type + '/' + pick.id + '.json', 15000).then(function (full) { return full && full.meta; });
    }).then(function (m) {
      var keep = !m ? { none: 1 } : {
        id: m.id, poster: m.poster || '', background: m.background || '', logo: m.logo || '', rating: m.imdbRating || '', runtime: m.runtime || '',
        year: m.releaseInfo || m.year || '', description: (m.description || '').slice(0, 500), genres: (m.genres || []).slice(0, 3).join(', '),
        cast: (m.cast || []).slice(0, 6).join(', '), director: (m.director || []).slice(0, 2).join(', '), trailer: trailerIn(m), country: m.country || '' };
      cache[k] = keep; order.push(k); while (order.length > MAX) delete cache[order.shift()];
      version++; save(); delete waiting[k];
      return keep.none ? null : keep;
    }, function () { delete waiting[k]; return null; });   // not kept: asked again next time
    waiting[k] = p; return p;
  }
  function trailerIn(m) {
    var l = (m.trailerStreams || []).concat(m.trailers || []);
    for (var i = 0; i < l.length; i++) { var v = l[i].ytId || l[i].source; if (v && /^[A-Za-z0-9_-]{11}$/.test(v)) return v; }
    return '';
  }
  // the country it comes from (for its original language): older entries didn't keep it, so those ask once more
  function country(it) {
    return find(it).then(function (m) {
      if (!m) return '';
      if (m.country !== undefined) return m.country;
      var type = it.kind === 'series' ? 'series' : 'movie';
      return Api.json(BASE + 'meta/' + type + '/' + m.id + '.json', 15000).then(function (full) { m.country = (full && full.meta && full.meta.country) || ''; save(); return m.country; }, function () { return ''; });
    });
  }
  // fills in what the item is missing; resolves true when it learned something
  function enrich(it) {
    return find(it).then(function (m) {
      if (!m) return false;
      if (!it.backdrop && m.background) it.backdrop = m.background;
      if (!it.image && m.poster) it.image = m.poster.replace('/poster/small/', '/poster/medium/');
      if (!it.logo && m.logo) it.logo = m.logo;
      if (!it.plot && m.description) it.plot = m.description;
      if (!it.genre && m.genres) it.genre = m.genres;
      if (!it.cast && m.cast) it.cast = m.cast;
      if (!it.director && m.director) it.director = m.director;
      if (!it.rating && m.rating) it.rating = m.rating;
      if (!it.duration && m.runtime) it.duration = m.runtime;
      if (!it.year && m.year) it.year = String(m.year).slice(0, 4);
      it.trailer = m.trailer || '';
      return true;
    });
  }
  // what's cached, put on the item without asking (for drawing rows quickly)
  function apply(it) {
    var m = cached(it); if (!m) return it;
    if (!it.backdrop && m.background) it.backdrop = m.background;
    if (!it.image && m.poster) it.image = m.poster.replace('/poster/small/', '/poster/medium/');
    if (!it.logo && m.logo) it.logo = m.logo;
    if (!it.genre && m.genres) it.genre = m.genres;
    if (!it.plot && m.description) it.plot = m.description;
    if (!it.rating && m.rating) it.rating = m.rating;
    if (!it.duration && m.runtime) it.duration = m.runtime;
    if (!it.cast && m.cast) it.cast = m.cast;
    if (!it.director && m.director) it.director = m.director;
    return it;
  }

  // the background lookups: four at a time, the newest titles first
  var queue = [], running = 0, onLearn = null;
  function warm(items) {
    var seen = {};
    queue = items.filter(function (x) { var k = keyOf(x); if (!k || cache[k] || seen[k]) return false; seen[k] = 1; return true; });
    for (var i = 0; i < 4; i++) next();
  }
  function next() {
    if (!queue.length || running >= 4) return;
    var it = queue.shift(); running++;
    find(it).then(function () { running--; apply(it); if (onLearn && !queue.length && !running) onLearn(); setTimeout(next, 60); });
  }

  return { cleanTitle: cleanTitle, squash: squash, cached: cached, country: country, asked: asked, find: find, enrich: enrich, apply: apply, warm: warm,
           get version() { return version; }, onDone: function (f) { onLearn = f; } };
})();
