// Turns song files (the output of the song transcription: bars of bass, chord and `"melody": [[slot, midi], ...]`) into the `songs` list of content/content.json.
// The app reads a note as an object, so each [slot, midi] pair becomes {slot, midi}; the chord name of a bar is only a label of the transcription and is dropped; every other key is kept as it is.
// Nothing here changes what it is given, and a file that is not whole is refused with a message that names it: the content is only written once every file is good.

const refuse = (file, message) => {
  throw new Error(`${file}: ${message}`);
};

const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

const DROPPED_BAR_KEYS = Object.freeze(['chordName']);

function convertNote(pair, file, barIndex, noteIndex) {
  if (!Array.isArray(pair) || pair.length !== 2 || !pair.every(Number.isInteger)) {
    refuse(file, `bar ${barIndex}, note ${noteIndex} must be [slot, midi] as two integers, got ${JSON.stringify(pair)}`);
  }
  const [slot, midi] = pair;
  return { slot, midi };
}

function convertMelody(melody, file, barIndex) {
  if (!Array.isArray(melody)) refuse(file, `bar ${barIndex}: melody must be a list of [slot, midi] pairs`);
  return melody.map((pair, noteIndex) => convertNote(pair, file, barIndex, noteIndex));
}

function convertBar(bar, file, barIndex) {
  if (!isObject(bar)) refuse(file, `bar ${barIndex} must be an object`);
  return Object.fromEntries(
    Object.entries(bar)
      .filter(([key]) => !DROPPED_BAR_KEYS.includes(key))
      .map(([key, value]) => [key, key === 'melody' ? convertMelody(value, file, barIndex) : structuredClone(value)]),
  );
}

/**
 * One song as the app reads it. `file` only names the source in a refusal.
 * @param {object} song a parsed song file
 * @param {string} file
 */
export function convertSong(song, file) {
  if (!isObject(song)) refuse(file, 'a song file must hold one JSON object');
  if (!Array.isArray(song.bars)) refuse(file, 'a song needs a list of bars');
  if (song.bars.length === 0) refuse(file, 'the list of bars is empty');
  return Object.fromEntries(
    Object.entries(song).map(([key, value]) => [key, key === 'bars' ? value.map((bar, barIndex) => convertBar(bar, file, barIndex)) : structuredClone(value)]),
  );
}

function parseSong({ file, text }) {
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    refuse(file, `is not valid JSON (${error.message})`);
  }
  return convertSong(parsed, file);
}

/**
 * The songs of the given files, in the order given.
 * @param {{file:string, text:string}[]} sources file name (for messages) and the text of the file
 */
export function importSongs(sources) {
  if (sources.length === 0) throw new Error('no song files given');
  return sources.map(parseSong);
}

/** `content` with its `songs` replaced by `songs`; every other key stays as it was. */
export const withSongs = (content, songs) => ({ ...content, songs: structuredClone(songs) });
