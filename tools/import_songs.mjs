#!/usr/bin/env node
// Writes the scored songs into content/content.json (`songs`), in the order given on the command line, replacing the songs that are there.
// Each song file has bars of bass, chord and `"melody": [[slot, midi], ...]`; the app reads the notes as {slot, midi} (tools/lib/songs-import.mjs).
// A file that is not whole stops the run with a message naming it, and content.json is not touched. tools/build_content.mjs keeps the songs on a rebuild.
// Usage: node tools/import_songs.mjs [--content content/content.json] <song.json>...
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { importSongs, withSongs } from './lib/songs-import.mjs';

const defaultContent = resolve(import.meta.dirname, '..', 'content', 'content.json');

function readText(file) {
  try {
    return readFileSync(file, 'utf8');
  } catch (error) {
    throw new Error(`${file}: cannot be read (${error.message})`);
  }
}

function main() {
  const { values, positionals } = parseArgs({ options: { content: { type: 'string', default: defaultContent } }, allowPositionals: true });
  if (positionals.length === 0) throw new Error('usage: node tools/import_songs.mjs [--content content/content.json] <song.json>...');

  const songs = importSongs(positionals.map((file) => ({ file, text: readText(file) })));
  const content = JSON.parse(readText(values.content));
  writeFileSync(values.content, JSON.stringify(withSongs(content, songs), null, 2) + '\n');
  for (const song of songs) {
    console.log(`${song.id}: ${song.bars.length} bars, ${song.bars.reduce((n, bar) => n + (bar.melody?.length ?? 0), 0)} melody notes`);
  }
  console.log(`wrote ${songs.length} songs to ${values.content}`);
}

try {
  main();
} catch (error) {
  console.error(`import_songs: ${error.message}`);
  process.exitCode = 1;
}
