// Where the hand tracker's code and model come from. Defaults are public CDNs (a pinned version, nothing of ours to host);
// an exhibition PC can point at local copies with ?mpbase= and ?model= (same-origin paths only, see sameOriginPath).
export const MEDIAPIPE_VERSION = '0.10.35';
const NPM_BASE = `https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@${MEDIAPIPE_VERSION}`;

export const DEFAULT_SOURCES = Object.freeze({
  bundleUrl: `${NPM_BASE}/vision_bundle.mjs`,
  wasmRoot: `${NPM_BASE}/wasm`,
  modelUrl: 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task',
});

/**
 * A page URL parameter may point to a path on this site, never to another host: otherwise a crafted link could make the page
 * (which has camera access) run code from anywhere. Returns the path or null.
 */
export function sameOriginPath(value) {
  if (typeof value !== 'string' || value.length === 0 || value.length > 200) return null;
  if (!value.startsWith('/') || value.startsWith('//')) return null;
  if (value.includes('..') || value.includes('\\') || value.includes(':') || value.includes('?') || value.includes('#')) return null;
  return /^[A-Za-z0-9/_.-]+$/.test(value) ? value : null;
}

/** @param {URLSearchParams} params */
export function resolveSources(params) {
  const base = sameOriginPath(params.get('mpbase') ?? '');
  const model = sameOriginPath(params.get('model') ?? '');
  return {
    bundleUrl: base ? `${base}/vision_bundle.mjs` : DEFAULT_SOURCES.bundleUrl,
    wasmRoot: base ? `${base}/wasm` : DEFAULT_SOURCES.wasmRoot,
    modelUrl: model ?? DEFAULT_SOURCES.modelUrl,
  };
}

/** Hosts other than this page that the tracker downloads from (for the privacy notice). */
export function externalHosts(sources, pageOrigin) {
  const hosts = new Set();
  for (const url of [sources.bundleUrl, sources.wasmRoot, sources.modelUrl]) {
    const parsed = new URL(url, pageOrigin);
    if (parsed.origin !== pageOrigin) hosts.add(parsed.host);
  }
  return [...hosts];
}
