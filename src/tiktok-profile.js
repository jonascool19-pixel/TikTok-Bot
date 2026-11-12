import { randomBytes } from 'node:crypto';

export function makeTikTokProfile(env = process.env, request = fetch) {
  const { BOT_SECURE_COOKIE, BOT_TIKTOK_CLIENT_KEY: key, BOT_TIKTOK_CLIENT_SECRET: secret,
    BOT_TIKTOK_REDIRECT_URI: callback } = env;
  let uri;
  try { uri = new URL(callback); } catch {}
  const enabled = Boolean(BOT_SECURE_COOKIE === '1' && key && secret && uri?.protocol === 'https:' &&
    uri.pathname === '/api/tiktok/profile/callback' && !uri.search && !uri.hash && !uri.username && !uri.password);
  let transaction;

  function start(session) {
    const state = randomBytes(32).toString('base64url');
    const browser = randomBytes(32).toString('base64url');
    transaction = { state, browser, session, expires: Date.now() + 300000 };
    const url = new URL('https://www.tiktok.com/v2/auth/authorize/');
    url.search = new URLSearchParams({ client_key: key, redirect_uri: callback, response_type: 'code',
      scope: 'user.info.basic,user.info.profile', state });
    return { url: url.href, browser };
  }

  async function complete(params, browser, active) {
    const current = transaction;
    const valid = () => current && current === transaction && current.expires >= Date.now() && active(current.session);
    if (!enabled || !valid() || current.state !== params.get('state') || current.browser !== browser || !params.get('code')) {
      throw new Error('TikTok-Autorisierung fehlt oder ist abgelaufen');
    }
    const code = params.get('code');
    if (current.used) throw new Error('TikTok-Autorisierung bereits verwendet');
    current.used = true;
    const options = () => ({ redirect: 'error', signal: AbortSignal.timeout(10000) });
    const token = await request('https://open.tiktokapis.com/v2/oauth/token/', {
      ...options(), method: 'POST', body: new URLSearchParams({ client_key: key, client_secret: secret,
        code, grant_type: 'authorization_code', redirect_uri: callback }),
    });
    if (!token.ok) throw new Error('TikTok hat die Autorisierung abgelehnt');
    const access = (await token.json()).access_token;
    if (!access) throw new Error('TikTok hat kein Zugriffstoken geliefert');
    const profile = await request('https://open.tiktokapis.com/v2/user/info/?fields=username,display_name', {
      ...options(), headers: { Authorization: 'Bearer ' + access },
    });
    if (!profile.ok) throw new Error('TikTok-Profil konnte nicht gelesen werden');
    const { data, error } = await profile.json();
    if (!valid() || error?.code !== 'ok' || !/^[a-z0-9_.]{1,64}$/i.test(data?.user?.username ?? '')) {
      throw new Error('TikTok-Benutzername konnte nicht bestätigt werden');
    }
    transaction = undefined;
    return { username: data.user.username, displayName: String(data.user.display_name ?? '').slice(0, 100) };
  }
  return { enabled, start, complete };
}
