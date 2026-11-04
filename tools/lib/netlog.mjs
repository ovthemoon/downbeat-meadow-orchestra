// Reads a Chrome net-log file (--log-net-log) and reduces it to the facts the privacy check needs: every URL request that Chrome made
// with its method, who started it (initiator) and whether it had an upload body. Chrome writes this for ALL its processes (page,
// workers, browser), so it catches what the DevTools page log could miss.
import { readFileSync } from 'node:fs';

export function parseNetLog(text) {
  const log = JSON.parse(text);
  const typeName = new Map(Object.entries(log.constants.logEventTypes).map(([name, id]) => [id, name]));
  const sources = new Map(); // source id -> { start, uploads: [] }
  for (const event of log.events) {
    const name = typeName.get(event.type);
    if (name === 'URL_REQUEST_START_JOB' && event.params) {
      sources.set(event.source.id, { url: event.params.url, method: event.params.method, initiator: event.params.initiator ?? null, uploadBytes: 0, hasUpload: false });
    } else if (name === 'UPLOAD_DATA_STREAM_INIT') {
      const entry = sources.get(event.source.id);
      if (entry) { entry.hasUpload = true; entry.uploadBytes += Number(event.params?.total_size ?? 0); }
    }
  }
  return [...sources.values()];
}

export function readNetLog(path) {
  return parseNetLog(readFileSync(path, 'utf8'));
}

/** Split into "started by the page" and "started by Chrome itself", and summarize each. */
export function summarizeNetLog(requests, pageOrigin) {
  const mine = requests.filter((r) => r.initiator === pageOrigin);
  const other = requests.filter((r) => r.initiator !== pageOrigin);
  const hosts = (list) => list.reduce((acc, r) => { try { const h = new URL(r.url).host; acc[h] = (acc[h] ?? 0) + 1; } catch { acc.unparsable = (acc.unparsable ?? 0) + 1; } return acc; }, {});
  const uploads = (list) => list.filter((r) => r.method !== 'GET' && r.method !== 'HEAD' || r.hasUpload).map((r) => ({ method: r.method, url: r.url.slice(0, 120), uploadBytes: r.uploadBytes }));
  return {
    pageOrigin,
    startedByThePage: { requests: mine.length, hosts: hosts(mine), nonGetOrWithUpload: uploads(mine) },
    startedByChromeItself: { requests: other.length, hosts: hosts(other), nonGetOrWithUpload: uploads(other) },
  };
}
