// Talking to the TV provider, as the launcher does (TvData.cs): an Xtream login (player_api.php) or a playlist link
// (M3U). A playlist link that's really a login (get.php?username=...&password=...) is used as the login: that gives
// what's on now and next.
var Api = (function () {
  function get(url, timeout) {
    return new Promise(function (ok, fail) {
      var x = new XMLHttpRequest();
      x.open('GET', url, true);
      x.timeout = timeout || 60000;
      x.onload = function () { if (x.status >= 200 && x.status < 300) ok(x.responseText); else fail(new Error('the server answered ' + x.status)); };
      x.onerror = function () { fail(new Error("couldn't reach the server")); };
      x.ontimeout = function () { fail(new Error('the server took too long')); };
      x.send();
    });
  }
  function json(url, timeout) { return get(url, timeout).then(function (t) { try { return JSON.parse(t); } catch (e) { throw new Error("the server's answer wasn't readable"); } }); }
  function list(o) { return Array.isArray(o) ? o : (o && typeof o === 'object' ? Object.keys(o).map(function (k) { return o[k]; }) : []); }
  function enc(s) { return encodeURIComponent(s || ''); }

  // "example.com:8080/" -> "http://example.com:8080"
  function cleanBase(s) {
    s = (s || '').trim().replace(/\/+$/, '').replace(/\/(player_api|get)\.php.*$/i, '');
    if (s && !/^https?:\/\//i.test(s)) s = 'http://' + s;
    return s;
  }
  // a playlist link that's a login in disguise
  function loginFromLink(url) {
    var m = /^(https?:\/\/[^\/\s?#]+)\/(?:get|player_api)\.php\?(.*)$/i.exec((url || '').trim());
    if (!m) return null;
    var q = {}; m[2].split('&').forEach(function (p) { var i = p.indexOf('='); if (i > 0) q[p.slice(0, i).toLowerCase()] = decodeURIComponent(p.slice(i + 1).replace(/\+/g, ' ')); });
    return q.username && q.password ? { type: 'xtream', base: m[1], user: q.username, pass: q.password, url: url.trim() } : null;
  }
  function api(a, action, extra) {
    return a.base + '/player_api.php?username=' + enc(a.user) + '&password=' + enc(a.pass) + (action ? '&action=' + action : '') + (extra || '');
  }

  // checks the login: resolves with the account (with its status), or fails with a reason to show
  function signIn(a) {
    if (a.type === 'm3u') {
      var l = loginFromLink(a.url);
      if (l) return signIn(l).catch(function () { return parseM3u(a.url).then(function (r) { if (!r.length) throw new Error('the playlist is empty'); return a; }); });
      return get(a.url, 180000).then(function (t) { if (!parseM3uText(t).length) throw new Error('that link has no channels in it'); return a; });
    }
    a.base = cleanBase(a.base);
    if (!/^https?:\/\/[^\/]+/.test(a.base)) return Promise.reject(new Error('the server should look like http://example-tv.com:8080'));
    return json(api(a, null), 30000).then(function (d) {
      var u = d && d.user_info;
      if (!u || Number(u.auth) !== 1) throw new Error("the server didn't accept this login");
      var status = u.status || '';
      if (status && !/^active$/i.test(status)) throw new Error('the server says this login is ' + status.toLowerCase());
      a.formats = list(u.allowed_output_formats).map(String);
      a.status = status || 'Active';
      a.exp = Number(u.exp_date) || 0;
      a.maxConn = Number(u.max_connections) || 0;
      return a;
    });
  }

  // the live channels: [{key, id, num, name, logo, cat, epg, url}] and the categories [{id, name}]
  function liveChannels(a) {
    if (a.type === 'm3u') return parseM3u(a.url).then(function (items) {
      var cats = {}, out = [];
      items.forEach(function (it, i) {
        if (it.vod) return;
        var c = it.group || 'Other';
        if (!cats[c]) cats[c] = { id: c, name: c };
        out.push({ kind: 'live', key: 'm3u:' + (it.tvgId || it.name) + ':' + it.url.length, id: String(i), num: out.length + 1, title: it.name, image: it.logo, cat: c, epg: it.tvgId, url: it.url });
      });
      return { cats: Object.keys(cats).map(function (k) { return cats[k]; }), chans: out };
    });
    var fmt = streamFormat(a);
    return Promise.all([json(api(a, 'get_live_categories')), json(api(a, 'get_live_streams'), 180000)]).then(function (r) {
      // a channel's category is its name (it's tidied, and its language read, like the films' and series')
      var names = catNames(r[0]), order = [];
      list(r[0]).forEach(function (c) { var n = c.category_name || 'Other'; if (order.indexOf(n) < 0) order.push(n); });
      var chans = list(r[1]).map(function (s, i) {
        var id = String(s.stream_id);
        return { kind: 'live', key: 'x:' + id, id: id, num: Number(s.num) || i + 1, title: (s.name || '').trim(), image: s.stream_icon || '', cat: names[String(s.category_id)] || 'Other',
                 epg: s.epg_channel_id || '', url: a.base + '/live/' + enc(a.user) + '/' + enc(a.pass) + '/' + id + '.' + fmt,
                 archiveDays: Number(s.tv_archive) > 0 ? Math.max(1, Number(s.tv_archive_duration) || 1) : 0 };
      });
      chans.sort(function (x, y) { return x.num - y.num; });
      if (chans.some(function (c) { return c.cat === 'Other'; }) && order.indexOf('Other') < 0) order.push('Other');
      return { cats: order.map(function (n) { return { id: n, name: n }; }), chans: chans };
    });
  }
  // HLS (m3u8) when the provider allows it, else MPEG-TS; Settings can choose
  function streamFormat(a) {
    var p = Store.pref('format', 'auto');
    if (p === 'm3u8' || p === 'ts') return p;
    return !a.formats || !a.formats.length || a.formats.indexOf('m3u8') >= 0 ? 'm3u8' : 'ts';
  }

  // now and next on a channel (Xtream's short guide; the titles come base64-encoded)
  function b64(s) { try { return decodeURIComponent(escape(atob(s || ''))); } catch (e) { return s || ''; } }
  var epgCache = {};
  function nowNext(a, ch) {
    if (!a || a.type !== 'xtream' || !ch) return Promise.resolve(null);
    var c = epgCache[ch.id];
    if (c && Date.now() - c.at < 5 * 60000 && (!c.list.length || c.list[0].stop * 1000 > Date.now())) return Promise.resolve(c.list);
    return json(api(a, 'get_short_epg', '&stream_id=' + ch.id + '&limit=3'), 15000).then(function (d) {
      var now = Date.now() / 1000;
      var l = list(d && d.epg_listings).map(function (x) {
        return { title: b64(x.title).trim(), desc: b64(x.description).trim(), start: Number(x.start_timestamp) || Date.parse(x.start) / 1000, stop: Number(x.stop_timestamp) || Date.parse(x.end || x.stop) / 1000 };
      }).filter(function (x) { return x.title && x.stop > now; });
      epgCache[ch.id] = { at: Date.now(), list: l };
      return l;
    }).catch(function () { return null; });
  }

  // a channel's whole guide (what's been on, and what's coming): Xtream's per-channel table, which also says what
  // the provider keeps for TV Archive. [{title, desc, start, stop (unix s), raw ("2024-05-01 20:00:00", the
  // provider's own time, for the archive link), archive}]
  var dayCache = {};
  function guideOf(a, ch) {
    if (!a || a.type !== 'xtream' || !ch) return Promise.resolve([]);
    var c = dayCache[ch.id];
    if (c && Date.now() - c.at < 15 * 60000) return c.p;
    var p = json(api(a, 'get_simple_data_table', '&stream_id=' + ch.id), 20000).then(function (d) {
      var seen = {};
      return list(d && d.epg_listings).map(function (x) {
        return { title: b64(x.title).trim(), desc: b64(x.description).trim(), start: Number(x.start_timestamp) || Date.parse(x.start) / 1000,
                 stop: Number(x.stop_timestamp) || Date.parse(x.end || x.stop) / 1000, raw: x.start || '', archive: Number(x.has_archive) === 1 };
      }).filter(function (x) { if (!x.title || !(x.stop > x.start) || seen[x.start]) return false; seen[x.start] = 1; return true; })
        .sort(function (x, y) { return x.start - y.start; });
    }).catch(function () { delete dayCache[ch.id]; return []; });
    dayCache[ch.id] = { at: Date.now(), p: p };
    return p;
  }
  // TV Archive: a past programme's link (the provider's timeshift: its length in minutes and its start, in the
  // provider's own time)
  function archiveUrl(a, ch, prog) {
    if (!a || a.type !== 'xtream' || !ch.archiveDays) return null;
    var m = /^(\d{4})-(\d\d)-(\d\d)[ T](\d\d):(\d\d)/.exec(prog.raw || '');
    if (!m) { var d = new Date(prog.start * 1000), p2 = function (n) { return (n < 10 ? '0' : '') + n; }; m = [0, d.getFullYear(), p2(d.getMonth() + 1), p2(d.getDate()), p2(d.getHours()), p2(d.getMinutes())]; }
    var mins = Math.ceil((prog.stop - prog.start) / 60);
    return a.base + '/timeshift/' + enc(a.user) + '/' + enc(a.pass) + '/' + mins + '/' + m[1] + '-' + m[2] + '-' + m[3] + ':' + m[4] + '-' + m[5] + '/' + ch.id + '.ts';
  }
  function canReplay(ch, prog) {
    var now = Date.now() / 1000;
    return !!(ch.archiveDays && prog && prog.stop <= now && prog.start > now - ch.archiveDays * 86400 && (prog.archive || prog.archive === undefined));
  }


  // ---- films and series ----
  function yearOf(s) { var m = /\b(19[3-9]\d|20[0-4]\d)\b/.exec(s || ''); return m ? m[1] : ''; }
  function firstOf(v) { return Array.isArray(v) ? (v.filter(Boolean)[0] || '') : (v || ''); }
  // [{kind:'movie', key, id, title, image, cat, ext, year, rating, url}], newest added first
  function movies(a) {
    if (a.type === 'm3u') return m3uVod(a).then(function (r) { return r.movies; });
    return Promise.all([json(api(a, 'get_vod_categories')), json(api(a, 'get_vod_streams'), 240000)]).then(function (r) {
      var names = catNames(r[0]);
      var l = list(r[1]).map(function (s) {
        var id = String(s.stream_id), ext = s.container_extension || 'mp4';
        return { kind: 'movie', key: 'm:' + id, id: id, title: (s.name || '').trim(), image: s.stream_icon || '', cat: names[String(s.category_id)] || 'Other',
                 ext: ext, year: yearOf(s.name) || (s.year ? String(s.year) : ''), rating: s.rating ? String(s.rating) : '', added: Number(s.added) || 0,
                 url: a.base + '/movie/' + enc(a.user) + '/' + enc(a.pass) + '/' + id + '.' + ext };
      });
      l.sort(function (x, y) { return y.added - x.added; });
      return l;
    });
  }
  // [{kind:'series', key, id, title, image, cat, plot, year, rating, genre, cast, backdrop}]
  function series(a) {
    if (a.type === 'm3u') return m3uVod(a).then(function (r) { return r.series; });
    return Promise.all([json(api(a, 'get_series_categories')), json(api(a, 'get_series'), 240000)]).then(function (r) {
      var names = catNames(r[0]);
      var l = list(r[1]).map(function (s) {
        var id = String(s.series_id), rel = s.releaseDate || s.release_date || '';
        return { kind: 'series', key: 's:' + id, id: id, title: (s.name || '').trim(), image: s.cover || '', cat: names[String(s.category_id)] || 'Other',
                 plot: s.plot || '', year: rel.length >= 4 ? rel.slice(0, 4) : yearOf(s.name), rating: s.rating ? String(s.rating) : '', genre: s.genre || '', cast: s.cast || '',
                 backdrop: firstOf(s.backdrop_path), added: Number(s.last_modified) || 0 };
      });
      l.sort(function (x, y) { return y.added - x.added; });
      return l;
    });
  }
  function catNames(o) { var n = {}; list(o).forEach(function (c) { n[String(c.category_id)] = c.category_name || 'Other'; }); return n; }
  // a film's details (plot, backdrop, length...), filled into it
  function movieInfo(a, m) {
    if (a.type !== 'xtream' || m.infoDone) return Promise.resolve(m);
    return json(api(a, 'get_vod_info', '&vod_id=' + m.id), 20000).then(function (d) {
      var i = d && d.info; m.infoDone = true; if (!i || Array.isArray(i)) return m;
      m.plot = i.plot || i.description || m.plot || ''; m.genre = i.genre || m.genre || ''; m.cast = i.cast || m.cast || ''; m.director = i.director || '';
      m.duration = i.duration || ''; m.rating = i.rating ? String(i.rating) : m.rating;
      m.backdrop = firstOf(i.backdrop_path) || m.backdrop || '';
      if (!m.image) m.image = i.movie_image || i.cover_big || '';
      var rel = i.releasedate || i.release_date || ''; if (rel.length >= 4) m.year = rel.slice(0, 4);
      return m;
    }, function () { return m; });
  }
  // a series' seasons and episodes: s.episodes = [{kind:'episode', key, id, title, season, episode, ...}]
  function seriesInfo(a, s) {
    if (s.episodes) return Promise.resolve(s);
    if (a.type !== 'xtream') { s.episodes = []; return Promise.resolve(s); }
    return json(api(a, 'get_series_info', '&series_id=' + s.id), 30000).then(function (d) {
      var info = d && d.info;
      if (info && !Array.isArray(info)) { s.plot = info.plot || s.plot; s.backdrop = firstOf(info.backdrop_path) || s.backdrop; s.genre = info.genre || s.genre; s.cast = info.cast || s.cast; if (!s.image) s.image = info.cover || ''; }
      var eps = [];
      var seasons = (d && d.episodes) || {};
      Object.keys(seasons).forEach(function (k) {
        list(seasons[k]).forEach(function (e) {
          var ei = e.info || {}, ext = e.container_extension || 'mp4', id = String(e.id);
          eps.push({ kind: 'episode', key: 'e:' + id, id: id, title: (e.title || '').trim(), season: Number(e.season) || Number(k) || 1, episode: Number(e.episode_num) || 0, ext: ext,
                     plot: ei.plot || '', image: ei.movie_image || '', duration: ei.duration || '', secs: Number(ei.duration_secs) || 0,
                     seriesId: s.id, seriesTitle: s.title, seriesImage: s.image, url: a.base + '/series/' + enc(a.user) + '/' + enc(a.pass) + '/' + id + '.' + ext });
        });
      });
      eps.sort(function (x, y) { return x.season - y.season || x.episode - y.episode; });
      s.episodes = eps; return s;
    }, function () { s.episodes = s.episodes || []; return s; });
  }
  // a playlist's films, and its episodes gathered into series by their names (Show S01E02)
  var vodCache = null;
  function m3uVod(a) {
    if (vodCache && vodCache.url === a.url) return Promise.resolve(vodCache.r);
    return parseM3u(a.url).then(function (items) {
      var movies = [], byName = {}, series = [];
      items.forEach(function (it, i) {
        if (!it.vod) return;
        var m = /^(.*?)[\s._-]*S(\d{1,2})\s*E(\d{1,3})/i.exec(it.name);
        if (m || /\/series\//i.test(it.url)) {
          var name = (m ? m[1] : it.name).replace(/[\s._-]+$/, '').trim() || it.name, key = (it.group || '') + '|' + name.toLowerCase();
          var s = byName[key];
          if (!s) { s = byName[key] = { kind: 'series', key: 's:m3u:' + name, id: 'm3u' + series.length, title: name, image: it.logo, cat: it.group || 'Other', plot: '', episodes: [] }; series.push(s); }
          s.episodes.push({ kind: 'episode', key: 'e:m3u:' + it.url.slice(-40), id: 'm3u' + i, title: it.name, season: m ? Number(m[2]) : 1, episode: m ? Number(m[3]) : s.episodes.length + 1,
                            ext: '', image: it.logo, seriesId: s.id, seriesTitle: name, seriesImage: s.image, url: it.url });
        } else movies.push({ kind: 'movie', key: 'm:m3u:' + it.url.slice(-40), id: 'm3u' + i, title: it.name, image: it.logo, cat: it.group || 'Other', year: yearOf(it.name), url: it.url, infoDone: true });
      });
      series.forEach(function (s) { s.episodes.sort(function (x, y) { return x.season - y.season || x.episode - y.episode; }); });
      vodCache = { url: a.url, r: { movies: movies, series: series } };
      return vodCache.r;
    });
  }

  // ---- M3U ----
  function parseM3u(url) { return get(url, 180000).then(parseM3uText); }
  function attr(line, name) { var m = new RegExp(name + '="([^"]*)"', 'i').exec(line); return m ? m[1] : ''; }
  function parseM3uText(text) {
    var out = [], cur = null;
    (text || '').split(/\r?\n/).forEach(function (line) {
      line = line.trim();
      if (!line) return;
      if (/^#EXTINF/i.test(line)) {
        var comma = line.lastIndexOf('",'); var name = comma >= 0 ? line.slice(comma + 2) : line.slice(line.indexOf(',') + 1);
        cur = { name: (attr(line, 'tvg-name') && !name.trim() ? attr(line, 'tvg-name') : name).trim(), logo: attr(line, 'tvg-logo'), group: attr(line, 'group-title'), tvgId: attr(line, 'tvg-id') };
      } else if (/^#EXTGRP:/i.test(line)) { if (cur && !cur.group) cur.group = line.slice(8).trim(); }
      else if (line.charAt(0) !== '#' && cur) {
        cur.url = line;
        // films and episodes in the playlist (they're for the Movies and Shows pages)
        cur.vod = /\/(movie|series)\//i.test(line) || /\.(mp4|mkv|avi|m4v|mov|webm|wmv)(\?|$)/i.test(line);
        out.push(cur); cur = null;
      }
    });
    return out;
  }

  return { signIn: signIn, liveChannels: liveChannels, nowNext: nowNext, guideOf: guideOf, archiveUrl: archiveUrl, canReplay: canReplay, movies: movies, series: series, movieInfo: movieInfo, seriesInfo: seriesInfo, get: get, json: json, loginFromLink: loginFromLink, cleanBase: cleanBase };
})();
