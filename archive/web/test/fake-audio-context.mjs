// A minimal stand-in for AudioContext, enough for the page audio modules. Records what was scheduled.
export function createFakeContext({ currentTime = 10, outputLatency = 0.2, timestamp = { contextTime: 9.8, performanceTime: 5000 } } = {}) {
  const ctx = {
    currentTime,
    state: 'suspended',
    sampleRate: 44100,
    baseLatency: 0.005,
    outputLatency,
    destination: { name: 'destination' },
    sources: [],
    gains: [],
    resumeCalls: 0,
    async resume() {
      ctx.resumeCalls += 1;
      ctx.state = 'running';
    },
    getOutputTimestamp: () => timestamp,
    createGain() {
      const node = {
        gain: { value: 1, targets: [], setTargetAtTime(v, t, c) { node.gain.targets.push([v, t, c]); } },
        connections: [],
        connect(target) { node.connections.push(target); return target; },
        disconnect() { node.connections.length = 0; },
      };
      ctx.gains.push(node);
      return node;
    },
    createBufferSource() {
      const source = {
        buffer: null,
        playbackRate: { value: 1 },
        startedAt: null,
        stoppedAt: null,
        onended: null,
        disconnected: false,
        connect(target) { source.target = target; return target; },
        disconnect() { source.disconnected = true; },
        start(t) { source.startedAt = t; },
        stop(t) { source.stoppedAt = t; },
      };
      ctx.sources.push(source);
      return source;
    },
    async decodeAudioData(arrayBuffer) {
      return { duration: arrayBuffer.byteLength / 1000, length: arrayBuffer.byteLength };
    },
  };
  return ctx;
}

export function fakeContextClass(ctx) {
  return class FakeAudioContext {
    constructor(options) {
      ctx.options = options;
      return ctx;
    }
  };
}

export const buffer = (duration = 1.3) => ({ duration });
