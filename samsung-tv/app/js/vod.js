// Watching a film or an episode, full screen: it starts where you left it, and where you are is kept every few
// seconds (Continue watching). The bar shows on any button: the time, how long it is, pause.
//   OK / Play-Pause  pause and go on          Left / Right  10 s back / forward (press more to jump further)
//   REW / FF         30 s back / forward      Up / green    audio and subtitles (timing and look in there too)
//   Back             stop (where you are is kept)
// As in the launcher:
//   - the original language is picked first when the film or series comes from elsewhere (no dubs), then the
//     profile's audio language (Settings > Original audio first)
//   - Dutch subtitles by default, English as an extra: the film's own when it has them, else from OpenSubtitles,
//     kept on the TV (subs.js); their timing and look can be changed here
//   - paused a while: the pause screen (what you're watching, its story); after three episodes in a row without a
//     button pressed: "Are you still watching?"
//   - episodes: in the last 20 seconds a card counts down to the next one: OK plays it now, Back stops at the end.
var Vod = (function () {
  var el = function (t, c, x) { return Browse.el(t, c, x); };
  var root, it = null, list = null, hideTimer, saveTimer, tickTimer, seekTo = null, seekTimer, nextCancelled = false, nextCard = null;
  var menuOpen = false, menu = [], menuIdx = 0, menuPage = 'main', started = false;
  var pausedAt = 0, pauseScreen = null, still = null, autoRun = 0, byHand = false;

  function open(item, start) {
    it = item; nextCancelled = false; started = false; seekTo = null; pausedAt = 0; pauseScreen = null; still = null;
    root = document.getElementById('vod');
    root.innerHTML = '';
    var top = el('div', 'v-top'); root.appendChild(top);
    top.appendChild(el('div', 'v-title', item.kind === 'episode' ? (item.seriesTitle || '') : Meta.cleanTitle(item.title).title));
    if (item.kind === 'archive') top.appendChild(el('div', 'v-sub', '↺ TV Archive  ·  ' + (item.sub || '')));
    if (item.kind === 'episode') top.appendChild(el('div', 'v-sub', 'S' + item.season + ':E' + item.episode + '  ' + epName(item)));
    var bot = el('div', 'v-bot'); root.appendChild(bot);
    var bar = el('div', 'v-bar'); bot.appendChild(bar);
    root._fill = el('i', 'fill'); bar.appendChild(root._fill);
    root._seek = el('i', 'seek'); bar.appendChild(root._seek);
    root._seekTip = el('div', 'v-seektip'); bar.appendChild(root._seekTip);
    var times = el('div', 'v-times'); bot.appendChild(times);
    root._state = el('span', 'v-state', '▶'); times.appendChild(root._state);
    root._time = el('span', 'v-time', '0:00'); times.appendChild(root._time);
    times.appendChild(el('span', 'v-hint', '◀ ▶  10 s     ▲  Audio & subtitles     OK  Pause'));
    root._dur = el('span', 'v-dur', ''); times.appendChild(root._dur);
    root._status = el('div', 'v-status', 'Loading…'); root.appendChild(root._status);
    root._subs = el('div', 'v-subs'); root.appendChild(root._subs);
    root._intro = el('div', 'v-intro', '⏭  Skip intro'); root._intro.hidden = true; root.appendChild(root._intro); introDone = false;
    root._menu = el('div', 'v-menu'); root._menu.hidden = true; root.appendChild(root._menu);
    menuOpen = false;
    if (!byHand) autoRun = 0;
    byHand = false;
    Subs.start(item, root._subs);
    if (item.kind !== 'archive') Store.remember(item);
    // an episode's series: its other episodes (for the next one)
    list = null;
    if (item.kind === 'episode') {
      var s = App.data.byKey['s:' + item.seriesId];
      if (s) Api.seriesInfo(App.acct, s).then(function () { if (it === item) list = s.episodes; });
    }
    // the country it comes from (for the original audio), asked now so it's there when it starts
    if (item.kind === 'movie' || item.kind === 'episode') Extras.originalLangs(item);
    Player.full();
    // the film's best copy at the provider (Best: the biggest file in your language)
    Best.pick(item).then(function (b) {
      if (it !== item) return;
      Player.play(b.url, start || 0);
      if (b.note) App.toast(b.note);
    }, function () { if (it === item) Player.play(item.url, start || 0); });
    show(true);
    clearInterval(tickTimer); tickTimer = setInterval(tick, 500);
    clearInterval(saveTimer); saveTimer = setInterval(save, 10000);
    own = []; ownGen++; clearInterval(ownT); ownT = setInterval(expireOwn, 200);
    Fmt.fitSoon(top.firstChild, 1, 30);
    if (top.children[1]) Fmt.fitSoon(top.children[1], 1, 22);
  }
  function epName(e) { return (e.title || '').replace(/^.*?S\d+\s*E\d+\s*[-:]?\s*/i, ''); }
  function close(toEnd) {
    clearInterval(tickTimer); clearInterval(saveTimer); clearTimeout(hideTimer); clearInterval(ownT); own = []; ownGen++;
    if (it && started) { save(toEnd); App.rebuild(); }   // Continue watching and the rest change
    Subs.stop(); Player.stop(); hideNext(); hidePause(); it = null;
  }
  function save(toEnd) {
    if (!it || it.kind === 'archive') return;
    var d = Player.duration(), t = toEnd ? d : (seekTo != null ? seekTo : Player.time());
    if (d > 0 && t > 0) Store.setProgress(it, t, d);
  }
  function clock(s) { s = Math.max(0, Math.round(s || 0)); var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + (m < 10 ? '0' : '') : '') + m + ':' + (x < 10 ? '0' : '') + x; }

  function show(stay) {
    root.classList.add('ui');
    clearTimeout(hideTimer);
    if (!stay && !Player.paused) hideTimer = setTimeout(function () { if (!menuOpen) root.classList.remove('ui'); }, 4000);
  }
  function tick() {
    if (!it) return;
    var t = seekTo != null ? seekTo : Player.time(), d = Player.duration();
    root._time.textContent = clock(t); root._dur.textContent = d ? clock(d) : '';
    root._fill.style.width = d ? Math.min(100, 100 * Player.time() / d) + '%' : '0';
    root._seek.style.left = d ? Math.min(100, 100 * t / d) + '%' : '0'; root._seek.hidden = seekTo == null;
    // seeking: where you'll land and how far that is from here
    root._seekTip.hidden = seekTo == null;
    if (seekTo != null) { var diff = Math.round(seekTo - Player.time()); root._seekTip.textContent = clock(seekTo) + '   (' + (diff >= 0 ? '+' : '−') + clock(Math.abs(diff)) + ')'; root._seekTip.style.left = d ? Math.min(100, 100 * t / d) + '%' : '0'; }
    root._state.textContent = Player.paused ? '❚❚' : '▶';
    // an episode's first minutes: "Skip intro" (OK jumps past it)
    root._intro.hidden = !introShowing(t);
    // paused a while: the pause screen
    if (Player.paused && started && !menuOpen && !still) { if (!pausedAt) pausedAt = Date.now(); else if (!pauseScreen && Date.now() - pausedAt > 6000) showPause(); }
    else pausedAt = 0;
    // the next episode, in the last 20 seconds
    if ((it.kind === 'episode' || queue.length) && d > 120 && d - t <= 20 && !nextCancelled && seekTo == null && !Player.paused && !still) nextUp(Math.ceil(d - t));
    else if (nextCard && (d - t > 21 || seekTo != null)) hideNext();
  }

  Player.onState(function (s) {
    if (!it || App.screenName() !== 'vod') return;
    if (s === 'playing') { root._status.hidden = true; if (!started) { started = true; pickLanguages(); show(); } }
    else if (s === 'loading' || s === 'buffering') { root._status.textContent = s === 'loading' ? 'Loading…' : 'Buffering…'; root._status.hidden = false; }
    else if (s === 'error') { root._status.textContent = "This won't play right now. Try again later."; root._status.hidden = false; show(true); }
    else if (s === 'ended') ended();
  });
  // the film's own subtitles (when one of ours isn't showing). Several can be up at once (two people talking, or a
  // sign at the top); each goes when its time in the film is up (so a paused film keeps its line), and Timing can
  // show them later. Fmt cleans what the file hands over ({\an8}, <i>, \N...).
  var own = [], ownT = null, ownGen = 0;
  function drawOwn() { if (root && !Subs.showing && !root._subsDemo) Fmt.subs(root._subs, own.map(function (x) { return x.t; })); }
  function clearOwn() { own = []; ownGen++; if (root && !Subs.showing) Fmt.subs(root._subs, []); }
  function expireOwn() {
    if (!own.length) return;
    var now = Player.time(), n = own.length;
    own = own.filter(function (x) { return x.until > now && x.until - now < 30; });
    if (own.length !== n) drawOwn();
  }
  Player.onSubtitle(function (text, dur) {
    if (!root || Subs.showing) return;
    var g = ownGen, d = Subs.delay();
    var add = function () {
      if (g !== ownGen || Subs.showing) return;
      if (!text) { own = []; drawOwn(); return; }
      var now = Player.time();
      own = own.filter(function (x) { return x.until > now && x.t !== text; });
      own.push({ t: text, until: now + Math.max(0.8, Math.min(15, (dur || 3000) / 1000)) });
      if (own.length > 3) own.shift();
      drawOwn();
    };
    if (d > 0.05) setTimeout(add, d * 1000); else add();
  });

  // ---------- the pause screen, and "Are you still watching?" ----------
  function pauseInfo(x) {
    var s = x.kind === 'episode' ? App.data.byKey['s:' + x.seriesId] : x;
    return { title: x.kind === 'episode' ? (x.seriesTitle || (s && s.title) || '') : Meta.cleanTitle(x.title).title,
             meta: x.kind === 'episode' ? 'S' + x.season + ':E' + x.episode + '   ' + epName(x) : [x.year, x.genre].filter(Boolean).join('  ·  '),
             plot: x.kind === 'episode' ? (x.plot || (s && s.plot) || '') : (x.plot || ''), art: (s && (s.backdrop || s.image)) || x.backdrop || x.image || '', logo: s && s.logo };
  }
  function buildScreen(kicker, x, hint) {
    var i = pauseInfo(x), p = el('div', 'v-pause');
    if (i.art) { var bg = el('div', 'v-pause-bg'); bg.style.backgroundImage = 'url("' + i.art + '")'; p.appendChild(bg); }
    p.appendChild(el('div', 'v-pause-fade'));
    var box = el('div', 'v-pause-box'); p.appendChild(box);
    box.appendChild(el('div', 'v-pause-k', kicker));
    var name = el('div', 'v-pause-t', i.title); box.appendChild(name); Fmt.fitSoon(name, 2, 48);
    if (i.logo) { var lg = new Image(); lg.className = 'v-pause-logo'; lg.onload = function () { if (name.parentNode) box.replaceChild(lg, name); }; lg.src = i.logo; }
    if (i.meta) box.appendChild(el('div', 'v-pause-m', i.meta));
    if (i.plot) box.appendChild(el('div', 'v-pause-p', i.plot));
    if (hint) box.appendChild(el('div', 'v-pause-h', hint));
    root.appendChild(p); p.offsetWidth; p.classList.add('on');
    return p;
  }
  function showPause() {
    if (!it || pauseScreen) return;
    root.classList.remove('ui');
    pauseScreen = buildScreen("You're watching", it, '');
    if (it.kind === 'movie' && !it.plot) Meta.enrich(it).then(function () { if (pauseScreen && it && it.plot) { hidePause(); showPause(); } });
  }
  function hidePause() { if (pauseScreen && pauseScreen.parentNode) pauseScreen.parentNode.removeChild(pauseScreen); pauseScreen = null; pausedAt = 0; }
  function askStill(n) {
    hideNext();
    still = { next: n, el: buildScreen('Are you still watching?', n, 'OK  Carry on watching        Back  Stop') };
  }

  // ---------- Skip intro: in an episode's first minutes, up to where the series' intro ends (85 s until you set it
  // from the player's menu, "The intro ends here"; kept per series) ----------
  var introDone = false;
  function introEnd() { return it && it.kind === 'episode' ? Number(Store.pref('intro:' + it.seriesId, 85)) || 85 : 0; }
  function introShowing(t) {
    if (!it || it.kind !== 'episode' || introDone || !started || seekTo != null || menuOpen || pauseScreen || still) return false;
    if (!Store.pref('skipIntro', true)) return false;
    var d = Player.duration(), end = introEnd();
    return (!d || d > 600) && t >= 2 && t < end - 3;
  }
  function skipIntro() { introDone = true; root._intro.hidden = true; clearOwn(); Player.seek(introEnd()); show(); }

  // ---------- what plays next: your queue first ("Play next", "Add to queue"), else the next episode ----------
  var queue = [];
  function queueAdd(x, front) {
    if (!x || x.kind === 'live') return;
    queue = queue.filter(function (q) { return q.key !== x.key; });
    if (front) queue.unshift(x); else queue.push(x);
    Store.remember(x);
  }
  function nextEp() {
    if (!list || !it) return null;
    for (var i = 0; i < list.length; i++) if (list[i].key === it.key) return list[i + 1] || null;
    return null;
  }
  function following() {
    if (!it) return null;
    var q = queue.filter(function (x) { return x.key !== it.key; })[0];
    return q || nextEp();
  }
  function nameOf(n) { return n.kind === 'episode' ? (n.seriesTitle && n.seriesId !== (it && it.seriesId) ? n.seriesTitle + '  ' : '') + 'S' + n.season + ':E' + n.episode + '  ' + epName(n) : Meta.cleanTitle(n.title).title; }
  function nextUp(secs) {
    var n = following(); if (!n) return;
    if (!nextCard) {
      var fromQueue = queue.indexOf(n) >= 0;
      nextCard = el('div', 'v-next');
      nextCard.appendChild(el('div', 'v-next-t', fromQueue ? 'Up next from your queue' : 'Next episode'));
      var nn = el('div', 'v-next-n', nameOf(n)); nextCard.appendChild(nn); Fmt.fitSoon(nn, 2, 22);
      nextCard._c = el('div', 'v-next-c'); nextCard.appendChild(nextCard._c);
      nextCard.appendChild(el('div', 'v-next-k', 'OK  Play now      Back  Cancel'));
      root.appendChild(nextCard);
    }
    nextCard._c.textContent = 'Starts in ' + secs;
  }
  function hideNext() { if (nextCard && nextCard.parentNode) nextCard.parentNode.removeChild(nextCard); nextCard = null; }
  function playNext(hand) {
    var n = following(); if (!n) return false;
    save(true); var fromQueue = queue.indexOf(n) >= 0, keep = list, runs = hand ? 0 : autoRun + 1;
    if (fromQueue) queue.splice(queue.indexOf(n), 1);
    close(false); byHand = true; autoRun = runs; open(n, Store.resumeAt(n.key));
    if (!fromQueue) list = keep;   // the same series: its episodes are known already
    return true;
  }
  function ended() {
    if (!it) return;
    save(true);
    if ((it.kind === 'episode' || queue.length) && !nextCancelled) {
      var n = following();
      // three in a row without a button: ask first
      if (n && autoRun >= 2) { Player.stop(); askStill(n); return; }
      if (playNext(false)) return;
    }
    close(false); App.back();
  }

  // ---------- audio and subtitles ----------
  function match(t, want) { return want && want.split(',').some(function (w) { return w && (t.lang === w || t.lang.indexOf(w) === 0); }); }
  // what it starts with: the original audio (or the profile's), Dutch subtitles (or the profile's choice)
  function pickLanguages() {
    var p = Store.profile, tr = Player.tracks(), x = it;
    var orig = Store.pref('origAudio', true) ? Extras.originalLangsNow(x) : null;
    var audio = tr.filter(function (t) { return t.type === 'audio'; });
    var a = null;
    if (orig) a = audio.filter(function (t) { return match(t, orig); })[0];
    if (!a && p.audioLang) a = audio.filter(function (t) { return match(t, p.audioLang); })[0];
    if (a && !a.on) Player.selectTrack('audio', a.index);
    // subtitles: off, the film's own in that language, else ours (kept or found online)
    var want = p.subLang || 'nl';
    if (want === 'off') { Player.selectTrack('text', -1); return; }
    // the audio is already that language: no subtitles needed
    var heard = a || audio.filter(function (t) { return t.on; })[0];
    if (heard && match(heard, want === 'nl' ? 'nl,nld,dut' : want) && !p.subLang) { Player.selectTrack('text', -1); return; }
    var own = tr.filter(function (t) { return t.type === 'text' && match(t, want === 'nl' ? 'nl,nld,dut' : want); })[0];
    if (own) { Player.selectTrack('text', own.index); return; }
    Player.selectTrack('text', -1);
    if (x.kind !== 'archive') Subs.auto(want);
  }

  function addMain() {
    var tr = Player.tracks();
    menu = [];
    tr.filter(function (t) { return t.type === 'audio'; }).forEach(function (t) { menu.push({ head: menu.length ? null : 'Audio', name: t.name, on: t.on, act: function () { Player.selectTrack('audio', t.index); } }); });
    if (!menu.length) menu.push({ head: 'Audio', name: 'Only one audio track', on: true, act: function () { } });
    var subs = tr.filter(function (t) { return t.type === 'text'; }), ownOn = !Subs.showing && subs.some(function (t) { return t.on; });
    menu.push({ head: 'Subtitles', name: 'Off', on: !Subs.showing && !ownOn, act: function () { Subs.show(null); Player.selectTrack('text', -1); clearOwn(); } });
    // two tracks in one language ("English", "English"): numbered, so you can tell them apart
    var seen = {}, count = {};
    subs.forEach(function (t) { count[t.name] = (count[t.name] || 0) + 1; });
    subs.forEach(function (t) {
      seen[t.name] = (seen[t.name] || 0) + 1;
      var name = t.name + (count[t.name] > 1 ? '  ' + seen[t.name] : '');
      menu.push({ name: name, on: !Subs.showing && t.on, act: function () { Subs.show(null); clearOwn(); Player.selectTrack('text', t.index); } });
    });
    Subs.LANGS.forEach(function (l) {
      if (Subs.has(l)) menu.push({ name: Subs.nameOf(l) + '  (saved)', on: Subs.showing === l, act: function () { Player.selectTrack('text', -1); Subs.show(l); } });
    });
    if (it.kind !== 'archive') {
      if (Subs.hasKey()) menu.push({ name: Subs.busy ? 'Looking online…' : (Subs.note ? Subs.note : 'Find Dutch and English online'), act: function () {
        if (Subs.busy) return;
        App.toast('Looking for subtitles online…');
        Subs.findNow().then(function (got) { if (got.nl || got.en) { Player.selectTrack('text', -1); Subs.show(got.nl ? 'nl' : 'en'); App.toast((got.nl ? 'Dutch' : 'English') + ' subtitles found'); } else App.toast(Subs.note || 'Nothing found online'); if (menuOpen) openMenu(menuPage); });
      } });
      else menu.push({ name: 'Set up OpenSubtitles (free key)', act: function () { App.ask('Your OpenSubtitles API key (opensubtitles.com → API consumers)', '', function (v) { Store.setPref('osKey', (v || '').trim()); if (v && v.trim()) { App.toast('OpenSubtitles is set up'); Subs.findNow().then(function (got) { if (got.nl || got.en) { Player.selectTrack('text', -1); Subs.show(got.nl ? 'nl' : 'en'); } }); } }); } });
    }
    if (Subs.showing || ownOn) menu.push({ name: 'Timing  (' + Subs.delayText() + ')', act: function () { openMenu('timing'); }, go: true });
    menu.push({ name: 'Style', act: function () { openMenu('style'); }, go: true });
    menu.push({ head: 'Sound', name: 'Original language first: ' + (Store.pref('origAudio', true) ? 'On' : 'Off'), act: function () { Store.setPref('origAudio', !Store.pref('origAudio', true)); } });
    menu.push({ head: 'Picture', name: 'Size: ' + Player.fitName(), act: function () { App.toast('Picture: ' + Player.nextFit()); } });
    if (it.kind === 'episode') {
      menu.push({ head: 'Intro', name: 'Skip intro button: ' + (Store.pref('skipIntro', true) ? 'On' : 'Off'), act: function () { Store.setPref('skipIntro', !Store.pref('skipIntro', true)); } });
      var at = Math.round(Player.time()), now = introEnd();
      menu.push({ name: 'The intro ends here  (now ' + clock(now) + ')', act: function () {
        if (at < 15 || at > 600) { App.toast('Do this at the moment the intro ends (within the first 10 minutes)'); return; }
        Store.setPref('intro:' + it.seriesId, at); introDone = true;
        App.toast('Skip intro now jumps to ' + clock(at) + ' in every episode of this series');
      } });
    }
    if (queue.length) menu.push({ head: 'Your queue', name: 'Clear the queue  (' + queue.length + ')', act: function () { queue = []; App.toast('The queue is empty'); } });
  }
  function openMenu(pg, focus) {
    var from = menuPage; menuPage = pg || 'main';
    if (menuPage === 'main') addMain();
    else if (menuPage === 'timing') menu = (Subs.showing ? Subs.timingItems() : Subs.ownTimingItems()).map(function (x) { return { name: x.name, act: x.act }; }).concat([{ name: 'Done', act: function () { openMenu('main'); }, go: true }]);
    else if (menuPage === 'style') menu = Subs.styleItems().map(function (x) { return { name: x.name + ':  ' + x.value, act: x.act }; }).concat([{ name: 'Done', act: function () { openMenu('main'); }, go: true }]);
    if (menuPage === 'main') {
      // back from Timing or Style: on that row again; else on the subtitles that show (or the audio)
      var back = from === 'timing' ? 'Timing' : from === 'style' ? 'Style' : null, at = -1;
      menu.forEach(function (m, i) { if (at < 0 && back && m.name.indexOf(back) === 0) at = i; });
      if (at < 0 && focus !== 'keep') { var subs = menu.filter(function (m, i) { return m.on && i > 0 && !m.head; }); at = subs.length ? menu.indexOf(subs[0]) : Math.max(0, menu.map(function (m) { return m.on; }).indexOf(true)); }
      if (at >= 0) menuIdx = at;
    }
    else if (from !== menuPage) menuIdx = 0;
    else menuIdx = Math.min(menuIdx, menu.length - 1);
    menuOpen = true; drawMenu(); show(true);
  }
  function drawMenu() {
    var m = root._menu; m.innerHTML = ''; m.hidden = !menuOpen; if (!menuOpen) return;
    var title = menuPage === 'timing' ? 'Subtitle timing  ·  ' + Subs.delayText() : menuPage === 'style' ? 'Subtitle style' : 'Audio & subtitles';
    m.appendChild(el('div', 'v-menu-t', title));
    if (menuPage === 'timing') m.appendChild(el('div', 'v-menu-n', Subs.showing ? 'Sync: press OK just as someone says the line.  ◀ ▶  0.1 s' : "The film's own subtitles can only come later, not sooner.  ◀ ▶  0.1 s"));
    if (menuPage === 'style') m.appendChild(el('div', 'v-menu-n', 'The subtitles show how they look now.'));
    // all of it in a list that scrolls with the focus (however many tracks a film has, it stays on the screen)
    var box = el('div', 'v-menu-list'); m.appendChild(box);
    var focusEl = null;
    menu.forEach(function (x, i) {
      if (x.head) box.appendChild(el('div', 'v-menu-h', x.head));
      var row = el('div', 'v-menu-i' + (i === menuIdx ? ' focus' : '') + (x.on ? ' on' : ''), (x.on ? '✓  ' : x.go ? '›  ' : '     ') + x.name);
      box.appendChild(row); if (i === menuIdx) focusEl = row;
    });
    if (focusEl) {
      var want = focusEl.offsetTop - (box.clientHeight - focusEl.offsetHeight) / 2;
      box.scrollTop = Math.max(0, Math.min(want, box.scrollHeight - box.clientHeight));
      box.classList.toggle('more-up', box.scrollTop > 2); box.classList.toggle('more-down', box.scrollTop + box.clientHeight < box.scrollHeight - 2);
    }
    if (menuPage === 'style' && !root._subs.textContent) { root._subsDemo = true; Fmt.subs(root._subs, ['This is how subtitles look']); }
    else if (menuPage === 'style' && root._subsDemo) Fmt.subs(root._subs, ['This is how subtitles look']);
    else if (menuPage !== 'style' && root._subsDemo) { root._subsDemo = false; if (!Subs.showing) drawOwn(); else Subs.redraw(); }
  }
  function closeMenu() { menuOpen = false; if (root._subsDemo) { root._subsDemo = false; if (!Subs.showing) Fmt.subs(root._subs, own.map(function (x) { return x.t; })); } drawMenu(); show(); }

  function key(k) {
    if (!it && !still) return false;
    if (k !== 'back' || !still) autoRun = still ? autoRun : 0;   // a button pressed: you're still there
    if (still) {
      if (k === 'ok' || k === 'play' || k === 'playpause') {
        var n = still.next, q = queue.indexOf(n); if (still.el.parentNode) still.el.parentNode.removeChild(still.el); still = null;
        if (q >= 0) queue.splice(q, 1);
        var keep = list; close(false); autoRun = 0; byHand = true; open(n, Store.resumeAt(n.key)); if (q < 0) list = keep; return true;
      }
      if (k === 'back' || k === 'stop') { if (still.el.parentNode) still.el.parentNode.removeChild(still.el); still = null; close(false); App.back(); return true; }
      return true;
    }
    if (pauseScreen) { hidePause(); if (k === 'ok' || k === 'play' || k === 'playpause') { Player.resume(); show(); return true; } if (k !== 'back') { show(true); return true; } }
    if (menuOpen) {
      if (k === 'up') menuIdx = Math.max(0, menuIdx - 1);
      else if (k === 'down') menuIdx = Math.min(menu.length - 1, menuIdx + 1);
      else if (k === 'ok') {
        var x = menu[menuIdx]; x.act();
        if (x.go) return true;
        if (menuPage === 'main') { var keepIdx = menuIdx; setTimeout(function () { if (!menuOpen) return; openMenu('main', 'keep'); menuIdx = Math.min(keepIdx, menu.length - 1); drawMenu(); }, 300); }
        else { openMenu(menuPage); if (menuPage === 'style') { Subs.applyStyle(); } }
        return true;
      }
      else if (menuPage === 'timing' && (k === 'left' || k === 'right')) { var by = menu.filter(function (m) { return m.name === (k === 'left' ? '0.1 second earlier' : '0.1 second later'); })[0]; if (by) by.act(); }
      else if (k === 'back' || k === 'left' || k === 'right') { if (menuPage !== 'main') { openMenu('main'); return true; } closeMenu(); return true; }
      drawMenu(); return true;
    }
    if (nextCard) {
      if (k === 'ok' || k === 'play') { playNext(true); return true; }
      if (k === 'back') { nextCancelled = true; hideNext(); return true; }
    }
    if (k === 'back' || k === 'stop') { close(false); App.back(); return true; }
    if (k === 'ok' && !root._intro.hidden) { skipIntro(); return true; }
    if (k === 'ok' || k === 'playpause' || k === 'pause' || k === 'play') {
      if (k === 'play' && !Player.paused) { show(); return true; }
      if (k === 'ok' && !root.classList.contains('ui')) { show(); return true; }
      if (Player.paused) Player.resume(); else { Player.pause(); save(); }
      show(Player.paused); tick(); return true;
    }
    if (k === 'left' || k === 'right' || k === 'rew' || k === 'ff') {
      var step = (k === 'rew' || k === 'ff') ? 30 : 10, d = (k === 'left' || k === 'rew') ? -step : step;
      if (seekTo == null) seekTo = Player.time();
      seekTo = Math.max(0, Math.min(Player.duration() || 1e9, seekTo + d));
      clearTimeout(seekTimer); seekTimer = setTimeout(function () { clearOwn(); Player.seek(seekTo); var t = seekTo; seekTo = null; if (t != null) save(); }, 700);
      show(); tick(); return true;
    }
    if (k === 'up' || k === 'green') { menuPage = 'main'; openMenu('main'); return true; }
    if (k === 'down' || k === 'info') { show(); return true; }
    return false;
  }

  return { open: open, close: close, key: key, get item() { return it; }, queueAdd: queueAdd, get queue() { return queue.slice(); },
           inQueue: function (k) { return queue.some(function (q) { return q.key === k; }); }, queueRemove: function (k) { queue = queue.filter(function (q) { return q.key !== k; }); } };
})();
