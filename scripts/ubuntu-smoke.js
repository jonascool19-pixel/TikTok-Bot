import assert from 'node:assert/strict';

const url = process.argv[2];
const initial = process.env.BOT_TEST_PASSWORD;
assert.ok(initial && url);
let cookie = '';

async function call(route, payload, expected = 200, headers = {}) {
  const response = await fetch(url + route, {
    method: payload === undefined ? 'GET' : 'POST',
    headers: { Cookie: cookie, ...(payload === undefined ? {} : {
      'Content-Type': 'application/json', 'X-Bot-Request': '1',
    }), ...headers },
    body: payload === undefined ? undefined : JSON.stringify(payload),
  });
  assert.equal(response.status, expected, route);
  if (response.headers.has('set-cookie')) cookie = response.headers.get('set-cookie').split(';')[0];
  return response.json();
}

assert.deepEqual(await call('/health'), { ready: true, service: 'tiktok-bot' });
assert.match(await (await fetch(url)).text(), /TikTok-Bot/u);
assert.match(await (await fetch(url + '/player')).text(), /LIVE-Wiedergabe/u);
await call('/api/state', undefined, 401);
await call('/api/gmail/status', undefined, 401);
await call('/api/gmail/callback', undefined, 403);
await call('/api/login', { username: 'admin', password: initial }, 403, { 'X-Bot-Request': 'invalid' });
await call('/api/login', { username: 'admin', password: 'intentionally-incorrect' }, 401);
assert.equal((await call('/api/login', { username: 'admin', password: initial })).firstLogin, true);
await call('/api/settings', {}, 403);
await call('/api/password', { current: initial, password: 'abc12' }, 400);
await call('/api/password', { current: initial, password: 'abc123' });
await call('/api/logout', {});
assert.equal((await call('/api/login', { username: 'admin', password: 'abc123' })).firstLogin, false);

const state = await call('/api/state');
assert.deepEqual(await call('/api/gmail/status'), { enabled: false, address: '', connected: false });
await call('/api/gmail/unlock', { password: 'abc123' }, 503);
assert.equal(state.config.voice, 'de');
for (const voice of Object.keys(state.voices)) {
  for (const style of (voice.includes('-') ? ['standard'] : state.styles)) {
    const result = await call('/api/simulate', { type: 'gift', config: { ...state.config, voice, style } });
    const wav = Buffer.from(result.audio, 'base64');
    assert.equal(wav.toString('ascii', 0, 4), 'RIFF');
    assert.equal(wav.toString('ascii', 8, 12), 'WAVE');
    assert.ok(wav.length > 1000);
    assert.match(result.actions[0].text, /3 × Rose/u);
  }
}
const sound = await call('/api/simulate', { type: 'comment' });
const clip = await call('/api/clip', { name: 'Smoke-Test', mime: 'audio/wav', bytes: sound.audio });
const config = { ...state.config, voice: 'en', rules: {
  ...state.config.rules, join: { enabled: true, clipId: clip.id, chatTemplate: 'Hallo {name}!' },
} };
await call('/api/settings', { config, secrets: { providerKey: 'integration-secret-never-in-log' } });
const preview = await call('/api/simulate', { type: 'join', player: true });
assert.deepEqual(preview.actions, [{ type: 'clip', clipId: clip.id }, { type: 'chat', text: 'Hallo Testgast!' }]);
assert.equal(preview.audio, sound.audio);
const playback = await call('/api/audio');
assert.equal(playback.clips.length, 1);
const stream = await fetch(url + '/api/audio/' + playback.clips[0].id, { headers: { Cookie: cookie } });
assert.equal(Buffer.from(await stream.arrayBuffer()).toString('base64'), sound.audio);
await call('/api/audio/clear', {});
assert.equal((await call('/api/audio')).clips.length, 0);
const password = 'integration-backup-passphrase';
await call('/api/backup/export', { password: 'abc123' }, 400);
const { backup } = await call('/api/backup/export', { password, includeSecrets: true });
assert.ok(!Buffer.from(backup, 'base64').includes('integration-secret-never-in-log'));
await call('/api/clip/delete', { id: clip.id });
await call('/api/backup/import', { password: 'intentionally-incorrect', backup }, 400);
assert.equal((await call('/api/state')).clips.length, 0);
await call('/api/backup/import', { password, backup });
const restored = await call('/api/state');
assert.equal(restored.config.voice, 'en');
assert.equal(restored.config.sendEnabled, false);
assert.equal(restored.clips[0].id, clip.id);
assert.equal((await call('/api/simulate', { type: 'join' })).audio, sound.audio);
await call('/api/send', { text: 'Offline' }, 503);
const logs = await call('/api/log');
for (const area of ['anmeldung', 'backup', 'chat']) assert.ok(logs.some((entry) => entry.area === area));
for (const secret of [initial, password, 'integration-secret-never-in-log']) assert.ok(!JSON.stringify(logs).includes(secret));
await call('/api/logout', {});
await call('/api/state', undefined, 401);
console.log('Dashboard, audio, backup, restore, login and failure logging passed.');
