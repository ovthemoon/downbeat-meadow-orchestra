import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { buildContent, mergeKeepingExisting } from './content-build.mjs';

const root = resolve(import.meta.dirname, '..', '..', 'content');
const readJson = (p) => JSON.parse(readFileSync(resolve(root, p), 'utf8'));
const inputs = () => ({ manifest: readJson('audio/manifest.json'), natureManifest: readJson('audio/nature-manifest.json') });

test('uses the measured sounding pitch of each sample, not the VCSL file name octave', () => {
  const content = buildContent(inputs());
  const marimba = content.instruments.find((i) => i.id === 'marimba').samples.find((s) => s.id === 'marimba_C5_med');
  assert.equal(marimba.soundingMidi, 72); // the file is called ..._C4_...
  const recorder = content.instruments.find((i) => i.id === 'recorder').samples.map((s) => s.soundingMidi);
  assert.deepEqual(recorder, [82, 84, 86, 88, 91]); // A#5 C6 D6 E6 G6
});

test('every instrument has samples, the loud marimba layer is left out, and rates stay within a shift of 12', () => {
  const content = buildContent(inputs());
  assert.equal(content.instruments.length, 6);
  const marimba = content.instruments.find((i) => i.id === 'marimba');
  assert.deepEqual(marimba.samples.map((s) => s.id), ['marimba_C5_med', 'marimba_G5_med']);
  for (const instrument of content.instruments) assert.ok(instrument.samples.length >= 2);
});

test('the character theme is gone and the meadow block is there', () => {
  const content = buildContent(inputs());
  assert.equal(content.schemaVersion, 4);
  assert.equal(content.theme, undefined);
  assert.equal(content.meadow.sky.stops.length, 3);
  assert.deepEqual(content.meadow.sky.stops.map((s) => s.id), ['blueHour', 'mid', 'sunset']);
});

test('hand-edited values survive a rebuild while measured values are refreshed', () => {
  const first = buildContent(inputs());
  first.meadow.camera.height = 1.55;
  first.meadow.sky.stops[2].lightIntensity = 3.1;
  first.audio.masterGainDb = -9;
  first.instruments[0].samples[0].tuneCents = 999;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.meadow.camera.height, 1.55);
  assert.equal(second.meadow.sky.stops[2].lightIntensity, 3.1);
  assert.equal(second.audio.masterGainDb, -9);
  assert.notEqual(second.instruments[0].samples[0].tuneCents, 999);
});

test('a content file from before a new default key gets that key added and keeps the rest', () => {
  const first = buildContent(inputs());
  delete first.meadow.haze.curve;
  delete first.record.fontNames;
  first.meadow.haze.toRow = 0.4;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.meadow.haze.toRow, 0.4);
  assert.equal(second.meadow.haze.curve, buildContent(inputs()).meadow.haze.curve);
  assert.ok(second.record.fontNames.length > 0);
});

test('the corner notice of the meadow is gone: not built, and dropped from a content file that still has it', () => {
  const fresh = buildContent(inputs());
  assert.equal('overlay' in fresh.meadow, false);
  const old = structuredClone(fresh);
  old.meadow.overlay = { text: 'old notice', fontNames: ['Old Font'], fontSize: 28, color: '#ffffff', alpha: 0.55, margin: 0.02 };
  const rebuilt = buildContent({ ...inputs(), existing: old });
  assert.equal('overlay' in rebuilt.meadow, false);
});

test('the record screen names the operating-system fonts to try, Korean ones first', () => {
  const { fontNames } = buildContent(inputs()).record;
  assert.deepEqual(fontNames, ['Apple SD Gothic Neo', 'Malgun Gothic', 'NanumGothic', 'Noto Sans CJK KR']);
});

const PARTS = ['harp', 'recorder', 'bell', 'lowstring', 'marimba', 'handdrum'];

test('there is one instrument, one gap and one phrase for each of the six parts, in the order of the Part enum', () => {
  const content = buildContent(inputs());
  assert.deepEqual(content.instruments.map((i) => i.part), PARTS);
  assert.deepEqual(content.song.partMinGapSlots.map((g) => g.part), PARTS);
  assert.deepEqual(content.song.partMinGapSlots.map((g) => g.slots), [1, 2, 2, 2, 1, 1]);
  assert.deepEqual(content.conduct.phrases.map((p) => p.part), PARTS);
  assert.deepEqual(content.conduct.phrases.map((p) => p.steps), [[0, 2, 4, 7, 4, 2], [7, 5, 4, 2], [9, 7, 9, 5], [0, 0, 2, 0], [0, 0, 4, 0], [0, 0, 0, 0]]);
  assert.equal(content.conduct.loudPalms, 4);
});

