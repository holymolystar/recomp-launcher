// Subtitles from the internet, as in the launcher (TvSubs.cs): Dutch is shown by default, English is there as an
// extra. They come from OpenSubtitles (your own free API key, Settings > OpenSubtitles) and are kept on the TV, so a
// film never needs them fetched twice. The app draws them itself, so their timing can be moved (per film or episode,
// remembered) and their look changed (size, colour, a box behind them).
var Subs = (function () {
  var LANGS = ['nl', 'en'], NAMES = { nl: 'Nederlands', en: 'English' };
  var it = null, box = null, have = {}, showing = null, timer = null, gen = 0, lastText = null, busy = false, note = '';

  // ---------- kept on the TV (IndexedDB: room for many files; localStorage would fill up) ----------
  var dbp = null;
  function db() {
    if (dbp) return dbp;
    dbp = new Promise(function (ok, fail) {
      try {
        var r = indexedDB.open('recomp-tv-subs', 1);
        r.onupgradeneeded = function () { r.result.createObjectStore('files'); };
        r.onsuccess = function () { ok(r.result); };
        r.onerror = function () { fail(r.error); };
      } catch (e) { fail(e); }
    });
    return dbp;
  }
  function dbGet(k) { return db().then(function (d) { return new Promise(function (ok) { try { var q = d.transaction('files').objectStore('files').get(k); q.onsuccess = function () { ok(q.result || null); }; q.onerror = function () { ok(null); }; } catch (e) { ok(null); } }); }, function () { return null; }); }
  function dbPut(k, v) { return db().then(function (d) { try { d.transaction('files', 'readwrite').objectStore('files').put(v, k); } catch (e) { } }, function () { }); }

  // the timing per film or episode, and the look (all profiles)
  var DKEY = 'recomp-tv-subdelay', delays = {};
  try { delays = JSON.parse(localStorage.getItem(DKEY) || '{}') || {}; } catch (e) { delays = {}; }
  function delay() { return it ? (delays[it.key] || 0) : 0; }
  function setDelay(v) { if (!it) return; v = Math.round(v * 10) / 10; if (Math.abs(v) < 0.05) delete delays[it.key]; else delays[it.key] = v; try { localStorage.setItem(DKEY, JSON.stringify(delays)); } catch (e) { } lastText = null; }

  // ---------- reading .srt and .vtt ----------
  function secs(h, m, s, ms) { return Number(h || 0) * 3600 + Number(m) * 60 + Number(s) + Number((ms || '0') + '000'.slice((ms || '').length)) / 1000; }
  function parse(text) {
    var out = [], blocks = String(text || '').replace(/\r/g, '').replace(/^﻿/, '').split(/\n\s*\n/);
    var re = /(?:(\d+):)?(\d{1,2}):(\d{2})[,.](\d{1,3})\s*-->\s*(?:(\d+):)?(\d{1,2}):(\d{2})[,.](\d{1,3})/;
    blocks.forEach(function (b) {
      var lines = b.split('\n'), i = 0;
      while (i < lines.length && !re.test(lines[i])) i++;
      if (i >= lines.length) return;
      var m = re.exec(lines[i]);
      // kept as written (italics, {\an8} for the top): Fmt cleans it when it shows
      var t = lines.slice(i + 1).join('\n').trim();
      if (t) out.push({ s: secs(m[1], m[2], m[3], m[4]), e: secs(m[5], m[6], m[7], m[8]), t: t });
    });
    out.sort(function (a, b) { return a.s - b.s; });
    return out;
  }
  function cueAt(cues, t) {
    var lo = 0, hi = cues.length - 1, hit = -1;
    while (lo <= hi) { var mid = (lo + hi) >> 1; if (cues[mid].s <= t) { hit = mid; lo = mid + 1; } else hi = mid - 1; }
    // overlapping lines: the latest one that started and hasn't ended
    for (var i = hit; i >= 0 && i > hit - 4; i--) if (cues[i].e > t) return i;
    return -1;
  }

  // ---------- OpenSubtitles ----------
  var OS = 'https://api.opensubtitles.com/api/v1/', token = null;
  function osKey() { return Store.pref('osKey', '') || (window.RecompPreset && RecompPreset.osKey) || ''; }
  function osCall(method, path, body, tok) {
    return new Promise(function (ok, fail) {
      var x = new XMLHttpRequest();
      x.open(method, OS + path, true); x.timeout = 20000;
      x.setRequestHeader('Api-Key', osKey());
      x.setRequestHeader('X-User-Agent', 'RecompTV v0.4');
      x.setRequestHeader('Accept', 'application/json');
      if (tok) x.setRequestHeader('Authorization', 'Bearer ' + tok);
      if (body) x.setRequestHeader('Content-Type', 'application/json');
      x.onload = function () { var d = null; try { d = JSON.parse(x.responseText); } catch (e) { } if (x.status >= 200 && x.status < 300 && d) ok(d); else fail(new Error((d && (d.message || d.errors && d.errors[0])) || 'OpenSubtitles answered ' + x.status)); };
      x.onerror = function () { fail(new Error("couldn't reach OpenSubtitles")); };
      x.ontimeout = function () { fail(new Error('OpenSubtitles took too long')); };
      x.send(body ? JSON.stringify(body) : null);
    });
  }
  // the film, or the episode's series, as Cinemeta knows it (for its IMDb number)
  function titleOf(x) { if (x.kind === 'episode') { var s = App.data.byKey['s:' + x.seriesId]; return s || { kind: 'series', title: x.seriesTitle || x.title, key: 's:' + x.seriesId }; } return x; }
  function find(x, langs) {
    var t = titleOf(x), ep = x.kind === 'episode';
    return Meta.find(t).then(function (m) {
      var imdb = m && /^tt(\d+)$/.exec(m.id || '');
      var q = Meta.cleanTitle(t.title).title, year = Meta.cleanTitle(t.title).year || (/(19|20)\d\d/.exec(t.year || '') || [''])[0];
      var path = 'subtitles?languages=' + langs.join(',') +
        (imdb ? (ep ? '&parent_imdb_id=' : '&imdb_id=') + String(Number(imdb[1])) : '&query=' + encodeURIComponent(q) + (year && !ep ? '&year=' + year : '')) +
        (ep && x.season ? '&season_number=' + Number(x.season) + '&episode_number=' + Number(x.episode) : '');
      return osCall('GET', path).then(function (d) { return (d && d.data) || []; });
    });
  }
  function download(fileId) {
    var login = Store.pref('osUser', '') && !token ? osCall('POST', 'login', { username: Store.pref('osUser', ''), password: Store.pref('osPass', '') }).then(function (d) { token = d.token || null; }, function () { }) : Promise.resolve();
    return login.then(function () { return osCall('POST', 'download', { file_id: Number(fileId) }, token); }).then(function (d) {
      if (!d.link) throw new Error(d.message || 'no download link');
      return Api.get(d.link, 30000);
    });
  }
  // the best file per language: the most downloaded, not made for the hard of hearing when there's a choice
  function best(list, lang) {
    var l = list.filter(function (r) { var a = r.attributes || {}; return a.language === lang && a.files && a.files.length; });
    l.sort(function (a, b) { var x = a.attributes, y = b.attributes; return (x.hearing_impaired ? 1 : 0) - (y.hearing_impaired ? 1 : 0) || (y.download_count || 0) - (x.download_count || 0); });
    return l[0] || null;
  }

  // ---------- one film or episode ----------
  function start(item, el) {
    stop(); it = item; box = el; have = {}; showing = null; note = ''; var g = ++gen;
    applyStyle();
    return Promise.all(LANGS.map(function (l) { return dbGet(item.key + '|' + l).then(function (r) { if (g === gen && r && r.text) have[l] = { cues: parse(r.text), name: r.name || NAMES[l] }; }); }));
  }
  function stop() { gen++; clearInterval(timer); timer = null; showing = null; it = null; lastText = null; }
  // shows one language (null: none of ours)
  function show(lang) {
    showing = lang && have[lang] ? lang : null; lastText = null;
    clearInterval(timer); timer = null;
    if (showing) { timer = setInterval(tick, 150); tick(); } else if (box) Fmt.subs(box, []);
  }
  function tick() {
    if (!showing || !it || !box) return;
    var c = have[showing].cues, t = Player.time() - delay(), i = cueAt(c, t), text = i >= 0 ? c[i].t : '';
    if (text !== lastText) { lastText = text; Fmt.subs(box, text ? [text] : []); }
  }
  // what the player starts with: the language wanted, from the TV's own copy or else found online.
  // English shows when there's no Dutch (and nothing else is showing). Resolves with the language shown, or null.
  function auto(want) {
    var g = gen, x = it;
    want = want || 'nl';
    var pick = function () { if (have[want]) return want; if (want === 'nl' && have.en) return 'en'; return null; };
    var p = pick();
    if (p) show(p);
    var missing = LANGS.filter(function (l) { return !have[l]; });
    if (!missing.length || !osKey() || LANGS.indexOf(want) < 0) return Promise.resolve(p);
    return fetchOnline(missing, g, x).then(function (got) {
      if (g !== gen) return showing;
      if (!showing || (got.nl && want === 'nl' && showing !== 'nl')) { var q = pick(); if (q) show(q); }
      if (got.nl || got.en) App.toast(got.nl ? 'Dutch subtitles found online' + (got.en ? ' (English too, under Subtitles)' : '') : 'English subtitles found online (no Dutch ones)', 3500);
      return showing;
    });
  }
  function fetchOnline(langs, g, x) {
    if (busy) return Promise.resolve({});
    busy = true; note = 'Looking online…';
    return find(x, langs).then(function (list) {
      var got = {};
      return Promise.all(langs.map(function (l) {
        var r = best(list, l); if (!r) return null;
        var a = r.attributes, name = NAMES[l] + '  –  ' + (a.release || 'OpenSubtitles');
        return download(a.files[0].file_id).then(function (text) {
          var cues = parse(text); if (!cues.length) return;
          dbPut(x.key + '|' + l, { text: text, name: name, when: Date.now() });
          if (g === gen) have[l] = { cues: cues, name: name };
          got[l] = 1;
        }, function (e) { note = e.message; });
      })).then(function () {
        busy = false;
        if (got.nl || got.en) note = '';
        else if (!list.length || note === 'Looking online…') note = 'Nothing found online for this one';
        return got;
      });
    }, function (e) { busy = false; note = e.message; return {}; });
  }

  // ---------- the look (Settings and the player's menu) ----------
  var SIZES = [['s', 'Small'], ['m', 'Normal'], ['l', 'Large'], ['xl', 'Very large']];
  var COLORS = [['white', 'White'], ['yellow', 'Yellow']];
  var BACKS = [['shadow', 'Outline'], ['box', 'Dark box']];
  function cycle(list, k, def) { var v = Store.pref(k, def), i = list.map(function (x) { return x[0]; }).indexOf(v); Store.setPref(k, list[(i + 1) % list.length][0]); applyStyle(); }
  function nameIn(list, v) { for (var i = 0; i < list.length; i++) if (list[i][0] === v) return list[i][1]; return list[0][1]; }
  function applyStyle() {
    var e = box || document.querySelector('.v-subs'); if (!e) return;
    e.className = 'v-subs size-' + Store.pref('subSize', 'm') + ' col-' + Store.pref('subColor', 'white') + ' back-' + Store.pref('subBack', 'shadow');
    lastText = null; if (showing) tick();
  }
  function styleItems() {
    return [
      { name: 'Size', value: nameIn(SIZES, Store.pref('subSize', 'm')), act: function () { cycle(SIZES, 'subSize', 'm'); } },
      { name: 'Colour', value: nameIn(COLORS, Store.pref('subColor', 'white')), act: function () { cycle(COLORS, 'subColor', 'white'); } },
      { name: 'Background', value: nameIn(BACKS, Store.pref('subBack', 'shadow')), act: function () { cycle(BACKS, 'subBack', 'shadow'); } },
      { name: 'Sound descriptions', value: Store.pref('subSdh', false) ? 'Hidden' : 'Shown', act: function () { Store.setPref('subSdh', !Store.pref('subSdh', false)); applyStyle(); } }];
  }

  // ---------- timing ----------
  function delayText(d) { d = d === undefined ? delay() : d; return Math.abs(d) < 0.05 ? 'in sync' : (d > 0 ? '+' : '−') + Math.abs(d).toFixed(1) + ' s'; }
  // "sync to a line": press just as someone says the next line (or the last one again)
  function syncTo(dir) {
    if (!showing) return;
    var c = have[showing].cues, now = Player.time(), t = now - delay(), i = cueAt(c, t);
    var j;
    if (dir > 0) { j = 0; while (j < c.length && c[j].s <= t) j++; if (j >= c.length) return; }
    else { j = i >= 0 ? i : (function () { var k = c.length - 1; while (k >= 0 && c[k].s > t) k--; return k; })(); if (j < 0) return; }
    setDelay(now - c[j].s); tick();
  }
  function timingItems() {
    return [
      { name: 'Sync: show the next line now', act: function () { syncTo(1); } },
      { name: 'Sync: show the last line again', act: function () { syncTo(-1); } },
      { name: '1 second earlier', act: function () { setDelay(delay() - 1); } },
      { name: '0.1 second earlier', act: function () { setDelay(delay() - 0.1); } },
      { name: '0.1 second later', act: function () { setDelay(delay() + 0.1); } },
      { name: '1 second later', act: function () { setDelay(delay() + 1); } },
      { name: 'Back in sync', act: function () { setDelay(0); } }];
  }
  // the film's own subtitles come from the player as they're due, so they can only be held back (shown later)
  function ownTimingItems() {
    return [
      { name: '1 second earlier', act: function () { setDelay(Math.max(0, delay() - 1)); } },
      { name: '0.1 second earlier', act: function () { setDelay(Math.max(0, delay() - 0.1)); } },
      { name: '0.1 second later', act: function () { setDelay(delay() + 0.1); } },
      { name: '1 second later', act: function () { setDelay(delay() + 1); } },
      { name: 'Back in sync', act: function () { setDelay(0); } }];
  }

  // ---------- Settings > OpenSubtitles ----------
  function settingsItems() {
    var k = osKey();
    return [
      { name: 'OpenSubtitles API key', value: k ? '••••' + k.slice(-4) : 'Not set', desc: 'Free: opensubtitles.com → your profile → API consumers. Dutch and English subtitles are then found for every film.', act: function () { App.ask('Your OpenSubtitles API key (empty: off)', Store.pref('osKey', ''), function (v) { Store.setPref('osKey', (v || '').trim()); token = null; }); } },
      { name: 'OpenSubtitles username', value: Store.pref('osUser', '') || 'None (5 a day)', desc: 'Optional: signed in you can get more subtitles a day', act: function () { App.ask('Your OpenSubtitles username (empty: none)', Store.pref('osUser', ''), function (v) { Store.setPref('osUser', (v || '').trim()); token = null; if ((v || '').trim()) App.ask('Your OpenSubtitles password', '', function (p) { Store.setPref('osPass', p || ''); token = null; }, true); }); } }];
  }

  return {
    start: start, stop: stop, show: show, auto: auto, tick: tick, applyStyle: applyStyle,
    get showing() { return showing; }, get busy() { return busy; }, get note() { return note; }, has: function (l) { return !!have[l]; }, nameOf: function (l) { return have[l] ? have[l].name : NAMES[l]; },
    LANGS: LANGS, NAMES: NAMES, hasKey: function () { return !!osKey(); },
    findNow: function () { var g = gen, x = it; if (!x) return Promise.resolve({}); var miss = LANGS.filter(function (l) { return !have[l]; }); return fetchOnline(miss.length ? miss : LANGS, g, x); },
    delayText: delayText, timingItems: timingItems, ownTimingItems: ownTimingItems, styleItems: styleItems, settingsItems: settingsItems, parse: parse,
    delay: delay, redraw: function () { lastText = null; tick(); }
  };
})();
