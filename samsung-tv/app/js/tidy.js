// A provider's lists, tidied, as the launcher does (TvTidy.cs):
//   language tags  "NL ◉ VIDEOLAND", "|PL| TVP 1", "EN - BBC One", "UK: Sky News": the tag is read (NL, EN, PL...),
//                  kept with the item (it.lang) and taken off its name; SHOUTING names are written normally
//   same name      categories with the same name in two languages get "· NL" / "· EN" after it
//   languages      each profile shows only the languages it picked (Settings > Languages), and hides what has
//                  adult words when "Hide adult content" is on
var Tidy = (function () {
  var CODES = {}; 'NL NLD BE EN ENG UK US USA GB CA AU IE PL POL DE GER AT CH FR FRA ES SPA IT ITA PT BR TR TUR AR ARA RU RUS UA RO BG HR RS BA AL GR SE NO DK FI HU CZ SK IN PK AF IR KU LAT EX MX KR JP CN TH ASIA TA TE HI ML KN BN'.split(' ').forEach(function (c) { CODES[c] = 1; });
  var PREFIX = /^\W*?\|?\s*([A-Z0-9]{2,4})\s*\|?\s*(?:[|:\-–★☆◉●•►▶»#*~❖✦✪]+\s*|\s+(?=[←-⯿■-➿]))/;
  var SUFFIX = /(?:[\(\[|]\s*([A-Z]{2,3})\s*[\)\]|]|[-–|:]\s*([A-Z]{2,3}))\s*$/;
  var WORDS = [
    ['NL', /\b(dutch|nederlands|nederland|holland|vlaams|flemish)\b/i], ['EN', /\b(english|british|american)\b/i],
    ['PL', /\b(polish|polska|polskie|polski)\b/i], ['FR', /\b(french|france|fran[cç]ais|francais|vf|vostfr)\b/i],
    ['DE', /\b(german|germany|deutsch|deutschland)\b/i], ['TR', /\b(turk\w*|t[uü]rk\w*)\b/i], ['AR', /\b(arab\w*|arabic|osn|mbc)\b/i],
    ['ES', /\b(spain|spanish|espa[nñ]\w*|latino|latin|castellano)\b/i], ['IT', /\b(italia\w*|italian)\b/i],
    ['PT', /\b(portug\w*|brasil|brazil\w*)\b/i], ['RU', /\b(russia\w*|russian)\b/i], ['IN', /\b(india\w*|hindi|tamil|telugu|malayalam|punjabi|bollywood|bangla\w*)\b/i],
    ['PK', /\b(pakistan\w*|urdu)\b/i], ['AF', /\b(africa\w*|nigeria\w*)\b/i], ['KU', /\b(kurd\w*)\b/i], ['AL', /\b(albania\w*|shqip\w*)\b/i],
    ['EX', /\b(ex[- ]?yu|balkan\w*|serbia\w*|croatia\w*|bosnia\w*|slovenia\w*)\b/i], ['RO', /\b(romania\w*)\b/i], ['BG', /\b(bulgaria\w*)\b/i],
    ['GR', /\b(greek|greece)\b/i], ['SE', /\b(swed\w*|nordic|scandi\w*|norw\w*|danish|denmark|finnish|finland)\b/i], ['HU', /\b(hungar\w*)\b/i],
    ['CZ', /\b(czech\w*|slovak\w*)\b/i], ['IR', /\b(persian|iran\w*|farsi)\b/i], ['AS', /\b(asia\w*|chinese|china|japan\w*|korea\w*|thai|filipino|pinoy|vietnam\w*|anime|[kcj]-?drama\w*|dorama|donghua|manhwa)\b/i]];
  var NAMES = { NL: 'Dutch', EN: 'English', PL: 'Polish', BE: 'Belgian', DE: 'German', FR: 'French', ES: 'Spanish', IT: 'Italian', PT: 'Portuguese', TR: 'Turkish', AR: 'Arabic', RU: 'Russian',
    IN: 'Indian', PK: 'Pakistani', AF: 'African', KU: 'Kurdish', AL: 'Albanian', EX: 'Balkan (Ex-Yu)', RO: 'Romanian', BG: 'Bulgarian', GR: 'Greek', SE: 'Scandinavian', HU: 'Hungarian',
    CZ: 'Czech / Slovak', IR: 'Persian', AS: 'Asian (K-drama, anime, Chinese drama)', HE: 'Hebrew', UA: 'Ukrainian', HR: 'Croatian', RS: 'Serbian', AT: 'Austrian', CH: 'Swiss',
    NO: 'Norwegian', DK: 'Danish', FI: 'Finnish', SK: 'Slovak', BA: 'Bosnian' };
  var ADULT = /\b(adult|adults|xxx|18\s*\+|\+\s*18|porn\w*|erotic\w*|sex\w*|hentai|playboy|hustler|brazzers|for adults)\b/i;

  function norm(c) {
    switch (c) {
      case 'UK': case 'US': case 'USA': case 'GB': case 'CA': case 'AU': case 'IE': case 'ENG': return 'EN';
      case 'NLD': return 'NL'; case 'POL': return 'PL'; case 'GER': return 'DE'; case 'FRA': return 'FR'; case 'SPA': return 'ES';
      case 'ITA': return 'IT'; case 'TUR': return 'TR'; case 'ARA': return 'AR'; case 'RUS': return 'RU'; case 'LAT': case 'MX': return 'ES'; case 'BR': return 'PT';
      case 'KR': case 'JP': case 'CN': case 'TH': case 'ASIA': return 'AS';
      case 'TA': case 'TE': case 'HI': case 'ML': case 'KN': case 'BN': return 'IN';
    }
    return c;
  }
  // a writing system that isn't Latin tells the language too
  function scriptOf(s) {
    if (!s) return null;
    if (/[؀-ۿ]/.test(s)) return 'AR'; if (/[Ѐ-ӿ]/.test(s)) return 'RU'; if (/[Ͱ-Ͽ]/.test(s)) return 'GR';
    if (/[֐-׿]/.test(s)) return 'HE'; if (/[ऀ-෿]/.test(s)) return 'IN'; if (/[฀-๿぀-ヿ一-鿿가-힯]/.test(s)) return 'AS';
    return null;
  }
  function langOf(s) { var m = PREFIX.exec(s || ''); return m && CODES[m[1]] ? norm(m[1]) : null; }
  function langOfTitle(s) { return langOf(s) || scriptOf(s); }
  function langOfGroup(s) {
    var l = langOf(s); if (l) return l;
    var m = SUFFIX.exec(s || ''), c = m ? (m[1] || m[2]) : null;
    if (c && CODES[c]) return norm(c);
    for (var i = 0; i < WORDS.length; i++) if (WORDS[i][1].test(s || '')) return WORDS[i][0];
    return scriptOf(s);
  }
  function titleCase(w) { return w.charAt(0).toUpperCase() + w.slice(1).toLowerCase(); }
  // the name without its tag and symbols, written normally
  function clean(s) {
    if (!s) return s;
    var t = s, m = PREFIX.exec(t);
    if (m) t = t.slice(m[0].length);
    t = t.replace(/^[\s|:\-–•*#~←-⯿■-➿]+/, '').trim();
    if (!t) return s.trim();
    // SHOUTING written normally; short words stay as they are (TVP, NPO, HD, BBC)
    if (t.length > 3 && /[A-Za-zÀ-ÿ]/.test(t) && t === t.toUpperCase()) t = t.replace(/[A-Za-zÀ-ÿ]{4,}/g, titleCase);
    return t;
  }

  // the whole provider, tidied once its lists are in: items have .lang, clean titles and clean categories
  function account(all) {
    var raw = {};
    all.forEach(function (x) { if (x.cat) raw[x.cat] = 1; });
    all.forEach(function (x) {
      x.lang = langOfTitle(x.title) || langOfGroup(x.cat);
      x.title = clean(x.title);
    });
    var names = Object.keys(raw), cleanOf = {}, byClean = {};
    names.forEach(function (r) { cleanOf[r] = clean(r); var k = cleanOf[r].toLowerCase(); (byClean[k] = byClean[k] || []).push(r); });
    var final = {};
    names.forEach(function (r) {
      var same = byClean[cleanOf[r].toLowerCase()], langs = {};
      same.forEach(function (x) { langs[langOfGroup(x) || ''] = 1; });
      var l = langOfGroup(r);
      final[r] = cleanOf[r] + (Object.keys(langs).length > 1 && l ? '  ·  ' + l : '');
    });
    all.forEach(function (x) { if (x.cat && final[x.cat]) x.cat = final[x.cat]; });
    return final;
  }
  // the languages in the lists, most first: [[code, count]]
  function langs(all) {
    var n = {}; all.forEach(function (x) { if (x.lang) n[x.lang] = (n[x.lang] || 0) + 1; });
    return Object.keys(n).sort(function (a, b) { return n[b] - n[a]; }).map(function (k) { return [k, n[k]]; });
  }
  // whether this profile shows it (its languages, and adult content hidden or not)
  function ok(x) {
    if (!x) return true;
    var p = Store.profile;
    if (p.noAdult && (ADULT.test(x.title || '') || ADULT.test(x.cat || '') || ADULT.test(x.genre || ''))) return false;
    if (!p.langs || !p.langs.length || x.kind === 'episode') return true;
    if (!x.lang) return !p.langStrict;
    return p.langs.indexOf(x.lang) >= 0;
  }
  function filter(list) { var p = Store.profile; if ((!p.langs || !p.langs.length) && !p.noAdult) return list; return list.filter(ok); }

  return { clean: clean, langOfTitle: langOfTitle, langOfGroup: langOfGroup, account: account, langs: langs, ok: ok, filter: filter,
           nameOf: function (c) { return NAMES[c] || c; } };
})();
