// A film or series you stay on opens up into a card (as in the launcher, TvPeek.cs): after a moment the poster grows
// into a card over the row with the wide picture, the title, what it is and its story. When it has a trailer
// (Cinemeta, on YouTube) it starts over the card's picture once it really plays; without one, or when it can't play,
// the picture simply stays. Moving on puts the card away and stops the trailer.
// On the remote: OK opens it (More info), Play plays it, yellow adds it to My list.
var Peek = (function () {
  var W = 520, PIC = 292;
  var layer = null, card = null, timer = null, page = null, item = null;
  var yt = null, ytReady = false, ytLoading = false, ytWant = null, ytBox = null, ytBroken = false;

  function ensureLayer() { if (!layer) { layer = document.createElement('div'); layer.id = 'peek-layer'; document.getElementById('stage').appendChild(layer); } }

  function soon(p) {
    hide();
    page = p; clearTimeout(timer);
    timer = setTimeout(function () { if (page === p && App.isOn(p)) show(p); }, 700);
  }
  function hide() {
    clearTimeout(timer);
    if (card && card.parentNode) card.parentNode.removeChild(card);
    card = null; item = null; stopTrailer();
  }

  function show(p) {
    var it = p.current(), row = p.currentRow();
    if (!it || p.r < 0 || !row || row.chips || !(it.kind === 'movie' || it.kind === 'series' || it.kind === 'episode')) return;
    var at = p.tileRect(); if (!at || at.y < 100 || at.y > 1000) return;
    ensureLayer(); item = it;
    var c = card = build(it);
    layer.appendChild(c);
    var h = c.offsetHeight;
    var left = Math.max(40, Math.min(1920 - W - 40, at.x + at.w / 2 - W / 2)), top = Math.max(120, Math.min(1080 - h - 30, at.y + at.h / 2 - h / 2));
    c.style.left = left + 'px'; c.style.top = top + 'px';
    // it grows out of the tile
    c.style.transform = 'scale(0.82)'; c.style.opacity = '0';
    c.offsetWidth; c.style.transform = ''; c.style.opacity = '';
    // the details (the wide picture, the genres, the trailer) come from Cinemeta the first time
    var forMeta = it.kind === 'episode' ? null : it;
    if (forMeta) Meta.enrich(forMeta).then(function () {
      if (item !== it) return;
      var n = build(it); n.style.left = c.style.left; n.style.top = c.style.top; n.style.transition = 'none';
      layer.replaceChild(n, card); card = n;
      if (it.trailer) startTrailer(it.trailer);
    });
  }
  function build(it) {
    var el = Browse.el, c = el('div', 'peek');
    var pic = el('div', 'peek-pic'); c.appendChild(pic);
    var art = it.kind === 'episode' ? (it.image || it.seriesImage) : (it.backdrop || it.image);
    if (art) { pic.style.backgroundImage = 'url("' + art + '")'; if (!it.backdrop && it.kind !== 'episode') pic.classList.add('poster'); }
    pic.appendChild(el('div', 'peek-fade'));
    var name = el('div', 'peek-title', it.kind === 'episode' ? (it.seriesTitle || '') : Meta.cleanTitle(it.title).title); pic.appendChild(name); Fmt.fitSoon(name, 2, 22);
    if (it.logo) { var lg = new Image(); lg.className = 'peek-logo'; lg.onload = function () { if (name.parentNode) pic.replaceChild(lg, name); }; lg.src = it.logo; }
    var info = el('div', 'peek-info'); c.appendChild(info);
    var btns = el('div', 'peek-btns'); info.appendChild(btns);
    var series = it.kind === 'series', resume = Store.resumeAt(it.key);
    btns.appendChild(el('span', 'pb main', series ? '☰' : '▶'));
    btns.appendChild(el('span', 'pb', Store.isFav(it.key) ? '✓' : '+'));
    btns.appendChild(el('span', 'pb more', 'ⓘ'));
    btns.appendChild(el('span', 'pk-hint', (series ? 'OK  Episodes' : resume ? 'OK  More info   ▶  Resume' : 'OK  More info   ▶  Play') + '    Hold OK  More'));
    if (it.kind === 'episode') info.appendChild(el('div', 'peek-ep', 'S' + it.season + ':E' + it.episode + '  ' + it.title));
    var meta = el('div', 'peek-meta'); Browse.metaLine(meta, it); info.appendChild(meta);
    if (it.plot) info.appendChild(el('div', 'peek-plot', it.plot));
    c._pic = pic;
    return c;
  }

  // ---------- the trailer (YouTube's player) ----------
  function loadYt(cb) {
    if (ytReady) return cb();
    ytWant = cb; if (ytLoading) return;
    ytLoading = true;
    window.onYouTubeIframeAPIReady = function () { ytReady = true; var f = ytWant; ytWant = null; if (f) f(); };
    var s = document.createElement('script'); s.src = 'https://www.youtube.com/iframe_api';
    s.onerror = function () { ytBroken = true; }; document.head.appendChild(s);
  }
  function startTrailer(id) {
    if (ytBroken || App.trailersOff()) return;
    var forItem = item;
    loadYt(function () {
      if (item !== forItem || !card) return;
      stopTrailer();
      ytBox = document.createElement('div'); ytBox.className = 'peek-yt';
      var holder = document.createElement('div'); ytBox.appendChild(holder);
      card._pic.appendChild(ytBox);
      var said = false;
      try {
        yt = new YT.Player(holder, {
          videoId: id, host: 'https://www.youtube-nocookie.com', width: '100%', height: '100%',
          playerVars: { autoplay: 1, controls: 0, disablekb: 1, fs: 0, iv_load_policy: 3, modestbranding: 1, playsinline: 1, rel: 0, cc_load_policy: 0 },
          events: {
            onReady: function (e) { try { e.target.setVolume(100); e.target.playVideo(); } catch (x) { } },
            // shown only once it really plays (until then, and on any error, the picture stays)
            onStateChange: function (e) { if (e.data === 1 && !said && ytBox) { said = true; setTimeout(function () { if (ytBox) ytBox.classList.add('on'); }, 350); } if (e.data === 0) stopTrailer(); },
            onError: function () { stopTrailer(); }
          }
        });
      } catch (e) { stopTrailer(); }
    });
  }
  function stopTrailer() {
    if (yt) { try { yt.stopVideo(); yt.destroy(); } catch (e) { } yt = null; }
    if (ytBox && ytBox.parentNode) ytBox.parentNode.removeChild(ytBox);
    ytBox = null;
  }

  return { soon: soon, hide: hide, get item() { return item; } };
})();
