// AudioWorklet processor for the S-1 output tap. It only copies what reaches the context's destination
// into memory chunks (mono) and sends them to the page; it never changes the audio that is heard.
const BLOCK_FRAMES = 128;
const CHUNK_BLOCKS = 32; // post every 4096 frames

class TapRecorder extends AudioWorkletProcessor {
  constructor() {
    super();
    this.recording = false;
    this.chunk = new Float32Array(BLOCK_FRAMES * CHUNK_BLOCKS);
    this.filled = 0;
    this.chunkFrame0 = 0;
    this.port.onmessage = (event) => {
      if (event.data === 'start') {
        this.recording = true;
        this.filled = 0;
      } else if (event.data === 'stop') {
        this.flush();
        this.recording = false;
      }
    };
  }

  flush() {
    if (this.filled === 0) return;
    const data = this.chunk.slice(0, this.filled);
    this.port.postMessage({ frame0: this.chunkFrame0, data }, [data.buffer]);
    this.filled = 0;
  }

  process(inputs) {
    if (!this.recording) return true;
    const input = inputs[0] && inputs[0][0];
    if (this.filled === 0) this.chunkFrame0 = currentFrame;
    if (input) this.chunk.set(input, this.filled);
    else this.chunk.fill(0, this.filled, this.filled + BLOCK_FRAMES);
    this.filled += BLOCK_FRAMES;
    if (this.filled >= this.chunk.length) this.flush();
    return true;
  }
}

registerProcessor('tap-recorder', TapRecorder);