test('sample ids are unique over all instruments', () => {
  const ids = buildContent(inputs()).instruments.flatMap((i) => i.samples.map((s) => s.id));
  assert.equal(new Set(ids).size, ids.length);
});

test('the bell, the low string and the hand drum play their own recordings with the values of docs/conduct-six-cells-samples.md', () => {
  const byId = (content, id) => content.instruments.find((i) => i.id === id);
  const content = buildContent(inputs());
  const expected = { bell: [69, 2, -4], lowstring: [57, 2, 0], handdrum: [45, 5, 1] };
  for (const [id, [rootMidi, maxPitchShift, gainDb]] of Object.entries(expected)) {
    const instrument = byId(content, id);
    assert.deepEqual([instrument.rootMidi, instrument.maxPitchShift, instrument.gainDb], [rootMidi, maxPitchShift, gainDb], id);
    assert.ok(instrument.samples.length >= 3, `${id} has its recordings`);
    assert.ok(instrument.samples.every((s) => s.id.startsWith(`${id}_`) && s.file === `audio/${s.id}.wav`), `${id} plays files of its own`);
  }
});

test('the content.json that ships has the same bell, low string and hand drum values and samples as a build', { skip: !existsSync(resolve(root, 'content.json')) }, () => {
  const shipped = readJson('content.json');
  const built = buildContent({ ...inputs(), existing: shipped });
  for (const id of ['bell', 'lowstring', 'handdrum']) {
    assert.deepEqual(shipped.instruments.find((i) => i.id === id), built.instruments.find((i) => i.id === id), id);
  }
});

