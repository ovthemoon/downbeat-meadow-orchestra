import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { convertSong, importSongs, withSongs } from './songs-import.mjs';

const toolsDir = resolve(import.meta.dirname, '..');
const contentPath = resolve(toolsDir, '..', 'content', 'content.json');

const songFile = (overrides = {}) => ({
  id: 'tiny', title: 'Tiny', credit: 'test', bpm: 96, beatsPerBar: 4, swing: 0.5, melodyOctave: { harp: 0, recorder: 0, bell: -12 },
  bars: [
    { bass: 57, chord: [69, 72, 76], chordName: 'Am', melody: [[0, 76], [4, 81]] },
    { bass: 53, chord: [65, 69, 72], melody: [] },
  ],
  ...overrides,
});
const source = (file, data) => ({ file, text: JSON.stringify(data) });

test('import converts pairs to objects and keeps order', () => {
  const second = songFile({ id: 'second', title: 'Second', bars: [{ bass: 60, chord: [60, 64, 67], melody: [[2, 84]] }] });
  const sources = [source('one.song.json', songFile()), source('two.song.json', second)];

  const songs = importSongs(sources);

  assert.deepEqual(songs.map((s) => s.id), ['tiny', 'second']);
  assert.deepEqual(songs[0].bars[0].melody, [{ slot: 0, midi: 76 }, { slot: 4, midi: 81 }]);
  assert.deepEqual(songs[0].bars[1].melody, []);
  assert.deepEqual(songs[1].bars[0].melody, [{ slot: 2, midi: 84 }]);
  assert.equal('chordName' in songs[0].bars[0], false, 'chordName is dropped');
  assert.deepEqual(songs[0].bars[0].chord, [69, 72, 76]);
  assert.equal(songs[0].bars[0].bass, 57);
  const { bars, ...header } = songs[0];
  assert.deepEqual(header, { id: 'tiny', title: 'Tiny', credit: 'test', bpm: 96, beatsPerBar: 4, swing: 0.5, melodyOctave: { harp: 0, recorder: 0, bell: -12 } });
});

test('import does not change what it was given', () => {
  const parsed = songFile();
  const before = structuredClone(parsed);
  const song = convertSong(parsed, 'one.song.json');
  assert.deepEqual(parsed, before);
  song.bars[0].chord.push(99);
  song.melodyOctave.bell = 7;
  assert.deepEqual(parsed, before, 'the converted song does not share arrays or objects with the parsed file');
});

test('import refuses a file without bars', () => {
  for (const broken of [{ id: 'x' }, { id: 'x', bars: 'no' }, { id: 'x', bars: null }, { id: 'x', bars: [] }]) {
    assert.throws(() => importSongs([source('good.song.json', songFile()), source('broken.song.json', broken)]), /broken\.song\.json.*bars/);
  }
});

test('import refuses a pair that is not two integers, naming the file', () => {
  for (const pair of [[4], [0, 76, 1], [0.5, 76], [0, '76'], null, 'ab', { slot: 0, midi: 76 }, [0, NaN]]) {
    const song = songFile({ bars: [{ bass: 57, chord: [69], melody: [[0, 76], pair] }] });
    assert.throws(() => importSongs([source('bad-pair.song.json', song)]), /bad-pair\.song\.json.*bar 0.*note 1/, JSON.stringify(pair));
  }
});

test('import refuses a melody that is not a list and text that is not JSON, naming the file', () => {
  const song = songFile({ bars: [{ bass: 57, chord: [69], melody: 'oops' }] });
  assert.throws(() => importSongs([source('melody.song.json', song)]), /melody\.song\.json.*bar 0.*melody/);
  assert.throws(() => importSongs([{ file: 'garbage.song.json', text: '{ not json' }]), /garbage\.song\.json/);
  assert.throws(() => importSongs([source('array.song.json', [])]), /array\.song\.json/);
});

test('import refuses an empty list of files', () => {
  assert.throws(() => importSongs([]), /no song files/);
});

test('withSongs puts the songs into the content and leaves every other key as it was', () => {
  const content = { schemaVersion: 4, audio: { masterGainDb: -6 }, songs: [{ id: 'old' }] };
  const next = withSongs(content, [{ id: 'new' }]);
  assert.deepEqual(next, { schemaVersion: 4, audio: { masterGainDb: -6 }, songs: [{ id: 'new' }] });
  assert.deepEqual(content.songs, [{ id: 'old' }], 'the content that was given is not changed');
  assert.deepEqual(withSongs({ schemaVersion: 4 }, [{ id: 'a' }]).songs, [{ id: 'a' }]);
});

test('the command writes the songs in the order given and refuses a bad file without touching the content', () => {
  const dir = mkdtempSync(join(tmpdir(), 'songs-import-'));
  const target = join(dir, 'content.json');
  writeFileSync(target, JSON.stringify({ schemaVersion: 4, audio: { masterGainDb: -6 } }, null, 2) + '\n');
  const b = join(dir, 'b.song.json');
  const a = join(dir, 'a.song.json');
  const bad = join(dir, 'bad.song.json');
  writeFileSync(b, JSON.stringify(songFile({ id: 'b' })));
  writeFileSync(a, JSON.stringify(songFile({ id: 'a' })));
  writeFileSync(bad, JSON.stringify({ id: 'bad' }));

  execFileSync('node', [join(toolsDir, 'import_songs.mjs'), '--content', target, b, a], { encoding: 'utf8' });
  const written = JSON.parse(readFileSync(target, 'utf8'));
  assert.deepEqual(written.songs.map((s) => s.id), ['b', 'a']);
  assert.equal(written.audio.masterGainDb, -6);
  assert.equal(readFileSync(target, 'utf8').endsWith('}\n'), true);

  const before = readFileSync(target, 'utf8');
  assert.throws(() => execFileSync('node', [join(toolsDir, 'import_songs.mjs'), '--content', target, a, bad], { encoding: 'utf8', stdio: 'pipe' }), (error) => {
    assert.match(String(error.stderr), /bad\.song\.json.*bars/);
    return true;
  });
  assert.equal(readFileSync(target, 'utf8'), before, 'nothing was written');
});

test('the content.json that ships has the three songs in order, with note objects and no chord names', () => {
  const shipped = JSON.parse(readFileSync(contentPath, 'utf8'));
  assert.deepEqual(shipped.songs.map((s) => s.id), ['quiet-meadow', 'bright-morning', 'auld-lang-syne']);
  for (const song of shipped.songs) {
    assert.ok(song.title.length > 0 && song.credit.length > 0, song.id);
    assert.equal(song.bpm, 96);
    assert.equal(song.bars.length, 24, song.id);
    for (const bar of song.bars) {
      assert.equal('chordName' in bar, false);
      for (const note of bar.melody) {
        assert.deepEqual(Object.keys(note).sort(), ['midi', 'slot']);
        assert.ok(Number.isInteger(note.slot) && Number.isInteger(note.midi));
      }
    }
  }
});
