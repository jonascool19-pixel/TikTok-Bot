import { createCipheriv, createDecipheriv, createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { convert } from 'html-to-text';

const scope = 'https://www.googleapis.com/auth/gmail.readonly';
const gmailBase = 'https://gmail.googleapis.com/gmail/v1/users/me/';
const maxAge = 48 * 3600000;
const transactionCookie = 'ttb_gmail';

export class GmailError extends Error {
  constructor(statusCode, message) {
    super(message);
    this.statusCode = statusCode;
  }
}

function equal(a, b) {
  return typeof a === 'string' && typeof b === 'string' && a.length === b.length &&
    /^[a-z0-9_-]+$/i.test(a) && /^[a-z0-9_-]+$/i.test(b) &&
    timingSafeEqual(Buffer.from(a), Buffer.from(b));
}

function bodyPart(part, mimeType) {
  if (part?.mimeType === mimeType && !part.filename && typeof part.body?.data === 'string') {
    return Buffer.from(part.body.data.slice(0, 131072), 'base64url').toString('utf8');
  }
  for (const child of part?.parts ?? []) {
    const content = bodyPart(child, mimeType);
    if (content) return content;
  }
  return '';
}

function messageText(message) {
  const subject = message.payload?.headers?.find(({ name }) => name.toLowerCase() === 'subject')?.value;
  const html = bodyPart(message.payload, 'text/html');
  const text = bodyPart(message.payload, 'text/plain') || (html && convert(html, {
    wordwrap: false,
    selectors: [
      { selector: 'a', options: { ignoreHref: true } },
      { selector: 'img', format: 'skip' },
      { selector: 'style', format: 'skip' },
      { selector: 'script', format: 'skip' },
    ],
  })) || message.snippet || 'Kein Text vorhanden.';
  return (subject ? subject.slice(0, 200) + '\n\n' : '') + text.slice(0, 3000);
}

export function makeGmail(directory, env = process.env, request = fetch) {
  const {
    BOT_SECURE_COOKIE, BOT_GMAIL_CLIENT_ID: clientId, BOT_GMAIL_CLIENT_SECRET: clientSecret,
    BOT_GMAIL_REDIRECT_URI: redirectUri, BOT_GMAIL_ADDRESS: configuredAddress, BOT_GMAIL_KEY: keyHex,
    BOT_GMAIL_LABEL: labelName = 'TikTok-Bot',
  } = env;
  const address = configuredAddress?.trim().toLowerCase();
  let redirect;
  try { redirect = new URL(redirectUri); } catch {}
  const enabled = Boolean(BOT_SECURE_COOKIE === '1' && clientId && clientSecret &&
    address && /^[^\s@]+@gmail\.com$/i.test(address) && /^[a-f0-9]{64}$/i.test(keyHex ?? '') &&
    redirect?.protocol === 'https:' && !redirect.username && !redirect.password &&
    redirect.pathname === '/api/gmail/callback' && !redirect.search && !redirect.hash);
  const vault = join(directory, 'gmail.enc');
  const key = enabled ? Buffer.from(keyHex, 'hex') : null;
  const binding = Buffer.from('TikTok-Bot:Gmail:v1:' + address + ':' + clientId);
  const transactions = new Map();
  const unlocked = new Map();
  let revision = 0;
  let busy = false;

  function requireEnabled() {
    if (!enabled) throw new GmailError(503, 'Gmail ist auf diesem CT noch nicht konfiguriert.');
  }

  function readToken() {
    try {
      const data = readFileSync(vault);
      const cipher = createDecipheriv('aes-256-gcm', key, data.subarray(0, 12));
      cipher.setAAD(binding);
      cipher.setAuthTag(data.subarray(12, 28));
      return Buffer.concat([cipher.update(data.subarray(28)), cipher.final()]).toString('utf8');
    } catch (error) {
      if (error.code === 'ENOENT') return null;
      throw new GmailError(503, 'Gmail-Verbindung kann nicht entschlüsselt werden. Schlüssel prüfen oder Verbindung neu einrichten.');
    }
  }

  function saveToken(token) {
    const iv = randomBytes(12);
    const cipher = createCipheriv('aes-256-gcm', key, iv);
    cipher.setAAD(binding);
    const encrypted = Buffer.concat([cipher.update(token, 'utf8'), cipher.final()]);
    const temporary = vault + '.tmp';
    writeFileSync(temporary, Buffer.concat([iv, cipher.getAuthTag(), encrypted]), { mode: 0o600 });
    renameSync(temporary, vault);
  }

  async function google(url, options) {
    let response;
    try {
      response = await request(url, { ...options, redirect: 'error', signal: AbortSignal.timeout(10000) });
    } catch {
      throw new GmailError(502, 'Google ist momentan nicht erreichbar.');
    }
    return response;
  }

  async function token(fields) {
    const response = await google('https://oauth2.googleapis.com/token', {
      method: 'POST',
      body: new URLSearchParams({ client_id: clientId, client_secret: clientSecret, ...fields }),
    });
    if (!response.ok) throw new GmailError(502, 'Google hat die Autorisierung abgelehnt. Prüfe die Verbindung und verbinde das Postfach bei Bedarf erneut.');
    return response.json();
  }

  async function query(access, path) {
    const response = await google(gmailBase + path, { headers: { Authorization: 'Bearer ' + access } });
    if (!response.ok) throw new GmailError(502, 'Gmail konnte die Anfrage nicht verarbeiten.');
    return response.json();
  }

  function status() {
    try { return { enabled, address: enabled ? address : '', connected: enabled && Boolean(readToken()) }; }
    catch { return { enabled, address, connected: false, unreadable: true }; }
  }

  function start(session) {
    requireEnabled();
    const state = randomBytes(32).toString('base64url');
    const browser = randomBytes(32).toString('base64url');
    const verifier = randomBytes(32).toString('base64url');
    const challenge = createHash('sha256').update(verifier).digest('base64url');
    transactions.clear();
    transactions.set(state, { browser, verifier, session, expires: Date.now() + 5 * 60000, revision });
    const url = new URL('https://accounts.google.com/o/oauth2/v2/auth');
    url.search = new URLSearchParams({ client_id: clientId, redirect_uri: redirectUri, response_type: 'code',
      scope, access_type: 'offline', prompt: 'consent', login_hint: address, state,
      code_challenge: challenge, code_challenge_method: 'S256' });
    return { url: url.href, cookie: transactionCookie + '=' + browser + '; HttpOnly; Secure; SameSite=Lax; Path=/api/gmail/callback; Max-Age=300' };
  }

  async function complete(parameters, cookies, isSessionActive) {
    requireEnabled();
    const state = parameters.get('state');
    const transaction = transactions.get(state);
    const browser = cookies?.match(/(?:^|;\s*)ttb_gmail=([A-Za-z0-9_-]+)(?:;|$)/)?.[1];
    if (!transaction || !equal(transaction.browser, browser)) throw new GmailError(403, 'Google-Anmeldung konnte nicht zugeordnet werden.');
    transactions.delete(state);
    const stillValid = () => transaction.expires >= Date.now() && transaction.revision === revision && isSessionActive(transaction.session);
    if (!stillValid() || !parameters.get('code')) throw new GmailError(403, 'Google-Anmeldung ist abgelaufen oder wurde abgebrochen.');
    const result = await token({ grant_type: 'authorization_code', code: parameters.get('code'),
      code_verifier: transaction.verifier, redirect_uri: redirectUri });
    if (!result.refresh_token || !result.access_token || !result.scope?.split(' ').includes(scope)) {
      throw new GmailError(502, 'Google hat den notwendigen Lesezugriff nicht freigegeben.');
    }
    const profile = await query(result.access_token, 'profile?fields=emailAddress');
    if (profile.emailAddress?.toLowerCase() !== address) throw new GmailError(403, 'Wähle das konfigurierte Bot-Postfach aus.');
    if (!stillValid()) throw new GmailError(403, 'Google-Anmeldung ist abgelaufen.');
    saveToken(result.refresh_token);
  }

  function unlock(session) {
    requireEnabled();
    unlocked.clear();
    unlocked.set(session, Date.now() + 2 * 60000);
  }

  function lock(session) {
    unlocked.delete(session);
    for (const [state, transaction] of transactions) {
      if (transaction.session === session) transactions.delete(state);
    }
  }

  function isRecent(message, labelId) {
    const received = Number(message.internalDate);
    return message.labelIds?.includes(labelId) && Number.isFinite(received) &&
      received > Date.now() - maxAge && received < Date.now() + 5 * 60000;
  }

  async function mailbox(session, id, isSessionActive) {
    requireEnabled();
    const canRead = () => (unlocked.get(session) ?? 0) > Date.now() && isSessionActive(session);
    if (!canRead()) throw new GmailError(403, 'Bitte bestätige erneut dein Admin-Passwort.');
    if (id !== undefined && (typeof id !== 'string' || !/^[a-z0-9_-]{1,80}$/i.test(id))) {
      throw new GmailError(400, 'Ungültige Nachricht.');
    }
    if (busy) throw new GmailError(429, 'Ein Abruf läuft bereits.');
    const refresh = readToken();
    if (!refresh) throw new GmailError(409, 'Verbinde zuerst dein Bot-Postfach.');
    const requestRevision = revision;
    busy = true;
    try {
      const { access_token: access } = await token({ grant_type: 'refresh_token', refresh_token: refresh });
      if (!access) throw new GmailError(502, 'Google hat keinen Zugriff erteilt.');
      const { labels } = await query(access, 'labels?fields=labels(id,name)');
      const label = labels?.find(({ name }) => name === labelName);
      if (!label) throw new GmailError(409, 'Gmail-Label fehlt. Lege das konfigurierte Label in Gmail an.');
      let result;
      if (id !== undefined) {
        const path = 'messages/' + encodeURIComponent(id);
        const metadata = await query(access, path + '?format=metadata&fields=id,internalDate,labelIds');
        if (!isRecent(metadata, label.id)) throw new GmailError(404, 'Nachricht gehört nicht zum Bot-Postfachbereich oder ist abgelaufen.');
        const message = await query(access, path + '?format=full&fields=id,internalDate,labelIds,snippet,payload');
        if (!isRecent(message, label.id)) throw new GmailError(404, 'Nachricht ist nicht mehr verfügbar.');
        result = { text: messageText(message) };
      } else {
        const params = new URLSearchParams({ labelIds: label.id, q: 'newer_than:2d', maxResults: '8', fields: 'messages(id)' });
        const { messages } = await query(access, 'messages?' + params);
        const list = await Promise.all((messages ?? []).slice(0, 8).map(({ id: messageId }) =>
          query(access, 'messages/' + encodeURIComponent(messageId) + '?format=metadata&fields=id,internalDate,labelIds')));
        result = { messages: list.filter((message) => isRecent(message, label.id))
          .map(({ id: messageId, internalDate }) => ({ id: messageId, received: Number(internalDate) })) };
      }
      if (!canRead() || requestRevision !== revision) throw new GmailError(403, 'Postfach ist wieder gesperrt.');
      return result;
    } finally { busy = false; }
  }

  async function disconnect() {
    requireEnabled();
    revision++;
    transactions.clear();
    unlocked.clear();
    let refresh;
    try { refresh = readToken(); } catch {}
    rmSync(vault, { force: true });
    if (!refresh) return true;
    try {
      const response = await google('https://oauth2.googleapis.com/revoke', {
        method: 'POST', body: new URLSearchParams({ token: refresh }),
      });
      return response.ok;
    } catch { return false; }
  }

  return { status, start, complete, unlock, lock, mailbox, disconnect };
}