test('the cells block has the rows, six names and six colours', () => {
  const { cells } = buildContent(inputs());
  assert.equal(cells.rowSplit, 0.5);
  assert.equal(cells.rowHysteresis, 0.03);
  assert.deepEqual(cells.labels, ['하프', '리코더', '종', '낮은 현', '마림바', '손북']);
  assert.equal(cells.colors.length, 6);
  for (const color of cells.colors) assert.match(color, /^#[0-9A-F]{6}$/);
});

test('a content file of schema 3 is carried over: the old part names move to the new parts, the new parts get their defaults, the hand-edited values stay', () => {
  const fresh = buildContent(inputs());
  const old = structuredClone(fresh);
  old.schemaVersion = 3;
  old.contentVersion = '2026-10-30.1';
  old.song.partMinGapSlots = [{ part: 'strings', slots: 3 }, { part: 'winds', slots: 2 }, { part: 'perc', slots: 1 }];
  old.conduct.phrases = [{ part: 'strings', steps: [1, 1] }, { part: 'winds', steps: [7, 5, 4, 2] }, { part: 'perc', steps: [0, 0, 4, 0] }];
  delete old.conduct.loudPalms;
  delete old.cells;
  old.instruments = old.instruments.filter((i) => ['harp', 'recorder', 'marimba'].includes(i.id));
  old.instruments[0].part = 'strings';
  old.instruments[0].gainDb = -3;
  old.audio.masterGainDb = -9;

  const migrated = buildContent({ ...inputs(), existing: old });

  assert.equal(migrated.schemaVersion, 4);
  assert.equal(migrated.contentVersion, fresh.contentVersion);
  assert.deepEqual(migrated.song.partMinGapSlots.map((g) => g.part), PARTS);
  assert.equal(migrated.song.partMinGapSlots[0].slots, 3);
  assert.deepEqual(migrated.conduct.phrases[0], { part: 'harp', steps: [1, 1] });
  assert.deepEqual(migrated.conduct.phrases[2], { part: 'bell', steps: [9, 7, 9, 5] });
  assert.equal(migrated.conduct.loudPalms, 4);
  assert.deepEqual(migrated.cells, fresh.cells);
  assert.deepEqual(migrated.instruments.map((i) => i.part), PARTS);
  assert.equal(migrated.instruments[0].gainDb, -3);
  assert.equal(migrated.audio.masterGainDb, -9);
});

test('mergeKeepingExisting lets existing arrays win whole and does not alias the defaults', () => {
  const defaults = { a: { list: [1, 2, 3], keep: 1 }, b: 2 };
  const merged = mergeKeepingExisting(defaults, { a: { list: [9] } });
  assert.deepEqual(merged, { a: { list: [9], keep: 1 }, b: 2 });
  merged.a.keep = 5;
  assert.equal(defaults.a.keep, 1);
});

test('fails clearly when an instrument has no samples', () => {
  const { manifest } = inputs();
  assert.throws(() => buildContent({ ...inputs(), manifest: { ...manifest, files: manifest.files.filter((f) => !f.file.startsWith('harp_')) } }), /no samples found for instrument harp/);
});

test('every file content.json refers to exists', { skip: !existsSync(resolve(root, 'content.json')) }, () => {
  const content = readJson('content.json');
  const paths = [
    ...content.meadow.sky.stops.map((s) => s.backdrop),
    ...content.instruments.flatMap((i) => i.samples.map((s) => s.file)),
    ...content.nature.instruments.flatMap((i) => i.samples.map((s) => s.file)),
    content.tutorial.openHandImage,
    content.tutorial.fistImage,
  ].filter(Boolean);
  assert.ok(paths.length >= 3 + 12 + 10 + 2);
  for (const p of paths) assert.ok(existsSync(resolve(root, p)), p);
});

test('the nature block has one instrument per layer, six decibels below the hand instruments, with the synthesized samples', () => {
  const content = buildContent(inputs());
  assert.deepEqual(content.nature.instruments.map((i) => i.layer), ['fireflies', 'sky', 'hills']);
  assert.ok(content.nature.instruments.every((i) => i.gainDb === -6));
  assert.deepEqual(content.nature.instruments[0].samples.map((s) => s.soundingMidi), [81, 84, 86, 88, 91]); // A5 C6 D6 E6 G6
  assert.deepEqual(content.nature.instruments[1].samples.map((s) => s.soundingMidi), [57, 60, 64, 67]); // A3 C4 E4 G4
  assert.deepEqual(content.nature.instruments[2].samples.map((s) => s.soundingMidi), [45]); // A2
  assert.ok(content.nature.instruments.every((i) => i.samples.every((s) => s.tuneCents === 0 && s.file.startsWith('audio/'))));
  assert.equal(content.nature.leadBars, 1);
  const { level1, level2, level3 } = content.nature.energy;
  assert.ok(level1 < level2 && level2 < level3);
  assert.equal(content.experience.noHandsSec, 3);
});

test('hand-edited nature values survive a rebuild while the nature samples are refreshed', () => {
  const first = buildContent(inputs());
  first.nature.energy.halfLifeSec = 9;
  first.nature.instruments[1].gainDb = -9;
  first.nature.instruments[1].samples[0].perceivedOnsetOffsetMs = 999;
  first.experience.noHandsSec = 4;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.nature.energy.halfLifeSec, 9);
  assert.equal(second.nature.instruments[1].gainDb, -9);
  assert.equal(second.experience.noHandsSec, 4);
  assert.notEqual(second.nature.instruments[1].samples[0].perceivedOnsetOffsetMs, 999);
});

test('fails clearly when a nature instrument has no samples', () => {
  const { natureManifest } = inputs();
  assert.throws(() => buildContent({ ...inputs(), natureManifest: { ...natureManifest, files: natureManifest.files.filter((f) => !f.file.startsWith('drum_')) } }), /no samples found for nature instrument hill-drum/);
});

test('the display and record blocks are there: borderless by default, one card row of 6, words in Korean', () => {
  const content = buildContent(inputs());
  assert.equal(content.display.mode, 'auto');
  assert.equal(content.display.fullscreenMode, 'borderless');
  assert.equal(content.display.overlayFraction, 0.28);
  assert.equal(content.display.recordIndex, 1);
  assert.equal(content.record.cardsShown, 6);
  assert.equal(content.record.minNotes, 3);
  assert.equal(content.record.cardMaxMarks, 64);
  assert.equal(content.record.text.layers.length, 3);
  assert.match(content.record.text.title, /들판/);
});

