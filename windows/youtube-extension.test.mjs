import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { runMusicCommand } from './youtube-extension/player.mjs';

const readJson = async path => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
const extension = await readJson('./youtube-extension/manifest.json');
const host = await readJson('./youtube-native-host.json');
const id = [...createHash('sha256').update(Buffer.from(extension.key, 'base64')).digest('hex').slice(0, 32)]
    .map(digit => String.fromCharCode(97 + parseInt(digit, 16))).join('');
assert.deepEqual(host.allowed_origins, ['chrome-extension://' + id + '/']);

const videoId = 'abcdefghijk';
const entry = { playlistPanelVideoRenderer: { videoId } };
const queue = { nextQueueItemId: 2, items: [entry] };
const queries = [];
const app = { networkManager: { async fetch(route, params) {
    queries.push([route, params]);
    if (route === '/music/get_queue') return { queueDatas: [{ content: entry }] };
    const item = { playlistItemData: { videoId }, flexColumns: [
        { musicResponsiveListItemFlexColumnRenderer: { text: { runs: [{ text: 'Testtitel' }] } } }
    ] };
    const row = { musicResponsiveListItemRenderer: item };
    return { contents: [row, row] };
} } };
const element = {
    queue: { store: { store: { getState: () => ({ queue }) } } },
    dispatch(action) { queue.items.push(...action.payload.items); }
};
globalThis.document = {
    querySelector: selector => selector === 'ytmusic-app' ? app : null,
    querySelectorAll: () => [element]
};
globalThis.location = { origin: 'https://music.youtube.com' };
globalThis.ytcfg = { get: () => true };
assert.deepEqual(await runMusicCommand({ action: 'status' }), { state: 'ready', account: 'signed-in' });
assert.deepEqual(await runMusicCommand({ action: 'search', value: ' Testtitel ' }),
    { state: 'ok', tracks: [{ id: videoId, title: 'Testtitel' }] });
assert.deepEqual(await runMusicCommand({ action: 'queue', value: videoId }), { state: 'ok' });
assert.deepEqual(queries.map(([route]) => route), ['/search', '/music/get_queue']);
queue.items.length = 0;
assert.deepEqual(await runMusicCommand({ action: 'queue', value: videoId }), { state: 'start' });
assert.deepEqual(await runMusicCommand({ action: 'next' }), { state: 'no-player' });
globalThis.location = { origin: 'https://example.com' };
assert.deepEqual(await runMusicCommand({ action: 'status' }), { state: 'wrong-site' });
