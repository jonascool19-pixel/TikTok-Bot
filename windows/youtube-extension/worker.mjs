import { runMusicCommand } from './player.mjs';

const url = 'https://music.youtube.com/';
let pipe;
let preferredTab;

function connect() {
    if (pipe) return;
    const connection = chrome.runtime.connectNative('com.tiktokbot.youtube_music');
    pipe = connection;
    connection.onDisconnect.addListener(() => {
        void chrome.runtime.lastError;
        if (pipe === connection) pipe = null;
    });
    connection.onMessage.addListener(async message => {
        let reply = { state: 'error' };
        try {
            if (message.idle) { connection.postMessage({ ready: true }); return; }
            reply = await execute(message);
        } catch {
            reply = { state: 'error' };
        }
        if (pipe === connection) {
            connection.postMessage(reply);
            connection.postMessage({ ready: true });
        }
    });
    connection.postMessage({ ready: true });
}

async function execute(command) {
    const tabs = await chrome.tabs.query({ url: 'https://music.youtube.com/*' });
    const tab = tabs.find(item => item.id === preferredTab) || tabs.find(item => item.audible) ||
        tabs.find(item => item.active) || tabs[0];
    if (!tab) return { state: 'no-tab' };
    const response = await chrome.scripting.executeScript({
        target: { tabId: tab.id }, world: 'MAIN', func: runMusicCommand, args: [command]
    });
    const result = response[0]?.result || { state: 'error' };
    if (result.state === 'start' && command.action === 'queue' && /^[\w-]{11}$/.test(command.value)) {
        await chrome.tabs.update(tab.id, { url: url + 'watch?v=' + command.value });
        return { state: 'starting' };
    }
    return result;
}

chrome.action.onClicked.addListener(async tab => {
    if (tab.url?.startsWith(url)) preferredTab = tab.id;
    else preferredTab = (await chrome.tabs.create({ url })).id;
    connect();
});
chrome.runtime.onInstalled.addListener(() => { chrome.alarms.create('reconnect', { periodInMinutes: 0.5 }); connect(); });
chrome.runtime.onStartup.addListener(connect);
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === 'reconnect') connect(); });
connect();
