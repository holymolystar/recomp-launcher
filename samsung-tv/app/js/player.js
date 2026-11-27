// The picture. On a Samsung TV: the TV's own player (AVPlay), which plays every kind of stream a provider sends
// (HLS, MPEG-TS, MP4, MKV) on a layer under the page. Anywhere else (a PC browser, to try the app out): a <video>,
// with hls.js for HLS. Only one stream is ever open (providers allow one or two at a time), and changing channel
// quickly only opens the last one asked for (each play() makes the ones before it stale).
// Films and episodes: start at a time (resume), pause, seek, the time and length, audio and subtitle tracks (the
// profile's languages are picked by vod.js), and the subtitles' text (AVPlay hands it over; the page shows it).
var Player = (function () {
  var tv = !!(window.webapis && webapis.avplay);
  var box = document.getElementById('video-box');
  var rect = { x: 0, y: 0, w: 1920, h: 1080 };
  var gen = 0, url = null, listeners = [], onSub = function () { }, video = null, hls = null, hlsLoading = null;
  var paused = false, startAt = 0, tvTime = 0;

  if (tv) {
    var obj = document.createElement('object');
    obj.type = 'application/avplayer';
    obj.style.cssText = 'position:absolute;left:0;top:0;width:1920px;height:1080px';
    box.appendChild(obj);
  }

  function state(g, s, why) { if (g === gen) listeners.forEach(function (f) { try { f(s, why); } catch (e) { } }); }

  function isFull() { return rect.x === 0 && rect.y === 0 && rect.w >= 1920 && rect.h >= 1080; }
  // the picture's place, and its size in it (Picture size only full screen: a small picture, as TV Live's preview,
  // simply fills its box; the TV's letter box there shows nothing at all on some sets)
  function applyRect() {
    if (tv) { applyFit(); try { webapis.avplay.setDisplayRect(rect.x, rect.y, rect.w, rect.h); } catch (e) { } }
    else if (video) { video.style.left = rect.x + 'px'; video.style.top = rect.y + 'px'; video.style.width = rect.w + 'px'; video.style.height = rect.h + 'px'; }
  }

  // ---------- the TV's player ----------
  function closeTv() {
    try { var st = webapis.avplay.getState(); if (st !== 'NONE' && st !== 'IDLE') webapis.avplay.stop(); } catch (e) { }
    try { webapis.avplay.close(); } catch (e) { }
  }
  function playTv(u, g) {
    closeTv(); tvTime = 0;
    try {
      webapis.avplay.open(u);
      webapis.avplay.setListener({
        onbufferingstart: function () { state(g, 'buffering'); },
        onbufferingcomplete: function () { state(g, 'playing'); },
        onstreamcompleted: function () { state(g, 'ended'); },
        onerror: function (e) { state(g, 'error', String(e)); },
        oncurrentplaytime: function (ms) { if (g === gen) tvTime = ms; },
        onsubtitlechange: function (dur, text) { if (g === gen) onSub(text, dur); },
        onevent: function () { }
      });
      applyRect();
      // a slow stream gives up after 20 s instead of hanging
      try { webapis.avplay.setTimeoutForBuffering(20); } catch (e) { }
      webapis.avplay.prepareAsync(function () {
        if (g !== gen) return;
        var go = function () { if (g !== gen) return; try { webapis.avplay.play(); paused = false; state(g, 'playing'); } catch (e) { state(g, 'error', String(e)); } };
        if (startAt > 0) { try { webapis.avplay.seekTo(startAt * 1000, go, go); } catch (e) { go(); } } else go();
      }, function (e) { state(g, 'error', String(e)); });
    } catch (e) { state(g, 'error', String(e)); }
  }

  // ---------- a browser's <video> ----------
  function loadHls() {
    if (window.Hls) return Promise.resolve();
    if (!hlsLoading) hlsLoading = new Promise(function (ok, fail) {
      var s = document.createElement('script');
      s.src = 'https://cdnjs.cloudflare.com/ajax/libs/hls.js/1.5.13/hls.min.js';
      s.onload = ok; s.onerror = fail; document.head.appendChild(s);
    });
    return hlsLoading;
  }
  function closeWeb() {
    if (hls) { try { hls.destroy(); } catch (e) { } hls = null; }
    if (video) { try { video.pause(); video.removeAttribute('src'); video.load(); } catch (e) { } video.style.display = 'none'; }
  }
  function playWeb(u, g) {
    closeWeb();
    if (!video) {
      video = document.createElement('video'); video.autoplay = true; video.playsInline = true; box.appendChild(video);
      video.addEventListener('playing', function () { paused = false; state(gen, 'playing'); });
      video.addEventListener('waiting', function () { state(gen, 'buffering'); });
      video.addEventListener('ended', function () { state(gen, 'ended'); });
      video.addEventListener('error', function () { if (url) state(gen, 'error', 'video error'); });
    }
    video.style.display = 'block'; applyRect(); applyFit();
    var start = startAt;
    var seekOnce = function () { if (start > 0) { try { video.currentTime = start; } catch (e) { } start = 0; } };
    video.addEventListener('loadedmetadata', seekOnce, { once: true });
    var isHls = /\.m3u8(\?|$)/i.test(u);
    // hls.js wherever it can run (a browser's own HLS, where there is one, is less dependable)
    if (isHls && (window.MediaSource || !video.canPlayType('application/vnd.apple.mpegurl'))) {
      loadHls().then(function () {
        if (g !== gen) return;
        if (!window.Hls || !Hls.isSupported()) { state(g, 'error', 'no HLS here'); return; }
        hls = new Hls({ lowLatencyMode: false, startPosition: start > 0 ? start : -1 });
        hls.on(Hls.Events.ERROR, function (ev, d) { if (d && d.fatal) state(g, 'error', d.details); });
        hls.loadSource(u); hls.attachMedia(video);
        video.play().catch(function () { });
      }, function () { state(g, 'error', "couldn't load the HLS player"); });
    } else {
      video.src = u; video.play().catch(function () { });
    }
  }

  // visibility: when the TV goes to another app the stream is closed, and opened again on coming back (where it was)
  document.addEventListener('visibilitychange', function () {
    if (!url) return;
    if (document.hidden) { var t = time(); gen++; if (tv) closeTv(); else closeWeb(); startAt = t > 5 ? t : 0; }
    else { var u = url; url = null; play(u, startAt); }
  });

  function play(u, start) {
    if (!u) return;
    url = u; startAt = start || 0; paused = false; var g = ++gen;
    state(g, 'loading');
    if (tv) playTv(u, g); else playWeb(u, g);
  }
  function stop() { url = null; gen++; if (tv) closeTv(); else closeWeb(); }
  function setRect(x, y, w, h) { rect = { x: Math.round(x), y: Math.round(y), w: Math.round(w), h: Math.round(h) }; applyRect(); }
  function full() { setRect(0, 0, 1920, 1080); }

  // ---------- films and episodes ----------
  function time() { if (tv) { try { return (webapis.avplay.getCurrentTime() || tvTime) / 1000; } catch (e) { return tvTime / 1000; } } return video ? video.currentTime || 0 : 0; }
  function duration() { if (tv) { try { return (webapis.avplay.getDuration() || 0) / 1000; } catch (e) { return 0; } } return video && isFinite(video.duration) ? video.duration : 0; }
  function pause() { paused = true; if (tv) { try { webapis.avplay.pause(); } catch (e) { } } else if (video) video.pause(); }
  function resume() { paused = false; if (tv) { try { webapis.avplay.play(); } catch (e) { } } else if (video) video.play().catch(function () { }); }
  function seek(sec) {
    var d = duration(); sec = Math.max(0, d ? Math.min(d - 3, sec) : sec);
    if (tv) { try { webapis.avplay.seekTo(Math.round(sec * 1000), function () { }, function () { }); } catch (e) { } }
    else if (video) try { video.currentTime = sec; } catch (e) { }
  }
  // the picture's size: Fit (all of it, black bars where it's narrower), Fill (the screen, edges cut off: for films
  // with black bars of their own, or 4:3 channels) or Stretch (the screen, pulled out of shape). For every profile.
  var FITS = [['fit', 'Fit'], ['fill', 'Fill (zoom)'], ['stretch', 'Stretch']];
  function fit() { return Store.pref('fit', 'fit'); }
  function applyFit() {
    var f = fit();
    if (tv) { try { webapis.avplay.setDisplayMethod(!isFull() || f === 'stretch' ? 'PLAYER_DISPLAY_MODE_FULL_SCREEN' : f === 'fill' ? 'PLAYER_DISPLAY_MODE_CROPPED_FULL' : 'PLAYER_DISPLAY_MODE_LETTER_BOX'); } catch (e) { } }
    else if (video) video.style.objectFit = f === 'fill' ? 'cover' : f === 'stretch' ? 'fill' : 'contain';
  }
  function nextFit() { var i = FITS.map(function (x) { return x[0]; }).indexOf(fit()); Store.setPref('fit', FITS[(i + 1) % FITS.length][0]); applyFit(); return fitName(); }
  function fitName() { var f = fit(); for (var i = 0; i < FITS.length; i++) if (FITS[i][0] === f) return FITS[i][1]; return 'Fit'; }

  // the audio and subtitle tracks: [{type: 'audio'|'text', index, lang, name, on}]
  function tracks() {
    var out = [];
    if (tv) {
      try {
        var cur = {}; try { webapis.avplay.getCurrentStreamInfo().forEach(function (s) { cur[s.type + s.index] = 1; }); } catch (e) { }
        webapis.avplay.getTotalTrackInfo().forEach(function (t) {
          if (t.type !== 'AUDIO' && t.type !== 'TEXT') return;
          var x = {}; try { x = JSON.parse(t.extra_info || '{}'); } catch (e) { }
          var lang = (x.language || x.track_lang || '').toLowerCase();
          out.push({ type: t.type === 'AUDIO' ? 'audio' : 'text', index: t.index, lang: lang, name: langName(lang) + (x.channels ? '  ' + x.channels + 'ch' : ''), on: !!cur[t.type + t.index] });
        });
      } catch (e) { }
    } else if (hls) {
      (hls.audioTracks || []).forEach(function (t, i) { out.push({ type: 'audio', index: i, lang: (t.lang || '').toLowerCase(), name: t.name || langName(t.lang), on: i === hls.audioTrack }); });
      (hls.subtitleTracks || []).forEach(function (t, i) { out.push({ type: 'text', index: i, lang: (t.lang || '').toLowerCase(), name: t.name || langName(t.lang), on: i === hls.subtitleTrack }); });
    }
    return out;
  }
  var subsOff = false;
  function selectTrack(type, index) {
    if (type === 'text' && index < 0) { subsOff = true; onSub('', 0); if (tv) { try { webapis.avplay.setSilentSubtitle(true); } catch (e) { } } else if (hls) hls.subtitleTrack = -1; return; }
    if (tv) {
      try { webapis.avplay.setSelectTrack(type === 'audio' ? 'AUDIO' : 'TEXT', index); } catch (e) { }
      if (type === 'text') { subsOff = false; try { webapis.avplay.setSilentSubtitle(false); } catch (e) { } }
    } else if (hls) { if (type === 'audio') hls.audioTrack = index; else { subsOff = false; hls.subtitleTrack = index; } }
  }
  var LANGS = { en: 'English', eng: 'English', nl: 'Nederlands', nld: 'Nederlands', dut: 'Nederlands', de: 'Deutsch', deu: 'Deutsch', ger: 'Deutsch', fr: 'Français', fra: 'Français', fre: 'Français',
    es: 'Español', spa: 'Español', it: 'Italiano', ita: 'Italiano', pt: 'Português', por: 'Português', tr: 'Türkçe', tur: 'Türkçe', ar: 'العربية', ara: 'العربية', pl: 'Polski', pol: 'Polski',
    ru: 'Русский', rus: 'Русский', ja: '日本語', jpn: '日本語', ko: '한국어', kor: '한국어', zh: '中文', chi: '中文', zho: '中文', sv: 'Svenska', swe: 'Svenska', da: 'Dansk', dan: 'Dansk',
    no: 'Norsk', nor: 'Norsk', fi: 'Suomi', fin: 'Suomi', hi: 'हिन्दी', hin: 'हिन्दी', el: 'Ελληνικά', gre: 'Ελληνικά', ell: 'Ελληνικά', ro: 'Română', ron: 'Română', rum: 'Română' };
  function langName(l) { l = (l || '').toLowerCase(); return LANGS[l] || (l ? l.toUpperCase() : 'Unknown'); }

  return { play: play, stop: stop, setRect: setRect, full: full, get playing() { return url; }, onState: function (f) { listeners.push(f); }, onSubtitle: function (f) { onSub = function (t, d) { if (!subsOff || !t) f(t, d); }; },
           isTv: tv, time: time, duration: duration, pause: pause, resume: resume, get paused() { return paused; }, seek: seek, tracks: tracks, selectTrack: selectTrack, langName: langName, nextFit: nextFit, fitName: fitName };
})();
