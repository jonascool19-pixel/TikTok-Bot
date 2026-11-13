import { chromium } from 'playwright';
import { join } from 'node:path';

const host = 'https://www.tiktok.com';
const composer = '[data-e2e="live-chat-input-container"] [contenteditable="true"], [data-e2e="live-chat-input-container"] [contenteditable="plaintext-only"], [data-e2e="live-chat-input-container"] textarea';
const profile = '[data-e2e="profile-icon"] a[href^="/@"], a[data-e2e="profile-icon"][href^="/@"], a[data-e2e="profile-link"][href^="/@"]';
export function browserChat(directory, { event, status, log, traffic = () => {} }, engine = chromium) {
  let context, page, creator, timer, opening, working = false, login = false;
  let ready = false, lastSent = 0, lastNavigation = 0;
  let serial = Promise.resolve();
  const run = (work) => {
    const job = serial.then(work);
    serial = job.catch(() => {});
    return job;
  };
  const roomUrl = () => host + '/@' + creator + '/live';
  const trusted = () => {
    const url = new URL(page.url());
    return url.origin === host;
  };
  const inRoom = () => trusted() && new URL(page.url()).pathname.replace(/\/$/, '').toLowerCase() === new URL(roomUrl()).pathname.toLowerCase();
  const account = async () => {
    const href = await page.locator(profile).first().getAttribute('href', { timeout: 1500 }).catch(() => null);
    return href?.match(/^\/@([\w.]{1,64})(?:[/?#]|$)/)?.[1] ?? '';
  };

  async function open() {
    if (page && !page.isClosed()) return;
    if (opening) return opening;
    opening = (async () => {
      if (!context) {
        context = await engine.launchPersistentContext(join(directory, 'tiktok-browser'), {
          viewport: { width: 1000, height: 720 }, chromiumSandbox: true,
        });
        context.once('close', () => { context = undefined; page = undefined; ready = false; });
        context.on('requestfinished', (request) => {
          void request.sizes().then(({ requestHeadersSize, requestBodySize, responseHeadersSize, responseBodySize }) =>
            traffic(requestHeadersSize + requestBodySize, responseHeadersSize + responseBodySize)).catch(() => {});
        });
      }
      page = context.pages()[0] ?? await context.newPage();
    })();
    try { await opening; } finally { opening = undefined; }
  }

  async function poll() {
    if (working || login || !creator) return;
    working = true;
    try {
      await open();
      if (!inRoom() || (!ready && Date.now() - lastNavigation > 30000)) {
        if (!inRoom() && Date.now() - lastNavigation < 30000) return;
        lastNavigation = Date.now();
        await page.goto(roomUrl(), { waitUntil: 'domcontentloaded', timeout: 18000 });
      }
      if (!inRoom()) { ready = false; status('LIVE-Seite nicht verfügbar oder Anmeldung erforderlich'); return; }
      const batch = await page.evaluate(() => {
        const root = document.querySelector('[data-e2e="live-chat-container"], [data-e2e="chat-room"]');
        if (!root) return null;
        const previous = window.__ttbChatRows;
        const seen = previous?.root === root ? previous.seen : new WeakMap();
        window.__ttbChatRows = { root, seen };
        const output = [];
        for (const row of [...root.querySelectorAll('[data-e2e="chat-message"]')].slice(-100)) {
          const name = row.querySelector('[data-e2e="message-owner-name"]')?.textContent?.trim();
          const text = row.querySelector('.break-words.align-middle')?.textContent?.trim();
          if (!name || !text) continue;
          const href = row.querySelector('a[href^="/@"]')?.getAttribute('href') ?? '';
          const handle = href.match(/^\/@([\w.]+)(?:[/?#]|$)/)?.[1] ?? '';
          const key = JSON.stringify([handle, name, text]);
          if (previous?.root === root && seen.get(row) !== key) output.push({ type: 'comment', handle, userId: handle, name, text });
          seen.set(row, key);
        }
        return output;
      });
      ready = batch !== null;
      status(ready ? 'LIVE-Webchat verbunden' : 'Warte auf den LIVE-Webchat');
      for (const item of batch ?? []) event(item);
    } catch (error) { ready = false; status('Browser-Verbindung fehlgeschlagen'); log('tiktok', 'Browser-Verbindung fehlgeschlagen', error); }
    finally { working = false; }
  }

  function configure(config) {
    clearInterval(timer);
    ready = false;
    lastNavigation = 0;
    creator = config.creator;
    if (!creator && !login) {
      login = false;
      void run(async () => { await context?.close(); context = undefined; page = undefined; }).catch((error) => log('tiktok', 'Browser konnte nicht geschlossen werden', error));
    } else if (creator) {
      void run(poll);
      timer = setInterval(() => {
        if (!working && !login) void run(poll);
      }, 2500);
      timer.unref();
    } else status('Creator-Name fehlt');
  }

  return {
    configure, online: () => ready,
    start: () => run(async () => {
      login = true;
      ready = false;
      try { await open(); await page.goto(host + '/login', { waitUntil: 'domcontentloaded', timeout: 20000 }); }
      catch (error) { login = false; throw error; }
      status('TikTok-Anmeldung geöffnet');
    }),
    screenshot: () => run(async () => {
      if (!login || !page || !trusted()) throw new Error('Anmeldung pausiert. Öffne die TikTok-Anmeldung erneut.');
      return page.screenshot({ type: 'jpeg', quality: 65, timeout: 5000, scale: 'css' });
    }),
    input: ({ kind, x, y, text, key, value, dx, dy }) => run(async () => {
      if (!login || !page || !trusted()) throw new Error('TikTok-Anmeldung ist nicht geöffnet');
      const point = Number.isFinite(x) && Number.isFinite(y) && x >= 0 && x < 1000 && y >= 0 && y < 720;
      if (kind === 'wheel' && point && [dx, dy].every((delta) => Number.isFinite(delta) && Math.abs(delta) <= 1500)) {
        await page.mouse.move(x, y);
        await page.mouse.wheel(dx, dy);
      } else if (kind === 'click' && point) {
        const choices = await page.evaluate(({ x, y }) => {
          const select = document.elementFromPoint(x, y)?.closest('select');
          if (!select || select.disabled) return null;
          select.focus();
          return Array.from(select.options, (option) => ({
            label: option.label, value: option.value, disabled: option.matches(':disabled'), selected: option.selected,
          })).slice(0, 250);
        }, { x, y });
        if (choices) return { choices };
        await page.mouse.click(x, y);
      } else if (kind === 'select' && typeof value === 'string' && value.length <= 200) {
        const select = page.locator('select:focus');
        if (await select.count() !== 1) throw new Error('Öffne zuerst die Auswahl in der TikTok-Ansicht.');
        await select.selectOption({ value }, { timeout: 2500 });
      } else if (kind === 'text' && typeof text === 'string' && text.length <= 300) {
        await page.keyboard.insertText(text);
      } else if (kind === 'key' && ['Tab', 'Enter', 'Backspace', 'Delete', 'Escape', 'Control+a', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End'].includes(key)) {
        await page.keyboard.press(key);
      } else throw new Error('Ungültige Eingabe');
      const focused = await page.evaluate(() => document.activeElement?.matches('input:not([type="button"]):not([type="submit"]), textarea, [contenteditable="true"]') ?? false);
      return { focused };
    }),
    finish: () => run(async () => {
      if (!login || !page || !trusted()) throw new Error('TikTok-Anmeldung ist nicht geöffnet');
      const handle = await account();
      if (handle) login = false;
      return handle;
    }),
    cancel: () => run(async () => { login = false; }),
    send: (text, bot) => run(async () => {
      if (!ready || login || !page || !inRoom()) throw new Error('Kein LIVE-Webchat geöffnet');
      if ((await account()).toLowerCase() !== bot.toLowerCase()) throw new Error('Bot-Konto im Browser konnte nicht bestätigt werden');
      if (Date.now() - lastSent < 6000) throw new Error('Sendepause aktiv');
      const input = page.locator(composer);
      if (await input.count() !== 1 || !await input.isEditable()) throw new Error('Chatfeld nicht verfügbar');
      await input.fill(text, { timeout: 2500 });
      if (!inRoom()) throw new Error('LIVE-Seite hat gewechselt');
      lastSent = Date.now();
      await input.press('Enter', { timeout: 2500 });
    }),
  };
}
