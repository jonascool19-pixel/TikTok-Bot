import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { makeGmail } from '../src/gmail.js';

const scope = 'https://www.googleapis.com/auth/gmail.readonly';
const env = {
  BOT_SECURE_COOKIE: '1',
  BOT_GMAIL_CLIENT_ID: 'fixture-client',
  BOT_GMAIL_CLIENT_SECRET: 'fixture-secret',
  BOT_GMAIL_REDIRECT_URI: 'https://bot.example.net/api/gmail/callback',
  BOT_GMAIL_ADDRESS: 'bot-fixture@gmail.com',
  BOT_GMAIL_KEY: 'a3'.repeat(32),
};
const redirect = (state, code = 'fixture-code') => new URLSearchParams({ state, code });

test('OAuth binds to the initiating browser and configured mailbox; token is encrypted', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'ttb-gmail-'));
  let emailAddress = 'other-fixture@gmail.com';
  let expectedChallenge;
  const mock = async (href, options) => {
    const { pathname } = new URL(href);
    if (pathname === '/token') {
      assert.equal(options.body.get('grant_type'), 'authorization_code');
      assert.equal(options.body.get('redirect_uri'), env.BOT_GMAIL_REDIRECT_URI);
      assert.equal(createHash('sha256').update(options.body.get('code_verifier')).digest('base64url'), expectedChallenge);
      return Response.json({ scope, access_token: 'mock-access', refresh_token: 'mock-refresh-private' });
    }
    if (pathname.endsWith('/profile')) return Response.json({ emailAddress });
    assert.fail('Unexpected endpoint');
  };
  const mailbox = makeGmail(directory, env, mock);
  let transaction = mailbox.start('admin-session');
  let google = new URL(transaction.url);
  let state = google.searchParams.get('state');
  expectedChallenge = google.searchParams.get('code_challenge');
  assert.equal(google.origin, 'https://accounts.google.com');
  assert.equal(google.searchParams.get('scope'), scope);
  assert.match(transaction.cookie, /HttpOnly; Secure; SameSite=Lax/);
  await assert.rejects(mailbox.complete(redirect(state), '', () => true), { statusCode: 403 });
  await assert.rejects(mailbox.complete(redirect(state), transaction.cookie, () => true), /konfigurierte Bot-Postfach/u);
  assert.equal(mailbox.status().connected, false);

  emailAddress = env.BOT_GMAIL_ADDRESS;
  transaction = mailbox.start('admin-session');
  google = new URL(transaction.url);
  state = google.searchParams.get('state');
  expectedChallenge = google.searchParams.get('code_challenge');
  await mailbox.complete(redirect(state), transaction.cookie, (id) => id === 'admin-session');
  assert.equal(mailbox.status().connected, true);
  assert.ok(!readFileSync(join(directory, 'gmail.enc')).includes('mock-refresh-private'));
  assert.equal(makeGmail(directory, env, mock).status().connected, true);
  assert.equal(makeGmail(directory, { ...env, BOT_GMAIL_KEY: '12'.repeat(32) }, mock).status().unreadable, true);
  await assert.rejects(mailbox.complete(redirect(state), transaction.cookie, () => true), { statusCode: 403 });
});

test('fetches only recent labeled messages after unlocking and revokes access on disconnect', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'ttb-gmail-'));
  const calls = [];
  const mock = async (href, options) => {
    const url = new URL(href);
    calls.push(url);
    const path = url.pathname;
    if (path === '/token') {
      if (options.body.get('grant_type') === 'refresh_token') {
        assert.equal(options.body.get('refresh_token'), 'mock-refresh-private');
      }
      return Response.json({ scope, access_token: 'mock-access', refresh_token: 'mock-refresh-private' });
    }
    if (path === '/revoke') {
      assert.equal(options.body.get('token'), 'mock-refresh-private');
      return new Response();
    }
    if (path.endsWith('/profile')) return Response.json({ emailAddress: env.BOT_GMAIL_ADDRESS });
    if (path.endsWith('/labels')) return Response.json({ labels: [{ id: 'Label_123', name: 'TikTok-Bot' }] });
    if (path.endsWith('/messages')) {
      assert.equal(url.searchParams.get('labelIds'), 'Label_123');
      return Response.json({ messages: [{ id: 'recent' }, { id: 'old' }, { id: 'unlabeled' }] });
    }
    const id = path.split('/').at(-1);
    const internalDate = String(Date.now() - (id === 'old' ? 3 * 86400000 : 30000));
    const metadata = { id, internalDate, labelIds: id === 'unlabeled' ? ['INBOX'] : ['Label_123'] };
    if (url.searchParams.get('format') === 'metadata') return Response.json(metadata);
    assert.equal(id, 'recent');
    return Response.json({ ...metadata, payload: {
      mimeType: 'text/html', headers: [{ name: 'Subject', value: 'TikTok-Test' }],
      body: { data: Buffer.from('<p>Code: <strong>123456</strong></p><script>secretScript</script>').toString('base64url') },
    } });
  };
  const mailbox = makeGmail(directory, env, mock);
  const transaction = mailbox.start('admin-session');
  const state = new URL(transaction.url).searchParams.get('state');
  await mailbox.complete(redirect(state), transaction.cookie, () => true);
  await assert.rejects(mailbox.mailbox('admin-session', undefined, () => true), { statusCode: 403 });
  mailbox.unlock('admin-session');
  const { messages } = await mailbox.mailbox('admin-session', undefined, () => true);
  assert.deepEqual(messages.map(({ id }) => id), ['recent']);
  assert.ok(!JSON.stringify(messages).includes('123456'));
  await assert.rejects(mailbox.mailbox('admin-session', 'unlabeled', () => true), { statusCode: 404 });
  const { text } = await mailbox.mailbox('admin-session', 'recent', () => true);
  assert.match(text, /TikTok-Test\n\nCode: 123456/u);
  assert.doesNotMatch(text, /secretScript/u);
  assert.equal(calls.filter((url) => url.searchParams.get('format') === 'full').length, 1);
  mailbox.lock('admin-session');
  await assert.rejects(mailbox.mailbox('admin-session', 'recent', () => true), { statusCode: 403 });
  assert.equal(await mailbox.disconnect(), true);
  assert.equal(mailbox.status().connected, false);
  assert.equal(makeGmail(directory, { ...env, BOT_SECURE_COOKIE: '0' }).status().enabled, false);
});
