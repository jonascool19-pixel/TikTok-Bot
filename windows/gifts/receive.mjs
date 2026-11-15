import { TikTokLiveConnection, WebcastEvent, ControlEvent } from 'tiktok-live-connector';

const emit = message => process.stdout.write(JSON.stringify(message) + '\n');
const clean = value => typeof value === 'string' ? value.replace(/[\x00-\x1f\x7f]/g, ' ').trim().slice(0, 80) : '';
const nonzero = value => value && String(value) !== '0' ? String(value) : '';

export function parseGift(event, roomId, seen, catalog = new Map()) {
    if (nonzero(event.common?.roomId) && event.common.roomId !== roomId) return null;
    const type = event.gift?.type || event.giftDetails?.giftType || event.extendedGiftInfo?.type || catalog.get(event.giftId)?.type;
    if ((type === 1 || event.gift?.combo) && !Number(event.repeatEnd)) return null;
    const giftId = nonzero(event.giftId);
    const id = nonzero(event.orderId) || nonzero(event.common?.msgId);
    if (!giftId || id && seen.has(id)) return null;
    if (id) {
        seen.add(id);
        if (seen.size > 1024) seen.delete(seen.values().next().value);
    }
    return {
        kind: 'gift',
        name: clean(event.user?.nickname) || clean(event.user?.displayId) || 'Ein Zuschauer',
        title: clean(event.gift?.name) || clean(event.giftDetails?.giftName) ||
            clean(event.extendedGiftInfo?.name) || clean(catalog.get(giftId)?.name) || 'Geschenk',
        count: Math.min(9999, Math.max(1, Number(event.repeatCount) || 1))
    };
}

export function parseAudience(event, roomId) {
    if (nonzero(event.common?.roomId) && String(event.common.roomId) !== String(roomId)) return null;
    const value = event.total;
    if (typeof value !== 'number' && (typeof value !== 'string' || !/^\d+$/.test(value))) return null;
    const count = Number(value);
    return Number.isSafeInteger(count) && count >= 0 ? { kind: 'audience', count } : null;
}

async function run(handle) {
    if (!/^[a-zA-Z0-9_.]{1,64}$/.test(handle)) process.exit(2);
    const connection = new TikTokLiveConnection(handle, {
        processInitialData: false,
        authenticateWs: false
    });
    const seen = new Set();
    const catalog = new Map();
    let connected = false;
    const timeout = setTimeout(() => { finish('timeout'); }, 40000);
    const finish = kind => process.stdout.write(JSON.stringify({ kind }) + '\n', () => process.exit(kind === 'error' ? 1 : 0));
    let reportedError = false;
    process.stdin.resume();
    process.stdin.on('end', () => process.exit());
    connection.on(ControlEvent.ERROR, () => {
        if (connected && !reportedError) { reportedError = true; emit({ kind: 'error' }); }
    });
    connection.on(ControlEvent.DISCONNECTED, () => { if (connected) finish('disconnected'); });
    connection.on(WebcastEvent.STREAM_END, () => { if (connected) finish('disconnected'); });
    connection.on(WebcastEvent.ROOM_USER, event => {
        if (!connected) return;
        const audience = parseAudience(event, connection.roomId);
        if (audience) emit(audience);
    });
    connection.on(WebcastEvent.GIFT, event => {
        if (!connected) return;
        const gift = parseGift(event, connection.roomId, seen, catalog);
        if (gift) emit(gift);
    });
    try {
        await connection.connect();
        connected = true;
        emit({ kind: 'connected' });
        connection.fetchAvailableGifts().then(gifts => {
            if (Array.isArray(gifts))
                for (const gift of gifts) catalog.set(String(gift.id), gift);
        }).catch(() => emit({ kind: 'catalog-error' }));
    } catch (error) {
        finish(error?.name === 'UserOfflineError' || error?.constructor?.name === 'UserOfflineError' ? 'offline' : 'error');
        process.exitCode = 1;
    } finally {
        clearTimeout(timeout);
    }
}

if (process.argv[2]) await run(process.argv[2]);
