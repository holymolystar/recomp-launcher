// How sharp a channel's copy really is. A provider's names say little: its "UHD" NPO 1 is 1080p at 3 Mbit/s, while
// a copy without any label has 6.7 Mbit/s. So the main channels' copies (Dutch.arrange: NPO 1 ... Viaplay TV, each
// several times) are measured: a stream's newest piece (a few seconds of video) is asked for, and its size (from the
// answer's header; the rest isn't downloaded) over its length is the bitrate. The first bytes say the video's codec:
// H.265 (HEVC) shows the same picture at about half the bitrate of H.264, so it counts for more.
// The provider allows few streams at once (often one), so it's only done while nothing plays, one request at a time,
// and it waits while something plays. Kept on the TV per provider; measured again after a week.
var Sharp = (function () {
  var AGE = 7 * 86400000, HEVC = 1.8, running = false, onDone = null;

  function prefKey() { var a = App.acct || {}; return 'sharp|' + (a.base || a.url || '') + '|' + (a.user || ''); }
  function all() { return Store.pref(prefKey(), {}) || {}; }
  // a copy's score (Mbit/s, H.265 counted higher), or null when it hasn't been measured
  function score(c) { var m = all()[c.key]; return m && m.mbit > 0 ? m.mbit * (m.hevc ? HEVC : 1) : null; }
  function info(c) { return all()[c.key] || null; }

  function req(u, opt) {
    return new Promise(function (ok) {
      var x = new XMLHttpRequest(), done = false, fin = function (v) { if (done) return; done = true; clearTimeout(t); try { x.abort(); } catch (e) { } ok(v); };
      var t = setTimeout(function () { fin(null); }, 15000);
      try {
        x.open('GET', u);
        if (opt.bin) x.overrideMimeType('text/plain; charset=x-user-defined');
        x.onreadystatechange = function () {
          if (x.readyState === 4) return fin(x.status < 400 ? { text: x.responseText, url: x.responseURL, len: x.responseText.length } : null);
          // a piece of video: its size from the header, and its first bytes for the codec; then stopped
          if (opt.bin && x.readyState === 3 && x.status < 400) {
            var len = Number(x.getResponseHeader('Content-Length')) || 0, s = x.responseText;
            if (s && s.length >= 188 * 40) fin({ len: len, text: s.slice(0, 188 * 40) });
          }
        };
        x.onerror = function () { fin(null); };
        x.send();
      } catch (e) { fin(null); }
    });
  }
  function abs(base, u) {
    if (/^https?:/i.test(u)) return u;
    if (u.charAt(0) === '/') return base.replace(/^(https?:\/\/[^\/]+).*$/, '$1') + u;
    return base.replace(/[^\/]*$/, '') + u;
  }
  // H.265 or not, from the stream's table of contents (MPEG-TS: PAT, then PMT: stream type 0x24 is HEVC)
  function isHevc(s) {
    var b = function (i) { return s.charCodeAt(i) & 255; }, pmt = -1;
    for (var pass = 0; pass < 2; pass++) for (var p = 0; p + 188 <= s.length; p += 188) {
      if (b(p) !== 0x47) continue;
      var pid = ((b(p + 1) & 0x1f) << 8) | b(p + 2), start = b(p + 1) & 0x40, off = p + 4;
      if ((b(p + 3) & 0x30) === 0x30) off += 1 + b(p + 4);
      if (!start || off >= p + 188) continue;
      off += 1 + b(off);   // pointer field
      if (pass === 0 && pid === 0 && b(off) === 0) { pmt = ((b(off + 10) & 0x1f) << 8) | b(off + 11); break; }
      if (pass === 1 && pid === pmt && b(off) === 2) {
        var secLen = ((b(off + 1) & 0x0f) << 8) | b(off + 2), end = Math.min(off + 3 + secLen - 4, p + 188);
        var i = off + 12 + (((b(off + 10) & 0x0f) << 8) | b(off + 11));
        for (; i + 5 <= end; i += 5 + (((b(i + 3) & 0x0f) << 8) | b(i + 4))) { var st = b(i); if (st === 0x24) return true; if (st === 0x1b) return false; }
        return false;
      }
      if (pass === 0 && pmt >= 0) break;
    }
    return false;
  }
  // one copy: its playlist, its newest piece
  function measure(c) {
    return req(c.url, {}).then(function (pl) {
      if (!pl || !/#EXTM3U/.test(pl.text)) return null;
      var m = pl.text.match(/#EXTINF:([\d.]+)[^\n]*\n([^\n#]+)/g); if (!m) return null;
      var last = m[m.length - 1].split('\n'), dur = Number(/#EXTINF:([\d.]+)/.exec(last[0])[1]);
      if (!(dur > 0)) return null;
      return req(abs(pl.url || c.url, last[1].trim()), { bin: true }).then(function (seg) {
        if (!seg || !seg.len) return null;
        return { mbit: Math.round(seg.len * 8 / dur / 1e5) / 10, hevc: isHevc(seg.text), when: Date.now() };
      });
    });
  }
  function idle() { return !Player.playing; }
  function wait(ms) { return new Promise(function (ok) { setTimeout(ok, ms); }); }

  // the copies not measured (or measured over a week ago), one at a time while nothing plays; done() when a
  // measurement changed something
  function start(copies, done) {
    onDone = done;
    if (running || !copies || !copies.length) return;
    var key = prefKey(), kept = all(), todo = copies.filter(function (c) { var m = kept[c.key]; return !m || Date.now() - m.when > AGE; });
    if (!todo.length) return;
    running = true;
    var changed = 0;
    var next = function (i) {
      if (key !== prefKey()) { running = false; return; }   // another provider meanwhile
      if (i >= todo.length) { running = false; if (changed && onDone) onDone(); return; }
      if (!idle()) return wait(20000).then(function () { next(i); });
      measure(todo[i]).then(function (r) {
        var k = all(); k[todo[i].key] = r || { mbit: 0, when: Date.now() }; Store.setPref(key, k);
        if (r) changed++;
        return wait(400);
      }).then(function () { next(i + 1); });
    };
    wait(8000).then(function () { next(0); });
  }
  return { start: start, score: score, info: info, isHevc: isHevc, measure: measure };
})();
