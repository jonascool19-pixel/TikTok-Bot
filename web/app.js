const $ = (selector) => document.querySelector(selector);
const form = $('#settings');
const titles = { comment: 'Kommentare', gift: 'Geschenke', join: 'Begrüßungen' };
const secretNames = ['providerKey', 'sessionId', 'targetIdc', 'aiUrl', 'aiModel', 'aiKey'];
let state;
let logEntries = [];
let previewUrl;
let recording;
let mailTimer;
let mailRevision = 0;

async function api(path, data) {
  const options = data === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Bot-Request': '1' },
    body: JSON.stringify(data),
  };
  const response = await fetch('/api/' + path, options);
  const result = await response.json();
  if (!response.ok) throw Object.assign(new Error(result.error), { status: response.status });
  return result;
}
function notice(text, error = false) {
  const node = $('#notice');
  node.textContent = text;
  node.classList.toggle('error', error);
  node.hidden = !text;
}
function perform(work, button) {
  return async (event) => {
    event?.preventDefault();
    const control = button || event?.submitter;
    if (control) control.disabled = true;
    try { await work(event); }
    catch (error) { notice(error.message, true); }
    finally { if (control) control.disabled = false; }
  };
}
function submit(selector, work) { $(selector).addEventListener('submit', perform(work)); }
function click(selector, work) { $(selector).addEventListener('click', perform(work, $(selector))); }

function show(view) {
  for (const key of ['login', 'password', 'dashboard']) $('#' + key).hidden = key !== view;
  $('#logout').hidden = view === 'login';
  if (view !== 'dashboard') clearMail();
  else showPage();
}

const navigation = $('#dashboard nav');
const pageLinks = [...navigation.querySelectorAll('a')];
const sections = [...$('#dashboard').querySelectorAll('section')];
const settingsPages = new Set([...form.querySelectorAll('section')].map((section) => section.id));
let activePage;

function showPage() {
  const hash = window.location.hash.slice(1);
  const requested = hash === 'backup' ? 'einstellungen' : hash;
  const selected = pageLinks.some((link) => link.hash === '#' + requested) ? requested : 'konto';
  if (activePage === 'postfach' && selected !== activePage && !$('#mail-inbox').hidden) {
    clearMail();
    void api('gmail/lock', {}).catch(() => {});
  }
  activePage = selected;
  form.hidden = !settingsPages.has(selected);
  for (const section of sections) section.hidden = (section.dataset.page || section.id) !== selected;
  for (const link of pageLinks) {
    if (link.hash === '#' + selected) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  }
}

navigation.addEventListener('click', (event) => {
  const link = event.target.closest('a');
  if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  if (link.hash !== window.location.hash) history.pushState(null, '', link.hash);
  showPage();
  window.scrollTo(0, 0);
  sections.find((section) => !section.hidden)?.querySelector('h2')?.focus({ preventScroll: true });
});
for (const section of sections) section.querySelector('h2').tabIndex = -1;
window.addEventListener('hashchange', showPage);

function option(value, label) { return new Option(label, value); }
function render() {
  form.elements.voice.replaceChildren(...Object.entries(state.voices).map(([key, value]) => option(key, value)));
  const styleLabels = { standard: 'Standard', tief: 'Tief', hell: 'Hell', roboter: 'Roboter' };
  form.elements.style.replaceChildren(...state.styles.map((style) => option(style, styleLabels[style] || style)));
  $('#rules').replaceChildren(...Object.entries(titles).map(([key, title]) => {
    const box = document.createElement('fieldset');
    box.dataset.rule = key;
    box.innerHTML = '<legend></legend><label class="check"><input type="checkbox" data-key="enabled"> Aktiv</label>' +
      '<label class="check"><input type="checkbox" data-key="speak"> Vorlesen</label>' +
      '<label>Vorlesetext<input data-key="spokenTemplate" maxlength="300" placeholder="Standardtext"></label>' +
      '<label>Eigener Audioclip<select data-key="clipId"></select></label>' +
      (key === 'comment' ? '' : '<label>Nachricht im Chat<input data-key="chatTemplate" maxlength="150"></label>');
    box.querySelector('legend').textContent = title;
    box.querySelector('select').replaceChildren(option('', 'Kein Clip'), ...state.clips.map(({ id, name }) => option(id, name)));
    for (const field of box.querySelectorAll('[data-key]')) {
      const value = state.config.rules[key][field.dataset.key];
      if (field.type === 'checkbox') field.checked = value === true;
      else field.value = value ?? '';
    }
    return box;
  }));
  for (const [key, value] of Object.entries(state.config)) {
    const field = form.elements.namedItem(key);
    if (!field) continue;
    if (key === 'commands') field.value = value.map(({ command, answer }) => command + ' = ' + answer).join('\n');
    else if (field.type === 'checkbox') field.checked = value;
    else field.value = value;
  }
  for (const key of secretNames) form.elements[key].value = '';
  $('#clips').replaceChildren(...state.clips.map(({ id, name }) => {
    const item = document.createElement('li');
    item.textContent = name + ' ';
    const remove = document.createElement('button');
    remove.textContent = 'Entfernen';
    remove.className = 'secondary';
    remove.addEventListener('click', perform(async () => { await api('clip/delete', { id }); await load(); }, remove));
    item.append(remove);
    return item;
  }));
  status();
}

