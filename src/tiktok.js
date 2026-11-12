import { SignConfig, TikTokLiveConnection, UserOfflineError, WebcastEvent } from 'tiktok-live-connector';
import EulerStreamApiClient from 'tiktok-live-api-sdk';

const identity = (user) => ({
  userId: String(user?.id ?? ''),
  handle: user?.displayId ?? '',
  name: user?.nickname || user?.displayId || '',
});

export function makeTikTok({ event, status, log }) {
  let connection;
  let sender;
  let timer;
  let revision = 0;
  let serial = Promise.resolve();
  let online = false;
  let lastSend = 0;
  let sending = false;

  function configure(config, secret) {
    const run = ++revision;
    online = false;
    clearTimeout(timer);
    const previous = connection;
    connection = undefined;
    sender = undefined;
    status(config.creator ? 'Verbinde' : 'Einrichtung erforderlich');

    function schedule(delay) {
      if (run === revision) {
        clearTimeout(timer);
        timer = setTimeout(connect, delay);
        timer.unref();
      }
    }

    async function connect() {
      if (run !== revision) return;
      try {
        if (!connection) {
          const session = config.sendEnabled && config.bot && secret.sessionId && secret.targetIdc
            ? { cookie: { type: 'cookie', value: { sessionId: secret.sessionId, ttTargetIdc: secret.targetIdc } } }
            : undefined;
          const { basePath, baseOptions } = SignConfig;
          const eulerApiInstance = new EulerStreamApiClient({ basePath, baseOptions, apiKey: secret.providerKey || undefined });
          const client = new TikTokLiveConnection(config.creator, { processInitialData: false, eulerApiInstance });
          connection = client;
          if (session) sender = new TikTokLiveConnection(config.creator, { session, eulerApiInstance });
          const emit = (data) => {
            if (run === revision && online) event(data);
          };
          client.on(WebcastEvent.CHAT, (value) => emit({ type: 'comment', ...identity(value.user), text: value.content }));
          client.on(WebcastEvent.MEMBER, (value) => emit({ type: 'join', ...identity(value.user) }));
          client.on(WebcastEvent.GIFT, (value) => emit({
            type: 'gift', ...identity(value.user), gift: value.gift?.name,
            count: Number(value.repeatCount ?? 1),
            streakPending: value.gift?.type === 1 && Number(value.repeatEnd) !== 1,
          }));
          client.on('error', ({ exception }) => {
            if (run === revision && !(exception instanceof UserOfflineError)) log('tiktok', 'Ereignisfehler', exception);
          });
          const disconnected = () => {
            if (run !== revision || !online) return;
            online = false;
            status('Getrennt. Neuer Versuch folgt.');
            schedule(30000);
          };
          client.on('disconnected', disconnected);
          client.on(WebcastEvent.STREAM_END, disconnected);
        }
        const client = connection;
        await client.connect();
        if (run !== revision) {
          await client.disconnect();
          return;
        }
        online = true;
        status('LIVE verbunden');
      } catch (error) {
        if (run !== revision) return;
        const offline = error instanceof UserOfflineError;
        status(offline ? 'Kanal offline' : 'Verbindung fehlgeschlagen. Neuer Versuch folgt.');
        if (!offline) log('tiktok', 'Verbindung fehlgeschlagen', error);
        schedule(offline ? 60000 : 90000);
      }
    }

    serial = serial.catch(() => {}).then(async () => {
      if (previous) await Promise.resolve(previous.disconnect()).catch(() => {});
      if (run === revision && config.creator) schedule(0);
    });
  }

  async function send(text) {
    if (!online || !connection?.roomId || !sender) throw new Error('Kein verbundener LIVE oder Bot-Absender');
    if (sending || Date.now() - lastSend < 6000) throw new Error('Sendepause aktiv');
    lastSend = Date.now();
    sending = true;
    try {
      const response = await sender.sendMessage(text, connection.roomId);
      if (response.data?.status_code !== 0) {
        const error = new Error('TikTok hat die Nachricht nicht bestätigt');
        error.code = response.data?.status_code ?? response.code;
        throw error;
      }
    } finally {
      sending = false;
    }
  }

  return { configure, send, online: () => online };
}