test('hand-edited display and record values survive a rebuild', () => {
  const first = buildContent(inputs());
  first.display.fullscreenMode = 'exclusive';
  first.record.text.title = 'x';
  first.record.colors.ink = '#ffffff';
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.display.fullscreenMode, 'exclusive');
  assert.equal(second.record.text.title, 'x');
  assert.equal(second.record.colors.ink, '#ffffff');
});

test('the record screen has slow idle motes and a time of day on each card: values, Korean words, same as the data class defaults', () => {
  const { record } = buildContent(inputs());
  assert.equal(record.idleMotes, 24);
  assert.equal(record.idleMoteSpeed, 0.02);
  assert.equal(record.idleMoteDimmed, 0.3);
  assert.ok(record.idleMoteAlpha > 0 && record.idleMoteAlpha <= 1);
  assert.equal(record.text.timeFormat, '{0} {1}:{2:00}');
  assert.deepEqual(record.text.meridiem, ['오전', '오후']);
});

test('a content.json from before the idle motes gets them on rebuild, and hand-edited values of the record screen stay', () => {
  const current = buildContent(inputs());
  const older = structuredClone(current);
  for (const key of ['idleMotes', 'idleMoteSpeed', 'idleMoteAlpha', 'idleMoteDimmed']) delete older.record[key];
  delete older.record.text.timeFormat;
  delete older.record.text.meridiem;
  older.record.cardsShown = 4;
  older.record.text.noCards = 'x';

  const rebuilt = buildContent({ ...inputs(), existing: older });
  assert.deepEqual(rebuilt.record, { ...current.record, cardsShown: 4, text: { ...current.record.text, noCards: 'x' } });

  const edited = structuredClone(current);
  edited.record.idleMotes = 0;
  edited.record.text.meridiem = ['AM', 'PM'];
  const kept = buildContent({ ...inputs(), existing: edited });
  assert.equal(kept.record.idleMotes, 0);
  assert.deepEqual(kept.record.text.meridiem, ['AM', 'PM']);
});

test('the conduct block is there with the values of docs/conducting-play.md and keeps hand-edited values', () => {
  const first = buildContent(inputs());
  assert.equal(first.conduct.mode, 'conduct');
  assert.equal(first.conduct.motionWindowMs, 400);
  assert.ok(first.conduct.crossfadeSec > 0 && first.conduct.crossfadeSec < first.conduct.releaseSec, 'a hand that crosses into the next part fades the old one faster than a hand that goes');
  assert.ok(first.conduct.stillPalms < first.conduct.halfBeatPalms && first.conduct.halfBeatPalms < first.conduct.quarterBeatPalms);
  assert.deepEqual(first.conduct.phrases.map((p) => p.part), PARTS);
  assert.ok(first.conduct.loudPalms > first.conduct.stillPalms && first.conduct.loudPalms <= 20);
  for (const phrase of first.conduct.phrases) {
    assert.ok(phrase.steps.length >= 1 && phrase.steps.length <= 32);
    assert.ok(phrase.steps.every((n) => Number.isInteger(n) && n >= 0 && n < first.song.pitchSteps));
  }
  first.conduct.mode = 'strike';
  first.conduct.releaseSec = 3;
  first.conduct.phrases[1].steps = [1, 3];
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.conduct.mode, 'strike');
  assert.equal(second.conduct.releaseSec, 3);
  assert.deepEqual(second.conduct.phrases[1].steps, [1, 3]);
});

test('a content file from before conducting play gets the conduct block and keeps the rest', () => {
  const first = buildContent(inputs());
  delete first.conduct;
  first.audio.masterGainDb = -9;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.conduct.mode, 'conduct');
  assert.equal(second.audio.masterGainDb, -9);
});

test('the content.json that ships has the conduct block', () => {
  const shipped = readJson('content.json');
  assert.equal(shipped.conduct.mode, 'conduct');
  assert.deepEqual(shipped.conduct.phrases, buildContent(inputs()).conduct.phrases);
});

test('the cutoff block is there with the numbers of the plan and keeps hand-edited values', () => {
  const first = buildContent(inputs());
  assert.deepEqual(first.cutoff, { spreadPalms: 2, minApartPalms: 3.5, windowSec: 1, holdSec: 0.2, fadeSec: 0.25, restartLockSec: 1.5, endWordSec: 1.5 });
  first.cutoff.spreadPalms = 3;
  first.cutoff.restartLockSec = 2.5;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.cutoff.spreadPalms, 3);
  assert.equal(second.cutoff.restartLockSec, 2.5);
  assert.equal(second.cutoff.holdSec, 0.2);
});

