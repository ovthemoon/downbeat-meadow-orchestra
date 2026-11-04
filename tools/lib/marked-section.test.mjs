import { test } from 'node:test';
import assert from 'node:assert/strict';
import { extractSection, replaceSection } from './marked-section.mjs';

const B = '<!-- b -->';
const E = '<!-- e -->';

test('a block is appended once and then replaced in place, leaving the rest of the file alone', () => {
  const once = replaceSection('# Title\n\ntext\n', B, E, 'one');
  assert.equal(once, '# Title\n\ntext\n\n<!-- b -->\none\n<!-- e -->\n');
  const twice = replaceSection(once, B, E, 'two\nlines');
  assert.equal(twice, '# Title\n\ntext\n\n<!-- b -->\ntwo\nlines\n<!-- e -->\n');
  assert.equal(extractSection(twice, B, E), '<!-- b -->\ntwo\nlines\n<!-- e -->');
});

test('a text without the block gives null, a block that is not closed is an error', () => {
  assert.equal(extractSection('nothing', B, E), null);
  assert.throws(() => extractSection('<!-- b --> open', B, E), /not "<!-- e -->"/);
});

test('replacement text containing $ patterns is inserted literally', () => {
  const out = replaceSection(`x\n${B}\nold\n${E}\n`, B, E, 'cost $& $1');
  assert.ok(out.includes('cost $& $1'));
});
