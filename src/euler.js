import { randomBytes } from 'node:crypto';

export function makeEuler(env = process.env, request = fetch) {
  const { BOT_EULER_CLIENT_ID: client_id, BOT_EULER_CLIENT_SECRET: client_secret,
    BOT_EULER_REDIRECT_URI: redirect_uri, BOT_EULER_VAULT_KEY: key } = env;
  let uri;
  try { uri = new URL(redirect_uri); } catch {}
  const enabled = Boolean(env.BOT_SECURE_COOKIE === '1' && client_id && client_secret &&
    /^[0-9a-f]{64}$/i.test(key ?? '') && uri?.protocol === 'https:' &&
    uri.pathname === '/api/tiktok/euler/callback' && !uri.search && !uri.hash && !uri.username && !uri.password);
  let pending;
  async function call(path, body, access) {
    const response = await request('https://api.eulerstream.com' + path, {
      method: body ? 'POST' : 'GET', redirect: 'error', signal: AbortSignal.timeout(12000),
      headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...(access ? { 'x-oauth-token': access } : {}) },
      body: body && JSON.stringify(body),
    });
    if (!response.ok) throw Object.assign(new Error('Euler Stream hat die Anfrage abgelehnt'), { code: response.status });
    return response.json();
  }
  function start(session) {
    const state = randomBytes(32).toString('base64url');
    const browser = randomBytes(32).toString('base64url');
    pending = { state, browser, session, expires: Date.now() + 300000 };
    const url = new URL('https://www.eulerstream.com/tiktok/oauth/authorize');
    url.search = new URLSearchParams({ client_id, redirect_uri, response_type: 'code', scope: 'user:info webcast:chat', state });
    return { url: url.href, browser };
  }
  async function exchange(fields) {
    const { data } = await call('/tiktok/oauth/token', { client_id, client_secret, ...fields });
    if (!data?.access_token || !data.refresh_token || !(Number(data.expires_in) > 0)) throw new Error('Euler-Token fehlt');
    return { access: data.access_token, refresh: data.refresh_token,
      expires: Date.now() + Number(data.expires_in) * 1000, scopes: data.scopes };
  }
  async function complete(params, browser, active) {
    const entry = pending;
    const valid = () => entry && entry === pending && entry.expires > Date.now() && active(entry.session);
    if (!enabled || !valid() || entry.browser !== browser || entry.state !== params.get('state') ||
        !params.get('code') || entry.used) throw new Error('Anmeldung ist abgelaufen');
    entry.used = true;
    const token = await exchange({ grant_type: 'authorization_code', code: params.get('code'), redirect_uri });
    if (!token.scopes?.includes('webcast:chat') || !token.scopes?.includes('user:info')) throw new Error('Versandfreigabe fehlt');
    const { user } = await call('/tiktok/oauth/userinfo', undefined, token.access);
    if (!valid() || !/^[a-z0-9_.]{1,64}$/i.test(user?.uniqueId ?? '')) throw new Error('Bot-Konto konnte nicht bestätigt werden');
    pending = undefined;
    return { token, username: user.uniqueId, displayName: String(user.nickName ?? '').slice(0, 100) };
  }
  async function send(room, text, token, update) {
    if (!/^\d+$/.test(room)) throw new Error('Ungültige LIVE-Raum-ID');
    if (token.expires < Date.now() + 30000) {
      token = await exchange({ grant_type: 'refresh_token', refresh_token: token.refresh });
      update(token);
    }
    const result = await call('/webcast/rooms/' + room + '/chat', { content: text }, token.access);
    if (result.data?.status_code !== 0) throw new Error('TikTok hat die Nachricht nicht bestätigt');
  }
  return { enabled, key, start, complete, send };
}