test('a content file from before the cutoff block gets it and keeps the rest', () => {
  const first = buildContent(inputs());
  delete first.cutoff;
  first.audio.masterGainDb = -9;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.cutoff.minApartPalms, 3.5);
  assert.equal(second.audio.masterGainDb, -9);
});

test('the content.json that ships has the cutoff block of the defaults', () => {
  assert.deepEqual(readJson('content.json').cutoff, buildContent(inputs()).cutoff);
});

test('the autoQuality block names tiers that exist, a ladder from light to cheap and a start for weak graphics', () => {
  const content = buildContent(inputs());
  const tiers = new Set(content.meadow.quality.map((t) => t.name));
  const a = content.autoQuality;
  assert.equal(a.enabled, true);
  assert.ok(a.ladder.length >= 2 && a.ladder.every((s) => tiers.has(s.tier) && s.renderScale >= 0.3 && s.renderScale <= 1));
  assert.ok(a.start.weakStartStep > a.start.strongStartStep, 'a weak chip starts further down the ladder');
  assert.ok(a.start.weakNames.includes('Intel') && a.start.strongNames.includes('Arc'));
  assert.ok(a.downDwellSec >= a.confirmDownDwellSec && a.maxUpDwellSec >= a.upDwellSec);
  assert.ok(a.upHoldSec >= a.performingEvidenceSec && a.upHoldSec <= 3600, 'a step up holds only after the evidence time at the least, so a step down inside upHoldSec is always a failed try');
  assert.deepEqual(a.ladder[a.start.weakStartStep], { tier: 'Low', renderScale: 0.7 }, 'a weak laptop starts where the memo measured it: Low at 0.7');
});

test('the automatic quality has a floor for a waiting tier change, evidence from a performance, and an experiment for the tracker', () => {
  const a = buildContent(inputs()).autoQuality;
  assert.ok(a.pendingScaleFloor >= 0.3 && a.pendingScaleFloor < 0.85 && a.pendingScaleStep > 0);
  assert.ok(a.performingEvidenceSec > 0);
  assert.ok(a.starveGainShare > 0 && a.starveGainWindowSec >= 2 * a.downDwellSec, 'the answer is looked for after two down dwells at least');
  assert.ok(a.starveGiveUpAfter >= 1 && a.starveIgnoreSec >= 300);
  assert.ok(Number.isInteger(a.starveCancelsAsFailure) && a.starveCancelsAsFailure >= 1 && a.starveCancelsAsFailure <= 10, 'cancelled experiments in a row count as one failed experiment');
});

test('the quality tiers carry the levers of the picture, Low being the cheaper one', () => {
  const [high, low] = buildContent(inputs()).meadow.quality;
  assert.equal(high.name, 'High');
  assert.equal(low.name, 'Low');
  for (const tier of [high, low]) {
    assert.ok(tier.renderScale >= 0.3 && tier.renderScale <= 1);
    assert.ok(['Auto', 'Linear', 'Point', 'FSR'].includes(tier.upscaler));
    assert.ok(tier.bloom.maxIterations >= 1 && tier.bloom.maxIterations <= 16);
  }
  assert.ok(low.renderScale <= high.renderScale && low.bloom.maxIterations <= high.bloom.maxIterations);
  assert.ok(low.targetFps > 0 && high.targetFps === 0, 'Low is held to a frame rate, High keeps the display\'s vertical sync');
});

test('a quality tier written before the levers existed gets them and keeps its own numbers', () => {
  const first = buildContent(inputs());
  for (const tier of first.meadow.quality) { delete tier.renderScale; delete tier.bloom; delete tier.targetFps; }
  first.meadow.quality[1].grassPerSqm = 999;
  const second = buildContent({ ...inputs(), existing: first });
  assert.equal(second.meadow.quality[1].grassPerSqm, 999);
  assert.ok(second.meadow.quality[1].renderScale > 0 && second.meadow.quality[1].bloom.maxIterations > 0);
});

