// The pages of rows (For You, Movies, Shows, a category, Search), drawn the launcher's way: a big featured title at
// the top, then rows of posters (channels are wide tiles; Top 10 has the big numbers). Only the rows near the one
// you're on exist, and only the tiles near the picked one, so a row of 5,000 films is as quick as a row of 10.
// Resting on a poster opens its card (TvPeek.cs) with the trailer playing over the picture when it has one.
var Browse = (function () {
  var POSTER = { w: 200, h: 300 }, WIDE = { w: 300, h: 169 }, GAP = 22, LEFT = 80, TOP10_NUM = 120;
  var ROW_TITLE = 56, ROW_PAD = 44, HERO_H = 760;

  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

  // ---------- a picture, from the provider or (when it sends none) from Cinemeta ----------
  var artQueue = [], artBusy = 0;
  function artLater(it, cb) {
    if (!it || !(it.kind === 'movie' || it.kind === 'series') || it._artAsked) return;
    it._artAsked = true; artQueue.push([it, cb]); artNext();
  }
  function artNext() {
    if (artBusy >= 4 || !artQueue.length) return;
    var j = artQueue.shift(); artBusy++;
    var had = j[0].image;
    Meta.enrich(j[0]).then(function () { artBusy--; if (j[0].image && j[0].image !== had) j[1](); artNext(); });
  }

  // ---------- a page ----------
  function Page(root, opt) {
    this.root = root; this.opt = opt || {};
    this.rows = []; this.r = -1; this.cols = []; this.hero = null; this.heroBtn = 0;
    this.content = el('div', 'b-content'); root.appendChild(this.content);
    this.heroEl = el('div', 'hero'); this.content.appendChild(this.heroEl);
    this.rowEls = {}; this.tops = []; this.peekTimer = null; this.firstVisit = true;
  }
  Page.prototype.setRows = function (rows, hero) {
    this.rows = rows.filter(function (r) { return (r.items && r.items.length) || (r.chips && r.chips.length); });
    this.cols = this.rows.map(function () { return 0; });
    this.hero = this.opt.noHero ? null : hero;
    for (var k in this.rowEls) this.content.removeChild(this.rowEls[k]);
    this.rowEls = {};
    // where each row is
    var y = this.hero ? HERO_H : (this.opt.top || 200), t = [];
    for (var i = 0; i < this.rows.length; i++) { t.push(y); y += this.rowH(this.rows[i]); }
    this.tops = t;
    this.drawHero();
    this.r = this.hero ? -1 : 0; this.heroBtn = 0;
    this.update();
  };
  Page.prototype.rowH = function (r) {
    if (r.chips) return ROW_TITLE + 76 + ROW_PAD;
    var t = r.grid ? 0 : ROW_TITLE;
    return t + (r.wide ? WIDE.h : POSTER.h) + (r.grid ? 34 : ROW_PAD);
  };
  Page.prototype.size = function (r) { return r.wide ? WIDE : POSTER; };
  Page.prototype.step = function (r) { return (r.top10 ? TOP10_NUM : 0) + this.size(r).w + GAP; };
  Page.prototype.visible = function (r) { return Math.max(1, Math.floor((this.opt.width || 1920 - LEFT - 40) / this.step(r))); };

  // ---------- the featured title ----------
  Page.prototype.drawHero = function () {
    var h = this.heroEl, it = this.hero; h.innerHTML = ''; h.style.display = it ? '' : 'none';
    if (!it) return;
    Meta.apply(it);
    var pic = el('div', 'hero-pic'); h.appendChild(pic);
    var setPic = function () { var src = it.backdrop || it.image; pic.classList.toggle('poster', !it.backdrop); if (pic._src === src) return; pic._src = src; Look.bgWhenLoaded(pic, src); var g = Look.glow(h, src); if (g && g.parentNode === h) h.insertBefore(g, h.querySelector('.hero-box')); };
    h.appendChild(el('div', 'hero-fade'));
    if (it.backdrop || it.image) setPic();
    var box = el('div', 'hero-box'); h.appendChild(box);
    var name = el('div', 'hero-title', Meta.cleanTitle(it.title).title); box.appendChild(name); Fmt.fitSoon(name, 2, 48);
    if (it.logo) { var lg = new Image(); lg.className = 'hero-logo'; lg.onload = function () { box.replaceChild(lg, name); }; lg.src = it.logo; }
    var meta = el('div', 'hero-meta'); metaLine(meta, it); box.appendChild(meta);
    var plot = el('div', 'hero-plot', it.plot || ''); box.appendChild(plot);
    var btns = el('div', 'hero-btns'); box.appendChild(btns);
    var resume = Store.resumeAt(it.key);
    btns.appendChild(el('div', 'hbtn play', it.kind === 'series' ? '▶  Play' : resume ? '▶  Resume' : '▶  Play'));
    btns.appendChild(el('div', 'hbtn', 'ⓘ  More info'));
    this.heroBtns = btns.children;
    var self = this;
    if (!it.backdrop || !it.plot) Meta.enrich(it).then(function (ok) { if (ok && self.hero === it) { if (it.backdrop || it.image) setPic(); plot.textContent = it.plot || ''; meta.innerHTML = ''; metaLine(meta, it); if (it.logo && !lg) { lg = new Image(); lg.className = 'hero-logo'; lg.onload = function () { if (name.parentNode) box.replaceChild(lg, name); }; lg.src = it.logo; } } });
  };

  // ---------- drawing what's near the picked row ----------
  Page.prototype.update = function () {
    var r = this.r, self = this;
    // the page moves so the picked row sits under the header
    var y = r < 0 ? 0 : this.tops[r] - (this.opt.top || 200);
    if (this.hero && r === 0) y = Math.max(0, this.tops[0] - 430);   // the first row: the featured title still peeks out
    this.content.style.transform = 'translateY(' + (-y) + 'px)';
    if (this.heroBtns) [].forEach.call(this.heroBtns, function (b, i) { b.classList.toggle('focus', r < 0 && i === self.heroBtn); });
    var from = Math.max(0, r - 2), to = Math.min(this.rows.length - 1, Math.max(r, 0) + 4);
    for (var k in this.rowEls) if (k < from || k > to) { this.content.removeChild(this.rowEls[k]); delete this.rowEls[k]; }
    for (var i = from; i <= to; i++) this.drawRow(i);
    App.Peek.soon(this);
  };
  Page.prototype.drawRow = function (i) {
    var row = this.rows[i], e = this.rowEls[i], self = this;
    if (!e) {
      e = this.rowEls[i] = el('div', 'brow' + (row.wide ? ' wide' : '') + (row.top10 ? ' top10' : '') + (row.chips ? ' chips' : '') + (row.grid ? ' grid' : ''));
      e.style.top = this.tops[i] + 'px';
      if (!row.grid) e.appendChild(el('div', 'brow-title', row.title));
      e._strip = el('div', 'strip'); e.appendChild(e._strip); e._tiles = {};
      this.content.appendChild(e);
      if (row.chips) row.chips.forEach(function (c, j) { var b = el('div', 'chip', c.name); e._strip.appendChild(b); e._tiles[j] = b; });
    }
    var picked = i === this.r, col = this.cols[i];
    e.classList.toggle('picked', picked);
    if (row.chips) {
      for (var j in e._tiles) e._tiles[j].classList.toggle('focus', picked && Number(j) === col);
      var c = e._tiles[col]; var x = c ? Math.max(0, c.offsetLeft - 200) : 0;
      e._strip.style.transform = 'translateX(' + (-x) + 'px)';
      return;
    }
    var vis = this.visible(row), n = row.items.length, step = this.step(row);
    var first = row.grid ? 0 : Math.max(0, Math.min(col - 1, n - vis));
    e._strip.style.transform = 'translateX(' + (-first * step) + 'px)';
    var lo = Math.max(0, first - 2), hi = Math.min(n - 1, first + vis + 2);
    for (var t in e._tiles) if (t < lo || t > hi) { e._strip.removeChild(e._tiles[t]); delete e._tiles[t]; }
    for (var j2 = lo; j2 <= hi; j2++) {
      var tile = e._tiles[j2];
      if (!tile) { tile = e._tiles[j2] = this.tile(row, row.items[j2], j2); tile.style.left = (j2 * step) + 'px'; e._strip.appendChild(tile); }
      tile.classList.toggle('focus', picked && j2 === col);
    }
  };
  Page.prototype.tile = function (row, it, j) {
    var s = this.size(row), t = el('div', 'tile' + (row.wide ? ' wide' : '')); t.style.height = s.h + 'px';
    var box = el('div', 'tbox'); box.style.width = s.w + 'px'; box.style.height = s.h + 'px';
    if (row.top10) { t.appendChild(el('div', 'num', String(j + 1))); box.style.left = TOP10_NUM + 'px'; }
    var shade = el('div', 'shade'); shade.style.left = (row.top10 ? TOP10_NUM : 0) + s.w * 0.08 + 'px'; shade.style.width = s.w * 0.84 + 'px'; shade.style.top = s.h * 0.7 + 'px'; shade.style.height = s.h * 0.4 + 'px'; t.appendChild(shade);
    t.appendChild(box);
    var pal = ['#5b2a86', '#1b4f72', '#7b241c', '#145a32', '#784212', '#4a235a'], h = 0;
    for (var i = 0; i < (it.title || '').length; i++) h = (h * 31 + it.title.charCodeAt(i)) | 0;
    box.style.background = 'linear-gradient(160deg,' + pal[Math.abs(h) % pal.length] + ',#0b1114)';
    var name = el('div', 'tname', it.kind === 'episode' ? (it.seriesTitle || it.title) : Meta.cleanTitle(it.title).title); box.appendChild(name);
    var pic = it.kind === 'episode' ? (it.seriesImage || it.image) : it.image;
    var put = function (src) {
      var img = new Image(); img.className = it.kind === 'live' ? 'logo' : '';
      img.onload = function () { box.appendChild(img); requestAnimationFrame(function () { img.classList.add('in'); }); if (it.kind !== 'live') name.style.display = 'none'; var old = box.querySelector('img:not(:last-child)'); if (old) box.removeChild(old); };
      img.src = src;
    };
    if (pic) put(pic); else artLater(it, function () { put(it.image); });
    // where you are in it (Continue watching), or that it's an episode
    var pr = Store.progress(it.key);
    if (it.kind === 'episode') box.appendChild(el('div', 'tlabel', 'S' + it.season + ':E' + it.episode));
    if (pr && pr.dur) { var bar = el('div', 'tbar'); var fill = el('i'); fill.style.width = Math.min(100, 100 * pr.pos / pr.dur) + '%'; bar.appendChild(fill); box.appendChild(bar); }
    if (it.kind === 'live') box.appendChild(el('div', 'tlive', it.title));
    if (Extras.isNew(it) && !(pr && pr.dur)) box.appendChild(el('div', 'tnew', 'New'));
    if (it.kind === 'collection') box.appendChild(el('div', 'tcol', it.films.length + ' films'));
    return t;
  };

  // ---------- moving ----------
  Page.prototype.current = function () { if (this.r < 0) return this.hero; var row = this.rows[this.r]; return row && row.items ? row.items[this.cols[this.r]] : null; };
  Page.prototype.currentRow = function () { return this.rows[this.r]; };
  Page.prototype.key = function (k) {
    var r = this.r, row = this.rows[r];
    if (k === 'up') {
      if (r < 0 || (r === 0 && !this.hero)) return 'top';   // to the header
      this.r--; this.update(); return true;
    }
    if (k === 'down') { if (r < this.rows.length - 1) { this.r++; if (this.rows[this.r].grid && row && row.grid) this.cols[this.r] = Math.min(this.cols[r], this.rows[this.r].items.length - 1); this.update(); } return true; }
    if (k === 'left' || k === 'right') {
      var d = k === 'left' ? -1 : 1;
      if (r < 0) { this.heroBtn = Math.max(0, Math.min(1, this.heroBtn + d)); this.update(); return true; }
      var n = row.chips ? row.chips.length : row.items.length, c = this.cols[r] + d;
      if (c < 0 || c >= n) return true;
      this.cols[r] = c;
      if (row.grid) for (var i = 0; i < this.rows.length; i++) if (this.rows[i].grid) this.cols[i] = Math.min(c, this.rows[i].items.length - 1);
      this.update(); return true;
    }
    if (k === 'chup' || k === 'chdown') { this.r = Math.max(this.hero ? -1 : 0, Math.min(this.rows.length - 1, r + (k === 'chup' ? -3 : 3))); this.update(); return true; }
    if (k === 'ok') {
      if (r < 0) { if (this.heroBtn === 0) App.play(this.hero); else App.openDetails(this.hero); return true; }
      if (row.chips) { row.chips[this.cols[r]].open(); return true; }
      App.open(this.current(), row); return true;
    }
    if (k === 'play' || k === 'playpause') { var it = this.current(); if (it) App.play(it); return true; }
    if (k === 'yellow') { var x = this.current(); if (x) App.toggleList(x); return true; }
    return false;
  };
  // the screen came back: redraw (progress, My list may have changed)
  Page.prototype.refresh = function () { var keep = this.rowEls; for (var k in keep) this.content.removeChild(keep[k]); this.rowEls = {}; this.update(); };
  // where the picked tile is on the screen
  Page.prototype.tileRect = function () {
    var e = this.rowEls[this.r]; if (!e) return null; var t = e._tiles[this.cols[this.r]]; if (!t) return null;
    var b = t.querySelector('.tbox') || t, rs = b.getBoundingClientRect(), st = document.getElementById('stage').getBoundingClientRect(), sc = App.scale();
    return { x: (rs.left - st.left) / sc, y: (rs.top - st.top) / sc, w: rs.width / sc, h: rs.height / sc };
  };

  // what it is: year, length, rating, genres
  function metaLine(e, it) {
    var add = function (t, cls) { if (!t) return; var s = el('span', cls, t); e.appendChild(s); };
    if (it.rating && Number(it.rating) > 0) add('★ ' + (Number(it.rating) > 10 ? (Number(it.rating) / 10).toFixed(1) : Number(it.rating).toFixed(1)), 'rate');
    add(it.year);
    add(it.duration ? String(it.duration).replace(/^0?(\d+):(\d\d):\d\d$/, function (m, hh, mm) { return (Number(hh) ? hh + 'h ' : '') + Number(mm) + 'm'; }) : '');
    if (it.kind === 'series') add('Series');
    var g = (it.genre || '').split(/[,\/]/).map(function (s) { return s.trim(); }).filter(Boolean).slice(0, 3).join(' · '); add(g, 'genres');
  }

  // the featured title of a page: one with a wide picture, a different one each day
  function featured(list) {
    var l = list.slice(0, 60).filter(function (x) { Meta.apply(x); return x.backdrop && x.image; });
    if (!l.length) l = list.slice(0, 30).filter(function (x) { return x.image; });
    if (!l.length) return list[0] || null;
    var d = new Date(), seed = (d.getFullYear() * 400 + d.getMonth() * 31 + d.getDate()) * 7;
    return l[seed % l.length];
  }

  return { Page: Page, metaLine: metaLine, featured: featured, el: el };
})();
