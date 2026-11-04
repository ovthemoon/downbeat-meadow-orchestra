// Locates a known waveform (the first part of a sample) in a recording near expected times.
// Used when notes overlap and ring on, so a threshold onset detector cannot re-arm: we ask "where exactly does each
// scheduled note start in the audio output?" and compare with where it was scheduled. Pure functions, no I/O.

/**
 * @param {Float32Array} recording mono signal
 * @param {Float32Array} template the attack of the sample, as it was played (same rate and gain shape)
 * @param {number[]} expectedStartFrames where each note should start (fractional frames allowed)
 * @param {number} halfWindowFrames search +/- this many frames around each expected start
 * @returns {{lagFrames:number, correlation:number, edge:boolean}[]} lag of the best match (sub-frame, parabolic) relative to expected
 */
export function locateTemplate(recording, template, expectedStartFrames, halfWindowFrames) {
  const m = template.length;
  let templateEnergy = 0;
  for (let i = 0; i < m; i++) templateEnergy += template[i] * template[i];
  if (templateEnergy === 0) throw new Error('template is silent');

  return expectedStartFrames.map((expected) => {
    const center = Math.round(expected);
    const first = center - halfWindowFrames;
    const scores = new Float64Array(2 * halfWindowFrames + 1);
    let best = -1;
    for (let k = 0; k < scores.length; k++) {
      const start = first + k;
      if (start < 0 || start + m > recording.length) { scores[k] = -1; continue; }
      let dot = 0;
      let energy = 0;
      for (let i = 0; i < m; i++) {
        const x = recording[start + i];
        dot += x * template[i];
        energy += x * x;
      }
      scores[k] = energy === 0 ? 0 : dot / Math.sqrt(energy * templateEnergy);
      if (best < 0 || scores[k] > scores[best]) best = k;
    }
    const edge = best <= 0 || best >= scores.length - 1;
    let offset = 0;
    if (!edge) {
      const a = scores[best - 1];
      const b = scores[best];
      const c = scores[best + 1];
      const denominator = a - 2 * b + c;
      offset = denominator === 0 ? 0 : (0.5 * (a - c)) / denominator;
    }
    return { lagFrames: first + best + offset - expected, correlation: scores[best], edge };
  });
}
