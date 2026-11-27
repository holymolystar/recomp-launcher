// TV Live: categories, channels, the picked channel playing small with what's on now and next, full screen with the
// channel's banner, fast zapping (CH+/CH- or up/down: the name changes at once, the channel opens once you stop),
// number keys, PRE-CH, My channels (the yellow button) and the channels watched lately. Only the profile's languages.
var Live = (function () {
  var $ = function (id) { return document.getElementById(id); };
  var catList, chanList, zone = 'chans', fullscreen = false, playingCh = null, zapList = [], prevCh = null, built = false;

  function D() { return App.data.live; }
  function cats() {
    var l = [{ id: 'fav', name: 'My channels' }, { id: 'recent', name: 'Watched lately' }, { id: 'all', name: 'All channels' }];
    return l.concat(D().cats.filter(function (c) { return chansOf(c).length; }));
  }
  var cacheFor = null, cache = {};
  function chansOf(cat) {
    var d = D(), key = Store.profile.id + '|' + JSON.stringify(Store.profile.langs || []) + Store.profile.langStrict + Store.profile.noAdult;
    if (cacheFor !== key) { cache = {}; cacheFor = key; }
    if (cat.id === 'fav') return Store.favs.map(function (k) { return d.byKey[k]; }).filter(Boolean);
    if (cat.id === 'recent') return Store.recent.map(function (k) { return d.byKey[k]; }).filter(Boolean);
    return cache[cat.id] || (cache[cat.id] = Tidy.filter(cat.id === 'all' ? d.chans : (d.lists[cat.id] || [])));
  }
  function initials(s) { return (s || '?').replace(/^[^A-Za-z0-9]+/, '').replace(/^(\w\w)[ :|-]+/, '').slice(0, 3).toUpperCase(); }
  function drawCat(row, c) {
    row.innerHTML = '';
    row.appendChild(Browse.el('span', 't', c.name));
    var n = chansOf(c).length; row.appendChild(Browse.el('span', 'c', n ? String(n) : ''));
  }
  function drawChan(row, c) {
    row.innerHTML = '';
    row.appendChild(Browse.el('span', 'n', String(c.num)));
    var fb = function () { var f = Browse.el('div', 'nologo', initials(c.title)); return f; };
    if (c.image) { var img = document.createElement('img'); img.src = c.image; img.onerror = function () { if (img.parentNode) img.parentNode.replaceChild(fb(), img); }; row.appendChild(img); }
    else row.appendChild(fb());
    row.appendChild(Browse.el('span', 't', c.title));
    if (Store.isFav(c.key)) row.appendChild(Browse.el('span', 'star', '★'));
    if (playingCh === c) row.appendChild(Browse.el('span', 'play', '▶'));
  }

  function enter(play) {
    if (!built) { catList = new VList($('cat-list'), 74, drawCat); chanList = new VList($('chan-list'), 82, drawChan); built = true; }
    fullscreen = false; $('osd').hidden = true;
    var all = cats(), last = Store.last && D().byKey[Store.last];
    var start = Store.favs.some(function (k) { return D().byKey[k]; }) ? 0 : 2;
    if (catList.items.length !== all.length || !catList.items.length) { catList.setItems(all, start); pickCat(); }
    else { catList.setItems(all, catList.sel); pickCat(chanList.sel); }
    zone = 'chans';
    if (play && last) {
      var i = chanList.items.indexOf(last);
      if (i < 0) { catList.setItems(all, 2); pickCat(); i = chanList.items.indexOf(last); }
      if (i >= 0) chanList.setItems(chanList.items, i);
    }
    drawZones(); chanMoved(true); placePreview();
    if (play && last && (Tidy.ok(last) || api.pendingFull)) { watch(last, chanList.items); if (api.pendingFull) { api.pendingFull = false; goFull(); } }
    else if (playingCh) watch(playingCh, zapList);
    else $('preview-msg').hidden = false;
  }
  function leave() { Player.stop(); playingCh = null; fullscreen = false; $('osd').hidden = true; $('osd-status').hidden = true; }
  function pickCat(sel) {
    var c = catList.current(), l = chansOf(c);
    chanList.setItems(l, sel || 0);
    $('chan-title').textContent = c.name + (l.length ? '  ·  ' + l.length : '');
    $('chan-empty').hidden = l.length > 0;
    $('chan-empty').textContent = c.id === 'fav' ? 'Press the yellow button on a channel to add it here.' : c.id === 'recent' ? 'Channels you watch show up here.' : 'No channels here.';
  }
  function drawZones() {
    $('cat-list').className = 'vlist' + (zone === 'cats' && !App.inHeader() ? ' active' : '');
    $('chan-list').className = 'vlist' + (zone === 'chans' && !App.inHeader() ? ' active' : '');
  }
  var infoTimer;
  function chanMoved(now) {
    clearTimeout(infoTimer);
    var c = chanList.current();
    $('info-name').textContent = c ? c.title : '';
    $('info-epg').innerHTML = '';
    if (!c) return;
    infoTimer = setTimeout(function () { epgInto($('info-epg'), c, function () { return chanList.current() === c; }); }, now ? 0 : 350);
  }
  function hhmm(d) { return (d.getHours() < 10 ? '0' : '') + d.getHours() + ':' + (d.getMinutes() < 10 ? '0' : '') + d.getMinutes(); }
  function epgInto(el, c, still) {
    Api.nowNext(App.acct, c).then(function (l) {
      if (!still()) return;
      el.innerHTML = '';
      if (!l || !l.length) { if (App.acct.type === 'xtream' && l) el.textContent = 'No guide for this channel.'; return; }
      var now = Date.now() / 1000, a = l[0];
      var line = function (x, cls) {
        var d = Browse.el('div', cls);
        d.appendChild(Browse.el('span', 'when', hhmm(new Date(x.start * 1000)) + ' – ' + hhmm(new Date(x.stop * 1000))));
        d.appendChild(document.createTextNode(x.title)); return d;
      };
      el.appendChild(line(a, 'now'));
      if (a.start <= now) { var b = Browse.el('div', 'bar'), i = document.createElement('i'); i.style.width = Math.min(100, 100 * (now - a.start) / Math.max(1, a.stop - a.start)) + '%'; b.appendChild(i); el.appendChild(b); }
      if (l[1]) el.appendChild(line(l[1], 'next'));
    });
  }
  function placePreview() {
    var p = $('preview').getBoundingClientRect(), s = $('stage').getBoundingClientRect(), sc = App.scale();
    Player.setRect((p.left - s.left) / sc, (p.top - s.top) / sc, p.width / sc, p.height / sc);
  }
  // plays a channel (in the preview, or full screen); list: the channels CH+/CH- go through
  function watch(c, list) {
    if (playingCh && playingCh !== c) prevCh = playingCh;
    playingCh = c; zapList = list && list.length ? list : Tidy.filter(D().chans);
    Store.last = c.key; Store.addRecent(c.key);
    if (fullscreen) Player.full(); else placePreview();
    Player.play(c.url);
    $('preview-msg').hidden = true;
    chanList.redraw();
    if (fullscreen) osd(c);
  }
  function favToggle(c) {
    var on = Store.toggleFav(c.key, c);
    App.toast(on ? '★  ' + c.title + ' is in My channels' : c.title + ' is out of My channels');
    if (catList.current().id === 'fav' && !fullscreen) pickCat(chanList.sel);
    catList.redraw(); chanList.redraw();
    if (fullscreen) osd(c);
  }

  function key(k) {
    if (fullscreen) return fullKey(k);
    if (/^d\d$/.test(k)) { digit(k.charAt(1)); return true; }
    var l = zone === 'cats' ? catList : chanList;
    if (k === 'up') { if (l.sel === 0) return 'top'; l.move(-1); moved(); }
    else if (k === 'down') { l.move(1); moved(); }
    else if (k === 'chup' || k === 'chdown') { l.page(k === 'chup' ? -1 : 1); moved(); }
    else if (k === 'left') { if (zone === 'chans') { zone = 'cats'; drawZones(); } }
    else if (k === 'right') { if (zone === 'cats' && chanList.items.length) { zone = 'chans'; drawZones(); } }
    else if (k === 'ok' || k === 'play' || k === 'playpause') {
      if (zone === 'cats') { if (chanList.items.length) { zone = 'chans'; drawZones(); } }
      else { var c = chanList.current(); if (!c) return true; if (c === playingCh || k !== 'ok') goFull(); else watch(c, chanList.items); }
    }
    else if (k === 'yellow') { var ch = zone === 'chans' ? chanList.current() : null; if (ch) favToggle(ch); }
    else if (k === 'green') { var gc = zone === 'chans' ? chanList.current() : playingCh; App.openGuide(gc ? gc.key : null); }
    else if (k === 'stop') { Player.stop(); playingCh = null; $('preview-msg').hidden = false; chanList.redraw(); }
    else if (k === 'back') { if (zone === 'chans') { zone = 'cats'; drawZones(); } else return false; }
    else if (k === 'prech' && prevCh) watch(prevCh, zapList);
    else return false;
    return true;
    function moved() { if (zone === 'cats') pickCat(); else chanMoved(); }
  }

  // ---------- full screen ----------
  var osdTimer, zapTimer, zapTo = null;
  function goFull() {
    if (!playingCh) return;
    fullscreen = true; Player.full();
    document.getElementById('live').classList.add('full'); App.headerShown(false);
    osd(playingCh);
  }
  function leaveFull() {
    clearTimeout(zapTimer); zapTo = null; fullscreen = false;
    document.getElementById('live').classList.remove('full'); App.headerShown(true);
    $('osd').hidden = true; $('osd-status').hidden = true;
    placePreview();
    var i = chanList.items.indexOf(playingCh); if (i >= 0) { chanList.setItems(chanList.items, i); chanMoved(true); }
    zone = 'chans'; drawZones();
  }
  function osd(c) {
    $('osd-num').textContent = c.num; $('osd-name').textContent = c.title;
    var logo = $('osd-logo'); if (c.image) { logo.src = c.image; logo.hidden = false; logo.onerror = function () { logo.hidden = true; }; } else logo.hidden = true;
    $('osd-fav').textContent = Store.isFav(c.key) ? '★ My channels' : '';
    $('osd-epg').innerHTML = '';
    epgInto($('osd-epg'), c, function () { return (zapTo || playingCh) === c; });
    $('osd').hidden = false;
    clearTimeout(osdTimer); osdTimer = setTimeout(function () { $('osd').hidden = true; }, 5000);
  }
  function zap(d) {
    var list = zapList.length ? zapList : D().chans; if (!list.length) return;
    var from = zapTo || playingCh, i = list.indexOf(from);
    var c = list[((i < 0 ? 0 : i + d) % list.length + list.length) % list.length];
    zapTo = c; osd(c);
    clearTimeout(zapTimer); zapTimer = setTimeout(function () { var t = zapTo; zapTo = null; if (t && t !== playingCh) watch(t, list); }, 450);
  }
  function fullKey(k) {
    if (/^d\d$/.test(k)) { digit(k.charAt(1)); return true; }
    // CH+ to the next number (as on any TV); the arrows go up and down the list
    if (k === 'chup' || k === 'down') zap(1);
    else if (k === 'chdown' || k === 'up') zap(-1);
    else if (k === 'ok' || k === 'info' || k === 'left' || k === 'right') { if ($('osd').hidden || k === 'info') osd(zapTo || playingCh); else $('osd').hidden = true; }
    else if (k === 'yellow') favToggle(zapTo || playingCh);
    else if (k === 'green') { var g = zapTo || playingCh; leaveFull(); App.openGuide(g ? g.key : null); }
    else if (k === 'prech' && prevCh) watch(prevCh, zapList);
    else if (k === 'back') leaveFull();
    else if (k === 'stop') { leaveFull(); Player.stop(); playingCh = null; $('preview-msg').hidden = false; chanList.redraw(); }
    else return false;
    return true;
  }

  // what the player says: a word over the picture while it tunes, or when it can't play
  var statusTimer;
  function onState(s) {
    var st = $('osd-status'), pm = $('preview-msg');
    clearTimeout(statusTimer);
    if (s === 'loading' || s === 'buffering') statusTimer = setTimeout(function () { if (fullscreen) { st.textContent = 'Tuning…'; st.hidden = false; } }, 700);
    else if (s === 'error') {
      if (fullscreen) { st.textContent = "This channel won't play right now. Try another, or Settings > Stream type."; st.hidden = false; statusTimer = setTimeout(function () { st.hidden = true; }, 6000); }
      else { pm.innerHTML = "This channel won't play right now.<br>Try another one."; pm.hidden = false; }
    } else { st.hidden = true; if (s === 'playing') pm.hidden = true; }
  }

  // ---------- number keys ----------
  var digits = '', digitTimer;
  function digit(n) {
    digits = (digits + n).slice(-5);
    $('digits').textContent = digits; $('digits').hidden = false;
    clearTimeout(digitTimer);
    digitTimer = setTimeout(function () {
      var want = digits, c = D().byNum[Number(want)]; digits = ''; $('digits').hidden = true;
      if (!c) { App.toast('There is no channel ' + want); return; }
      if (fullscreen) { watch(c, Tidy.filter(D().chans)); return; }
      catList.setItems(catList.items, 2); pickCat(); zone = 'chans';
      chanList.setItems(chanList.items, Math.max(0, chanList.items.indexOf(c))); drawZones(); chanMoved(true); watch(c, chanList.items);
    }, 1500);
  }

  var api = { pendingFull: false, enter: enter, leave: leave, key: key, onState: onState, drawZones: drawZones, watchKey: function (k) { var c = D().byKey[k]; if (c) { Store.last = k; } }, get full() { return fullscreen; },
    // the category you're in (the guide opens there): {id, name}, or null before TV Live was opened
    get cat() { return built && catList.current() || null; } };
  return api;
})();
