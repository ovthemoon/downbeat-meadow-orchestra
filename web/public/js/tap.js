// S-1 audio output tap. MUST be loaded as a classic script before the Unity loader.
// It wraps window.AudioContext so that every context created afterwards
//  - exposes a GainNode as `destination`, which feeds the real destination (optionally muted for silent test runs)
//    and an AudioWorklet recorder tap, so whatever Unity or the page plays is captured in the page itself.
// Audio heard by the user is unchanged (the tap sits in parallel, same render quantum).
(function () {
  'use strict';
  var NativeContext = window.AudioContext || window.webkitAudioContext;
  if (!NativeContext) return;

  var params = new URLSearchParams(location.search);
  var muteOutput = params.get('mute') !== '0';
  var entries = [];
  var marks = [];
  var recordingAll = false;
  var nextTag = null;

  function install(ctx, tag) {
    var realDestination = ctx.destination;
    var tapGain = new GainNode(ctx, { gain: 1 });
    tapGain.channelCount = 2;
    tapGain.channelCountMode = 'explicit';
    var outGain = new GainNode(ctx, { gain: muteOutput ? 0 : 1 });
    tapGain.connect(outGain);
    outGain.connect(realDestination);
    Object.defineProperty(tapGain, 'maxChannelCount', { value: realDestination.maxChannelCount });
    Object.defineProperty(ctx, 'destination', { configurable: true, get: function () { return tapGain; } });

    var entry = {
      id: entries.length, tag: tag || 'unity', ctx: ctx, chunks: [], node: null, createdPerfMs: performance.now(),
      workletError: null,
    };
    entry.ready = ctx.audioWorklet.addModule('/js/tap-worklet.js').then(function () {
      var node = new AudioWorkletNode(ctx, 'tap-recorder', {
        numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [1], channelCount: 1, channelCountMode: 'explicit',
      });
      node.port.onmessage = function (event) { entry.chunks.push(event.data); };
      var keepAlive = new GainNode(ctx, { gain: 0 });
      tapGain.connect(node);
      node.connect(keepAlive);
      keepAlive.connect(realDestination);
      entry.node = node;
      if (recordingAll) node.port.postMessage('start');
    }).catch(function (error) { entry.workletError = String(error); });
    entries.push(entry);
  }

  function TappedAudioContext(options) {
    var ctx = new NativeContext(options);
    var tag = nextTag;
    nextTag = null;
    install(ctx, tag);
    return ctx;
  }
  TappedAudioContext.prototype = NativeContext.prototype;
  window.AudioContext = TappedAudioContext;
  window.webkitAudioContext = TappedAudioContext;

  function assemble(entry) {
    var chunks = entry.chunks;
    if (chunks.length === 0) return null;
    var first = chunks[0].frame0;
    var last = chunks[chunks.length - 1];
    var total = last.frame0 + last.data.length - first;
    var samples = new Float32Array(total);
    var gaps = 0;
    var expected = first;
    chunks.forEach(function (chunk) {
      if (chunk.frame0 !== expected) gaps += 1;
      samples.set(chunk.data, chunk.frame0 - first);
      expected = chunk.frame0 + chunk.data.length;
    });
    return { samples: samples, firstFrame: first, gaps: gaps };
  }

  window.__tap = {
    muted: muteOutput,
    tagNextContext: function (tag) { nextTag = tag; },
    contexts: function () { return entries.slice(); },
    unityContext: function () {
      var found = entries.filter(function (e) { return e.tag === 'unity'; })[0];
      return found ? found.ctx : null;
    },
    resumeAll: function () {
      return Promise.all(entries.map(function (e) { return e.ctx.state === 'running' ? null : e.ctx.resume(); }));
    },
    whenReady: function () { return Promise.all(entries.map(function (e) { return e.ready; })); },
    mark: function (label) {
      var now = performance.now();
      var times = {};
      entries.forEach(function (e) { times[e.id] = e.ctx.currentTime; });
      marks.push({ label: label, perfMs: now, ctxTimes: times });
    },
    marks: function () { return marks.slice(); },
    start: function () {
      entries.forEach(function (e) { e.chunks = []; if (e.node) e.node.port.postMessage('start'); });
      recordingAll = true;
    },
    // Stops recording and returns one recording per context that captured something.
    stop: function () {
      recordingAll = false;
      entries.forEach(function (e) { if (e.node) e.node.port.postMessage('stop'); });
      return new Promise(function (resolve) {
        setTimeout(function () {
          resolve(entries.map(function (e) {
            var rec = assemble(e);
            var base = {
              id: e.id, tag: e.tag, sampleRate: e.ctx.sampleRate, state: e.ctx.state,
              baseLatency: e.ctx.baseLatency, outputLatency: e.ctx.outputLatency, workletError: e.workletError,
            };
            return rec ? Object.assign(base, rec) : Object.assign(base, { samples: null, firstFrame: null, gaps: 0 });
          }));
        }, 300);
      });
    },
  };
})();