function status() {
  $('#status').textContent = state.status;
  const profileAvailable = state.profileLogin && location.protocol === 'https:';
  $('#tiktok-profile-help').hidden = profileAvailable;
  $('#account-status').textContent = (state.botDisplayName ? 'TikTok-Profil: ' + state.botDisplayName + '. ' : '') +
    (state.connectedAccount ? 'Bot-Sitzung für den Versand hinterlegt.' : 'Keine Bot-Sitzung für den Versand hinterlegt.');
}
async function load() {
  try { state = await api('state'); }
  catch (error) {
    if (error.status !== 401) throw error;
    show('login');
    $('#status').textContent = 'Dashboard im Heimnetz';
    return;
  }
  if (state.firstLogin) return show('password');
  show('dashboard');
  render();
  await mailStatus().catch(() => { $('#mail-status').textContent = 'Postfachstatus konnte nicht geladen werden.'; });
}

function clearMail() {
  clearTimeout(mailTimer);
  mailRevision++;
  $('#mail-inbox').hidden = true;
  $('#mail-list').replaceChildren();
  $('#mail-empty').hidden = true;
  $('#mail-message').textContent = '';
  $('#mail-message').hidden = true;
  $('#mail-form').reset();
}

async function mailStatus() {
  const mail = await api('gmail/status');
  const secure = window.isSecureContext && window.location.protocol === 'https:';
  $('#mail-form').hidden = !mail.enabled || !secure;
  $('#mail-connect').hidden = mail.connected;
  $('#mail-unlock').hidden = !mail.connected;
  $('#mail-disconnect').hidden = !mail.connected && !mail.unreadable;
  $('#mail-setup').hidden = mail.enabled && secure;
  $('#mail-status').textContent = !secure
    ? 'Öffne das Dashboard über HTTPS, um Gmail zu verbinden.'
    : !mail.enabled ? 'Für diesen CT fehlt noch der einmalige Google-Zugang. Folge der Einrichtungsanleitung.'
      : mail.address + (mail.connected ? ' · Verbunden. Angezeigt werden Nachrichten aus dem Bot-Label.'
        : mail.unreadable ? ' · Verbindung konnte nicht entschlüsselt werden.' : ' · Noch nicht verbunden.');
}

async function loadMail() {
  const revision = mailRevision;
  const list = await api('gmail/messages', {});
  if (revision !== mailRevision) return;
  $('#mail-empty').hidden = list.messages.length !== 0;
  $('#mail-list').replaceChildren(...list.messages.map(({ id, received }) => {
    const item = document.createElement('li');
    const open = document.createElement('button');
    open.className = 'secondary';
    open.textContent = 'Nachricht vom ' + new Date(received).toLocaleString();
    open.addEventListener('click', perform(async () => {
      $('#mail-message').textContent = '';
      $('#mail-message').hidden = true;
      const message = await api('gmail/message', { id });
      if (revision !== mailRevision) return;
      $('#mail-message').textContent = message.text;
      $('#mail-message').hidden = false;
    }, open));
    item.append(open);
    return item;
  }));
}

