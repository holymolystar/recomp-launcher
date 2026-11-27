// Making text fit, and subtitles readable.
//  - Fmt.fit(e, lines, min): a title shrinks until it fits in that many lines, and is cut off with "…" if it
//    still doesn't (a long film name never runs over the buttons or off the screen).
//  - Fmt.subs(box, texts): shows subtitle lines. A film's own subtitles often come as they are inside the file:
//    ASS/SSA markup ({\an8}, {\i1}, \N, "12,0,Default,,0,0,0,,"), HTML (<i>, <font>), &amp; and the like. Those
//    are turned into plain lines (italics kept), lines meant for the top go to the top, a long line is split in two
//    of about the same length, and the text shrinks a little rather than covering half the picture.
var Fmt = (function () {
  // ---------- titles and the like ----------
  function fit(e, lines, min) {
    if (!e || !e.parentNode) return;
    e.style.fontSize = ''; e.classList.remove('clamp');
    var cs = getComputedStyle(e), size = parseFloat(cs.fontSize), lh = parseFloat(cs.lineHeight) / size || 1.2;
    if (!size || !e.offsetWidth) return;   // not on screen yet
    min = min || Math.round(size * 0.6);
    var tooBig = function () { return e.scrollHeight > Math.ceil(size * lh * lines) + 2 || e.scrollWidth > e.clientWidth + 2; };
    while (tooBig() && size > min) { size = Math.max(min, size - 4); e.style.fontSize = size + 'px'; }
    if (tooBig()) { e.style.webkitLineClamp = String(lines); e.classList.add('clamp'); }
  }
  // after the page is drawn (fonts and widths known)
  function fitSoon(e, lines, min) { if (e) requestAnimationFrame(function () { fit(e, lines, min); }); }

  // ---------- subtitles ----------
  var ENT = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', hellip: '…', ndash: '–', mdash: '—', lsquo: '‘', rsquo: '’', ldquo: '“', rdquo: '”' };
  function decode(s) {
    return s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, function (m, e) {
      if (e.charAt(0) === '#') { var n = e.charAt(1) === 'x' || e.charAt(1) === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10); try { return n > 0 ? String.fromCodePoint(n) : ''; } catch (x) { return ''; } }
      var k = e.toLowerCase(); return ENT.hasOwnProperty(k) ? ENT[k] : m;
    });
  }
  var I_ON = '\u0001', I_OFF = '\u0002';
  // one subtitle as it came → { lines: [[{t, i}]], top } (null: nothing to show)
  function clean(raw) {
    var s = String(raw || '');
    if (!s) return null;
    // an ASS event as it sits in a Matroska file, or a whole "Dialogue:" line
    s = s.replace(/^Dialogue:\s*(?:[^,]*,){9}/i, '').replace(/^\d+,-?\d+,[^,]*,[^,]*,-?\d+,-?\d+,-?\d+,[^,]*,/, '');
    if (/\{[^}]*\\p[1-9][^}]*\}/.test(s)) return null;          // a drawing (shapes), not words
    var top = /\{[^}]*\\an[789][^}]*\}/.test(s) || /\{[^}]*\\a(5|6|7)(?!\d)[^}]*\}/.test(s);
    s = decode(s);
    // override blocks ({\i1}, {\an8\fs40}): italics are kept, the rest goes
    s = s.replace(/\{[^}]*\}/g, function (m) { if (m.indexOf('\\') < 0) return m; if (/\\i1/.test(m)) return I_ON; if (/\\i0|\\i(?![a-z\d])/.test(m)) return I_OFF; return ''; });
    s = s.replace(/<\s*i\s*>/gi, I_ON).replace(/<\s*\/\s*i\s*>/gi, I_OFF).replace(/<\/?[a-z][^>]*>/gi, '');
    s = s.replace(/\\[Nn]/g, '\n').replace(/\\h/g, ' ').replace(/\r/g, '').replace(/[\t ]/g, ' ');
    if (Store.pref('subSdh', false)) s = stripSdh(s);
    var out = [], ital = false;
    s.split('\n').forEach(function (line) {
      var segs = [], buf = '';
      for (var k = 0; k < line.length; k++) {
        var c = line.charAt(k);
        if (c === I_ON || c === I_OFF) { if (buf) segs.push({ t: buf, i: ital }); buf = ''; ital = c === I_ON; }
        else buf += c;
      }
      if (buf) segs.push({ t: buf, i: ital });
      // tidy spaces; a line that's only a dash or empty goes
      var txt = segs.map(function (x) { return x.t; }).join('').replace(/\s+/g, ' ').trim();
      if (!txt || /^[-–—]$/.test(txt)) return;
      segs.forEach(function (x) { x.t = x.t.replace(/\s+/g, ' '); });
      segs[0].t = segs[0].t.replace(/^\s+/, ''); segs[segs.length - 1].t = segs[segs.length - 1].t.replace(/\s+$/, '');
      out.push(segs.filter(function (x) { return x.t; }));
    });
    return out.length ? { lines: out, top: top } : null;
  }
  // [door slams], (laughs), ♪-free sound notes and "JOHN:" before a line, for those who'd rather not see them
  function stripSdh(s) {
    return s.split('\n').map(function (l) {
      return l.replace(/\[[^\]]*\]/g, '').replace(/\([^)]*\)/g, '')
        .replace(/^(\s*[-–]?\s*)[A-Z][A-Z0-9 .'’\-]{1,24}:\s+/, '$1').replace(/^\s*[-–]?\s*[A-Z][A-Z0-9 .'’\-]{1,24}:\s*$/, '');
    }).join('\n');
  }
  function len(line) { var n = 0; line.forEach(function (x) { n += x.t.length; }); return n; }
  // a line too long for one row: split at the space nearest the middle (two even rows, not a long one and a word)
  function balance(lines, perRow) {
    if (lines.length > 2) return lines;
    var r = [];
    lines.forEach(function (line) {
      var n = len(line);
      if (n <= perRow || lines.length > 1 && n <= perRow * 1.6) { r.push(line); return; }
      var flat = [], k;
      line.forEach(function (x) { for (k = 0; k < x.t.length; k++) flat.push({ c: x.t.charAt(k), i: x.i }); });
      var mid = flat.length / 2, best = -1;
      for (k = 1; k < flat.length - 1; k++) if (flat[k].c === ' ' && (best < 0 || Math.abs(k - mid) < Math.abs(best - mid))) best = k;
      if (best < 0) { r.push(line); return; }
      var join = function (a) { var segs = []; a.forEach(function (x) { var l = segs[segs.length - 1]; if (l && l.i === x.i) l.t += x.c; else segs.push({ t: x.c, i: x.i }); }); return segs; };
      r.push(join(flat.slice(0, best))); r.push(join(flat.slice(best + 1)));
    });
    return r;
  }
  function draw(box, cues) {
    box.innerHTML = '';
    box.style.fontSize = '';
    if (!cues.length) return;
    var size = parseFloat(getComputedStyle(box).fontSize) || 46, width = box.clientWidth || 1600;
    var perRow = Math.max(20, Math.floor(width / (size * 0.5)));
    var lines = [];
    cues.forEach(function (c) { lines = lines.concat(balance(c.lines, perRow)); });
    lines.forEach(function (line) {
      var row = document.createElement('div');
      line.forEach(function (x) { var sp = document.createElement('span'); if (x.i) sp.className = 'i'; sp.textContent = x.t; row.appendChild(sp); });
      box.appendChild(row);
    });
    // more than three rows (or rows that wrapped anyway): a little smaller, never below about two thirds
    var rows = function () { return Math.round(box.scrollHeight / (size * 1.25)); };
    var s = size;
    while (rows() > Math.max(3, lines.length > 3 ? 4 : 3) && s > size * 0.7) { s = Math.round(s * 0.9); box.style.fontSize = s + 'px'; }
  }
  // shows subtitle texts (several can be on screen at once); lines meant for the top get a box of their own there
  function subs(box, texts) {
    if (!box) return;
    if (!box._top) { box._top = document.createElement('div'); if (box.parentNode) box.parentNode.insertBefore(box._top, box.nextSibling); }
    box._top.className = box.className.replace(/\btop\b/g, '') + ' top';
    var bottom = [], top = [];
    (texts || []).forEach(function (t) { var c = clean(t); if (c) (c.top ? top : bottom).push(c); });
    draw(box, bottom); draw(box._top, top);
  }
  function plain(raw) { var c = clean(raw); return c ? c.lines.map(function (l) { return l.map(function (x) { return x.t; }).join(''); }).join('\n') : ''; }
  return { fit: fit, fitSoon: fitSoon, subs: subs, clean: clean, plain: plain };
})();
