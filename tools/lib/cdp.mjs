// Minimal Chrome DevTools Protocol helper (Node 22 built-in WebSocket and fetch, no dependencies).
// Launches a separate Chrome with a throwaway profile and drives it over --remote-debugging-port.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const DEFAULT_CHROME = {
  darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  linux: '/usr/bin/google-chrome',
};
// Override with CHROME_PATH=... (for example on the Windows exhibition PC).
export const CHROME_PATH = process.env.CHROME_PATH ?? DEFAULT_CHROME[process.platform];
const STARTUP_TIMEOUT_MS = 20000;

export function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export class CdpConnection {
  #ws;
  #nextId = 1;
  #pending = new Map();
  #listeners = new Map();

  static async open(webSocketUrl) {
    const ws = new WebSocket(webSocketUrl);
    await new Promise((resolve, reject) => {
      ws.addEventListener('open', resolve, { once: true });
      ws.addEventListener('error', () => reject(new Error(`cannot connect to ${webSocketUrl}`)), { once: true });
    });
    return new CdpConnection(ws);
  }

  constructor(ws) {
    this.#ws = ws;
    ws.addEventListener('message', (event) => this.#onMessage(JSON.parse(event.data)));
    ws.addEventListener('close', () => {
      for (const { reject } of this.#pending.values()) reject(new Error('CDP connection closed'));
      this.#pending.clear();
    });
  }

  #onMessage(message) {
    if (message.id !== undefined) {
      const pending = this.#pending.get(message.id);
      if (!pending) return;
      this.#pending.delete(message.id);
      if (message.error) pending.reject(new Error(`${pending.method}: ${message.error.message}`));
      else pending.resolve(message.result);
      return;
    }
    for (const handler of this.#listeners.get(message.method) ?? []) handler(message.params);
  }

  send(method, params = {}) {
    const id = this.#nextId++;
    return new Promise((resolve, reject) => {
      this.#pending.set(id, { resolve, reject, method });
      this.#ws.send(JSON.stringify({ id, method, params }));
    });
  }

  on(method, handler) {
    this.#listeners.set(method, [...(this.#listeners.get(method) ?? []), handler]);
  }

  /** Evaluate an expression in the page; awaits promises; throws on exceptions. */
  async evaluate(expression, { timeoutMs = 600000 } = {}) {
    const call = this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, timeout: timeoutMs });
    const result = await call;
    if (result.exceptionDetails) {
      const text = result.exceptionDetails.exception?.description ?? result.exceptionDetails.text;
      throw new Error(`page exception: ${text}`);
    }
    return result.result.value;
  }

  /** Poll until the expression is truthy (for example a promise handle the page creates after its modules run). */
  async waitFor(expression, { timeoutMs = 30000, intervalMs = 200 } = {}) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (await this.evaluate(`Boolean(${expression})`, { timeoutMs: 5000 })) return;
      await sleep(intervalMs);
    }
    throw new Error(`timeout waiting for ${expression}`);
  }

  close() {
    this.#ws.close();
  }
}

export async function launchChrome({ port, args = [], url = 'about:blank' }) {
  const userDataDir = mkdtempSync(join(tmpdir(), 'downbeat-chrome-'));
  const chromeArgs = [
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${userDataDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-features=Translate',
    ...args,
    url,
  ];
  const proc = spawn(CHROME_PATH, chromeArgs, { stdio: 'ignore' });
  let exited = false;
  proc.on('exit', () => { exited = true; });

  const deadline = Date.now() + STARTUP_TIMEOUT_MS;
  let version = null;
  while (Date.now() < deadline && !exited) {
    try {
      version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
      break;
    } catch {
      await sleep(250);
    }
  }
  if (!version) {
    proc.kill();
    throw new Error('Chrome did not expose the DevTools endpoint in time');
  }
  const browser = await CdpConnection.open(version.webSocketDebuggerUrl);

  return {
    port,
    version,
    browser,
    async pages() {
      return (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter((t) => t.type === 'page');
    },
    async connectToPage(urlPart) {
      const deadlineTab = Date.now() + STARTUP_TIMEOUT_MS;
      while (Date.now() < deadlineTab) {
        const page = (await this.pages()).find((p) => p.url.includes(urlPart));
        if (page) return { page, cdp: await CdpConnection.open(page.webSocketDebuggerUrl) };
        await sleep(250);
      }
      throw new Error(`no tab with url containing "${urlPart}"`);
    },
    async close() {
      try { browser.close(); } catch { /* already closed */ }
      proc.kill();
      await sleep(500);
      rmSync(userDataDir, { recursive: true, force: true });
    },
  };
}
