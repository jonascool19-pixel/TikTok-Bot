export async function runMusicCommand(command) {
    if (location.origin !== 'https://music.youtube.com') return { state: 'wrong-site' };
    const app = document.querySelector('ytmusic-app');
    const network = app?.networkManager;
    if (!network) return { state: 'loading' };

    if (command.action === 'status') {
        const flag = globalThis.ytcfg?.get?.('LOGGED_IN');
        return { state: 'ready', account: typeof flag === 'boolean' ? (flag ? 'signed-in' : 'signed-out') : null };
    }
    if (command.action === 'search') {
        const query = typeof command.value === 'string' ? command.value.trim() : '';
        if (!query || query.length > 250) return { state: 'invalid' };
        const response = await network.fetch('/search', { query });
        const tracks = new Map();
        const scan = node => {
            if (!node || typeof node !== 'object' || tracks.size >= 12) return;
            const item = node.musicResponsiveListItemRenderer;
            if (item) {
                const id = item.playlistItemData?.videoId;
                const title = item.flexColumns?.[0]?.musicResponsiveListItemFlexColumnRenderer?.text?.runs
                    ?.map(run => run.text).join('').trim();
                if (/^[\w-]{11}$/.test(id || '') && title && !tracks.has(id)) tracks.set(id, { id, title: title.slice(0, 150) });
            }
            for (const value of Object.values(node)) scan(value);
        };
        scan(response?.contents);
        return { state: 'ok', tracks: [...tracks.values()] };
    }
    if (command.action === 'queue') {
        const id = command.value;
        if (typeof id !== 'string' || !/^[\w-]{11}$/.test(id)) return { state: 'invalid' };
        const queue = [...document.querySelectorAll('#queue, ytmusic-player-queue')]
            .find(element => element.queue?.store?.store && typeof element.dispatch === 'function');
        const store = queue?.queue.store.store;
        const before = store?.getState()?.queue;
        if (!before?.items?.length) return { state: 'start' };
        const response = await network.fetch('/music/get_queue', {
            queueContextParams: before.queueContextParams,
            queueInsertPosition: 'INSERT_AT_END',
            videoIds: [id]
        });
        const items = response?.queueDatas?.map(entry => entry?.content).filter(Boolean);
        if (!items?.length) return { state: 'no-track' };
        const current = store.getState()?.queue;
        if (!current || !Array.isArray(current.items)) return { state: 'loading' };
        const contains = list => list.filter(item =>
            (item.playlistPanelVideoRenderer || item.playlistPanelVideoWrapperRenderer?.primaryRenderer?.playlistPanelVideoRenderer)?.videoId === id).length;
        const expected = contains(current.items);
        queue.dispatch({
            type: 'ADD_ITEMS',
            payload: {
                nextQueueItemId: current.nextQueueItemId,
                index: current.items.length,
                items,
                shuffleEnabled: false,
                shouldAssignIds: true
            }
        });
        for (let tries = 0; tries < 10; tries++) {
            if (contains(store.getState()?.queue?.items || []) > expected) return { state: 'ok' };
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        return { state: 'not-confirmed' };
    }
    const selectors = {
        'toggle-play': 'ytmusic-player-bar #play-pause-button',
        next: 'ytmusic-player-bar .next-button',
        previous: 'ytmusic-player-bar .previous-button'
    };
    const selector = selectors[command.action];
    if (!selector || typeof selector !== 'string') return { state: 'invalid' };
    const button = document.querySelector(selector);
    if (!button || button.disabled || button.getAttribute('aria-disabled') === 'true') return { state: 'no-player' };
    button.click();
    return { state: 'ok' };
}
