import { TikTokLiveConnection, WebcastEvent, ControlEvent } from 'tiktok-live-connector';

const emit = message => process.stdout.write(JSON.stringify(message) + '\n');
const clean = value => typeof value === 'string' ? value.replace(/[\x00-\x1f\x7f]/g, ' ').trim().slice(0, 80) : '';
const nonzero = value => value && String(value) !== '0' ? String(value) : '';

export function parseGift(event, roomId, seen, catalog = new Map()) {
    if (nonzero(event.common?.roomId) && String(event.common.roomId) !== String(roomId)) return null;
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
    const count = asCount(event.viewerCount ?? event.userCount ?? event.total);
    const total = asCount(event.totalUser);
    return count === null && total === null ? null : { kind: 'audience', count, total };
}

function asCount(value) {
    if (typeof value !== 'number' && (typeof value !== 'string' || !/^\d+$/.test(value))) return null;
    const count = Number(value);
    return Number.isSafeInteger(count) && count >= 0 && count <= 2147483647 ? count : null;
}

export function roomAudience(info) {
    const room = info?.data ?? info;
    if (!room || Number(room.status) === 4) return null;
    const count = asCount(room.user_count ?? room.userCount ?? room.stats?.user_count);
    const total = asCount(room.total_user ?? room.stats?.total_user);
    return count === null && total === null ? null : { kind: 'audience', count, total };
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
    let audienceTimer;
    let lastAudience = 0;
    let checkingAudience = false;
    const timeout = setTimeout(() => { finish('timeout'); }, 40000);
    const finish = kind => {
        connected = false;
        clearInterval(audienceTimer);
        process.stdout.write(JSON.stringify({ kind }) + '\n', () => process.exit(kind === 'error' ? 1 : 0));
    };
    const publishAudience = audience => {
        if (!audience || !connected) return;
        if (audience.count !== null) lastAudience = Date.now();
        emit(audience);
    };
    let reportedError = false;
    process.stdin.resume();
    process.stdin.on('end', () => process.exit());
    connection.on(ControlEvent.ERROR, () => {
        if (connected && !reportedError) { reportedError = true; emit({ kind: 'error' }); }
    });
    connection.on(ControlEvent.DISCONNECTED, () => { if (connected) finish('disconnected'); });
    connection.on(WebcastEvent.STREAM_END, () => { if (connected) finish('ended'); });
    connection.on(WebcastEvent.ROOM_USER, event => {
        if (!connected) return;
        publishAudience(parseAudience(event, connection.roomId));
    });
    connection.on(WebcastEvent.GIFT, event => {
        if (!connected) return;
        const gift = parseGift(event, connection.roomId, seen, catalog);
        if (gift) emit(gift);
    });
    try {
        const state = await connection.connect();
        connected = true;
        const info = state?.roomInfo ?? connection.roomInfo;
        const started = asCount((info?.data ?? info)?.create_time);
        emit({ kind: 'connected', roomId: nonzero(connection.roomId), started });
        publishAudience(roomAudience(info));
        audienceTimer = setInterval(async () => {
            if (!connected || checkingAudience || Date.now() - lastAudience < 45000) return;
            checkingAudience = true;
            try {
                const snapshot = await connection.fetchRoomInfo();
                if (connected && Number((snapshot?.data ?? snapshot)?.status) === 4) finish('ended');
                else publishAudience(roomAudience(snapshot));
            } catch {
                // The WebView can still provide a count when a room snapshot is unavailable.
            } finally { checkingAudience = false; }
        }, 45000);
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
