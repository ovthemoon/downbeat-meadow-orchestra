import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HandIdAssigner, PALM_LANDMARKS, palmCenter } from '../WebGLTemplates/Downbeat/js/hand-ids.js';

const at = (x, y) => ({ x, y });

test('palm center is the mean of the wrist and the four finger base joints', () => {
  const landmarks = Array.from({ length: 21 }, () => ({ x: 9, y: 9 }));
  PALM_LANDMARKS.forEach((index, i) => { landmarks[index] = { x: i, y: 10 + i }; });
  assert.deepEqual(palmCenter(landmarks), { x: 2, y: 12 });
  assert.deepEqual([...PALM_LANDMARKS], [0, 5, 9, 13, 17]);
});

test('a single hand gets id 0 and keeps it while it moves', () => {
  const assigner = new HandIdAssigner();
  assert.deepEqual(assigner.assign([at(0.5, 0.5)], 0), [0]);
  assert.deepEqual(assigner.assign([at(0.52, 0.55)], 33), [0]);
  assert.deepEqual(assigner.assign([at(0.6, 0.7)], 66), [0]);
});

test('two hands keep their ids when the detector returns them in the other order', () => {
  const assigner = new HandIdAssigner();
  assert.deepEqual(assigner.assign([at(0.2, 0.5), at(0.8, 0.5)], 0), [0, 1]);
  assert.deepEqual(assigner.assign([at(0.8, 0.5), at(0.2, 0.5)], 33), [1, 0]);
  assert.deepEqual(assigner.assign([at(0.21, 0.52), at(0.79, 0.5)], 66), [0, 1]);
});

test('ids follow the hands when they move towards each other without jumping', () => {
  const assigner = new HandIdAssigner();
  let ids = assigner.assign([at(0.3, 0.5), at(0.7, 0.5)], 0);
  assert.deepEqual(ids, [0, 1]);
  for (let step = 1; step <= 10; step++) {
    ids = assigner.assign([at(0.3 + step * 0.015, 0.5), at(0.7 - step * 0.015, 0.5)], step * 33);
    assert.deepEqual(ids, [0, 1]);
  }
});

test('the best overall match wins, not the greedy one', () => {
  const assigner = new HandIdAssigner({ maxJump: 0.35 });
  assigner.assign([at(0.30, 0.5), at(0.60, 0.5)], 0); // ids 0 and 1
  // detection A is slightly nearer to hand 0 than B is, but B can only match hand 1
  const ids = assigner.assign([at(0.44, 0.5), at(0.30, 0.5)], 33);
  assert.deepEqual(ids, [1, 0]);
});

test('a hand that disappears for a moment gets its id back; a newcomer meanwhile gets the other id', () => {
  const assigner = new HandIdAssigner({ holdMs: 400 });
  assert.deepEqual(assigner.assign([at(0.2, 0.5), at(0.8, 0.5)], 0), [0, 1]);
  assert.deepEqual(assigner.assign([at(0.8, 0.5)], 33), [1]); // hand 0 is lost for a frame
  assert.deepEqual(assigner.assign([at(0.2, 0.52), at(0.8, 0.5)], 66), [0, 1]);
});

test('a new hand does not take the id of a hand that was only unseen for a moment', () => {
  const assigner = new HandIdAssigner({ holdMs: 400 });
  assert.deepEqual(assigner.assign([at(0.2, 0.5)], 0), [0]);
  assert.deepEqual(assigner.assign([at(0.9, 0.2)], 100), [1]);
});

test('after the hold time a lost id is free again', () => {
  const assigner = new HandIdAssigner({ holdMs: 400 });
  assigner.assign([at(0.2, 0.5)], 0);
  assert.deepEqual(assigner.assign([], 100), []);
  assert.deepEqual(assigner.assign([at(0.8, 0.5)], 1000), [0]);
});

test('a hand that jumps farther than maxJump is a different hand', () => {
  const assigner = new HandIdAssigner({ maxJump: 0.3 });
  assert.deepEqual(assigner.assign([at(0.1, 0.5)], 0), [0]);
  assert.deepEqual(assigner.assign([at(0.9, 0.5)], 33), [1]);
});

test('at most maxHands detections are used and ids are never duplicated', () => {
  const assigner = new HandIdAssigner();
  const ids = assigner.assign([at(0.1, 0.1), at(0.5, 0.5), at(0.9, 0.9)], 0);
  assert.equal(ids.length, 2);
  assert.notEqual(ids[0], ids[1]);
});

test('reset forgets everything', () => {
  const assigner = new HandIdAssigner();
  assigner.assign([at(0.2, 0.5), at(0.8, 0.5)], 0);
  assigner.reset();
  assert.deepEqual(assigner.assign([at(0.8, 0.5)], 10), [0]);
});
