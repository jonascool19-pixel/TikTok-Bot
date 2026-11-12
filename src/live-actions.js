const templates = {
  comment: '{name}: {text}',
  gift: 'Danke, {name}, für {count} × {gift}!',
  join: 'Willkommen, {name}!',
};

const clean = (value, length) =>
  typeof value === 'string' ? value.replace(/\s+/gu, ' ').trim().slice(0, length) : '';

export function getLiveActions(event, rules = {}, botId) {
  if (!Object.hasOwn(templates, event.type) || (botId && event.userId === botId)) {
    return [];
  }

  const rule = rules[event.type];
  if (!rule?.enabled || (event.type === 'gift' && event.streakPending)) {
    return [];
  }

  const fields = {
    name: clean(event.name, 80) || 'Gast',
    text: clean(event.text, 300),
    gift: clean(event.gift, 80) || 'Geschenk',
    count: Number.isSafeInteger(event.count) && event.count > 0 ? event.count : 1,
  };
  if (event.type === 'comment' && (!fields.text || /https?:\/\/|www\./iu.test(fields.text))) {
    return [];
  }

  const render = (template) =>
    template.replace(/\{(name|text|gift|count)\}/gu, (_, field) => fields[field]);
  const actions = [];
  if (rule.clipId) {
    actions.push({ type: 'clip', clipId: rule.clipId });
  } else if (rule.speak) {
    actions.push({ type: 'speak', text: render(rule.spokenTemplate ?? templates[event.type]) });
  }
  if (rule.chatTemplate?.trim() && event.type !== 'comment') {
    actions.push({ type: 'chat', text: render(rule.chatTemplate) });
  }
  return actions;
}
