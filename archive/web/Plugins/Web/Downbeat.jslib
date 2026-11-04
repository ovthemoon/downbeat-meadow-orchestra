// Bridge between Unity and the page script js/downbeat-audio.js (window.Downbeat).
// ES5 syntax on purpose (Emscripten's jslib parser). All audio calls use primitive arguments, so they do not allocate in C#.
mergeInto(LibraryManager.library, {

  Downbeat_Audio_State: function () {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.state() : 0;
  },

  Downbeat_Audio_Now: function () {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.now() : 0;
  },

  Downbeat_Audio_HeardNow: function () {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.heardNow() : 0;
  },

  Downbeat_Audio_Configure: function (masterGain, maxVoices) {
    var a = window.Downbeat && window.Downbeat.audio;
    if (a) a.configure(masterGain, maxVoices);
  },

  Downbeat_Audio_LoadSample: function (index, urlPtr) {
    var a = window.Downbeat && window.Downbeat.audio;
    if (a) a.loadSample(index, UTF8ToString(urlPtr));
  },

  Downbeat_Audio_SamplesLoaded: function () {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.samplesLoaded() : 0;
  },

  Downbeat_Audio_SamplesFailed: function () {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.samplesFailed() : 0;
  },

  // Returns the audio time the note will really start (>= when; later only if the request arrived too late).
  Downbeat_Audio_Schedule: function (sampleIndex, when, rate, gain) {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.schedule(sampleIndex, when, rate, gain) : when;
  },

  Downbeat_Audio_LogFlash: function (when, heardNow) {
    var a = window.Downbeat && window.Downbeat.audio;
    if (a) a.logFlash(when, heardNow);
  },

  Downbeat_Audio_Stat: function (which) {
    var a = window.Downbeat && window.Downbeat.audio;
    return a ? a.stat(which) : 0;
  },

  Downbeat_Page_Ready: function () {
    var p = window.Downbeat && window.Downbeat.page;
    if (p) p.ready();
  },

  Downbeat_Page_Status: function (messagePtr) {
    var p = window.Downbeat && window.Downbeat.page;
    if (p) p.status(UTF8ToString(messagePtr));
  },

  // The label of the active input mode ("Camera mode: ...", "Mouse mode: camera permission was denied") for the badge on the page.
  Downbeat_Page_InputMode: function (labelPtr) {
    var p = window.Downbeat && window.Downbeat.page;
    if (p) p.setInputMode(UTF8ToString(labelPtr));
  },

  // performance.now(): the one clock that camera capture times, Unity decisions and audio times are all converted to.
  Downbeat_Page_NowMs: function () {
    return performance.now();
  },

  // Frame times of Unity's own loop for measurements: recorded only while a script has set window.Downbeat.page.frameTimes = [].
  Downbeat_Page_FrameTick: function () {
    var p = window.Downbeat && window.Downbeat.page;
    if (p && p.frameTimes) p.frameTimes.push(performance.now());
  },

  // Pull model (plan 5.2): copy the latest hand frame into the double[] C# passed in.
  // Returns the frame sequence number (0 = no frame yet), or -1 when the page has no hand tracker, -2 on a misaligned pointer.
  Downbeat_Hands_Read: function (dstPtr, length) {
    var h = window.Downbeat && window.Downbeat.hands;
    var src = h && h.buffer;
    if (!src) return -1;
    if ((dstPtr & 7) !== 0) return -2; // HEAPF64 needs 8-byte alignment
    var n = Math.min(length, src.length);
    HEAPF64.set(src.subarray(0, n), dstPtr >> 3);
    return src[0] | 0;
  },

  // Measurement log of every note the scheduler saw. Primitive arguments only: no string or array is built in C#.
  Downbeat_Page_LogNote: function (part, step, velocity, slot, requestedAt, when, audioNow, captureMs, inferEndMs, receivedMs, decidedMs, triggerCaptureMs) {
    var p = window.Downbeat && window.Downbeat.page;
    if (p) p.logNote({ part: part, step: step, velocity: velocity, slot: slot, requestedAt: requestedAt, when: when, audioNow: audioNow,
      captureMs: captureMs, inferEndMs: inferEndMs, receivedMs: receivedMs, decidedMs: decidedMs, triggerCaptureMs: triggerCaptureMs });
  },

  Downbeat_Page_Query: function (keyPtr) {
    // Returns a query-string parameter of the page, or "" (used for ?view=). Called once at start-up.
    var value = new URLSearchParams(window.location.search).get(UTF8ToString(keyPtr)) || "";
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  Downbeat_Page_BaseUrl: function () {
    var base = new URL(".", window.location.href).href;
    var size = lengthBytesUTF8(base) + 1;
    var buffer = _malloc(size);
    stringToUTF8(base, buffer, size);
    return buffer;
  }
});