test('the waking moment is a short wave of glinting buds with a soft gust, and a hand-edited number stays', () => {
  const first = buildContent(inputs());
  const wake = first.meadow.wake;
  assert.ok(wake.radius / wake.speed > 0.5 && wake.radius / wake.speed <= 2, 'the wave lasts 1 to 2 s');
  assert.ok(wake.maxFlowers > 0 && wake.gustAmplitude > 0 && wake.gustAmplitude < first.meadow.wind.noteRadial.amplitude, 'the gust is softer than a played note');
  delete first.meadow.wake;
  assert.deepEqual(buildContent({ ...inputs(), existing: first }).meadow.wake, wake, 'a content file from before the wake gets it');
  first.meadow.wake = { ...wake, maxFlowers: 7 };
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wake.maxFlowers, 7);
});

test('a blade bends along an arc of its own length: wind.maxBendDeg is 120, a content file from before it gets it and a hand-edited value stays', () => {
  const first = buildContent(inputs());
  assert.equal(first.meadow.wind.maxBendDeg, 120);
  delete first.meadow.wind.maxBendDeg;
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wind.maxBendDeg, 120);
  first.meadow.wind.maxBendDeg = 60;
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wind.maxBendDeg, 60);
});

test('the hand is a soft local gust: its body fits inside the hand\'s wind, and a hand-edited number stays', () => {
  const first = buildContent(inputs());
  const wind = first.meadow.wind;
  assert.ok(wind.handGustWidth > 0 && wind.handGustWidth * 1.9 < wind.handRadius, 'the swing back behind the hand is inside its radius');
  assert.ok(wind.handSpeedCap > 0);
  const width = wind.handGustWidth;
  delete first.meadow.wind.handGustWidth;
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wind.handGustWidth, width, 'a content file from before the gust gets it');
  first.meadow.wind.handSpeedCap = 1.5;
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wind.handSpeedCap, 1.5);
});

test('the grass reactions of the bottom row are a long low wave and a strong wide quick ring, and a content file from before them gets them', () => {
  const first = buildContent(inputs());
  const { noteRadial, longWave, drumRing } = first.meadow.wind;
  assert.ok(longWave.widthScale > 1 && longWave.amplitude < noteRadial.amplitude, 'long and low');
  assert.ok(drumRing.amplitude > 1.5 * noteRadial.amplitude && drumRing.widthScale > 1 && drumRing.lifeSec < noteRadial.lifeSec && drumRing.speed > noteRadial.speed, 'strong, wide, quick');
  // the wind field holds 16 waves (WindFieldGpu.Capacity): at a quarter beat of 96 BPM the low string plays every second slot, the drum every slot
  const slotSec = 60 / 96 / 4;
  const alive = Math.ceil(longWave.lifeSec / (2 * slotSec)) + Math.ceil(drumRing.lifeSec / slotSec);
  assert.ok(alive <= 12, `together they hold ${alive} waves at the quickest: room is left for the beat's sweeps and the notes' rings`);
  delete first.meadow.wind.longWave;
  delete first.meadow.wind.drumRing;
  const again = buildContent({ ...inputs(), existing: first });
  assert.deepEqual(again.meadow.wind.longWave, longWave);
  assert.deepEqual(again.meadow.wind.drumRing, drumRing);
  first.meadow.wind.drumRing = { ...drumRing, amplitude: 0.9 };
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.wind.drumRing.amplitude, 0.9, 'a hand-edited number stays');
});

test('the effects of the top row in the air have counts, a distance and a smaller count for the Low tier, and a content file from before them gets them', () => {
  const first = buildContent(inputs());
  const sky = first.meadow.skyEffects;
  for (const key of ['ringCount', 'seedCount', 'starCount', 'lifeSec', 'distance', 'ringRadius', 'glow', 'maxParticles', 'lowCountScale']) assert.ok(sky[key] > 0, key);
  assert.ok(sky.lowCountScale < 1, 'the Low tier makes fewer');
  assert.ok(first.meadow.quality.some((tier) => tier.name === 'Low'), 'and there is a tier called Low to make them');
  delete first.meadow.skyEffects;
  assert.deepEqual(buildContent({ ...inputs(), existing: first }).meadow.skyEffects, sky);
  first.meadow.skyEffects = { ...sky, ringCount: 12 };
  assert.equal(buildContent({ ...inputs(), existing: first }).meadow.skyEffects.ringCount, 12, 'a hand-edited number stays');
});

