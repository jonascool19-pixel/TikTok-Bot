import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const profile = await mkdtemp(join(tmpdir(), 'tiktok-chrome-'));
const report = join(homedir(), 'tiktok-diagnose.txt');
const browser = spawn(process.env.CHROME_BIN || 'google-chrome-stable', [
  '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check',
  `--user-data-dir=${profile}`, 'about:blank',
], { stdio: 'ignore' });
let launchError;
browser.on('error', error => { launchError = error; });
const closed = new Promise(resolve => browser.once('close', resolve));
const lines = new Set();
const requests = new Map();
let socket;

function record(line) {
  lines.add(line);
}

function observe({ method, params, sessionId }) {
  if (method === 'Runtime.consoleAPICalled') {
    const message = params.args.map(arg => typeof arg.value === 'string' ? arg.value : '').join(' ');
    const warning = message.match(/Failed to fetch csrf token: host=webcast\.tiktok\.com status_code=(\d{3}) message=(.*)/i);
    if (warning) {
      const reason = /^kms err\s*$/i.test(warning[2]) ? 'kms err' : 'anderer Fehlertext';
      record(`CSRF-Warnung: HTTP ${warning[1]}, ${reason}`);
    }
  }
  if (method === 'Network.responseReceived' || method === 'Network.requestWillBeSent') {
    const entry = params.response ?? params.request;
    if (new URL(entry.url).hostname !== 'webcast.tiktok.com') return;
    const key = `${sessionId}:${params.requestId}`;
    if (params.request) requests.set(key, true);
    if (params.response) {
      requests.delete(key);
      const address = entry.remoteIPAddress ?? '';
      const family = address.includes(':') ? 'IPv6' : /^\d+\.\d+\.\d+\.\d+$/.test(address) ? 'IPv4' : 'unbekannt';
      record(`webcast.tiktok.com: HTTP ${entry.status}, IP-Familie ${family}`);
    }
  }
  if (method === 'Network.loadingFailed' && requests.delete(`${sessionId}:${params.requestId}`)) {
    const error = /^net::ERR_[A-Z0-9_]+$/.test(params.errorText) ? params.errorText : 'unbekannt';
    record(`webcast.tiktok.com: Netzwerkfehler ${error}`);
  }
}

try {
  let port;
  let endpoint;
  for (let attempt = 0; attempt < 100; attempt++) {
    if (launchError || browser.exitCode !== null || browser.signalCode !== null) break;
    try {
      [port, endpoint] = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).trim().split(/\r?\n/);
      if (/^\d+$/.test(port) && endpoint?.startsWith('/devtools/browser/')) break;
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
    await delay(100);
  }
  if (launchError) throw launchError;
  if (!/^\d+$/.test(port ?? '') || !endpoint?.startsWith('/devtools/browser/')) throw new Error('Chrome konnte nicht gestartet werden. Starte das Skript in einem Terminal auf dem Linux-Desktop.');
  socket = new WebSocket(`ws://127.0.0.1:${port}${endpoint}`);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let id = 0;
  const send = (method, params = {}, sessionId) =>
    socket.send(JSON.stringify({ id: ++id, method, params, sessionId }));
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.method === 'Target.attachedToTarget') {
      const { sessionId, targetInfo, waitingForDebugger } = message.params;
      if (targetInfo.type === 'page' || targetInfo.type === 'iframe') {
        send('Runtime.enable', {}, sessionId);
        send('Network.enable', {}, sessionId);
        send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: true, flatten: true }, sessionId);
      }
      if (waitingForDebugger) send('Runtime.runIfWaitingForDebugger', {}, sessionId);
    }
    if (message.method && message.params) observe(message);
  });
  send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: true, flatten: true });
  send('Target.createTarget', { url: 'https://www.tiktok.com/login' });
  console.log('Im Diagnosefenster bei TikTok anmelden, LIVE öffnen und den Fehler einmal auslösen. Danach alle Diagnosefenster schließen.');
  await closed;
  const content = ['TikTok LIVE – Chrome-Diagnose', ...lines];
  if (!lines.size) content.push('Keine passende CSRF-Warnung oder Netzwerkantwort erfasst.');
  await writeFile(report, content.join('\n') + '\n', { mode: 0o600 });
  console.log(`Bericht gespeichert: ${report}`);
} catch (error) {
  console.error(error.message || 'Chrome-Diagnose konnte nicht gestartet werden.');
  process.exitCode = 1;
} finally {
  socket?.close();
  if (browser.exitCode === null && browser.signalCode === null && browser.pid) browser.kill();
  await closed;
  await rm(profile, { recursive: true, force: true, maxRetries: 3 });
}
