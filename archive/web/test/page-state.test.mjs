import { test } from 'node:test';
import assert from 'node:assert/strict';
import { NOTE_LOG_LIMIT, createPage } from '../WebGLTemplates/Downbeat/js/page-state.js';
import { createFakeContext } from './fake-audio-context.mjs';

test('ready, status and the input label are passed on to listeners', () => {
  const page = createPage();
  const seen = [];
  page.onStatus = (t) => seen.push(['status', t]);
  page.onInputMode = (t) => seen.push(['mode', t]);
  page.readyCallbacks.push(() => seen.push(['ready']));
  page.status('loading');
  page.setInputMode('Mouse mode');
  page.ready();
  assert.deepEqual(seen, [['status', 'loading'], ['mode', 'Mouse mode'], ['ready']]);
  assert.equal(page.isReady, true);
  assert.equal(page.inputLabel, 'Mouse mode');
});

test('a logged note gets the page time at which it will be heard', () => {
  const ctx = createFakeContext({ timestamp: { contextTime: 9.8, performanceTime: 5000 } });
  const page = createPage({ now: () => 5100, getContext: () => ctx });
  page.logNote({ part: 1, step: 3, velocity: 0.5, slot: 12, requestedAt: 10, when: 10.1, audioNow: 9.9, captureMs: NaN });
  const [note] = page.noteLog;
  assert.ok(Math.abs(note.whenPerfMs - 5300) < 1e-9, '10.1 is 0.2 s after what is heard now (9.9)');
  assert.equal(note.slot, 12);
  assert.equal(note.loggedMs, 5100);
});

test('a dropped note (when = NaN) or a missing audio context is logged without a page time', () => {
  const page = createPage({ now: () => 1, getContext: () => null });
  page.logNote({ slot: 3, when: 5 });
  assert.equal(page.noteLog[0].whenPerfMs, null);
  const withContext = createPage({ now: () => 1, getContext: () => createFakeContext({}) });
  withContext.logNote({ slot: -1, when: NaN });
  assert.equal(withContext.noteLog[0].whenPerfMs, null);
});

test('the note log is bounded', () => {
  const page = createPage();
  for (let i = 0; i < NOTE_LOG_LIMIT + 5; i++) page.logNote({ slot: i, when: NaN });
  assert.equal(page.noteLog.length, NOTE_LOG_LIMIT);
  assert.equal(page.noteLog[0].slot, 5);
});

test('frame times are off until a measurement script switches them on', () => {
  const page = createPage();
  assert.equal(page.frameTimes, null);
});
