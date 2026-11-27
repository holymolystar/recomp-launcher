// The best copy of a film: a provider often has the same film several times (other categories, other languages, a
// 4K one), and names don't say which is better. The file's size does: for the same film, the biggest file is the
// best picture (1 GB is a small re-encode, 9 GB a proper 1080p, 17 GB 4K). Each copy's size is asked for with a
// one-byte request (the provider answers in under half a second), one at a time (providers count connections), and
// kept for as long as the app runs. Play then plays the biggest copy of the ones you'd want:
//   Settings → Best version   "Same language" (default): only copies in the language of the one you picked
//                             "My languages": also copies in the profile's other languages
//                             "Off": always the one you picked
// The film stays the one you picked (where you are in it, My list, Continue watching); only its file is another.
var Best = (function () {
  var sizes = {}, asking = {}, CHOICES = [['same', 'Same language'], ['mine', 'My languages'], ['off', 'Off']];
  var JUNK = /\b(cam|camrip|hdcam|ts|telesync|hdts)\b/i;

  function mode() { return Store.pref('bestVer', 'same'); }
  function modeName() { var m = mode(); for (var i = 0; i < CHOICES.length; i++) if (CHOICES[i][0] === m) return CHOICES[i][1]; return CHOICES[0][1]; }
  function nextMode() { var i = CHOICES.map(function (c) { return c[0]; }).indexOf(mode()); Store.setPref('bestVer', CHOICES[(i + 1) % CHOICES.length][0]); return modeName(); }

  // the copies worth comparing: the film and its versions that are films, playable, not cinema recordings, in
  // a language you'd want
  function candidates(it) {
    if (!it || it.kind !== 'movie' || !it.url || mode() === 'off') return [];
    var mine = (Store.profile.langs || []);
    var ok = function (y) {
      if (y.kind !== 'movie' || !y.url || JUNK.test(y.title || '')) return false;
      if ((y.lang || '') === (it.lang || '')) return true;
      return mode() === 'mine' && !!y.lang && mine.indexOf(y.lang) >= 0;
    };
    return [it].concat(Extras.versionsOf(it).filter(ok)).slice(0, 8);
  }
  // a file's size in bytes (0: unknown)
  function sizeOf(y, timeout) {
    if (sizes[y.key] !== undefined) return Promise.resolve(sizes[y.key]);
    if (asking[y.key]) return asking[y.key];
    return (asking[y.key] = new Promise(function (ok) {
      var done = function (n) { delete asking[y.key]; if (n >= 0) sizes[y.key] = n; ok(n > 0 ? n : 0); }, x = new XMLHttpRequest();
      try {
        x.open('GET', y.url); x.setRequestHeader('Range', 'bytes=0-0'); x.timeout = timeout || 8000;
        x.onreadystatechange = function () {
          if (x.readyState !== 2) return;
          var cr = x.getResponseHeader('Content-Range'), m = cr && /\/(\d+)\s*$/.exec(cr);
          var n = m ? Number(m[1]) : x.status === 200 ? Number(x.getResponseHeader('Content-Length')) || 0 : 0;
          try { x.abort(); } catch (e) { }
          done(x.status >= 400 ? 0 : n);
        };
        x.onerror = function () { done(-1); }; x.ontimeout = function () { done(-1); };
        x.send();
      } catch (e) { done(-1); }
    }));
  }
  // asks for the sizes one by one; stops at the deadline (what's known by then counts)
  function measure(l, until) {
    var next = function (i) {
      if (i >= l.length || Date.now() > until) return Promise.resolve();
      return sizeOf(l[i], Math.max(1000, Math.min(8000, until - Date.now()))).then(function () { return next(i + 1); });
    };
    return next(0);
  }
  // on a film's page: found out already, so Play doesn't wait
  function warm(it) { var l = candidates(it); if (l.length > 1) measure(l, Date.now() + 20000); }
  // what Play plays: {url, note} (note: what was picked, for a line on screen; null when it's the one you picked)
  function pick(it) {
    var l = candidates(it);
    if (l.length < 2) return Promise.resolve({ url: it.url, note: null });
    return measure(l, Date.now() + 3000).then(function () {
      var best = it, bs = sizes[it.key] || 0;
      l.forEach(function (y) { var s = sizes[y.key] || 0; if (s > bs) { best = y; bs = s; } });
      // only for a real difference (a quarter more), not the same file twice
      if (best === it || bs < (sizes[it.key] || 0) * 1.25) return { url: it.url, note: null };
      return { url: best.url, note: 'The best copy: ' + (best.cat || '') + '  ·  ' + gb(bs) + (sizes[it.key] ? ' (instead of ' + gb(sizes[it.key]) + ')' : '') };
    });
  }
  function gb(n) { return n >= 1e9 ? (n / 1e9).toFixed(1) + ' GB' : Math.round(n / 1e6) + ' MB'; }

  return { pick: pick, warm: warm, modeName: modeName, nextMode: nextMode };
})();
