// A block of a Markdown file between two marker comments, owned by one tool. Lets two tools write different parts of the same file
// (content/audio/LICENSES.md: the VCSL table and the table of synthesized sounds) without wiping each other's part.

export const NATURE_BEGIN = '<!-- nature-samples:begin (written by tools/prepare_nature_samples.mjs) -->';
export const NATURE_END = '<!-- nature-samples:end -->';

/** The block including its markers, or null when the text has none. */
export function extractSection(text, begin, end) {
  const from = text.indexOf(begin);
  if (from < 0) return null;
  const to = text.indexOf(end, from);
  if (to < 0) throw new Error(`found "${begin}" but not "${end}"`);
  return text.slice(from, to + end.length);
}

/** `text` with the block replaced (or appended after a blank line when it has none). `body` is the text between the markers. */
export function replaceSection(text, begin, end, body) {
  const block = `${begin}\n${body.trim()}\n${end}`;
  const existing = extractSection(text, begin, end);
  if (existing !== null) return text.replace(existing, () => block);
  return `${text.trimEnd()}\n\n${block}\n`;
}
