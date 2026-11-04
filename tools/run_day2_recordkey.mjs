#!/usr/bin/env node
// Checks the R key: first press starts recording (badge shows [REC]), second press stops and saves a replay file (a download).
// Usage: node tools/run_day2_recordkey.mjs --video <hands1.y4m>
import { parseArgs } from 'node:util';
import { openProduct, sleep, startAndWaitReady, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: { video: { type: 'string' } } });
const server = startSiteServer({ port: 8798, outDir: '/tmp' });
await sleep(800);
let session;
try {
  session = await openProduct({ port: 8798, debugPort: 9368, query: 'input=hand', video: values.video });
  const { cdp } = session;
  const downloads = [];
  cdp.on('Page.downloadWillBegin', (p) => downloads.push(p.suggestedFilename));
  await cdp.send('Page.setDownloadBehavior', { behavior: 'deny' });
  await startAndWaitReady(cdp);
  if ((await waitTrackerSettled(cdp)) !== 2) throw new Error('tracker did not start');
  const press = async () => {
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'r', code: 'KeyR', text: 'r' });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'r', code: 'KeyR' });
  };
  await press();
  await sleep(1500);
  const during = await cdp.evaluate('({ recording: window.Downbeat.hands.recorder.isRecording, badge: document.getElementById("mode-badge").textContent, frames: window.Downbeat.hands.recorder.frameCount })');
  await press();
  await sleep(1500);
  const after = await cdp.evaluate('({ recording: window.Downbeat.hands.recorder.isRecording, badge: document.getElementById("mode-badge").textContent })');
  console.log(JSON.stringify({ during, after, downloads }));
  const ok = during.recording && /REC/.test(during.badge) && during.frames > 20 && !after.recording && !/REC/.test(after.badge) && downloads.length === 1 && /^downbeat-hands-.*\.json$/.test(downloads[0]);
  console.log(ok ? 'PASS' : 'FAIL');
  process.exitCode = ok ? 0 : 1;
} finally {
  await session?.chrome.close();
  server.stop();
}
