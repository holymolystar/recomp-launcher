// The channels in a Dutch order: the Dutch categories first (the one with NPO 1 at the top), and the numbers a Dutch
// TV has (1 NPO 1, 2 NPO 2, 3 NPO 3, 4 RTL 4 ... 10 RTL 8), whatever the provider numbers them. A provider has each
// main channel several times (UHD, "16ᴋ", "8ᴋ", HD, SD, one with the TV Archive: "rec"): the number goes to the best
// picture of them in the Dutch categories (measured: sharp.js; till then, what the names say). The Guide shows the first copy instead (the one with the archive, where
// you can watch back): it has the number's place there (gnum), and the best copy that copy's. Every other channel
// follows from 20 on, category by category, in the provider's order. The provider's own number is kept (pnum), so this can be
// done again on the same lists (they're kept on the TV) and comes out the same.
var Dutch = (function () {
  var NUMBERS = {
    npo1: 1, npo2: 2, npo3: 3, rtl4: 4, rtl5: 5, sbs6: 6, rtl7: 7, veronica: 8, net5: 9, rtl8: 10,
    npo1extra: 11, npo2extra: 12, npopolitiek: 13, rtlz: 14, sbs9: 15, viaplaytv: 16, 'viaplaytv+': 17
  };
  var REST = 20;
  var THEMES = [/^Algemeen/i, /^Sports?\b/i, /^Entertainment/i, /^Films?\b|^Film1|^Movies/i, /^Documentaire/i, /^Kinderen|^Kids/i,
    /^Muziek/i, /^Regionale|^Regio/i, /^Nederland HD$/i, /^Nederland/i];
  // "NPO 1 HD 8ᴋ ◉ rec", "VERONICA HD / DISNEY XD", "NL: Net 5 FHD" → npo1, veronica, net5
  var SMALL = { 'ᴀ': 'a', 'ʙ': 'b', 'ᴄ': 'c', 'ᴅ': 'd', 'ᴇ': 'e', 'ғ': 'f', 'ɢ': 'g', 'ʜ': 'h', 'ɪ': 'i', 'ᴊ': 'j', 'ᴋ': 'k', 'ʟ': 'l', 'ᴍ': 'm', 'ɴ': 'n', 'ᴏ': 'o', 'ᴘ': 'p', 'ʀ': 'r', 'ꜱ': 's', 'ᴛ': 't', 'ᴜ': 'u', 'ᴠ': 'v', 'ᴡ': 'w', 'ʏ': 'y', 'ᴢ': 'z' };
  function keyOf(t) {
    var s = String(t || '').replace(/[ᴀ-ᴢʀ-ʟꜱғɢɪɴ]/g, function (c) { return SMALL[c] || c; }).toLowerCase();
    s = s.split('/')[0].replace(/^\s*nl\s*[:|\-]\s*/, '');
    s = s.replace(/\b(hd|fhd|uhd|sd|hevc|h265|raw|rec|nl|backup|live|tv\s*$)\b/g, ' ').replace(/\b\d+\s*k\b/g, ' ');
    s = s.replace(/politiek\s*(en|&)?\s*nieuws/, 'politiek');
    var k = s.replace(/[^a-z0-9+]/g, '');
    if (NUMBERS[k]) return k;
    // "Viaplay TV" lost its "tv" above; the others don't end in it
    if (NUMBERS[k + 'tv']) return k + 'tv';
    return null;
  }
  // how good a copy's picture is, from its name and category: UHD (4K) best, then the bitrate tiers ("16ᴋ", "8ᴋ":
  // Mbit/s, small capital ᴋ), then FHD, HD, nothing said, SD
  var SUP = { 'ᵁ': 'U', 'ᴴ': 'H', 'ᴰ': 'D', 'ᶠ': 'F', 'ˢ': 'S' };
  function quality(c) {
    var t = String(c.title || '') + ' ' + String(c.cat || ''), u = t.replace(/[ᵁᴴᴰᶠˢ]/g, function (x) { return SUP[x]; });
    if (/UHD|2160|\b4K\b|\b8K\b/.test(u)) return 100;
    var m = /(\d+)\s*ᴋ/.exec(t); if (m) return 20 + Math.min(60, Number(m[1]));
    if (/\bFHD\b|1080/i.test(u)) return 15;
    if (/\bSD\b/i.test(u)) return 1;
    if (/\bHD\b/i.test(u)) return 10;
    return 5;
  }
  // measured (Sharp: the real bitrate and codec) wins over what the names say; a copy that didn't play comes last
  function q(c) {
    var m = window.Sharp && Sharp.info(c);
    if (m) return m.mbit > 0 ? 1000 + Sharp.score(c) : -1;
    return quality(c);
  }
  function isDutchCat(name, list) {
    if (/·\s*NL$|\bNL\b|Nederland|\bRegionale\b|Feyenoord|Feynoord|Volendam|Videoland/i.test(name)) return true;
    if (!list || list.length < 3) return false;
    var nl = 0; list.forEach(function (c) { if (c.lang === 'NL') nl++; });
    return nl / list.length >= 0.6;
  }

  // order: the category names, in the provider's order; chans: every channel. Gives both back in the Dutch order,
  // with the channels' numbers changed.
  function arrange(order, chans) {
    var byCat = {};
    chans.forEach(function (c) { if (c.pnum == null) c.pnum = c.num; (byCat[c.cat] = byCat[c.cat] || []).push(c); });
    var byP = function (x, y) { return x.pnum - y.pnum; };
    Object.keys(byCat).forEach(function (k) { byCat[k].sort(byP); });
    var names = order.slice(); Object.keys(byCat).forEach(function (k) { if (names.indexOf(k) < 0) names.push(k); });
    var dutch = names.filter(function (n) { return isDutchCat(n, byCat[n]); });
    if (!dutch.length) { chans.forEach(function (c) { c.num = c.pnum; }); return { order: order, chans: chans.slice().sort(byP) }; }
    // the main one: where NPO 1 is (else the one with the most main channels)
    var score = function (n) { var s = 0; (byCat[n] || []).forEach(function (c) { var k = keyOf(c.title); if (k) s += k === 'npo1' ? 100 : 1; }); return s; };
    var main = dutch.slice().sort(function (a, b) { return score(b) - score(a); })[0];
    if (!score(main)) main = dutch[0];
    // then the everyday ones, as a Dutch TV has them; then the other Dutch ones (events, on demand, raw feeds) in the
    // provider's order
    var rank = function (n) {
      if (n === main) return 0;
      for (var i = 0; i < THEMES.length; i++) if (THEMES[i].test(n)) return i + 1;
      return THEMES.length + 1;
    };
    dutch = dutch.map(function (n, i) { return [n, i]; }).sort(function (a, b) { return rank(a[0]) - rank(b[0]) || a[1] - b[1]; }).map(function (x) { return x[0]; });
    var rest = names.filter(function (n) { return dutch.indexOf(n) < 0; });
    var newOrder = dutch.concat(rest);
    // the main channels' copies: the first in the Dutch categories (for the Guide), the best picture (the number)
    var first = {}, best = {}, keys = [], copies = [];
    dutch.forEach(function (n) { (byCat[n] || []).forEach(function (c) {
      var k = keyOf(c.title); if (!k) return;
      copies.push(c);
      if (!first[k]) { first[k] = best[k] = c; keys.push(k); }
      else if (q(c) > q(best[k])) best[k] = c;
    }); });
    chans.forEach(function (c) { c.num = 0; delete c.gnum; delete c.recKey; });
    keys.forEach(function (k) { best[k].num = NUMBERS[k]; first[k].gnum = NUMBERS[k]; if (best[k] !== first[k]) best[k].recKey = first[k].key; });
    // in the main category the main channels come first, in their numbers
    if (byCat[main]) byCat[main].sort(function (x, y) { return (x.gnum || x.num || 1e9) - (y.gnum || y.num || 1e9) || x.pnum - y.pnum; });
    var n = REST, out = [];
    newOrder.forEach(function (name) { (byCat[name] || []).forEach(function (c) { if (!c.num) c.num = n++; out.push(c); }); });
    // the Guide's place of a best copy: its first copy's number (they swap places there)
    keys.forEach(function (k) { if (best[k] !== first[k]) best[k].gnum = first[k].num; });
    out.sort(function (x, y) { return x.num - y.num; });
    return { order: newOrder.filter(function (n) { return order.indexOf(n) >= 0 || byCat[n]; }), chans: out, copies: copies };
  }
  return { arrange: arrange, keyOf: keyOf, quality: quality, guideOrder: function (l) { return l.slice().sort(function (x, y) { return (x.gnum || x.num) - (y.gnum || y.num) || (y.gnum ? 1 : 0) - (x.gnum ? 1 : 0); }); } };
})();
