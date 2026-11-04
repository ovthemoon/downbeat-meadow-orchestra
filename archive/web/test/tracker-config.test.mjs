import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_SOURCES, MEDIAPIPE_VERSION, externalHosts, resolveSources, sameOriginPath } from '../WebGLTemplates/Downbeat/js/tracker-config.js';

test('the defaults load a pinned MediaPipe version from jsDelivr and the model from Google storage', () => {
  assert.equal(MEDIAPIPE_VERSION, '0.10.35');
  assert.match(DEFAULT_SOURCES.bundleUrl, /^https:\/\/cdn\.jsdelivr\.net\/npm\/@mediapipe\/tasks-vision@0\.10\.35\/vision_bundle\.mjs$/);
  assert.match(DEFAULT_SOURCES.wasmRoot, /tasks-vision@0\.10\.35\/wasm$/);
  assert.match(DEFAULT_SOURCES.modelUrl, /^https:\/\/storage\.googleapis\.com\/mediapipe-models\/hand_landmarker\/.*hand_landmarker\.task$/);
});

test('a page URL may point the tracker only at paths on the same site', () => {
  assert.equal(sameOriginPath('/vendor/mediapipe'), '/vendor/mediapipe');
  for (const bad of ['https://evil.example/x', '//evil.example/x', '/a/../b', 'vendor/x', '/a:b', '/a?b=1', '/a#b', '/a\\b', '', null, undefined, '/' + 'a'.repeat(300), '/a b']) {
    assert.equal(sameOriginPath(bad), null, String(bad));
  }
});

test('?mpbase and ?model replace the sources, a hostile value is ignored', () => {
  const local = resolveSources(new URLSearchParams('mpbase=/vendor/mediapipe&model=/vendor/models/hand_landmarker.task'));
  assert.equal(local.bundleUrl, '/vendor/mediapipe/vision_bundle.mjs');
  assert.equal(local.wasmRoot, '/vendor/mediapipe/wasm');
  assert.equal(local.modelUrl, '/vendor/models/hand_landmarker.task');
  const hostile = resolveSources(new URLSearchParams('mpbase=https://evil.example/&model=//evil.example/m'));
  assert.deepEqual(hostile, { ...DEFAULT_SOURCES });
});

test('the privacy notice lists exactly the hosts that are not this site', () => {
  assert.deepEqual(externalHosts({ ...DEFAULT_SOURCES }, 'http://localhost:8780').sort(), ['cdn.jsdelivr.net', 'storage.googleapis.com']);
  const local = resolveSources(new URLSearchParams('mpbase=/vendor/mediapipe&model=/vendor/models/hand_landmarker.task'));
  assert.deepEqual(externalHosts(local, 'http://localhost:8780'), []);
});