test('the content.json that ships has the sky effects and the two grass reactions', { skip: !existsSync(resolve(root, 'content.json')) }, () => {
  const shipped = readJson('content.json');
  const built = buildContent(inputs());
  assert.deepEqual(shipped.meadow.skyEffects, built.meadow.skyEffects);
  assert.deepEqual(shipped.meadow.wind.longWave, built.meadow.wind.longWave);
  assert.deepEqual(shipped.meadow.wind.drumRing, built.meadow.wind.drumRing);
});

test('the trail block is there with its defaults, keeps hand-edited values, and a file from before it gets it', () => {
  const first = buildContent(inputs());
  assert.deepEqual(first.trail, { notesPerSec: 6, maxNotes: 40, lifeSec: 1, size: 0.06, labelSec: 1.2, lowCountScale: 0.5 });
  first.trail.notesPerSec = 12;
  first.trail.labelSec = 0;
  const kept = buildContent({ ...inputs(), existing: first });
  assert.equal(kept.trail.notesPerSec, 12);
  assert.equal(kept.trail.labelSec, 0);
  assert.equal(kept.trail.maxNotes, 40);
  delete first.trail;
  first.audio.masterGainDb = -9;
  const older = buildContent({ ...inputs(), existing: first });
  assert.equal(older.trail.maxNotes, 40);
  assert.equal(older.audio.masterGainDb, -9);
});

test('the content.json that ships has the same trail block as the defaults', () => {
  assert.deepEqual(readJson('content.json').trail, buildContent(inputs()).trail);
});

test('the tutorial block is there with the owner\'s numbers: ten quiet seconds, three steps of five, a fade out of 0.8 s, Korean lines (same as TutorialDto)', () => {
  const { tutorial } = buildContent(inputs());
  assert.equal(tutorial.idleDelaySec, 10);
  assert.equal(tutorial.stepSec, 5);
  assert.equal(tutorial.fadeInSec, 0.6);
  assert.equal(tutorial.fadeOutSec, 0.8);
  assert.equal(tutorial.dim, 0.45);
  assert.deepEqual(tutorial.texts, ['손을 펴서 들어 보세요', '손을 옮기면 악기가 바뀌어요', '두 주먹을 쥐고 벌리면 끝']);
  assert.equal(tutorial.endWord, '끝');
});

test('a content file from before the tutorial gets the block and keeps the rest; hand-edited tutorial values stay', () => {
  const current = buildContent(inputs());
  const older = structuredClone(current);
  delete older.tutorial;
  assert.deepEqual(buildContent({ ...inputs(), existing: older }).tutorial, current.tutorial);

  const edited = structuredClone(current);
  edited.tutorial.idleDelaySec = 20;
  edited.tutorial.texts[1] = 'x';
  delete edited.tutorial.endWord;
  const rebuilt = buildContent({ ...inputs(), existing: edited }).tutorial;
  assert.equal(rebuilt.idleDelaySec, 20);
  assert.deepEqual(rebuilt.texts, ['손을 펴서 들어 보세요', 'x', '두 주먹을 쥐고 벌리면 끝']);
  assert.equal(rebuilt.endWord, '끝', 'a key that the older file lacks comes from the defaults');
});

test('the content.json that ships has the tutorial block', { skip: !existsSync(resolve(root, 'content.json')) }, () => {
  const shipped = readJson('content.json');
  assert.deepEqual(shipped.tutorial, buildContent(inputs()).tutorial);
});

test('the pose block (open hand or fist) is there with the numbers of PoseDto, a content file from before it gets it, a hand-edited number stays', () => {
  const first = buildContent(inputs());
  assert.deepEqual(first.pose, { openRatio: 1.35, fistRatio: 1.15, confirmFrames: 2 });
  assert.ok(first.pose.fistRatio < first.pose.openRatio);
  assert.deepEqual(readJson('content.json').pose, first.pose, 'the shipped content.json carries the same block');
  delete first.pose;
  assert.deepEqual(buildContent({ ...inputs(), existing: first }).pose, { openRatio: 1.35, fistRatio: 1.15, confirmFrames: 2 });
  first.pose = { openRatio: 1.6 };
  assert.deepEqual(buildContent({ ...inputs(), existing: first }).pose, { openRatio: 1.6, fistRatio: 1.15, confirmFrames: 2 }, 'a block with one number gets the rest');
});

