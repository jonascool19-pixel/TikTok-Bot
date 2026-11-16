import assert from 'node:assert/strict';
import { runMusicCommand } from './youtube-music-player.mjs';

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
const video = { readyState: 4, paused: true, async play() { this.paused = false; } };
globalThis.location.search = '?v=' + videoId;
globalThis.document.querySelector = selector => selector === 'ytmusic-app' ? app : video;
assert.deepEqual(await runMusicCommand({ action: 'play-current', value: videoId }), { state: 'playing' });
video.paused = true;
video.play = async () => { throw { name: 'NotAllowedError' }; };
assert.deepEqual(await runMusicCommand({ action: 'play-current', value: videoId }), { state: 'autoplay-blocked' });
globalThis.location = { origin: 'https://example.com' };
assert.deepEqual(await runMusicCommand({ action: 'status' }), { state: 'wrong-site' });
