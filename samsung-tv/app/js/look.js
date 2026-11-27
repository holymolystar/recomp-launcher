// How it looks and moves (effects.css does the moving): the film's colour glowing behind a page, pictures that fade
// in when they've loaded, and Calm mode. The glow is the picture drawn into a canvas of 32 x 18 pixels and stretched
// over the screen: the browser smooths it out, so it looks like a heavy blur but costs nothing while it shows (a
// real blur would be worked out again on every frame, which a TV can't keep up with).
var Look = (function () {
  function calm() { return !!Store.pref('calm', false); }
  function applyCalm() { document.body.classList.toggle('calm', calm()); }

  // a glow layer from a picture, put first in 'into'; replaces the one that's there (fades in when ready)
  function glow(into, src) {
    if (!into) return null;
    var old = into.querySelector(':scope > .glow');
    if (old && old._src === src) return old;
    if (old) old.parentNode.removeChild(old);
    if (!src) return null;
    var g = document.createElement('div'); g.className = 'glow'; g._src = src;
    into.insertBefore(g, into.firstChild);
    var img = new Image();
    img.onload = function () {
      try {
        var c = document.createElement('canvas'); c.width = 32; c.height = 18;
        var x = c.getContext('2d'), r = Math.max(32 / img.width, 18 / img.height), w = img.width * r, h = img.height * r;
        x.drawImage(img, (32 - w) / 2, (18 - h) / 2, w, h);
        g.appendChild(c);
        requestAnimationFrame(function () { g.classList.add('on'); });
      } catch (e) { }
    };
    img.src = src;
    return g;
  }
  // a picture set as an element's background, shown once it has loaded (so it fades in instead of popping)
  function bgWhenLoaded(e, src, known) {
    e.style.backgroundImage = 'url("' + src + '")';
    if (known || calm()) { e.classList.add('in'); return; }
    var img = new Image();
    img.onload = img.onerror = function () { e.classList.add('in'); };
    img.src = src;
  }
  // a page comes in (the animation runs again each time)
  function enter(e, cls) {
    if (!e || calm()) return;
    cls = cls || 'enter';
    e.classList.remove(cls); void e.offsetWidth; e.classList.add(cls);
    clearTimeout(e._enterT); e._enterT = setTimeout(function () { e.classList.remove(cls); }, 700);
  }
  return { calm: calm, applyCalm: applyCalm, glow: glow, bgWhenLoaded: bgWhenLoaded, enter: enter };
})();
