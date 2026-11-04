// Minimal WAV reader/writer for the Day 0 sample and recording tools.
// Supports PCM 8/16/24/32-bit and IEEE float 32/64 on read; PCM16 mono on write.

const FORMAT_PCM = 1;
const FORMAT_FLOAT = 3;
const FORMAT_EXTENSIBLE = 0xfffe;

/**
 * @param {Buffer} buffer
 * @returns {{sampleRate:number, channels:number, bitsPerSample:number, format:number, frames:number, data:Float32Array[]}}
 */
export function parseWav(buffer) {
  if (buffer.length < 44) throw new Error('WAV too short');
  if (buffer.toString('ascii', 0, 4) !== 'RIFF' || buffer.toString('ascii', 8, 12) !== 'WAVE') {
    throw new Error('not a RIFF/WAVE file');
  }
  let fmt = null;
  let dataOffset = -1;
  let dataSize = 0;
  let pos = 12;
  while (pos + 8 <= buffer.length) {
    const id = buffer.toString('ascii', pos, pos + 4);
    const size = buffer.readUInt32LE(pos + 4);
    const body = pos + 8;
    if (id === 'fmt ') {
      fmt = {
        format: buffer.readUInt16LE(body),
        channels: buffer.readUInt16LE(body + 2),
        sampleRate: buffer.readUInt32LE(body + 4),
        bitsPerSample: buffer.readUInt16LE(body + 14),
      };
      if (fmt.format === FORMAT_EXTENSIBLE && size >= 26) {
        fmt = { ...fmt, format: buffer.readUInt16LE(body + 24) };
      }
    } else if (id === 'data') {
      dataOffset = body;
      dataSize = Math.min(size, buffer.length - body);
      break;
    }
    pos = body + size + (size % 2);
  }
  if (!fmt) throw new Error('WAV has no fmt chunk');
  if (dataOffset < 0) throw new Error('WAV has no data chunk');

  const bytesPerSample = fmt.bitsPerSample / 8;
  const frames = Math.floor(dataSize / (bytesPerSample * fmt.channels));
  const data = Array.from({ length: fmt.channels }, () => new Float32Array(frames));
  for (let i = 0; i < frames; i++) {
    for (let c = 0; c < fmt.channels; c++) {
      const at = dataOffset + (i * fmt.channels + c) * bytesPerSample;
      data[c][i] = readSample(buffer, at, fmt.format, fmt.bitsPerSample);
    }
  }
  return { ...fmt, frames, data };
}

function readSample(buffer, at, format, bits) {
  if (format === FORMAT_FLOAT) {
    return bits === 64 ? buffer.readDoubleLE(at) : buffer.readFloatLE(at);
  }
  if (format !== FORMAT_PCM) throw new Error(`unsupported WAV format ${format}`);
  switch (bits) {
    case 8: return (buffer.readUInt8(at) - 128) / 128;
    case 16: return buffer.readInt16LE(at) / 32768;
    case 24: return buffer.readIntLE(at, 3) / 8388608;
    case 32: return buffer.readInt32LE(at) / 2147483648;
    default: throw new Error(`unsupported bit depth ${bits}`);
  }
}

/** Average all channels into one. Returns a new array. */
export function toMono(channels) {
  if (channels.length === 1) return Float32Array.from(channels[0]);
  const frames = channels[0].length;
  const out = new Float32Array(frames);
  for (let i = 0; i < frames; i++) {
    let sum = 0;
    for (const ch of channels) sum += ch[i];
    out[i] = sum / channels.length;
  }
  return out;
}

/** Encode mono float samples (clamped to [-1,1]) as a 16-bit PCM WAV. */
export function encodeWavPcm16Mono(samples, sampleRate) {
  const dataBytes = samples.length * 2;
  const out = Buffer.alloc(44 + dataBytes);
  out.write('RIFF', 0, 'ascii');
  out.writeUInt32LE(36 + dataBytes, 4);
  out.write('WAVE', 8, 'ascii');
  out.write('fmt ', 12, 'ascii');
  out.writeUInt32LE(16, 16);
  out.writeUInt16LE(FORMAT_PCM, 20);
  out.writeUInt16LE(1, 22);
  out.writeUInt32LE(sampleRate, 24);
  out.writeUInt32LE(sampleRate * 2, 28);
  out.writeUInt16LE(2, 32);
  out.writeUInt16LE(16, 34);
  out.write('data', 36, 'ascii');
  out.writeUInt32LE(dataBytes, 40);
  for (let i = 0; i < samples.length; i++) {
    const clamped = Math.max(-1, Math.min(1, samples[i]));
    out.writeInt16LE(Math.round(clamped * (clamped < 0 ? 32768 : 32767)), 44 + i * 2);
  }
  return out;
}
