// Recomp TV: TV & Movies from Recomp Launcher, as an app on a Samsung TV.
//   For You, TV Live, Movies, Shows, Search  the header's tabs (up from the top of a page reaches them)
//   "Who's watching?"   profiles, each with its own My list, Continue watching, languages and settings
//   Settings            the login, profiles, languages, hiding adult content, audio and subtitles, stream type
// This file: the screens and getting from one to another, loading the provider's lists, the header, the profiles,
// Settings and Search. The pages themselves: browse.js (rows), live.js, details.js, vod.js.
var App = (function () {
  var $ = function (id) { return document.getElementById(id); };
  var stage = $('stage'), scale = 1, screen = null, history = [], inHeader = false, headIdx = 0;
  var data = { live: { cats: [], chans: [], byKey: {}, byNum: {}, lists: {} }, movies: [], series: [], byKey: {} };
  var acct = null, pages = {}, loaded = false;
  var TABS = [['home', 'For You'], ['live', 'TV Live'], ['guide', 'Guide'], ['movies', 'Movies'], ['shows', 'Shows'], ['search', 'Search']];

  // ---------- the page, fitted to the screen (a TV is 1920x1080 already) ----------
  function fit() {
    scale = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    stage.style.transform = scale === 1 ? '' : 'scale(' + scale + ')';
    stage.style.left = ((window.innerWidth - 1920 * scale) / 2) + 'px';
    stage.style.top = ((window.innerHeight - 1080 * scale) / 2) + 'px';
  }
  window.addEventListener('resize', fit);
  if (Player.isTv) document.body.className = 'tizen';

  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function tick() { var d = new Date(), t = pad(d.getHours()) + ':' + pad(d.getMinutes()); [].forEach.call(document.querySelectorAll('.clock'), function (c) { c.textContent = t; }); $('osd-clock').textContent = t; }
  setInterval(tick, 10000); tick();
  var toastTimer;
  function toast(msg, ms) { var t = $('toast'); t.textContent = msg; t.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(function () { t.hidden = true; }, ms || 2500); }

  // ---------- screens ----------
  var BROWSE = { home: 1, movies: 1, shows: 1, search: 1, category: 1 };
  var HEADER = { home: 1, live: 1, guide: 1, movies: 1, shows: 1, search: 1, settings: 1, category: 1 };
  function show(name, noHistory) {
    if (screen === name && name !== 'category') return;
    if (screen && !noHistory && screen !== 'loading' && screen !== 'setup' && screen !== 'profiles') history.push(screen);
    if (history.length > 20) history.shift();
    leave(screen);
    screen = name; inHeader = false;
    ['setup', 'loading', 'profiles', 'browse', 'live', 'guide', 'settings', 'details', 'vod'].forEach(function (s) { $(s).className = 'screen' + (s === place(name) ? ' on' : ''); });
    [].forEach.call(document.querySelectorAll('.bpage'), function (p) { p.style.display = p.id === 'page-' + name ? '' : 'none'; });
    var sk = $('search-keys'); if (sk) sk.style.display = name === 'search' ? '' : 'none';
    headerShown(!!HEADER[name]);
    drawHeader();
    enter(name);
    // the page comes in softly
    if (BROWSE[name]) Look.enter($('page-' + name));
    else if (name === 'settings' || name === 'live' || name === 'guide') Look.enter($(name), 'enter-fade');
  }
  function place(name) { return BROWSE[name] ? 'browse' : name; }
  function leave(name) {
    Peek.hide();
    if (name === 'live') Live.leave();
    if (name === 'guide') Guide.leave();
    if (name === 'vod') Vod.close(false);
  }
  function enter(name) {
    if (BROWSE[name]) { var p = page(name); if (p.built) p.refresh(); else build(name); }
    if (name === 'live') Live.enter(true);
    if (name === 'guide') { var gk = guideKey; guideKey = null; Guide.open(gk); }
    if (name === 'settings') Settings.open();
  }
  function back() {
    var to = history.pop();
    while (to === 'vod' || to === 'details' && !Details.item) to = history.pop();
    if (!to) to = 'home';
    show(to, true);
  }
  function page(name) {
    if (!pages[name]) { var root = document.createElement('div'); root.className = 'bpage'; root.id = 'page-' + name; $('browse').appendChild(root); pages[name] = new Browse.Page(root, name === 'category' ? { noHero: true, top: 290 } : name === 'search' ? { noHero: true, top: 150, width: 1920 - 600 - 80 } : {}); }
    return pages[name];
  }
  function build(name) {
    var p = page(name); p.built = true;
    if (name === 'home') p.setRows(Rows.home(), Browse.featured(Tidy.filter(data.movies)));
    else if (name === 'movies') p.setRows(Rows.vod(false), Browse.featured(Tidy.filter(data.movies).slice(0, 60).reverse()));
    else if (name === 'shows') p.setRows(Rows.vod(true), Browse.featured(Tidy.filter(data.series)));
    else if (name === 'search') { p.setRows(Rows.search(Search.text)); p.r = -1; p.update(); Search.draw(); }
    if (!p.rows.length && name !== 'search') p.root.appendChild(Browse.el('div', 'b-empty', name === 'shows' ? 'Your provider has no series (in the languages this profile shows).' : 'Nothing here yet (in the languages this profile shows).'));
  }
  // every page built again (a profile, its languages or the lists changed)
  function rebuild() { for (var k in pages) { pages[k].built = false; [].forEach.call(pages[k].root.querySelectorAll('.b-empty'), function (e) { e.parentNode.removeChild(e); }); } }

  // ---------- the header ----------
  function headerShown(on) { $('header').hidden = !on; }
  function drawHeader() {
    var tabs = $('htabs'); tabs.innerHTML = '';
    TABS.forEach(function (t, i) { var e = Browse.el('div', 'tab' + (t[0] === screen ? ' current' : '') + (inHeader && headIdx === i ? ' focus' : ''), t[1]); tabs.appendChild(e); });
    var lb = $('hlang'); lb.textContent = '🌐  ' + langLabel(); lb.className = 'hlang' + (inHeader && headIdx === TABS.length ? ' focus' : '');
    var p = Store.profile, av = $('hprofile');
    av.innerHTML = ''; av.className = 'hprofile' + (inHeader && headIdx === TABS.length + 1 ? ' focus' : '') + (screen === 'settings' ? ' current' : '');
    var a = Browse.el('span', 'avatar', (p.name || '?').charAt(0).toUpperCase()); a.style.background = p.color; av.appendChild(a);
    av.appendChild(Browse.el('span', 'pname', p.name));
    $('header').classList.toggle('solid', !BROWSE[screen] || screen === 'search' || screen === 'category');
  }
  function headerKey(k) {
    if (k === 'left') headIdx = Math.max(0, headIdx - 1);
    else if (k === 'right') headIdx = Math.min(TABS.length + 1, headIdx + 1);
    else if (k === 'back') { exitSoon(); return true; }   // twice: leave the app
    else if (k === 'down') { inHeader = false; drawHeader(); if (screen === 'live') Live.drawZones(); if (BROWSE[screen]) page(screen).update(); if (screen === 'search') Search.draw(); if (screen === 'settings') Settings.draw(); return true; }
    else if (k === 'ok') {
      if (headIdx === TABS.length) { pickLanguages(false); return true; }
      if (headIdx === TABS.length + 1) { show('settings'); return true; }
      var t = TABS[headIdx][0];
      if (t === screen) { inHeader = false; drawHeader(); if (screen === 'live') Live.drawZones(); if (BROWSE[screen]) page(screen).update(); }
      else show(t);
      if (t === 'search') Search.focus();
      return true;
    } else return false;
    drawHeader(); return true;
  }
  function toHeader() {
    inHeader = true; Peek.hide();
    headIdx = screen === 'settings' ? TABS.length + 1 : Math.max(0, TABS.map(function (t) { return t[0]; }).indexOf(screen));
    drawHeader(); if (screen === 'live') Live.drawZones(); if (screen === 'search') Search.draw();
  }

  // ================= sign in =================
  var Setup = (function () {
    var type = 'xtream', idx = 0, cancel = false, connecting = false, adding = false;
    function items() {
      var l = [].slice.call(document.querySelectorAll('#setup-type .seg-item'));
      l = [l[type === 'xtream' ? 0 : 1]];
      if (type === 'xtream') l.push($('f-server'), $('f-user'), $('f-pass')); else l.push($('f-url'));
      l.push($('setup-go')); if (cancel) l.push($('setup-cancel'));
      return l;
    }
    function draw() {
      [].forEach.call(document.querySelectorAll('#setup-type .seg-item'), function (s) { s.className = 'seg-item focusable' + (s.getAttribute('data-type') === type ? ' sel' : ''); });
      $('setup-xtream').hidden = type !== 'xtream'; $('setup-m3u').hidden = type !== 'm3u'; $('setup-cancel').hidden = !cancel;
      $('setup-hint').textContent = type === 'xtream' ? "Use the login from your provider's e-mail: the server, your username and your password." : 'Type the playlist link from your provider (it often ends in .m3u or has type=m3u_plus in it).';
      var l = items(); idx = Math.min(idx, l.length - 1);
      [].forEach.call(document.querySelectorAll('#setup .focusable'), function (e) { e.classList.remove('focus'); });
      l[idx].classList.add('focus');
    }
    // adding: another provider (empty fields; the one in use stays until this one signs in)
    function open(cancelable, add) {
      cancel = !!cancelable; adding = !!add;
      var a = adding ? null : Store.account;
      if (adding) { $('f-server').value = ''; $('f-user').value = ''; $('f-pass').value = ''; $('f-url').value = ''; }
      if (a) { type = a.type === 'm3u' ? 'm3u' : 'xtream'; $('f-server').value = a.base || ''; $('f-user').value = a.user || ''; $('f-pass').value = a.pass || ''; $('f-url').value = a.url || ''; }
      idx = type === 'xtream' && !$('f-server').value ? 1 : items().length - (cancel ? 2 : 1);
      $('setup-msg').textContent = ''; $('setup-msg').className = 'msg';
      show('setup'); draw();
    }
    function key(k) {
      var l = items(), cur = l[idx], typing = document.activeElement && document.activeElement.tagName === 'INPUT';
      if (typing) {
        if (k === 'imedone' || k === 'ok' || k === 'down') { document.activeElement.blur(); idx = Math.min(idx + 1, l.length - 1); draw(); return true; }
        if (k === 'imecancel' || k === 'back' || k === 'up') { document.activeElement.blur(); if (k === 'up') { idx = Math.max(0, idx - 1); draw(); } return true; }
        return false;
      }
      if (k === 'up') { idx = Math.max(0, idx - 1); draw(); }
      else if (k === 'down') { idx = Math.min(l.length - 1, idx + 1); draw(); }
      else if ((k === 'left' || k === 'right') && cur.classList.contains('seg-item')) { type = k === 'left' ? 'xtream' : 'm3u'; draw(); }
      else if ((k === 'left' || k === 'right') && cancel && (cur.id === 'setup-go' || cur.id === 'setup-cancel')) { idx = l.indexOf($(k === 'left' ? 'setup-go' : 'setup-cancel')); draw(); }
      else if (k === 'ok') {
        if (cur.classList.contains('seg-item')) { type = type === 'xtream' ? 'm3u' : 'xtream'; draw(); }
        else if (cur.tagName === 'INPUT') cur.focus();
        else if (cur.id === 'setup-go') connect();
        else if (cur.id === 'setup-cancel') show('settings', true);
      } else if (k === 'back') { if (cancel) show('settings', true); else exitSoon(); }
      return true;
    }
    function connect() {
      if (connecting) return;
      var a = type === 'xtream' ? { type: 'xtream', base: $('f-server').value.trim(), user: $('f-user').value.trim(), pass: $('f-pass').value } : { type: 'm3u', url: $('f-url').value.trim() };
      var msg = $('setup-msg');
      if (a.type === 'xtream' && (!a.base || !a.user || !a.pass)) { msg.textContent = 'Fill in the server, your username and your password.'; msg.className = 'msg bad'; return; }
      if (a.type === 'm3u' && !/^https?:\/\//i.test(a.url)) { msg.textContent = 'The link should start with http:// or https://'; msg.className = 'msg bad'; return; }
      connecting = true; msg.textContent = 'Connecting…'; msg.className = 'msg';
      Api.signIn(a).then(function (ok) {
        connecting = false;
        var old = Store.account;
        msg.textContent = 'Signed in.'; msg.className = 'msg good';
        // another provider (or one that's on the TV already): switched to, with its own lists and its own My list
        if (adding || !old) { switchTo(ok); return; }
        if (Store.sameAccount(old, ok)) Store.account = ok; else Store.replaceAccount(old, ok);
        acct = ok; load(true);
      }, function (e) { connecting = false; msg.textContent = "Couldn't sign in: " + e.message + '.'; msg.className = 'msg bad'; });
    }
    return { open: open, key: key };
  })();

  // ================= getting everything =================
  var loadFailed = false;
  // The lists are kept on the TV (Saved): the app opens with them at once. Once they're older than STALE it gets them
  // fresh from the provider at the start instead (waiting, as the first time). Never both at once: a big provider's
  // lists (140,000 films) twice over are more than a TV's memory holds, and the app would be closed.
  var STALE = 12 * 3600 * 1000;
  function load(fresh) {
    show('loading', true); loadFailed = false;
    $('loading-text').textContent = 'Getting your channels, films and series…';
    var a = acct, gen = ++loadGen;
    if (fresh) warmed = false;
    if (fresh) { fetchLists(a, gen); return; }
    Saved.get(a).then(function (c) {
      if (gen !== loadGen) return;
      if (!c || Date.now() - c.when > STALE) { c = null; fetchLists(a, gen); return; }
      use(c.cats, c.chans, c.movies, c.series);
      loaded = true; rebuild(); history = [];
      afterLoad();
      warmSoon(c.movies, c.series);
    });
  }
  var loadGen = 0;
  // to another provider: its lists kept on the TV (or got fresh), its My list and Continue watching
  function switchTo(a) {
    Player.stop();
    if (loaded) { use([], [], [], []); rebuild(); loaded = false; }
    Store.account = a; acct = a; warmed = false;
    load(false);
  }
  function provName(a) { a = a || {}; var host = (a.base || a.url || '').replace(/^https?:\/\//, '').split(/[\/?:]/)[0]; return host + (a.type === 'xtream' && a.user ? '  ·  ' + a.user : ''); }
  function providersMenu() {
    var l = Store.accounts.map(function (a) {
      var on = Store.sameAccount(a, acct);
      return { name: (on ? '✓  ' : '     ') + provName(a), act: on ? function () { } : function () { switchTo(a); } };
    });
    l.push({ name: '+  Add a provider', act: function () { setTimeout(function () { Setup.open(true, true); }, 0); } });
    l.push({ name: '✎  Change this login', act: function () { setTimeout(function () { Setup.open(true); }, 0); } });
    l.push({ name: '✕  Remove ' + provName(acct), act: function () { setTimeout(removeCurrent, 0); } });
    menu('Providers', l);
  }
  // the provider in use off the TV: to the next one, or (the last one) back to signing in
  function removeCurrent() {
    var a = acct, others = Store.accounts.filter(function (x) { return !Store.sameAccount(x, a); });
    menu('Remove ' + provName(a) + '?', [
      { name: others.length ? 'Yes, remove it (to ' + provName(others[0]) + ')' : 'Yes, remove it (sign in again after)', act: function () {
        Saved.forget(a);
        if (others.length) { switchTo(others[0]); Store.removeAccount(a); return; }
        Player.stop(); Store.signOut(); acct = null; $('f-pass').value = ''; Setup.open(false);
      } },
      { name: 'No', act: function () { } }]);
  }
  // the provider's lists
  function fetchLists(a, gen) {
    // the old lists let go of first (getting them again from Settings), so there's one copy in memory
    if (loaded) { use([], [], [], []); rebuild(); }
    // channels first (the rest can follow); films or series that fail don't stop the channels
    // (a 404 means the provider has none of them, as some do for series: that's an answer, kept like one)
    var gone = 0, none = function (e) { if (!/ 404$/.test(e && e.message || '')) gone++; return null; };
    Promise.all([Api.liveChannels(a), Api.movies(a).catch(none), Api.series(a).catch(none)]).then(function (r) {
      if (gen !== loadGen) return;
      var live = r[0], movies = r[1] || [], series = r[2] || [];
      // tidied as in the launcher: language tags read and taken off names and categories
      var final = Tidy.account(live.chans.concat(movies, series));
      var order = []; live.cats.forEach(function (c) { var n = final[c.name] || c.name; if (order.indexOf(n) < 0) order.push(n); });
      use(order, live.chans, movies, series);
      loaded = true; rebuild();
      history = []; afterLoad(); warmSoon(movies, series);
      // kept only when everything came: when films or series failed, the lists are got again at the next start
      if (!gone) Saved.put(a, { cats: order, chans: live.chans, movies: movies, series: series });
      else Saved.forget(a);
    }, function (e) {
      if (gen !== loadGen) return;
      $('loading-text').textContent = "Couldn't get the lists: " + e.message + '. Press OK to try again, or Back to change the login.';
      loadFailed = true;
    });
  }
  function use(order, chans, movies, series) {
    // the Dutch channels first, with a Dutch TV's numbers (NPO 1 is 1)
    var nl = Dutch.arrange(order, chans); order = nl.order; chans = nl.chans;
    // the main channels' copies measured (while nothing plays): the sharpest one gets the number
    if (nl.copies.length) Sharp.start(nl.copies, renumber);
    var L = data.live = { cats: order.map(function (n) { return { id: n, name: n }; }), chans: chans, byKey: {}, byNum: {}, lists: {} };
    chans.forEach(function (c) { L.byKey[c.key] = c; if (!L.byNum[c.num]) L.byNum[c.num] = c; (L.lists[c.cat] = L.lists[c.cat] || []).push(c); });
    data.movies = movies; data.series = series; data.byKey = {};
    chans.concat(movies, series).forEach(function (x) { data.byKey[x.key] = x; applyMeta(x); });
    // what you're in the middle of: its newest details
    Store.continuing().forEach(function (c) { var x = data.byKey[c.key]; if (x) Store.remember(x); });
  }
  // after a measurement: the numbers again (the lists stay; TV Live and the Guide show it when opened next)
  function renumber() { if (!loaded) return; use(data.live.cats.map(function (c) { return c.name; }), data.live.chans, data.movies, data.series); rebuild(); }
  var warmed = false;
  function warmSoon(movies, series) {
    if (warmed) return; warmed = true;
    // the newest titles looked up quietly (genres, pictures, trailers), then the pages get their genre rows
    setTimeout(function () {
      Meta.onDone(function () { rebuild(); if (BROWSE[screen] && !inHeader && page(screen).r <= 0) { build(screen); } });
      Meta.warm(Tidy.filter(movies).slice(0, 250).concat(Tidy.filter(series).slice(0, 250)));
    }, 3000);
  }

  // the lists kept on the TV, per login (IndexedDB: there's room for tens of thousands of titles). Only the plain
  // details go in (what the app adds while running, the fields starting with _, is left out).
  var Saved = (function () {
    var dbp = null;
    function db() {
      if (!dbp) dbp = new Promise(function (ok, fail) {
        try { var r = indexedDB.open('recomp-tv-lists', 1); r.onupgradeneeded = function () { r.result.createObjectStore('lists'); }; r.onsuccess = function () { ok(r.result); }; r.onerror = function () { fail(r.error); }; } catch (e) { fail(e); }
      });
      return dbp;
    }
    function id(a) { return (a.type || '') + '|' + (a.base || a.url || '') + '|' + (a.user || '') + '|' + Store.pref('format', 'auto'); }
    // Kept in pieces of PART titles, each read and written on its own: one piece of text for everything is tens of MB
    // (a big provider's), and a TV's memory doesn't hold that next to the lists themselves.
    var PART = 4000, KINDS = ['chans', 'movies', 'series'];
    function one(d, mode, f) {
      return new Promise(function (ok) {
        try { var t = d.transaction('lists', mode), s = t.objectStore('lists'), q = f(s); t.oncomplete = function () { ok(q ? q.result : null); }; t.onerror = t.onabort = function () { ok(null); }; } catch (e) { ok(null); }
      });
    }
    function get(a) {
      var key = id(a), out = { chans: [], movies: [], series: [] };
      return db().then(function (d) {
        return one(d, 'readonly', function (s) { return s.get(key); }).then(function (m) {
          if (!m || !m.parts) return null;   // nothing kept (or kept by 0.7 in one piece: got fresh instead)
          out.cats = m.cats; out.when = m.when;
          var jobs = [];
          KINDS.forEach(function (k) { for (var i = 0; i < (m.parts[k] || 0); i++) jobs.push([k, i]); });
          var next = function (n) {
            if (n >= jobs.length) return out;
            return one(d, 'readonly', function (s) { return s.get(key + '#' + jobs[n][0] + '#' + jobs[n][1]); }).then(function (v) {
              if (!v) throw new Error('a piece is missing');
              var l = JSON.parse(v); for (var i = 0; i < l.length; i++) out[jobs[n][0]].push(l[i]);
              return next(n + 1);
            });
          };
          return next(0);
        });
      }).catch(function () { return null; });
    }
    // a series' episodes and a film's extra details are asked for again when opened (new episodes show up);
    // your own name, picture or story goes back to the provider's (it's put on again when the lists load)
    var skip = { episodes: 1, infoDone: 1 };
    function plain(k, v) {
      if (k.charAt(0) === '_' || skip[k]) return undefined;
      if (v && v._orig) { var c = {}; for (var f in v) c[f] = v[f]; c.title = v._orig.title; c.image = v._orig.image; c.plot = v._orig.plot; c.backdrop = v._orig.backdrop; return c; }
      return v;
    }
    var putGen = 0;
    function put(a, lists) {
      var key = id(a), g = ++putGen, parts = {}, jobs = [];
      KINDS.forEach(function (k) { var l = lists[k] || []; parts[k] = Math.ceil(l.length / PART); for (var i = 0; i < parts[k]; i++) jobs.push([k, i]); });
      setTimeout(function () {
        db().then(function (d) {
          // this login's pieces kept before go first (each login's lists are kept apart); the list of pieces last, so a half-written
          // set (the app closed meanwhile) is never read
          return one(d, 'readwrite', function (s) { tidy(s, key); }).then(function () {
            var next = function (n) {
              if (g !== putGen) return;
              if (n >= jobs.length) return one(d, 'readwrite', function (s) { s.put({ parts: parts, cats: lists.cats, when: Date.now() }, key); });
              var k = jobs[n][0], i = jobs[n][1], text;
              try { text = JSON.stringify(lists[k].slice(i * PART, (i + 1) * PART), plain); } catch (e) { return; }
              return one(d, 'readwrite', function (s) { s.put(text, key + '#' + k + '#' + i); }).then(function () {
                text = null;
                return new Promise(function (ok) { setTimeout(ok, 30); }).then(function () { return next(n + 1); });
              });
            };
            return next(0);
          });
        }).catch(function () { });
      }, 5000);
    }
    // this login's pieces kept before, and those of logins no longer on the TV (removed, or another stream type)
    function tidy(s, key) {
      var keep = Store.accounts.map(id), r = s.getAllKeys();
      r.onsuccess = function () { (r.result || []).forEach(function (k) { var base = String(k).split('#')[0]; if (base === key || keep.indexOf(base) < 0) s['delete'](k); }); };
    }
    function forget(a) { putGen++; var key = id(a); db().then(function (d) { return one(d, 'readwrite', function (s) { tidy(s, key); }); }).catch(function () { }); }
    return { get: get, put: put, forget: forget };
  })();
  function afterLoad() {
    if ((Store.profiles.length > 1 || Store.profile.pin) && !Store.chosen) Profiles.open();
    else { Store.useProfile(Store.profile.id); show(Store.pref('startTab', 'home'), true); askLanguagesOnce(); }
  }
  function askLanguagesOnce() {
    var p = Store.profile;
    if (p.langsAsked || (p.langs && p.langs.length)) return;
    if (Tidy.langs(data.live.chans.concat(data.movies, data.series)).length < 2) return;
    setTimeout(function () { if (!menuing && !asking) pickLanguages(true); }, 400);
  }

  // ================= "Who's watching?" =================
  var Profiles = (function () {
    var idx = 0, editing = null, removeAsk = null;
    function open() {
      idx = Math.max(0, Store.profiles.indexOf(Store.profile)); removeAsk = null; leaving = false;
      $('profiles').classList.remove('drawn', 'leaving'); show('profiles', true); draw();
      setTimeout(function () { $('profiles').classList.add('drawn'); }, 700);
    }
    var leaving = false;
    function draw() {
      var g = $('prof-grid'); g.innerHTML = '';
      Store.profiles.forEach(function (p, i) {
        var t = Browse.el('div', 'prof' + (i === idx ? ' focus' : ''));
        var a = Browse.el('div', 'prof-av', (p.name || '?').charAt(0).toUpperCase()); a.style.background = p.color; t.appendChild(a);
        if (p.pin) a.appendChild(Browse.el('div', 'prof-lock', '🔒'));
        t.appendChild(Browse.el('div', 'prof-name', p.name)); g.appendChild(t);
      });
      if (Store.profiles.length < 8) { var add = Browse.el('div', 'prof add' + (idx === Store.profiles.length ? ' focus' : '')); add.appendChild(Browse.el('div', 'prof-av', '+')); add.appendChild(Browse.el('div', 'prof-name', 'Add profile')); g.appendChild(add); }
      $('prof-hint').textContent = removeAsk ? 'Press red again to remove ' + removeAsk.name + ' (its lists go too).' : 'OK  Watch      Green  Rename      Blue  Colour      Red  Remove';
      $('prof-edit').hidden = !editing;
    }
    function ask(title, value, done) {
      editing = done; $('prof-edit-t').textContent = title; var f = $('prof-edit-f'); f.value = value || ''; $('prof-edit').hidden = false; f.focus();
    }
    function key(k) {
      var n = Store.profiles.length, max = n < 8 ? n : n - 1;
      if (editing) {
        if (k === 'imedone' || k === 'ok') { var v = $('prof-edit-f').value.trim(), f = editing; editing = null; $('prof-edit-f').blur(); if (v) f(v); draw(); return true; }
        if (k === 'imecancel' || k === 'back') { editing = null; $('prof-edit-f').blur(); draw(); return true; }
        return false;
      }
      var p = Store.profiles[idx];
      if (k === 'left') { idx = Math.max(0, idx - 1); removeAsk = null; }
      else if (k === 'right') { idx = Math.min(max, idx + 1); removeAsk = null; }
      else if (k === 'ok') {
        if (idx === n) { ask('Name of the new profile', '', function (v) { Store.addProfile(v); idx = Store.profiles.length - 1; }); return true; }
        if (leaving) return true;
        var go = function () { leaving = false; Store.useProfile(p.id); rebuild(); history = []; show(Store.pref('startTab', 'home'), true); askLanguagesOnce(); };
        // the chosen profile zooms towards you, the rest fades
        var at = idx;
        unlock(p, function () {
          if (Look.calm()) { go(); return; }
          leaving = true; var tiles = $('prof-grid').children; if (tiles[at]) tiles[at].classList.add('picked'); $('profiles').classList.add('leaving');
          setTimeout(go, 430);
        });
        return true;
      }
      // a locked profile can only be changed with its PIN
      else if (k === 'green' && p) { unlock(p, function () { setTimeout(function () { ask('New name', p.name, function (v) { Store.renameProfile(p.id, v); }); }, 0); }, 'Enter the PIN to rename ' + p.name); return true; }
      else if (k === 'blue' && p) { unlock(p, function () { Store.nextColor(p.id); draw(); }, 'Enter the PIN to change ' + p.name); return true; }
      else if (k === 'red' && p) {
        if (n < 2) { toast('The last profile stays'); return true; }
        if (removeAsk === p) { unlock(p, function () { Store.removeProfile(p.id); removeAsk = null; idx = Math.max(0, idx - 1); draw(); }, 'Enter the PIN to remove ' + p.name); return true; }
        else removeAsk = p;
      }
      else if (k === 'back') { if (Store.chosen) show(history.length ? history.pop() : 'home', true); else exitSoon(); return true; }
      draw(); return true;
    }
    return { open: open, key: key };
  })();

  // ================= Settings =================
  var Settings = (function () {
    var idx = 0, sub = null, subIdx = 0;
    function audioName(v) { return !v ? 'Automatic' : Player.langName(v.split(',')[0]); }
    var LANG_CHOICES = ['', 'en', 'nl', 'de', 'fr', 'es', 'it', 'pt', 'tr', 'ar', 'pl'];
    function items() {
      var p = Store.profile, a = acct || {}, f = Store.pref('format', 'auto'), langs = p.langs || [];
      return [
        { name: 'Profiles', value: p.name, desc: "Switch profile, or add, rename and remove profiles (Who's watching?)", act: function () { Profiles.open(); } },
        { name: 'Providers', value: provName(acct) + (Store.accounts.length > 1 ? '  (+' + (Store.accounts.length - 1) + ')' : ''), desc: 'Switch, add, change or remove a provider (each has its own My list)', act: providersMenu },
        { name: 'Languages', value: langs.length ? langs.map(Tidy.nameOf).join(', ') : 'All', desc: 'Only the channels, films and series in the languages this profile picks', act: function () { pickLanguages(false); } },
        { name: 'PIN lock', value: p.pin ? 'On' : 'Off', desc: p.pin ? 'This profile asks for its PIN before it opens. Change or remove it here' : 'Ask for a PIN of 4 digits before this profile opens (for children)', act: function () { pinSetting(p, draw); } },
        { name: 'Hide adult content', value: p.noAdult ? 'On' : 'Off', desc: 'Categories and titles with adult words stay out of this profile', act: function () { p.noAdult = !p.noAdult; Store.save(); rebuild(); draw(); } },
        { name: 'Audio language', value: audioName(p.audioLang), desc: 'The audio track films and episodes start with, when they have that language', act: function () { p.audioLang = LANG_CHOICES[(LANG_CHOICES.indexOf(p.audioLang || '') + 1) % LANG_CHOICES.length]; Store.save(); draw(); } },
        { name: 'Subtitles', value: p.subLang === 'off' ? 'Off' : !p.subLang ? 'Nederlands (English extra)' : Player.langName(p.subLang), desc: "Subtitles to start with: the film's own, else found online (OpenSubtitles)", act: function () { var l = ['', 'off'].concat(LANG_CHOICES.slice(1).filter(function (x) { return x !== 'nl'; })); p.subLang = l[(l.indexOf(p.subLang || '') + 1) % l.length]; Store.save(); draw(); } },
        { name: 'Original language first', value: Store.pref('origAudio', true) ? 'On' : 'Off', desc: 'A film or series from elsewhere is heard in its own language, not dubbed (when your provider has that audio)', act: function () { Store.setPref('origAudio', !Store.pref('origAudio', true)); draw(); } },
      ].concat(Subs.styleItems().map(function (x) { return { name: 'Subtitle ' + x.name.toLowerCase(), value: x.value, desc: 'How subtitles look', act: function () { x.act(); draw(); } }; }), Subs.settingsItems().map(function (x) { return { name: x.name, value: x.value, desc: x.desc, act: function () { x.act(); setTimeout(draw, 50); } }; }), [
        { name: 'Picture size', value: Player.fitName(), desc: 'Fit shows all of it; Fill zooms in to fill the screen (films with black bars, 4:3 channels)', act: function () { Player.nextFit(); draw(); } },
        { name: 'Best version of a film', value: Best.modeName(), desc: 'Plays the best copy your provider has (the biggest file): in the same language, or in any of your languages', act: function () { Best.nextMode(); draw(); } },
        { name: 'Skip intro button', value: Store.pref('skipIntro', true) ? 'On' : 'Off', desc: "In an episode's first minutes: OK jumps past the intro", act: function () { Store.setPref('skipIntro', !Store.pref('skipIntro', true)); draw(); } },
        { name: 'Calm mode', value: Look.calm() ? 'On' : 'Off', desc: 'No animations or fades (if the TV stutters)', act: function () { Store.setPref('calm', !Look.calm()); Look.applyCalm(); draw(); } },
        { name: 'Trailers in the cards', value: Store.pref('trailers', true) ? 'On' : 'Off', desc: 'A film or series you stay on plays its trailer over its card', act: function () { Store.setPref('trailers', !Store.pref('trailers', true)); draw(); } },
        { name: 'Opens on', value: { home: 'For You', live: 'TV Live', guide: 'Guide', movies: 'Movies', shows: 'Shows' }[Store.pref('startTab', 'home')], desc: 'The page the app starts on', act: function () { var l = ['home', 'live', 'guide', 'movies', 'shows']; Store.setPref('startTab', l[(l.indexOf(Store.pref('startTab', 'home')) + 1) % l.length]); draw(); } },
        { name: 'Stream type', value: f === 'auto' ? 'Automatic' : f === 'm3u8' ? 'HLS' : 'MPEG-TS', desc: "If channels won't play, try the other one", act: function () { var n = f === 'auto' ? 'm3u8' : f === 'm3u8' ? 'ts' : 'auto'; Store.setPref('format', n); toast('Stream type: ' + (n === 'auto' ? 'Automatic' : n === 'm3u8' ? 'HLS' : 'MPEG-TS') + '. Getting the lists again…'); setTimeout(function () { load(true); }, 600); } },
        { name: 'Get the lists again', desc: 'When your provider has added channels, films or series', act: function () { load(true); } }
      ]);
    }
    function langItems() {
      var p = Store.profile; if (!p.langs) p.langs = [];
      var all = data.live.chans.concat(data.movies, data.series), l = Tidy.langs(all);
      var none = all.filter(function (x) { return !x.lang; }).length;
      var out = l.map(function (kv) { var on = p.langs.indexOf(kv[0]) >= 0; return { name: Tidy.nameOf(kv[0]) + (Tidy.nameOf(kv[0]) !== kv[0] ? '  (' + kv[0] + ')' : ''), value: on ? 'On' : 'Off', desc: kv[1] + ' channels, films and series', on: on, act: function () { var i = p.langs.indexOf(kv[0]); if (i >= 0) p.langs.splice(i, 1); else p.langs.push(kv[0]); Store.save(); rebuild(); } }; });
      out.push({ name: 'Without a language tag', value: p.langStrict ? 'Hidden' : 'Shown', desc: none + ' have no language in their name or category', act: function () { p.langStrict = !p.langStrict; Store.save(); rebuild(); } });
      out.push({ name: 'All languages', desc: 'Turns the language filter off', act: function () { p.langs = []; p.langStrict = false; Store.save(); rebuild(); } });
      out.push({ name: 'Done', act: function () { sub = null; } });
      return out;
    }
    function open() { idx = 0; sub = null; card(); draw(); }
    function card() {
      var a = acct || {}, c = $('acct-card'), host = (a.base || a.url || '').replace(/^https?:\/\//, '').split(/[\/?]/)[0];
      c.innerHTML = ''; c.appendChild(Browse.el('h2', null, 'Your provider'));
      var add = function (label, v) { if (!v) return; var d = Browse.el('div', null, label + ': '); d.appendChild(Browse.el('b', null, v)); c.appendChild(d); };
      add('Server', host); if (a.type === 'xtream') add('Username', a.user); add('Status', a.status);
      if (a.exp) { var d = new Date(a.exp * 1000); add('Until', d.getDate() + ' ' + ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'][d.getMonth()] + ' ' + d.getFullYear()); }
      if (a.maxConn) add('Screens at once', String(a.maxConn));
      add('Channels', data.live.chans.length ? String(data.live.chans.length) : ''); add('Films', data.movies.length ? String(data.movies.length) : ''); add('Series', data.series.length ? String(data.series.length) : '');
      c.appendChild(Browse.el('div', 'card-note', 'Recomp TV 0.7.10  ·  from Recomp Launcher'));
    }
    function draw() {
      var el = $('setting-list'); el.innerHTML = '';
      var l = sub ? langItems() : items(), i0 = sub ? subIdx : idx;
      $('settings-title').textContent = sub ? 'Languages  ·  ' + Store.profile.name : 'Settings  ·  ' + Store.profile.name;
      var first = Math.max(0, Math.min(i0 - 2, l.length - 6));
      l.slice(first, first + 6).forEach(function (s, j) {
        var i = first + j, d = Browse.el('div', 'setting' + (!inHeader && i === i0 ? ' focus' : '') + (s.on ? ' on' : ''));
        var left = Browse.el('div', null, s.name); if (s.desc) left.appendChild(Browse.el('span', 'd', s.desc)); d.appendChild(left);
        if (s.value) d.appendChild(Browse.el('div', 'v', s.value));
        el.appendChild(d);
      });
    }
    function key(k) {
      var l = sub ? langItems() : items(), i = sub ? subIdx : idx;
      if (k === 'up') { if (i === 0) { if (sub) return true; return 'top'; } i--; }
      else if (k === 'down') i = Math.min(l.length - 1, i + 1);
      else if (k === 'ok') { l[i].act(); if (screen !== 'settings') return true; }
      else if (k === 'back') { if (sub) { sub = null; draw(); return true; } back(); return true; }
      else return false;
      if (sub) subIdx = i; else idx = i;
      draw(); return true;
    }
    return { open: open, key: key, draw: draw };
  })();

  // ================= Search =================
  // As in the launcher (TvSearch.cs): letters on the left to type with the arrows (or the TV's own keyboard, or the
  // remote's numbers), your recent searches under them, and what's found on the right as it's typed.
  //   letters   OK types it; right from the last column goes to the results; down under the letters: recent searches
  //   results   left from the first one goes back to the letters; OK opens it (and keeps the search as a recent one)
  var Search = (function () {
    var text = '', timer, zone = 'keys', kr = 0, kc = 0, ri = 0, panel = null;
    var LETTERS = 'abcdefghijklmnopqrstuvwxyz1234567890'.split(''), COLS = 6;
    var LAST = [['space', '␣  Space'], ['del', '⌫  Delete'], ['kbd', '⌨  Keyboard']];
    function rowsN() { return Math.ceil(LETTERS.length / COLS) + 1; }
    function recents() { var r = Store.pref('recentSearches', []); return Array.isArray(r) ? r : []; }
    function keepRecent() {
      var t = text.trim(); if (t.length < 2) return;
      var l = recents().filter(function (x) { return x.toLowerCase() !== t.toLowerCase(); }); l.unshift(t); if (l.length > 8) l.length = 8;
      Store.setPref('recentSearches', l);
    }
    function ensurePanel() {
      if (panel) return panel;
      panel = Browse.el('div', null); panel.id = 'search-keys'; $('browse').appendChild(panel);
      var f = document.createElement('input'); f.id = 'search-f'; f.type = 'text'; f.setAttribute('autocomplete', 'off'); f.className = 'search-ime';
      f.addEventListener('input', function () { text = f.value; changed(); });
      panel.appendChild(f);
      return panel;
    }
    function changed() {
      clearTimeout(timer); draw();
      timer = setTimeout(function () { var p = page('search'); p.setRows(Rows.search(text)); if (zone !== 'results') { p.r = -1; p.update(); } else if (!p.rows.length) toKeys(); draw(); }, 300);
    }
    function draw() {
      var pn = ensurePanel(), f = $('search-f'), on = function (cond) { return cond && !inHeader ? ' focus' : ''; };
      [].forEach.call(pn.querySelectorAll('.sk-box,.sk-grid,.sk-recent,.sk-found'), function (e) { pn.removeChild(e); });
      var bx = Browse.el('div', 'sk-box'); bx.appendChild(Browse.el('span', 'sk-icon', '⌕'));
      var tx = Browse.el('span', 'sk-text' + (text ? '' : ' empty') + (zone === 'keys' ? ' caret' : ''), text || 'Films, series, people'); bx.appendChild(tx);
      pn.insertBefore(bx, f);
      var g = Browse.el('div', 'sk-grid'); pn.appendChild(g);
      LETTERS.forEach(function (c, i) { g.appendChild(Browse.el('div', 'sk-k' + on(zone === 'keys' && Math.floor(i / COLS) === kr && i % COLS === kc), c.toUpperCase())); });
      var lr = rowsN() - 1;
      LAST.forEach(function (x, j) { g.appendChild(Browse.el('div', 'sk-k wide' + on(zone === 'keys' && kr === lr && Math.floor(kc / 2) === j), x[1])); });
      var p = page('search');
      pn.appendChild(Browse.el('div', 'sk-found', !text ? '' : p.rows.length ? '' : 'Nothing found for "' + text + '"'));
      var rc = recents();
      if (rc.length) {
        var box = Browse.el('div', 'sk-recent'); box.appendChild(Browse.el('div', 'sk-recent-t', 'Recent searches'));
        rc.slice(0, 5).forEach(function (x, i) { box.appendChild(Browse.el('div', 'sk-r' + on(zone === 'recent' && i === ri), '↺   ' + x)); });
        pn.appendChild(box);
      }
      pn.style.display = screen === 'search' ? '' : 'none';
      if (f && document.activeElement !== f) f.value = text;
    }
    function press() {
      var lr = rowsN() - 1;
      if (kr < lr) { text += LETTERS[kr * COLS + kc]; changed(); return; }
      var what = LAST[Math.floor(kc / 2)][0];
      if (what === 'space') { if (text && !/ $/.test(text)) { text += ' '; changed(); } }
      else if (what === 'del') { text = text.slice(0, -1); changed(); }
      else if (what === 'kbd') { var f = $('search-f'); f.value = text; f.focus(); }
    }
    function toResults() { var p = page('search'); if (!p.rows.length) return false; zone = 'results'; p.r = 0; p.cols[0] = 0; p.update(); draw(); return true; }
    function toKeys() { var p = page('search'); zone = 'keys'; p.r = -1; p.update(); draw(); }
    function focus() { inHeader = false; drawHeader(); if (zone === 'results') toKeys(); else draw(); }
    function key(k) {
      var f = $('search-f'), typing = document.activeElement === f;
      if (typing) {
        if (k === 'imedone' || k === 'ok' || k === 'down') { f.blur(); text = f.value; changed(); return true; }
        if (k === 'imecancel' || k === 'back' || k === 'up') { f.blur(); return true; }
        return false;
      }
      var p = page('search'), lr = rowsN() - 1, rc = recents().slice(0, 5);
      if (/^d\d$/.test(k) && zone !== 'results') { text += k.charAt(1); changed(); return true; }
      if (zone === 'keys') {
        if (k === 'up') { if (kr === 0) return 'top'; kr--; }
        else if (k === 'down') { if (kr < lr - 1) kr++; else if (kr === lr - 1) { kr = lr; kc = Math.min(kc, COLS - 1); } else if (rc.length) { zone = 'recent'; ri = 0; } }
        else if (k === 'left') { if (kr === lr) kc = Math.max(0, Math.floor(kc / 2) * 2 - 2); else kc = Math.max(0, kc - 1); }
        else if (k === 'right') {
          var edge = kr === lr ? Math.floor(kc / 2) === LAST.length - 1 : kc === COLS - 1;
          if (edge) { toResults(); return true; }
          kc = kr === lr ? Math.floor(kc / 2) * 2 + 2 : kc + 1;
        }
        else if (k === 'ok') { press(); return true; }
        else if (k === 'red') { text = text.slice(0, -1); changed(); return true; }
        else if (k === 'back') { back(); return true; }
        else return false;
        draw(); return true;
      }
      if (zone === 'recent') {
        if (k === 'up') { if (ri === 0) { zone = 'keys'; kr = lr; } else ri--; }
        else if (k === 'down') ri = Math.min(rc.length - 1, ri + 1);
        else if (k === 'ok') { text = rc[ri]; zone = 'keys'; changed(); return true; }
        else if (k === 'right') { if (toResults()) return true; }
        else if (k === 'back') zone = 'keys';
        else return false;
        draw(); return true;
      }
      // the results
      if (k === 'left' && (!p.rows[p.r] || p.cols[p.r] === 0)) { toKeys(); return true; }
      if (k === 'ok' || k === 'play' || k === 'playpause') keepRecent();
      if (k === 'back') { toKeys(); return true; }
      var r = p.key(k);
      if (r === 'top') { toKeys(); return true; }
      return r;
    }
    return { draw: draw, key: key, focus: focus, get text() { return text; } };
  })();

  // ================= opening things =================
  function open(it, row) {
    if (!it) return;
    if (it.kind === 'live') { Store.last = it.key; show('live'); return; }
    if (it.kind === 'episode') { play(it, Store.resumeAt(it.key)); return; }
    if (it.kind === 'collection') { openCategory(it.title, it.films); return; }
    openDetails(it);
  }
  function openDetails(it) { if (!it) return; if (it.kind === 'live' || it.kind === 'collection') return open(it); Peek.hide(); show('details'); Details.open(it); }
  function play(it, start) {
    if (!it) return;
    if (it.kind === 'live' || it.kind === 'collection') return open(it);
    if (it.kind === 'archive') { Peek.hide(); show('vod'); Vod.open(it, start || 0); return; }
    if (it.kind === 'series') {
      Api.seriesInfo(acct, it).then(function () { var e = Details.nextEpisode(it); if (e) play(e, Store.resumeAt(e.key)); else openDetails(it); });
      return;
    }
    if (!it.url) { var full = data.byKey[it.key]; if (full) it = full; else { toast("This isn't at your provider any more"); return; } }
    if (start === undefined) start = Store.resumeAt(it.key);
    Peek.hide(); show('vod'); Vod.open(it, start);
  }
  // a channel full screen (from the guide, a reminder, a row)
  function watchLive(ch, full) { Store.last = ch.key; Live.pendingFull = !!full; if (screen === 'live') { Live.leave(); Live.enter(true); } else show('live'); }
  var guideKey = null;
  function openGuide(chKey) { guideKey = chKey || null; if (screen === 'guide') Guide.open(guideKey); else show('guide'); }

  // ---------- reminders (set in the guide): a note when the programme starts, OK watches it ----------
  var reminderOn = null, reminderTimer;
  setInterval(function () {
    if (!loaded) return;
    var l = Store.profile.reminders || [], t = Date.now() / 1000, due = null;
    l.forEach(function (r) { if (!r.shown && r.start <= t + 30 && r.stop > t) due = r; });
    if (!due) return;
    due.shown = true; Store.save();
    var ch = data.live.byKey[due.ch]; if (!ch) return;
    reminderOn = ch;
    var n = $('reminder'); n.innerHTML = '';
    n.appendChild(Browse.el('div', 'rm-t', 'Starting now on ' + ch.title));
    n.appendChild(Browse.el('div', 'rm-n', due.title));
    n.appendChild(Browse.el('div', 'rm-k', 'OK  Watch now      Back  Not now'));
    n.hidden = false; clearTimeout(reminderTimer); reminderTimer = setTimeout(function () { n.hidden = true; reminderOn = null; }, 20000);
  }, 15000);

  // ---------- More: what you can do with a film, series or episode (as in the launcher's item menu) ----------
  // Play next / Add to queue, My list, watched or not, out of Continue watching; 'extra' adds the page's own items
  function more(x, extra) {
    if (!x || x.kind === 'live' || x.kind === 'collection') return;
    var name = x.kind === 'episode' ? 'S' + x.season + ':E' + x.episode + '  ' + (x.seriesTitle || '') : Meta.cleanTitle(x.title).title;
    var after = function (msg) { rebuild(); if (BROWSE[screen]) page(screen).refresh(); if (screen === 'details') Details.redraw(); if (msg) toast(msg); };
    // a series: its next episode is what's queued, and "watched" means all its episodes
    var withEpisodes = function (f) { if (x.kind !== 'series') return f(); toast('Getting the episodes…'); Api.seriesInfo(acct, x).then(function () { f(); }, function () { toast("Couldn't get the episodes"); }); };
    var queued = function () { return x.kind === 'series' ? (x.episodes || []).some(function (e) { return Vod.inQueue(e.key); }) : Vod.inQueue(x.key); };
    var items = [];
    if (queued()) items.push({ name: '✕  Take it out of the queue', act: function () { (x.kind === 'series' ? x.episodes || [] : [x]).forEach(function (e) { Vod.queueRemove(e.key); }); toast('Out of the queue'); } });
    else {
      var q = function (front) { withEpisodes(function () {
        var e = x.kind === 'series' ? Details.nextEpisode(x) : x;
        if (!e) { toast('No episode to queue'); return; }
        Vod.queueAdd(e, front);
        toast(front ? '⏭  Plays next, after what you start watching' : '＋  Added to the queue (' + Vod.queue.length + ')');
      }); };
      items.push({ name: '⏭  Play next', act: function () { q(true); } });
      items.push({ name: '＋  Add to queue', act: function () { q(false); } });
    }
    if (x.kind !== 'episode') items.push({ name: Store.isFav(x.key) ? '✓  Take it out of My list' : '+  Add to My list', act: function () { toggleList(x); after(); } });
    // watched or not
    var eps = function () { return x.kind === 'series' ? x.episodes || [] : [x]; };
    var allSeen = x.kind === 'series' ? !!(x.episodes && x.episodes.length && x.episodes.every(function (e) { return Store.isWatched(e.key); })) : Store.isWatched(x.key);
    items.push({ name: allSeen ? '○  Mark as not watched' : '✓  Mark as watched' + (x.kind === 'series' ? ' (every episode)' : ''), act: function () {
      withEpisodes(function () {
        eps().forEach(function (e) { Store.setWatched(e.key, !allSeen); Store.clearProgress(e.key); });
        if (x.kind === 'series') Store.clearProgress('s:' + x.id);
        after(allSeen ? 'Marked as not watched' : 'Marked as watched');
      });
    } });
    // out of Continue watching (where you were is forgotten; nothing else changes)
    var cont = Store.continuing().filter(function (c) {
      if (c.key === x.key) return true;
      var r = Store.remembered(c.key); return x.kind === 'series' && r && r.seriesId === x.id;
    });
    if (cont.length) items.push({ name: '✕  Remove from Continue watching', act: function () {
      cont.forEach(function (c) { Store.clearProgress(c.key); });
      if (x.kind === 'series') Store.clearProgress('s:' + x.id);
      after('Removed from Continue watching');
    } });
    if (x.kind !== 'episode' && screen !== 'details') items.push({ name: 'ⓘ  More info', act: function () { openDetails(x); } });
    (extra || []).forEach(function (i) { items.push(i); });
    menu(name, items);
  }
  function moreFor(p) { var x = p.r < 0 ? p.hero : p.current(); if (x) more(x); }

  // ---------- People: everything with someone in it ----------
  function openPerson(who) {
    var rows = Rows.person(who), p = page('category'); p.built = true;
    var head = p.root.querySelector('.cat-head'); if (!head) { head = Browse.el('div', 'cat-head'); p.root.appendChild(head); }
    head.textContent = who;
    show('category');
    if (!rows.length) { p.setRows([]); toast('Nothing else with ' + who + ' at your provider'); return; }
    p.setRows(rows);
  }

  // ---------- your own details (a name, picture or story), as in the launcher ----------
  function applyMeta(x) {
    var m = Store.meta(x.key); if (!m && !x._orig) return;
    if (!x._orig) x._orig = { title: x.title, image: x.image, plot: x.plot, backdrop: x.backdrop };
    var o = x._orig; m = m || {};
    x.title = m.title || o.title; x.image = m.image || o.image; x.plot = m.plot || o.plot; x.backdrop = m.image ? '' : o.backdrop;
    x._same = null; x._artAsked = !!m.image;
  }
  function setMeta(x, field, v) { Store.setMeta(x.key, field, v); applyMeta(x); rebuild(); }
  function resetMeta(x) { Store.clearMeta(x.key); applyMeta(x); rebuild(); }

  // ---------- a profile's PIN lock (4 digits), as in the launcher ----------
  // Kept as a hash, never as the digits. It keeps children out of a profile; it isn't a safe (the TV's own storage
  // isn't locked, and removing the app's data removes the PINs too).
  function pinHash(p, code) {
    var h = 2166136261, s = 'recomp-tv|' + p.id + '|' + code;
    for (var i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    return 'f' + h.toString(36);
  }
  var Pin = (function () {
    var st = null, KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'del', '0', 'cancel'];
    function open(p, title, sub, done, cancel) { st = { p: p, title: title, sub: sub || '', typed: '', done: done, cancel: cancel, idx: 0, bad: '' }; draw(); }
    function close() { st = null; draw(); }
    function draw() {
      var e = $('pin'); e.hidden = !st; e.innerHTML = ''; if (!st) return;
      var av = Browse.el('div', 'pin-av', (st.p.name || '?').charAt(0).toUpperCase()); av.style.background = st.p.color; e.appendChild(av);
      e.appendChild(Browse.el('div', 'pin-t', st.title));
      e.appendChild(Browse.el('div', 'pin-s' + (st.bad ? ' bad' : ''), st.bad || st.sub));
      var dots = Browse.el('div', 'pin-dots'); for (var i = 0; i < 4; i++) dots.appendChild(Browse.el('div', 'pin-dot' + (i < st.typed.length ? ' on' : ''))); e.appendChild(dots); st.dots = dots;
      var pad = Browse.el('div', 'pin-pad');
      KEYS.forEach(function (k, i) { pad.appendChild(Browse.el('div', 'pin-k' + (k.length > 1 ? ' small' : '') + (i === st.idx ? ' focus' : ''), k === 'del' ? '⌫' : k === 'cancel' ? 'Cancel' : k)); });
      e.appendChild(pad);
      e.appendChild(Browse.el('div', 'pin-h', 'The number buttons work too      Back  Cancel'));
    }
    function press(k) {
      if (k === 'cancel') { var c = st.cancel; close(); if (c) c(); return; }
      if (k === 'del') { st.typed = st.typed.slice(0, -1); st.bad = ''; draw(); return; }
      if (st.typed.length >= 4 || (st.p._pinWait && Date.now() < st.p._pinWait)) return;
      st.typed += k; st.bad = ''; draw();
      if (st.typed.length < 4) return;
      var s = st, code = st.typed;
      // done: true (or nothing) closes the pad; a text says what was wrong and the pad stays for another go
      setTimeout(function () {
        if (st !== s) return;
        var r = s.done(code);
        if (st !== s) return;   // done opened another question
        if (typeof r === 'string') { s.typed = ''; s.bad = r; draw(); s.dots.classList.add('shake'); }
        else close();
      }, 160);
    }
    function key(k) {
      if (!st) return false;
      if (/^d\d$/.test(k)) press(k.charAt(1));
      else if (k === 'left' && st.idx % 3) st.idx--;
      else if (k === 'right' && st.idx % 3 < 2) st.idx++;
      else if (k === 'up' && st.idx > 2) st.idx -= 3;
      else if (k === 'down' && st.idx < 9) st.idx += 3;
      else if (k === 'ok') { press(KEYS[st.idx]); return true; }
      else if (k === 'back') { press('cancel'); return true; }
      else return true;
      if (st) draw();
      return true;
    }
    return { open: open, key: key, get on() { return !!st; } };
  })();
  // then() runs once the profile's PIN is right (at once when it has none)
  function unlock(p, then, why) {
    if (!p.pin) { then(); return; }
    Pin.open(p, why || 'Enter the PIN for ' + p.name, '', function (code) {
      if (p._pinWait && Date.now() < p._pinWait) return 'Too many tries: wait half a minute';
      if (pinHash(p, code) === p.pin) { p._pinFails = 0; then(); return true; }
      p._pinFails = (p._pinFails || 0) + 1;
      if (p._pinFails >= 5) { p._pinFails = 0; p._pinWait = Date.now() + 30000; return 'Too many tries: wait half a minute'; }
      return 'Wrong PIN, try again';
    });
  }
  function choosePin(p, after) {
    Pin.open(p, 'Choose a PIN of 4 digits', 'For ' + p.name + ': it asks for it before the profile opens', function (c1) {
      Pin.open(p, 'Enter the same PIN again', '', function (c2) {
        if (c2 !== c1) return 'Not the same: enter the new PIN again';
        p.pin = pinHash(p, c2); Store.save(); toast('🔒  ' + p.name + ' now needs its PIN'); if (after) after(); return true;
      });
    });
  }
  function pinSetting(p, after) {
    if (!p.pin) { choosePin(p, after); return; }
    unlock(p, function () {
      setTimeout(function () {
        menu('PIN lock  ·  ' + p.name, [
          { name: 'Change the PIN', act: function () { setTimeout(function () { choosePin(p, after); }, 0); } },
          { name: 'Remove the PIN', act: function () { delete p.pin; Store.save(); toast('🔓  ' + p.name + ' opens without a PIN'); if (after) after(); } }]);
      }, 0);
    });
  }

  // ---------- a question with a text box, and a small menu (both on top of everything) ----------
  var asking = null, menuing = null, menuIdx = 0;
  function ask(title, value, done, secret) {
    asking = done; $('ask-t').textContent = title; var f = $('ask-f'); f.type = secret ? 'password' : 'text'; f.value = value || ''; $('ask').hidden = false; f.focus();
  }
  function askKey(k) {
    var f = $('ask-f');
    if (k === 'imedone' || k === 'ok') { var v = f.value, d = asking; asking = null; f.blur(); $('ask').hidden = true; d(v); return true; }
    if (k === 'imecancel' || k === 'back') { asking = null; f.blur(); $('ask').hidden = true; return true; }
    if (document.activeElement !== f && k === 'down') { f.focus(); return true; }
    return false;
  }
  function menu(title, items, opt) { menuing = { title: title, items: items, sub: opt && opt.sub, done: opt && opt.done }; menuIdx = 0; drawMenu(); }

  // ---------- languages, made easy: one press from the header, and asked the first time ----------
  function langLabel() { var l = Store.profile.langs || []; return l.length ? l.join(' · ') : 'All languages'; }
  function pickLanguages(first) {
    var p = Store.profile; if (!p.langs) p.langs = [];
    var all = data.live.chans.concat(data.movies, data.series), langs = Tidy.langs(all), none = all.filter(function (x) { return !x.lang; }).length;
    var before = JSON.stringify([p.langs, p.langStrict]);
    menu(first ? 'Which languages do you watch?' : 'Languages  ·  ' + p.name, function () {
      var l = langs.map(function (kv) {
        return { name: Tidy.nameOf(kv[0]), count: kv[1] + '', on: p.langs.indexOf(kv[0]) >= 0, keep: true,
                 act: function () { var i = p.langs.indexOf(kv[0]); if (i >= 0) p.langs.splice(i, 1); else p.langs.push(kv[0]); } };
      });
      if (p.langs.length) l.push({ name: 'Also show what has no language (' + none + ')', on: !p.langStrict, keep: true, act: function () { p.langStrict = !p.langStrict; } });
      l.push({ name: p.langs.length ? 'Done' : 'Done (show every language)', act: function () { } });
      if (p.langs.length) l.push({ name: 'Show every language again', act: function () { p.langs = []; p.langStrict = false; } });
      return l;
    }, { sub: first ? 'Tick the languages you want: only their channels, films and series show. You can change this any time with the 🌐 button at the top.' : 'Tick the languages you want; the rest stays out of sight.',
         done: function () { p.langsAsked = true; Store.save(); if (JSON.stringify([p.langs, p.langStrict]) !== before) { rebuild(); if (BROWSE[screen]) build(screen); if (screen === 'live') { Live.leave(); Live.enter(false); } toast('🌐  ' + langLabel()); } drawHeader(); } });
  }
  function drawMenu() {
    var m = $('menu'); m.innerHTML = ''; m.hidden = !menuing; if (!menuing) return;
    m.appendChild(Browse.el('div', 'menu-t', menuing.title));
    if (menuing.sub) m.appendChild(Browse.el('div', 'menu-s', menuing.sub));
    // as many as fit on the screen; arrows say there's more above or below
    var items = typeof menuing.items === 'function' ? menuing.items() : menuing.items, SHOW = 8, first = Math.max(0, Math.min(menuIdx - 3, items.length - SHOW));
    m.appendChild(Browse.el('div', 'menu-more', first > 0 ? '▲  ' + first + ' more' : ''));
    items.slice(first, first + SHOW).forEach(function (x, j) { var i = first + j; m.appendChild(Browse.el('div', 'menu-i' + (i === menuIdx ? ' focus' : '') + (x.on ? ' on' : ''), (x.on === undefined ? '' : x.on ? '✓   ' : '      ') + x.name + (x.count ? '' : ''))); if (x.count) m.lastChild.appendChild(Browse.el('span', 'menu-c', x.count)); });
    var below = items.length - first - SHOW; m.appendChild(Browse.el('div', 'menu-more', below > 0 ? '▼  ' + below + ' more' : ''));
  }
  function menuKey(k) {
    var items = typeof menuing.items === 'function' ? menuing.items() : menuing.items;
    if (k === 'up') menuIdx = Math.max(0, menuIdx - 1);
    else if (k === 'down') menuIdx = Math.min(items.length - 1, menuIdx + 1);
    else if (k === 'ok') { var x = items[menuIdx]; if (x.keep) { x.act(); drawMenu(); return true; } var was = menuing; menuing = null; drawMenu(); x.act(); if (was.done) was.done(); return true; }
    else if (k === 'back') { var w = menuing; menuing = null; if (w.done) w.done(); }
    drawMenu(); return true;
  }

  function toggleList(it) {
    var on = Store.toggleFav(it.key, it);
    toast(on ? '✓  ' + Meta.cleanTitle(it.title).title + ' is in My list' : Meta.cleanTitle(it.title).title + ' is out of My list');
    rebuild();
  }
  function openCategory(title, items) {
    var p = page('category'); p.built = true;
    var head = p.root.querySelector('.cat-head'); if (!head) { head = Browse.el('div', 'cat-head'); p.root.appendChild(head); }
    head.textContent = title + '  ·  ' + items.length;
    show('category');
    p.setRows(Rows.grid(Tidy.filter(items)));
  }

  // ================= leaving =================
  var exitAt = 0;
  function exitSoon() {
    if (Date.now() - exitAt < 2500) { Player.stop(); Store.save(); try { tizen.application.getCurrentApplication().exit(); } catch (e) { } return; }
    exitAt = Date.now(); toast('Press Back again to leave Recomp TV');
  }

  // ================= the remote =================
  Player.onState(function (s, why) { if (screen === 'live') Live.onState(s, why); if (screen === 'guide') Guide.onState(s, why); });
  var vodState = null;   // vod.js listens itself (it sets its own handler when it opens)
  document.addEventListener('keydown', function (e) {
    var typing = document.activeElement && document.activeElement.tagName === 'INPUT';
    var k = Keys.name(e, typing);
    if (!k) return;
    var done = false;
    if (Pin.on) { Pin.key(k); e.preventDefault(); return; }
    if (asking) { if (askKey(k)) e.preventDefault(); return; }
    if (menuing) { menuKey(k); e.preventDefault(); return; }
    if (reminderOn && (k === 'ok' || k === 'back')) { $('reminder').hidden = true; var rc = reminderOn; reminderOn = null; if (k === 'ok') watchLive(rc, true); e.preventDefault(); return; }
    // hold OK on a film or series: its More menu (the Samsung remote has no spare buttons). OK then acts when it's
    // let go; a TV that never sends "let go" keeps OK acting at once, and More opens when the key repeats.
    if (k === 'ok' && BROWSE[screen] && !inHeader && !typing && screen !== 'search') {
      if (e.repeat || okHold) {
        e.preventDefault();
        if (okHold && !okHold.fired && Date.now() - okHold.t > 500) { okHold.fired = true; moreFor(page(screen)); }
        else if (!okHold && !holdFired) { holdFired = true; moreFor(page(screen)); }
        return;
      }
      holdFired = false;
      if (keyupSeen) {
        e.preventDefault();
        okHold = { t: Date.now(), fired: false };
        okHold.timer = setTimeout(function () { if (okHold && !okHold.fired) { okHold.fired = true; moreFor(page(screen)); } }, 600);
        return;
      }
    }
    if (inHeader && HEADER[screen]) done = headerKey(k);
    else if (screen === 'setup') done = Setup.key(k);
    else if (screen === 'loading') { if (loadFailed && k === 'ok') load(); else if (loadFailed && k === 'back') Setup.open(true); done = true; }
    else if (screen === 'profiles') done = Profiles.key(k);
    else if (screen === 'vod') done = Vod.key(k);
    else if (screen === 'details') done = Details.key(k);
    else {
      var r;
      if (screen === 'live') r = Live.key(k);
      else if (screen === 'guide') r = Guide.key(k);
      else if (screen === 'settings') r = Settings.key(k);
      else if (screen === 'search') r = Search.key(k);
      else if (BROWSE[screen]) { r = page(screen).key(k); if (!r && k === 'back') { if (screen === 'category') back(); else if (page(screen).r > 0 || (page(screen).hero && page(screen).r === 0)) { page(screen).r = page(screen).hero ? -1 : 0; page(screen).update(); } else toHeader(); r = true; } }
      if (r === 'top') { toHeader(); r = true; }
      if (!r && k === 'back') { if (screen === 'live' && !Live.full) toHeader(); r = true; }
      done = !!r;
    }
    if (done) e.preventDefault();
  });
  var okHold = null, keyupSeen = false, holdFired = false;
  document.addEventListener('keyup', function (e) {
    keyupSeen = true;
    if (!okHold) return;
    var h = okHold; okHold = null; clearTimeout(h.timer);
    if (h.fired || !BROWSE[screen] || inHeader || menuing || asking) return;
    var p = page(screen), r = p.key('ok');
    if (r === 'top') toHeader();
  });

  // ================= start =================
  var api = {
    get data() { return data; }, get acct() { return acct; }, scale: function () { return scale; },
    isOn: function (p) { return BROWSE[screen] && pages[screen] === p && !inHeader; }, screenName: function () { return screen; },
    inHeader: function () { return inHeader; }, headerShown: headerShown, toast: toast, back: back, rebuild: rebuild,
    open: open, openDetails: openDetails, play: play, openPerson: openPerson, ask: ask, menu: menu, setMeta: setMeta, resetMeta: resetMeta, watchLive: watchLive, openGuide: openGuide, toggleList: toggleList, openCategory: openCategory, more: more,
    providerName: function () { var h = (acct && (acct.base || acct.url) || '').replace(/^https?:\/\//, '').split(/[\/:?]/)[0]; return h.replace(/^www\./, '') || 'your provider'; },
    trailersOff: function () { return !Store.pref('trailers', true); }, Peek: null
  };
  window.App = api; api.Peek = Peek;
  Keys.register();
  Look.applyCalm();
  fit();
  acct = Store.account;
  if (acct) load(); else Setup.open(false);
  return api;
})();
