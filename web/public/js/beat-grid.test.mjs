import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGrid, firstSlotAtOrAfter, lastSlotAtOrBefore, slotDuration, slotTime } from './beat-grid.js';

test('96 BPM sixteenth slots last 156.25 ms', () => {
  assert.equal(slotDuration(96, 4), 0.15625);
});

test('slot time is index based (no accumulated error over an hour)', () => {
  const grid = createGrid(10, 96, 4);
  assert.ok(Math.abs(slotTime(grid, 23040) - (10 + 23040 * 0.15625)) < 1e-9);
});

test('on-grid times map to their own slot, off-grid times round up / down', () => {
  const grid = createGrid(1, 96, 4);
  assert.equal(firstSlotAtOrAfter(grid, slotTime(grid, 5)), 5);
  assert.equal(firstSlotAtOrAfter(grid, 1.4), 3);
  assert.equal(lastSlotAtOrBefore(grid, 1.4), 2);
});

test('rejects invalid bpm and slot counts', () => {
  assert.throws(() => slotDuration(0, 4), RangeError);
  assert.throws(() => slotDuration(96, 0), RangeError);
  assert.throws(() => slotDuration(96, 1.5), RangeError);
});

test('grid objects are frozen', () => {
  assert.throws(() => { 'use strict'; createGrid(0, 96, 4).origin = 5; }, TypeError);
});
