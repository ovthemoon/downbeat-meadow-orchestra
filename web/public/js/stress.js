// Synthetic main-thread load: block the thread for `ms` every `everyMs` (models inference cost, plan 6.3 step 3).

export function startBlocker({ ms, everyMs }) {
  const blocks = [];
  const id = setInterval(() => {
    const start = performance.now();
    while (performance.now() - start < ms) {
      // busy wait
    }
    blocks.push({ startMs: start, durationMs: performance.now() - start });
  }, everyMs);
  return {
    stop() {
      clearInterval(id);
      return blocks;
    },
  };
}