function config() {
  const result = {};
  for (const key of ['creator', 'bot', 'voice', 'style', 'paused', 'sendEnabled', 'aiEnabled', 'volume', 'speed', 'delay']) {
    const field = form.elements[key];
    result[key] = field.type === 'checkbox' ? field.checked : field.type === 'number' ? field.valueAsNumber : field.value;
  }
  result.commands = form.elements.commands.value.split('\n').map((line) => {
    const separator = line.indexOf('=');
    return separator < 0 ? {} : { command: line.slice(0, separator), answer: line.slice(separator + 1) };
  });
  result.rules = Object.fromEntries([...$('#rules').children].map((box) => [box.dataset.rule,
    Object.fromEntries([...box.querySelectorAll('[data-key]')].map((field) =>
      [field.dataset.key, field.type === 'checkbox' ? field.checked : field.value]))]));
  return result;
}
function secretInput() { return Object.fromEntries(secretNames.map((key) => [key, form.elements[key].value])); }
function download(data, mime, name) {
  const anchor = document.createElement('a');
  const url = URL.createObjectURL(new Blob([data], { type: mime }));
  anchor.href = url;
  anchor.download = name;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function decode(value, mime) {
  return new Blob([Uint8Array.from(atob(value), (character) => character.charCodeAt(0))], { type: mime });
}
async function addClip(file, name) {
  if (!file || file.size > 2097152) throw new Error('Wähle eine Audiodatei bis 2 MB.');
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 8192) binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
  await api('clip', { name, mime: file.type.split(';')[0], bytes: btoa(binary) });
  await load();
  notice('Aufnahme gespeichert.');
}

