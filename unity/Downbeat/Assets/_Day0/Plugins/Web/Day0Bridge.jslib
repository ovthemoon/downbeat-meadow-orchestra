// Day 0 JS <-> Unity bridge. ES5 syntax on purpose (jslib is parsed by Emscripten's older parser).
mergeInto(LibraryManager.library, {

  Day0_NowMs: function () {
    return performance.now();
  },

  // currentTime of the AudioContext that Unity created (captured by the page's tap wrapper), or -1.
  Day0_CtxTime: function () {
    var tap = window.__tap;
    var ctx = tap && tap.unityContext && tap.unityContext();
    return ctx ? ctx.currentTime : -1;
  },

  Day0_Emit: function (kindPtr, jsonPtr) {
    var d = window.day0;
    if (d && d.onUnityEvent) d.onUnityEvent(UTF8ToString(kindPtr), UTF8ToString(jsonPtr));
  },

  Day0_Mark: function (labelPtr) {
    var tap = window.__tap;
    if (tap && tap.mark) tap.mark(UTF8ToString(labelPtr));
  },

  // Pull model (plan 5.2): copy the latest hand frame into the double[] that C# passed in.
  // Returns the frame sequence number, or -1 when no tracker buffer exists.
  Day0_ReadHands: function (dstPtr, length) {
    var d = window.day0;
    var src = d && d.hands;
    if (!src) return -1;
    if ((dstPtr & 7) !== 0) return -2; // HEAPF64 needs 8-byte alignment
    var n = Math.min(length, src.length);
    HEAPF64.set(src.subarray(0, n), dstPtr >> 3);
    return src[0] | 0;
  },

  // Write test: JS writes a known pattern into the caller's buffer so C# can verify the write reached managed memory.
  Day0_FillTest: function (dstPtr, length) {
    if ((dstPtr & 7) !== 0) return -2;
    var base = dstPtr >> 3;
    for (var i = 0; i < length; i++) HEAPF64[base + i] = i * 1.5 + 0.25;
    return length;
  },

  // The rejected alternative (plan 5.2): hand over the same frame as a JSON string.
  Day0_ReadHandsJson: function () {
    var d = window.day0;
    var src = d && d.hands;
    var text = src ? JSON.stringify(Array.prototype.slice.call(src)) : "[]";
    var size = lengthBytesUTF8(text) + 1;
    var buf = _malloc(size);
    stringToUTF8(text, buf, size);
    return buf;
  }
});
