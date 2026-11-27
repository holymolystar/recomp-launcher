// What the app keeps on the TV (its own storage; nothing is sent anywhere else): the provider login, the profiles
// ("Who's watching?") and, per profile, My list / My channels, what was watched lately, where each film or episode
// was left (Continue watching) and what's been watched to the end.
var Store = (function () {
  var KEY = 'recomp-tv';
  var COLORS = ['#e50914', '#2e9bda', '#f5a623', '#3ccf7a', '#9b59b6', '#ff6f91', '#1abc9c', '#e67e22'];
  var d = {};
  try { d = JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { d = {}; }
  if (!d.prefs) d.prefs = {};
  // the first version kept one list for everybody: it becomes the first profile's
  if (!d.profiles || !d.profiles.length) {
    d.profiles = [newProfile('Me', 0)];
    if (d.favs) d.profiles[0].favs = d.favs;
    if (d.recent) d.profiles[0].recent = d.recent;
    if (d.last) d.profiles[0].last = d.last;
    delete d.favs; delete d.recent; delete d.last;
  }
  function newProfile(name, i) { return { id: 'p' + Date.now().toString(36) + i, name: name, color: COLORS[i % COLORS.length], favs: [], recent: [], progress: {}, watched: [], items: {} }; }
  var cur = null, chosen = false;   // chosen: picked in "Who's watching?" since the app started
  function p() { if (!cur) cur = d.profiles.filter(function (x) { return x.id === d.current; })[0] || d.profiles[0]; return cur; }
  function fix(x) { ['favs', 'recent', 'watched'].forEach(function (k) { if (!x[k]) x[k] = []; }); if (!x.progress) x.progress = {}; if (!x.items) x.items = {}; return x; }
  d.profiles.forEach(fix);
  // Several providers: their logins in d.accounts, the one in use in d.account. What belongs to a provider (My list,
  // Continue watching, what's been watched, the last channel, reminders, your own names and pictures) is kept per
  // login: switching puts the old provider's away and takes the new one's out (each provider numbers its titles
  // itself, so film 123 at one is another film at the next).
  var PER = ['favs', 'recent', 'progress', 'watched', 'items', 'last', 'reminders'];
  function aid(a) { return a ? (a.type || '') + '|' + (a.base || a.url || '') + '|' + (a.user || '') : ''; }
  if (!d.accounts) d.accounts = d.account ? [d.account] : [];
  function swapTo(a) {
    var from = aid(d.account), to = aid(a);
    if (from === to) return;
    d.profiles.forEach(function (x) {
      if (!x.acc) x.acc = {};
      var keep = {}; PER.forEach(function (k) { if (x[k] !== undefined) keep[k] = x[k]; delete x[k]; });
      if (from) x.acc[from] = keep;
      var got = x.acc[to] || {}; delete x.acc[to];
      PER.forEach(function (k) { if (got[k] !== undefined) x[k] = got[k]; });
      fix(x);
    });
    if (!d.metaAcc) d.metaAcc = {};
    if (from) d.metaAcc[from] = d.meta || {};
    d.meta = d.metaAcc[to] || {}; delete d.metaAcc[to];
  }
  function indexOfAcct(a) { var k = aid(a); for (var i = 0; i < d.accounts.length; i++) if (aid(d.accounts[i]) === k) return i; return -1; }
  var saveTimer = null;
  function save() { clearTimeout(saveTimer); saveTimer = setTimeout(saveNow, 300); }
  function saveNow() { clearTimeout(saveTimer); try { localStorage.setItem(KEY, JSON.stringify(d)); } catch (e) { } }
  window.addEventListener('beforeunload', saveNow);
  document.addEventListener('visibilitychange', function () { if (document.hidden) saveNow(); });

  // a film's, series' or episode's few details, kept so Continue watching and My list show it before the lists load
  function remember(it) {
    if (!it || it.kind === 'live') return;
    var x = { kind: it.kind, id: it.id, title: it.title, image: it.image || '', ext: it.ext || '', cat: it.cat || '' };
    if (it.kind === 'episode') { x.seriesId = it.seriesId; x.season = it.season; x.episode = it.episode; x.seriesTitle = it.seriesTitle; x.seriesImage = it.seriesImage || ''; }
    p().items[it.key] = x;
  }

  return {
    COLORS: COLORS,
    save: saveNow,
    get account() { return d.account || null; },
    // signing in, or switching: another provider's things are swapped in; the same login only gets its new details
    set account(a) { swapTo(a); d.account = a; var i = indexOfAcct(a); if (i >= 0) d.accounts[i] = a; else d.accounts.push(a); saveNow(); },
    get accounts() { return d.accounts; },
    sameAccount: function (a, b) { return aid(a) === aid(b); },
    // the same provider with a new login (another password, a new server address): its things stay with it
    replaceAccount: function (old, a) { var i = indexOfAcct(old); if (i >= 0) d.accounts.splice(i, 1); var j = indexOfAcct(a); if (j >= 0) d.accounts.splice(j, 1); d.accounts.push(a); d.account = a; d.profiles.forEach(function (x) { delete x.last; }); saveNow(); },
    // a provider off the TV, with what was kept for it (not the one in use: switch first)
    removeAccount: function (a) { var k = aid(a); if (k === aid(d.account)) return; d.accounts = d.accounts.filter(function (x) { return aid(x) !== k; }); d.profiles.forEach(function (x) { if (x.acc) delete x.acc[k]; }); if (d.metaAcc) delete d.metaAcc[k]; saveNow(); },
    pref: function (k, def) { return d.prefs[k] === undefined ? def : d.prefs[k]; },
    setPref: function (k, v) { d.prefs[k] = v; save(); },
    // the login off the TV; what was kept for it stays (signing in with it again brings it back)
    signOut: function () { var k = aid(d.account); swapTo(null); d.accounts = d.accounts.filter(function (x) { return aid(x) !== k; }); delete d.account; saveNow(); },

    // ---- profiles ----
    get profiles() { return d.profiles; },
    get profile() { return p(); },
    get chosen() { return chosen; },
    useProfile: function (id) { d.current = id; cur = null; chosen = true; saveNow(); },
    addProfile: function (name) { var x = newProfile(name, d.profiles.length); d.profiles.push(x); saveNow(); return x; },
    renameProfile: function (id, name) { d.profiles.forEach(function (x) { if (x.id === id) x.name = name; }); saveNow(); },
    removeProfile: function (id) { if (d.profiles.length < 2) return; d.profiles = d.profiles.filter(function (x) { return x.id !== id; }); if (d.current === id) { d.current = d.profiles[0].id; cur = null; } saveNow(); },
    nextColor: function (id) { d.profiles.forEach(function (x) { if (x.id === id) x.color = COLORS[(COLORS.indexOf(x.color) + 1) % COLORS.length]; }); saveNow(); },

    // ---- per profile ----
    get last() { return p().last || null; },
    set last(k) { p().last = k; save(); },
    isFav: function (k) { return p().favs.indexOf(k) >= 0; },
    toggleFav: function (k, it) { var f = p().favs, i = f.indexOf(k); if (i >= 0) f.splice(i, 1); else { f.unshift(k); remember(it); } save(); return i < 0; },
    get favs() { return p().favs.slice(); },
    addRecent: function (k) { var r = p().recent, i = r.indexOf(k); if (i >= 0) r.splice(i, 1); r.unshift(k); if (r.length > 30) r.length = 30; save(); },
    get recent() { return p().recent.slice(); },
    // where a film or episode was left: {pos, dur} in seconds
    progress: function (k) { return p().progress[k] || null; },
    setProgress: function (it, pos, dur) {
      var pr = p().progress;
      if (dur > 0 && pos > dur - Math.max(30, dur * 0.04)) { delete pr[it.key]; this.setWatched(it.key, true); if (it.kind === 'episode') pr['s:' + it.seriesId] = { ep: it.key, when: Date.now(), done: true }; save(); return; }
      if (pos < 20) return;
      pr[it.key] = { pos: Math.round(pos), dur: Math.round(dur), when: Date.now() };
      if (it.kind === 'episode') pr['s:' + it.seriesId] = { ep: it.key, when: Date.now() };
      remember(it); save();
    },
    resumeAt: function (k) { var x = p().progress[k]; return x && x.pos ? x.pos : 0; },
    clearProgress: function (k) { delete p().progress[k]; save(); },
    isWatched: function (k) { return p().watched.indexOf(k) >= 0; },
    setWatched: function (k, on) { var w = p().watched, i = w.indexOf(k); if (on && i < 0) { w.push(k); if (w.length > 3000) w.shift(); } if (!on && i >= 0) w.splice(i, 1); save(); },
    get watched() { return p().watched.slice(); },
    // what's in the middle of being watched, newest first: [{key, pos, dur, when}]
    continuing: function () {
      var pr = p().progress;
      return Object.keys(pr).filter(function (k) { return pr[k].pos; }).map(function (k) { return { key: k, pos: pr[k].pos, dur: pr[k].dur, when: pr[k].when }; })
        .sort(function (a, b) { return b.when - a.when; });
    },
    lastEpisodeOf: function (seriesId) { return p().progress['s:' + seriesId] || null; },
    remembered: function (k) { return p().items[k] || null; },
    // your own details for a film, series or channel: {title, image, plot}
    meta: function (k) { return (d.meta || {})[k] || null; },
    setMeta: function (k, field, v) { if (!d.meta) d.meta = {}; var m = d.meta[k] = d.meta[k] || {}; if (v && v.trim()) m[field] = v.trim(); else delete m[field]; if (!Object.keys(m).length) delete d.meta[k]; saveNow(); },
    clearMeta: function (k) { if (d.meta) delete d.meta[k]; saveNow(); },
    remember: remember
  };
})();
