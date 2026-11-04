// S-1 scenario definitions: which stress is applied to which run, and when tab disturbances happen.
export const BLOCK_STRESS = Object.freeze({ ms: 50, everyMs: 2000 });
export const DISTURBANCE_AT_SEC = 20;
export const DISTURBANCE_LENGTH_SEC = 10;

/** @returns {{name:string, block:object|null, disturbance:'hidden'|'blur'|null}[]} */
export function scenariosFor(names) {
  const all = {
    calm: { name: 'calm', block: null, disturbance: null },
    stress: { name: 'stress', block: BLOCK_STRESS, disturbance: null },
    hidden: { name: 'hidden', block: BLOCK_STRESS, disturbance: 'hidden' },
    blur: { name: 'blur', block: BLOCK_STRESS, disturbance: 'blur' }, // synthetic window blur/focus events
  };
  return names.map((n) => {
    if (!all[n]) throw new Error(`unknown scenario "${n}"`);
    return all[n];
  });
}