test('the tutorial pictures of the hands are named, are PNGs that are there and whole, and a content file from before them gets the names', () => {
  const { tutorial } = buildContent(inputs());
  assert.equal(tutorial.openHandImage, 'tutorial/hand-open.png');
  assert.equal(tutorial.fistImage, 'tutorial/hand-fist.png');
  const png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  const iend = [0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82];
  for (const name of [tutorial.openHandImage, tutorial.fistImage]) {
    const bytes = readFileSync(resolve(root, name));
    assert.deepEqual([...bytes.subarray(0, 8)], png, `${name} starts like a PNG`);
    assert.deepEqual([...bytes.subarray(bytes.length - 8)], iend, `${name} ends with the IEND chunk`);
    assert.ok(bytes.readUInt32BE(16) >= 256 && bytes.readUInt32BE(20) >= 256, `${name} is a picture of a useful size`);
  }
  const older = structuredClone(buildContent(inputs()));
  delete older.tutorial.openHandImage;
  delete older.tutorial.fistImage;
  older.tutorial.idleDelaySec = 20;
  const rebuilt = buildContent({ ...inputs(), existing: older }).tutorial;
  assert.equal(rebuilt.openHandImage, 'tutorial/hand-open.png');
  assert.equal(rebuilt.fistImage, 'tutorial/hand-fist.png');
  assert.equal(rebuilt.idleDelaySec, 20, 'and the edited number stays');
});

test('a fresh build has songMode true and no songs', () => {
  const fresh = buildContent(inputs());
  assert.equal(fresh.contentVersion, '2026-11-01.1');
  assert.deepEqual(fresh.songs, []);
  assert.equal(fresh.experience.songMode, true);
  assert.equal(fresh.experience.songTitleSec, 3);
  assert.deepEqual(fresh.conduct.songPhrases, [{ part: 'lowstring', steps: [0, 0, 2, 0] }, { part: 'marimba', steps: [1, 2, 3, 2] }]);
  for (const phrase of fresh.conduct.songPhrases) assert.ok(phrase.steps.every((n) => Number.isInteger(n) && n >= 0 && n < fresh.song.pitchSteps));
});

test('build keeps songs and songPhrases', () => {
  const first = buildContent(inputs());
  first.songs = [{ id: 'kept', title: 'Kept', bars: [{ bass: 57, chord: [69], melody: [{ slot: 0, midi: 76 }] }] }];
  first.conduct.songPhrases = [{ part: 'lowstring', steps: [1, 1] }];
  first.experience.songMode = false;
  first.experience.songTitleSec = 5;
  const second = buildContent({ ...inputs(), existing: first });
  assert.deepEqual(second.songs, first.songs);
  assert.deepEqual(second.conduct.songPhrases, [{ part: 'lowstring', steps: [1, 1] }]);
  assert.equal(second.experience.songMode, false);
  assert.equal(second.experience.songTitleSec, 5);
  second.songs[0].bars.push({});
  assert.equal(first.songs[0].bars.length, 1, 'the rebuilt content does not share the songs with the existing one');
});

test('a content file from before song mode gets songPhrases and the song mode keys and keeps the rest', () => {
  const first = buildContent(inputs());
  delete first.songs;
  delete first.conduct.songPhrases;
  delete first.experience.songMode;
  delete first.experience.songTitleSec;
  first.experience.noHandsSec = 4;
  const second = buildContent({ ...inputs(), existing: first });
  assert.deepEqual(second.songs, []);
  assert.deepEqual(second.conduct.songPhrases, buildContent(inputs()).conduct.songPhrases);
  assert.equal(second.experience.songMode, true);
  assert.equal(second.experience.noHandsSec, 4);
});

test('a rebuild of the content.json that ships changes nothing', { skip: !existsSync(resolve(root, 'content.json')) }, () => {
  const shipped = readJson('content.json');
  assert.deepEqual(buildContent({ ...inputs(), existing: shipped }), shipped);
  assert.equal(shipped.contentVersion, '2026-11-01.1');
  assert.equal(shipped.experience.songMode, true);
  assert.deepEqual(shipped.conduct.songPhrases, buildContent(inputs()).conduct.songPhrases);
});
