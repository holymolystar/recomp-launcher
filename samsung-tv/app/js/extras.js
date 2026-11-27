// The launcher's extras for films and series (TvVersions.cs, TvExtras.cs, TvLook.cs):
//   original audio   the country a film or series comes from (Cinemeta) tells its original language, which the
//                    player picks first, so a Korean film is heard in Korean, not dubbed
//   versions         the same film or series elsewhere at your provider (another language, another quality)
//   collections      films of one series together (Toy Story, Toy Story 2...): two or more, one of them numbered
//   new              what the provider added in the last two weeks has a "New" badge; New this week: the last 7 days
var Extras = (function () {
  var COUNTRY = {
    'south korea': 'ko,kor', korea: 'ko,kor', japan: 'ja,jpn', china: 'zh,chi,zho,cmn', 'hong kong': 'yue,zh,chi,zho', taiwan: 'zh,chi,zho,cmn',
    thailand: 'th,tha', philippines: 'tl,fil,tgl', india: 'hi,hin,ta,tam,te,tel', france: 'fr,fra,fre', germany: 'de,deu,ger', 'west germany': 'de,deu,ger',
    austria: 'de,deu,ger', italy: 'it,ita', spain: 'es,spa', mexico: 'es,spa', argentina: 'es,spa', colombia: 'es,spa', chile: 'es,spa',
    brazil: 'pt,por', portugal: 'pt,por', netherlands: 'nl,nld,dut', belgium: 'nl,nld,dut,fr,fra,fre', sweden: 'sv,swe', denmark: 'da,dan',
    norway: 'no,nor,nob,nno', finland: 'fi,fin', iceland: 'is,isl,ice', poland: 'pl,pol', russia: 'ru,rus', 'soviet union': 'ru,rus',
    ukraine: 'uk,ukr', turkey: 'tr,tur', 'türkiye': 'tr,tur', iran: 'fa,per,fas', egypt: 'ar,ara', lebanon: 'ar,ara', greece: 'el,ell,gre',
    hungary: 'hu,hun', 'czech republic': 'cs,ces,cze', czechia: 'cs,ces,cze', romania: 'ro,ron,rum',
    'united states': 'en,eng', usa: 'en,eng', 'united kingdom': 'en,eng', uk: 'en,eng', canada: 'en,eng', australia: 'en,eng', ireland: 'en,eng', 'new zealand': 'en,eng' };

  // ---------- original audio ----------
  function titleOf(x) {
    if (x.kind === 'episode') return App.data.byKey['s:' + x.seriesId] || { kind: 'series', title: x.seriesTitle || '', key: 's:' + x.seriesId };
    return x;
  }
  function langsFor(country) { var c = (country || '').split(',')[0].trim().toLowerCase(); return COUNTRY[c] || null; }
  // asks Cinemeta (once) for where it comes from; resolves with the languages ("ko,kor") or null
  function originalLangs(x) { return Meta.country(titleOf(x)).then(langsFor, function () { return null; }); }
  // what's known already, without asking
  function originalLangsNow(x) { var m = Meta.cached(titleOf(x)); return m ? langsFor(m.country) : null; }

  // ---------- versions ----------
  var vIndex = null, vFor = null;
  function vKey(x) { return (x.kind === 'series' ? 's|' : 'm|') + Meta.squash(Meta.cleanTitle(x.title).title); }
  function versionsOf(x) {
    if (!x || !(x.kind === 'movie' || x.kind === 'series')) return [];
    var D = App.data;
    if (vFor !== D.movies.length + '|' + D.series.length) {
      vIndex = {}; vFor = D.movies.length + '|' + D.series.length;
      D.movies.concat(D.series).forEach(function (y) { var k = vKey(y); if (k.length < 4) return; (vIndex[k] = vIndex[k] || []).push(y); });
    }
    return (vIndex[vKey(x)] || []).filter(function (y) { return y.key !== x.key; });
  }
  function openVersions(x) {
    var l = versionsOf(x); if (!l.length) return;
    App.menu('Other versions  ·  ' + Meta.cleanTitle(x.title).title, l.slice(0, 40).map(function (y) {
      var bits = [y.lang ? Tidy.nameOf(y.lang) : '', y.cat || '', y.year || ''].filter(Boolean).join('  ·  ');
      return { name: y.title + (bits ? '   —   ' + bits : ''), act: function () { App.openDetails(y); } };
    }), { sub: 'The same ' + (x.kind === 'series' ? 'series' : 'film') + ' elsewhere at ' + App.providerName() + ': another language, another quality. OK opens it.' });
  }

  // ---------- collections ----------
  var SEQUEL = /^(.*?)(?:\s*[:\-–]?\s*(?:part|chapter|deel|hoofdstuk|vol\.?|volume)\s+(?:\d+|[ivx]+|one|two|three|four|five)\b.*|\s+(?:[2-9]|1[0-9]|ii|iii|iv|v|vi|vii|viii|ix|x)(?:\s*[:\-–].*)?)$/i;
  function base(title) { var t = Meta.cleanTitle(title).title, m = SEQUEL.exec(t); var sq = !!(m && m[1].trim().length >= 3); return { name: sq ? m[1].trim() : t, sequel: sq }; }
  function collections(films, max) {
    var groups = {}, sequel = {}, names = {}, order = [];
    films.forEach(function (f) {
      if (!f || f.kind !== 'movie' || !f.title) return;
      var b = base(f.title), k = Meta.squash(b.name); if (k.length < 3) return;
      if (!groups[k]) { groups[k] = []; names[k] = b.name; order.push(k); }
      groups[k].push(f); if (b.sequel) sequel[k] = 1;
    });
    var out = [];
    order.forEach(function (k) {
      if (!sequel[k]) return;
      var seen = {}, one = groups[k].filter(function (f) { var s = Meta.squash(Meta.cleanTitle(f.title).title); if (seen[s]) return false; seen[s] = 1; return true; });
      if (one.length < 2) return;
      one.sort(function (a, b) { return (Number(a.year) || 9999) - (Number(b.year) || 9999) || (a.title < b.title ? -1 : 1); });
      var img = one.filter(function (f) { return f.image; })[0];
      out.push({ kind: 'collection', key: 'col:' + k, title: names[k] + ' collection', image: img ? img.image : '', films: one,
                 plot: one.length + ' films: ' + one.map(function (f) { return Meta.cleanTitle(f.title).title; }).join(', ') });
    });
    out.sort(function (a, b) { return b.films.length - a.films.length || (a.title < b.title ? -1 : 1); });
    return out.slice(0, max || 30);
  }

  // ---------- new ----------
  function isNew(x) { return x && x.added > 0 && (Date.now() / 1000 - x.added) < 14 * 86400 && (x.kind === 'movie' || x.kind === 'series'); }
  function newThisWeek(l) { var since = Date.now() / 1000 - 7 * 86400; return l.filter(function (x) { return x.added > since; }).sort(function (a, b) { return b.added - a.added; }).slice(0, 40); }

  return { originalLangs: originalLangs, originalLangsNow: originalLangsNow, versionsOf: versionsOf, openVersions: openVersions, collections: collections, isNew: isNew, newThisWeek: newThisWeek };
})();
