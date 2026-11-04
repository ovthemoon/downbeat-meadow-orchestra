import { test } from 'node:test';
import assert from 'node:assert/strict';
import { encodeWavPcm16Mono, parseWav, toMono } from './wav.mjs';

test('round trips a mono PCM16 signal within one LSB', () => {
  // Arrange
  const input = Float32Array.from([0, 0.5, -0.5, 1, -1, 0.25]);
  // Act
  const parsed = parseWav(encodeWavPcm16Mono(input, 44100));
  // Assert
  assert.equal(parsed.sampleRate, 44100);
  assert.equal(parsed.channels, 1);
  assert.equal(parsed.frames, input.length);
  input.forEach((v, i) => assert.ok(Math.abs(parsed.data[0][i] - v) < 1 / 16384));
});

test('toMono averages channels without mutating the input', () => {
  const left = Float32Array.from([1, 0]);
  const right = Float32Array.from([0, 1]);
  const mono = toMono([left, right]);
  assert.deepEqual(Array.from(mono), [0.5, 0.5]);
  assert.deepEqual(Array.from(left), [1, 0]);
});

test('rejects non-WAV input with a clear error', () => {
  assert.throws(() => parseWav(Buffer.alloc(64)), /RIFF/);
  assert.throws(() => parseWav(Buffer.alloc(10)), /too short/);
});

test('parses 24-bit PCM stereo', () => {
  const header = encodeWavPcm16Mono(new Float32Array(1), 48000);
  // Hand-build a 24-bit stereo file with 2 frames
  const body = Buffer.alloc(2 * 2 * 3);
  body.writeIntLE(4194304, 0, 3); // +0.5 L
  body.writeIntLE(-4194304, 3, 3); // -0.5 R
  body.writeIntLE(0, 6, 3);
  body.writeIntLE(8388607, 9, 3);
  const out = Buffer.alloc(44 + body.length);
  header.copy(out, 0, 0, 44);
  out.writeUInt32LE(36 + body.length, 4);
  out.writeUInt16LE(2, 22);
  out.writeUInt32LE(48000 * 6, 28);
  out.writeUInt16LE(6, 32);
  out.writeUInt16LE(24, 34);
  out.writeUInt32LE(body.length, 40);
  body.copy(out, 44);
  const parsed = parseWav(out);
  assert.equal(parsed.channels, 2);
  assert.equal(parsed.frames, 2);
  assert.ok(Math.abs(parsed.data[0][0] - 0.5) < 1e-6);
  assert.ok(Math.abs(parsed.data[1][0] + 0.5) < 1e-6);
});
