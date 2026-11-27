// The TV remote's buttons, as names. On a Samsung TV the colour, channel, media and number buttons only reach the app
// once it asks for them (registerKey); on a PC keyboard (for trying the app out) some keys stand in for them.
var Keys = (function () {
  var codes = {
    37: 'left', 38: 'up', 39: 'right', 40: 'down', 13: 'ok',
    10009: 'back', 8: 'back', 27: 'back',
    427: 'chup', 428: 'chdown', 33: 'chup', 34: 'chdown',
    415: 'play', 19: 'pause', 10252: 'playpause', 413: 'stop', 412: 'rew', 417: 'ff',
    403: 'red', 404: 'green', 405: 'yellow', 406: 'blue',
    457: 'info', 10190: 'prech',
    65376: 'imedone', 65385: 'imecancel'
  };
  // a PC keyboard: F1-F4 the colour buttons, I info, P the previous channel
  var pc = { 112: 'red', 113: 'green', 114: 'yellow', 115: 'blue', 73: 'info', 80: 'prech' };
  for (var d = 0; d <= 9; d++) { codes[48 + d] = 'd' + d; codes[96 + d] = 'd' + d; }

  function register() {
    try {
      if (!window.tizen || !tizen.tvinputdevice) return;
      ['ChannelUp', 'ChannelDown', 'MediaPlay', 'MediaPause', 'MediaPlayPause', 'MediaStop', 'MediaRewind', 'MediaFastForward',
       'ColorF0Red', 'ColorF1Green', 'ColorF2Yellow', 'ColorF3Blue', 'Info', 'PreviousChannel',
       '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'].forEach(function (k) {
        try { tizen.tvinputdevice.registerKey(k); } catch (e) { }
      });
    } catch (e) { }
  }
  function name(e, typing) {
    var n = codes[e.keyCode];
    if (!n && !typing && !window.tizen) n = pc[e.keyCode];
    return n || null;
  }
  return { register: register, name: name };
})();
