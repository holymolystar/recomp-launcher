// The TV Guide, as in the launcher (TvGuide.cs, TvLiveTools.cs). On top: the picked channel playing small and
// what's on it; under it the channels, with what's on in a timeline that starts half an hour ago.
//   Up / Down      channels (CH+/CH- a page)          Left / Right  programmes (the timeline follows)
//   OK             on now: watch it full screen; in the past: replay it (TV Archive, when the provider keeps it);
//                  later: a reminder when it starts (OK again takes it away)
//   Blue           another category (My channels, All channels, the provider's)          Back  back
// The listings come per channel from the provider, only for the channels on the screen.
var Guide = (function () {
  var el = function (t, c, x) { return Browse.el(t, c, x); };
  var ROWS = 6, ROW_H = 88, PX = 8, SPAN = 180 * 60, GRID_X = 440, GRID_W = 1440;   // 8 px a minute, three hours across
  var root, chans = [], catIdx = 0, row = 0, top = 0, winStart = 0, focusT = 0, progs = {}, previewTimer, previewing = null, built = false;

  function cats() {
    var D = App.data.live, l = [];
    var favs = Store.favs.map(function (k) { return D.byKey[k]; }).filter(Boolean);
    if (favs.length) l.push({ id: 'fav', name: 'My channels', list: favs });
    var recent = Store.recent.map(function (k) { return D.byKey[k]; }).filter(Boolean);
    if (recent.length) l.push({ id: 'recent', name: 'Watched lately', list: recent });
    // the main channels' copies with the TV Archive on top (the best picture has the number, for watching live)
    l.push({ id: 'all', name: 'All channels', list: Dutch.guideOrder(Tidy.filter(D.chans)) });
    D.cats.forEach(function (c) { var x = Dutch.guideOrder(Tidy.filter(D.lists[c.id] || [])); if (x.length) l.push({ id: c.id, name: c.name, list: x }); });
    return l;
  }
  // where to open: the category TV Live is in (on the channel, when it's in there, else at the top); before TV Live
  // was opened, the first category the channel is in
  function startAt(cs, chKey) {
    // a main channel's best copy: on its copy with the TV Archive, when that's there
    var ch = chKey && App.data.live.byKey[chKey], twin = ch && ch.recKey;
    var at = function (i) { var ks = cs[i].list.map(function (c) { return c.key; }), r = twin ? ks.indexOf(twin) : -1; return r >= 0 ? r : chKey ? ks.indexOf(chKey) : -1; };
    var lc = Live.cat;
    // watching a main channel (its sharpest copy): the Guide opens in the category with its TV Archive copy, on that
    if (twin) for (var j = 0; j < cs.length; j++) if (!/^(fav|recent|all)$/.test(cs[j].id)) { var t = cs[j].list.map(function (c) { return c.key; }).indexOf(twin); if (t >= 0) return { cat: j, row: t }; }
    for (var i = 0; lc && i < cs.length; i++) if (cs[i].id === lc.id) return { cat: i, row: Math.max(0, at(i)) };
    for (var k = 0; chKey && k < cs.length; k++) { var r = at(k); if (r >= 0) return { cat: k, row: r }; }
    return null;
  }
  function now() { return Date.now() / 1000; }
  function startOfWindow(t) { return Math.floor((t - 30 * 60) / 1800) * 1800; }

  function open(chKey) {
    root = document.getElementById('guide');
    if (!built) build();
    var cs = cats(); catIdx = Math.min(catIdx, cs.length - 1);
    chans = cs[catIdx].list;
    row = 0; top = 0;
    var at = startAt(cs, chKey);
    if (at) { catIdx = at.cat; chans = cs[at.cat].list; row = at.row; }
    top = Math.max(0, Math.min(row - 2, chans.length - ROWS));
    focusT = now(); winStart = startOfWindow(focusT);
    previewing = null;
    draw(); previewSoon();
  }
  function build() {
    root.innerHTML = '';
    var topBox = el('div', 'g-top'); root.appendChild(topBox);
    root._preview = el('div', 'g-preview'); topBox.appendChild(root._preview);
    root._previewMsg = el('div', 'g-preview-msg', ''); root._preview.appendChild(root._previewMsg);
    var info = el('div', 'g-info'); topBox.appendChild(info);
    root._ch = el('div', 'g-ch'); info.appendChild(root._ch);
    root._title = el('div', 'g-title'); info.appendChild(root._title);
    root._when = el('div', 'g-when'); info.appendChild(root._when);
    root._desc = el('div', 'g-desc'); info.appendChild(root._desc);
    root._act = el('div', 'g-act'); info.appendChild(root._act);
    root._cat = el('div', 'g-cat'); root.appendChild(root._cat);
    root._ruler = el('div', 'g-ruler'); root.appendChild(root._ruler);
    root._rows = el('div', 'g-rows'); root.appendChild(root._rows);
    root._now = el('div', 'g-now'); root.appendChild(root._now);
    root.appendChild(el('div', 'g-keys', 'OK  watch / replay / remind      ◀ ▶  programmes      CH  page      Blue  category      Back'));
    built = true;
  }
  function hhmm(t) { var d = new Date(t * 1000); return (d.getHours() < 10 ? '0' : '') + d.getHours() + ':' + (d.getMinutes() < 10 ? '0' : '') + d.getMinutes(); }
  function day(t) {
    var d = new Date(t * 1000), today = new Date(); today.setHours(0, 0, 0, 0);
    var diff = Math.round((new Date(d.getFullYear(), d.getMonth(), d.getDate()) - today) / 86400000);
    return diff === 0 ? 'Today' : diff === -1 ? 'Yesterday' : diff === 1 ? 'Tomorrow' : ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][d.getDay()] + ' ' + d.getDate();
  }

  // the listings of a channel (asked once, then redrawn when they come)
  function listings(ch) {
    if (progs[ch.key]) return progs[ch.key];
    progs[ch.key] = 'loading';
    Api.guideOf(App.acct, ch).then(function (l) { progs[ch.key] = l; if (App.screenName() === 'guide') draw(); });
    return 'loading';
  }
  function focused() {
    var ch = chans[row]; if (!ch) return null;
    var l = listings(ch); if (!Array.isArray(l)) return null;
    for (var i = 0; i < l.length; i++) if (l[i].start <= focusT && l[i].stop > focusT) return l[i];
    return null;
  }

  function draw() {
    if (!root) return;
    var cs = cats(); root._cat.textContent = (cs[catIdx] ? cs[catIdx].name : '') + '  ·  ' + chans.length;
    // the ruler: every half hour
    root._ruler.innerHTML = '';
    root._ruler.appendChild(el('div', 'g-day', day(winStart + SPAN / 2)));
    for (var t = winStart; t < winStart + SPAN; t += 1800) { var m = el('div', 'g-mark', hhmm(t)); m.style.left = (GRID_X + (t - winStart) / 60 * PX) + 'px'; root._ruler.appendChild(m); }
    var nx = (now() - winStart) / 60 * PX;
    root._now.style.display = nx >= 0 && nx <= GRID_W ? '' : 'none'; root._now.style.left = (GRID_X + nx) + 'px';
    // the channels on the screen
    root._rows.innerHTML = '';
    var cur = focused();
    for (var i = 0; i < ROWS; i++) {
      var ch = chans[top + i]; if (!ch) break;
      var r = el('div', 'g-row' + (top + i === row ? ' sel' : '')); r.style.top = (i * ROW_H) + 'px';
      var head = el('div', 'g-head'); r.appendChild(head);
      head.appendChild(el('span', 'g-num', String(ch.num)));
      if (ch.image) { var img = new Image(); img.className = 'g-logo'; img.onerror = function () { this.style.visibility = 'hidden'; }; img.src = ch.image; head.appendChild(img); }
      head.appendChild(el('span', 'g-name', ch.title));
      if (ch.archiveDays) head.appendChild(el('span', 'g-arch', '↺'));
      var cells = el('div', 'g-cells'); r.appendChild(cells);
      var l = listings(ch);
      if (l === 'loading') cells.appendChild(el('div', 'g-none', '…'));
      else if (!l.length) cells.appendChild(el('div', 'g-none', 'No guide for this channel'));
      else l.forEach(function (p) {
        if (p.stop <= winStart || p.start >= winStart + SPAN) return;
        var x = Math.max(0, (p.start - winStart) / 60 * PX), x2 = Math.min(GRID_W, (p.stop - winStart) / 60 * PX);
        var past = p.stop <= now(), on = p.start <= now() && p.stop > now();
        var b = el('div', 'g-prog' + (past ? ' past' : '') + (on ? ' on' : '') + (p === cur && top + i === row ? ' focus' : ''));
        b.style.left = x + 'px'; b.style.width = Math.max(4, x2 - x - 4) + 'px';
        var label = (Api.canReplay(ch, p) ? '↺ ' : '') + (reminded(ch, p) ? '⏰ ' : '') + p.title;
        b.appendChild(el('div', 'g-pt', label));
        if (x2 - x > 150) b.appendChild(el('div', 'g-ptime', hhmm(p.start) + ' – ' + hhmm(p.stop)));
        cells.appendChild(b);
      });
      root._rows.appendChild(r);
    }
    // what's picked
    var ch0 = chans[row];
    root._ch.textContent = ch0 ? ch0.num + '   ' + ch0.title : '';
    root._title.textContent = cur ? cur.title : (ch0 && Array.isArray(progs[ch0.key]) ? 'No guide information' : '');
    root._when.textContent = cur ? day(cur.start) + '   ' + hhmm(cur.start) + ' – ' + hhmm(cur.stop) : '';
    root._desc.textContent = cur ? cur.desc : '';
    var act = '';
    if (ch0) {
      if (!cur || (cur.start <= now() && cur.stop > now())) act = 'OK  Watch';
      else if (cur.stop <= now()) act = Api.canReplay(ch0, cur) ? 'OK  Replay (TV Archive)' : (ch0.archiveDays ? 'Not in the TV Archive any more' : '');
      else act = reminded(ch0, cur) ? 'OK  Remove the reminder' : 'OK  Remind me when it starts';
    }
    root._act.textContent = act;
    if (cur && cur.start <= now() && cur.stop > now()) { var bar = el('div', 'bar'), f = document.createElement('i'); f.style.width = Math.min(100, 100 * (now() - cur.start) / (cur.stop - cur.start)) + '%'; bar.appendChild(f); root._when.appendChild(bar); }
  }

  // the picked channel, playing small after a moment
  function previewSoon() {
    clearTimeout(previewTimer);
    previewTimer = setTimeout(function () {
      var ch = chans[row]; if (!ch || App.screenName() !== 'guide' || previewing === ch) return;
      previewing = ch; root._previewMsg.hidden = true;
      var p = root._preview.getBoundingClientRect(), s = document.getElementById('stage').getBoundingClientRect(), sc = App.scale();
      Player.setRect((p.left - s.left) / sc, (p.top - s.top) / sc, p.width / sc, p.height / sc);
      Player.play(ch.url);
    }, 1200);
  }
  function onState(s) {
    if (!root) return;
    if (s === 'error') { root._previewMsg.textContent = "This channel won't play right now."; root._previewMsg.hidden = false; }
    else if (s === 'playing') root._previewMsg.hidden = true;
  }

  // ---------- reminders ----------
  function reminded(ch, p) { return (Store.profile.reminders || []).some(function (r) { return r.ch === ch.key && r.start === p.start; }); }
  function toggleReminder(ch, p) {
    var pr = Store.profile, l = pr.reminders = (pr.reminders || []).filter(function (r) { return r.stop > now(); });
    var i = -1; l.forEach(function (r, j) { if (r.ch === ch.key && r.start === p.start) i = j; });
    if (i >= 0) { l.splice(i, 1); App.toast('Reminder removed'); }
    else { l.push({ ch: ch.key, start: p.start, stop: p.stop, title: p.title, chTitle: ch.title }); App.toast('⏰  You get a reminder when ' + p.title + ' starts'); }
    Store.save(); draw();
  }

  function moveRow(d) {
    var n = Math.max(0, Math.min(chans.length - 1, row + d)); if (n === row) return;
    row = n;
    if (row < top) top = row; if (row >= top + ROWS) top = row - ROWS + 1;
    draw(); previewSoon();
  }
  function moveProg(d) {
    var ch = chans[row], l = ch && listings(ch), cur = focused();
    if (!Array.isArray(l) || !l.length) { focusT += d * 1800; }
    else {
      var i = cur ? l.indexOf(cur) : -1, nxt;
      if (i >= 0) nxt = l[i + d];
      else nxt = d > 0 ? l.filter(function (p) { return p.start > focusT; })[0] : l.filter(function (p) { return p.stop <= focusT; }).pop();
      // the next programme: its start (or now, for the one on now, so up and down stay at the same time)
      if (nxt) focusT = nxt.start <= now() && nxt.stop > now() ? now() : nxt.start + 1; else focusT += d * 1800;
    }
    // the timeline follows (it doesn't go further back than the archive, or 3 days ahead)
    while (focusT < winStart) winStart -= 1800;
    while (focusT >= winStart + SPAN - 600) winStart += 1800;
    draw();
  }

  function key(k) {
    if (k === 'up') { if (row === 0) return 'top'; moveRow(-1); }
    else if (k === 'down') moveRow(1);
    else if (k === 'chup' || k === 'chdown') moveRow(k === 'chup' ? -ROWS : ROWS);
    else if (k === 'left') moveProg(-1);
    else if (k === 'right') moveProg(1);
    else if (k === 'blue') { var cs = cats(); catIdx = (catIdx + 1) % cs.length; chans = cs[catIdx].list; row = 0; top = 0; App.toast(cs[catIdx].name); draw(); previewSoon(); }
    else if (k === 'ok' || k === 'play') {
      var ch = chans[row], p = focused(); if (!ch) return true;
      if (!p || (p.start <= now() && p.stop > now())) { App.watchLive(ch, true); return true; }
      if (p.stop <= now()) { if (Api.canReplay(ch, p)) replay(ch, p); else App.toast(ch.archiveDays ? 'This programme isn\'t in the TV Archive any more' : ch.title + ' has no TV Archive'); return true; }
      toggleReminder(ch, p);
    }
    else if (k === 'back') { App.back(); }
    else if (/^d\d$/.test(k)) return true;
    else return false;
    return true;
  }
  function replay(ch, p) {
    var url = Api.archiveUrl(App.acct, ch, p); if (!url) return;
    App.play({ kind: 'archive', key: 'a:' + ch.id + '@' + p.start, id: ch.id, title: p.title, plot: p.desc, image: ch.image, url: url,
               sub: ch.title + '  ·  ' + day(p.start) + '  ' + hhmm(p.start) + ' – ' + hhmm(p.stop) }, 0);
  }
  function leave() { clearTimeout(previewTimer); previewing = null; Player.stop(); }
  // past programmes of a channel, newest first (TV Live's "TV Archive" list)
  function pastOf(ch) { return Api.guideOf(App.acct, ch).then(function (l) { return l.filter(function (p) { return Api.canReplay(ch, p); }).reverse(); }); }

  return { open: open, key: key, leave: leave, onState: onState, replay: replay, pastOf: pastOf, redraw: function () { if (App.screenName() === 'guide') draw(); } };
})();
