export const voices = {
  de: 'Deutsch', en: 'Englisch', fr: 'Französisch', es: 'Spanisch', it: 'Italienisch',
};
const naturalVoiceIds = ['de_DE-thorsten-medium', 'de_DE-kerstin-low', 'en_US-amy-low'];
export const styles = { standard: '', tief: '+m3', hell: '+f3', roboter: '+robot' };
const types = ['comment', 'gift', 'join'];
const defaults = {
  comment: { enabled: true, speak: true },
  gift: { enabled: true, speak: true },
  join: { enabled: false, speak: true },
};
export const string = (value, size = 300) => typeof value === 'string' ? value.trim().slice(0, size) : '';
const numeric = (value, fallback, min, max) =>
  Number.isFinite(value) ? Math.round(Math.min(max, Math.max(min, value))) : fallback;

export function settings(input = {}) {
  const rules = Object.fromEntries(types.map((type) => {
    const rule = input.rules?.[type] || {};
    const spokenTemplate = string(rule.spokenTemplate);
    return [type, {
      enabled: rule.enabled === undefined ? defaults[type].enabled : rule.enabled === true,
      speak: rule.speak === undefined ? true : rule.speak === true,
      clipId: string(rule.clipId, 36), chatTemplate: string(rule.chatTemplate),
      ...(spokenTemplate ? { spokenTemplate } : {}),
    }];
  }));
  const commands = Array.isArray(input.commands) ? input.commands.slice(0, 20)
    .map(({ command, answer }) => ({ command: string(command, 40).toLowerCase(), answer: string(answer) }))
    .filter(({ command, answer }) => /^![a-z0-9_-]+$/.test(command) && answer) : [];
  return {
    creator: string(input.creator, 64).replace(/^@/, '').replace(/[^a-z0-9_.]/gi, ''),
    bot: string(input.bot, 64).replace(/^@/, '').replace(/[^a-z0-9_.]/gi, ''),
    voice: Object.hasOwn(voices, input.voice) || naturalVoiceIds.includes(input.voice) ? input.voice : 'de',
    style: Object.hasOwn(styles, input.style) ? input.style : 'standard',
    volume: numeric(input.volume, 75, 0, 100),
    speed: numeric(input.speed, 165, 90, 260),
    delay: numeric(input.delay, 30, 5, 180),
    paused: input.paused === true,
    sendEnabled: input.sendEnabled === true,
    aiEnabled: input.aiEnabled === true,
    rules, commands,
  };
}

export const secretFields = ['providerKey', 'sessionId', 'targetIdc', 'aiKey', 'aiUrl', 'aiModel'];
export function secrets(input = {}, previous = {}) {
  return Object.fromEntries(secretFields.map((key) =>
    [key, input[key] === undefined || input[key] === '' ? string(previous[key], 2048) : string(input[key], 2048)]));
}