submit('#login-form', async (event) => {
  await api('login', Object.fromEntries(new FormData(event.target)));
  event.target.reset();
  await load();
  notice('');
});
submit('#password-form', async (event) => {
  await api('password', Object.fromEntries(new FormData(event.target)));
  event.target.reset();
  await load();
  notice('Passwort geändert.');
});
click('#logout', async () => { await api('logout', {}); await load(); notice(''); });
click('#change-password', async () => { show('password'); window.scrollTo(0, 0); });
click('#tiktok-profile', async () => {
  if (!state.profileLogin || location.protocol !== 'https:') {
    const help = $('#tiktok-profile-help');
    help.open = true;
    help.querySelector('summary').focus();
    return;
  }
  const { url } = await api('tiktok/profile/start', {});
  window.location.assign(url);
});
submit('#mail-form', async (event) => {
  const button = event.submitter;
  if (!button) return;
  const password = event.target.elements.password.value;
  event.target.reset();
  if (button.value === 'start') {
    const { url } = await api('gmail/start', { password });
    window.location.assign(url);
  } else if (button.value === 'disconnect') {
    const { revoked } = await api('gmail/disconnect', { password });
    clearMail();
    await mailStatus();
    notice(revoked ? 'Gmail-Verbindung getrennt.' : 'Verbindung lokal entfernt. Entziehe den Zugriff zusätzlich im Google-Konto.');
  } else {
    clearMail();
    await api('gmail/unlock', { password });
    $('#mail-inbox').hidden = false;
    mailTimer = setTimeout(clearMail, 115000);
    await loadMail();
  }
});
click('#mail-refresh', loadMail);
click('#mail-lock', async () => { clearMail(); await api('gmail/lock', {}); });
document.addEventListener('visibilitychange', () => {
  if (document.hidden && !$('#mail-inbox').hidden) {
    clearMail();
    void api('gmail/lock', {}).catch(() => {});
  }
});
submit('#settings', async () => {
  await api('settings', { config: config(), secrets: secretInput() });
  await load();
  notice('Einstellungen gespeichert.');
});
click('#disconnect', async () => {
  const updated = config();
  updated.sendEnabled = false;
  updated.aiEnabled = false;
  await api('settings', { config: updated, disconnect: true });
  await load();
  notice('Bot-Sitzung entfernt.');
});
click('#clear-audio', async () => {
  await api('audio/clear', {});
  notice('Audiowarteschlange geleert.');
});
submit('#simulate', async (event) => {
  const selected = config();
  const player = event.target.elements.player.checked;
  const result = await api('simulate', { ...Object.fromEntries(new FormData(event.target)), player, config: selected });
  const audio = $('#preview');
  audio.pause();
  if (previewUrl) URL.revokeObjectURL(previewUrl);
  audio.hidden = !result.audio;
  const display = $('#simulation');
  display.hidden = false;
  display.textContent = result.actions.map((action) => action.type === 'clip' ? 'Eigener Audioclip' :
    (action.type === 'chat' ? 'Chatvorschau: ' : 'Sprachausgabe: ') + action.text).join('\n') || 'Für dieses Ereignis ist keine Ausgabe eingestellt.';
  if (result.audio) {
    previewUrl = URL.createObjectURL(decode(result.audio, result.mime));
    audio.src = previewUrl;
    audio.volume = selected.volume / 100;
    if (!player) await audio.play().catch(() => {});
  }
  notice('Test abgeschlossen.');
});
submit('#clip-form', async (event) => {
  const { file, name } = event.target.elements;
  await addClip(file.files[0], name.value);
  event.target.reset();
});
click('#record', async () => {
  if (recording) {
    recording.stop();
    return;
  }
  if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) throw new Error('Mikrofon benötigt HTTPS oder localhost.');
  const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  try { recording = new MediaRecorder(stream, { audioBitsPerSecond: 64000 }); }
  catch (error) { stream.getTracks().forEach((track) => track.stop()); throw error; }
  const chunks = [];
  let stopTimer;
  recording.ondataavailable = (event) => { if (event.data.size) chunks.push(event.data); };
  recording.onstop = () => {
    clearTimeout(stopTimer);
    const mime = recording.mimeType;
    recording = null;
    stream.getTracks().forEach((track) => track.stop());
    $('#record').textContent = 'Mikrofonaufnahme starten';
    addClip(new Blob(chunks, { type: mime }), 'Mikrofonaufnahme').catch((error) => notice(error.message, true));
  };
  recording.start();
  stopTimer = setTimeout(() => { if (recording?.state === 'recording') recording.stop(); }, 30000);
  $('#record').textContent = 'Aufnahme stoppen und speichern';
});
submit('#send', async (event) => {
  await api('send', Object.fromEntries(new FormData(event.target)));
  event.target.reset();
  notice('Nachricht an den Anbieter übergeben.');
});
submit('#backup-form', async (event) => {
  const { password, file, includeSecrets } = event.target.elements;
  if (event.submitter?.value === 'import') {
    if (!file.files[0] || file.files[0].size > 32 * 1024 * 1024) throw new Error('Wähle eine gültige Backupdatei.');
    if (!window.confirm('Einstellungen und Aufnahmen durch das Backup ersetzen?')) return;
    await api('backup/import', { password: password.value, backup: await file.files[0].text() });
    await load();
    notice('Backup wiederhergestellt. Prüfe das Bot-Konto, bevor du den Versand aktivierst.');
  } else {
    const { backup } = await api('backup/export', { password: password.value, includeSecrets: includeSecrets.checked });
    download(backup, 'application/octet-stream', 'tiktok-bot-backup.ttb');
    notice('Verschlüsseltes Backup heruntergeladen.');
  }
  event.target.reset();
});
click('#logs', async () => {
  logEntries = await api('log');
  $('#log-content').textContent = logEntries.map(({ time, area, message, type, code }) =>
    new Date(time).toLocaleString() + ' · ' + area + ' · ' + message + ' (' + type + (code ? ', ' + code : '') + ')').join('\n') || 'Keine Fehler aufgezeichnet.';
});
click('#download-log', async () => {
  logEntries = await api('log');
  download(logEntries.map((entry) => JSON.stringify(entry)).join('\n'), 'application/jsonl', 'tiktok-bot-fehler.jsonl');
});
setInterval(async () => {
  if ($('#dashboard').hidden) return;
  try { state = await api('state'); status(); }
  catch (error) { $('#status').textContent = error.message; }
}, 5000);
await load().catch((error) => notice(error.message, true));
const mailResult = new URLSearchParams(window.location.search).get('gmail');
const tiktokResult = new URLSearchParams(window.location.search).get('tiktok');
if (mailResult) {
  history.replaceState(null, '', '/#postfach');
  showPage();
  notice(mailResult === 'connected' ? 'Gmail-Postfach verbunden.' : 'Google-Verbindung fehlgeschlagen. Prüfe Einrichtung und Fehlerlog.',
    mailResult !== 'connected');
}
if (tiktokResult) {
  history.replaceState(null, '', '/#konto');
  showPage();
  notice(tiktokResult === 'connected' ? 'TikTok-Profil übernommen.' : 'TikTok-Profil konnte nicht übernommen werden. Prüfe das Fehlerlog.',
    tiktokResult !== 'connected');
}
