const button = document.querySelector('#start');
const status = document.querySelector('#player-status');
const player = document.querySelector('#audio');
let running = false;
let cursor = 0;
let reset;
let generation = 0;
const silent = 'data:audio/wav;base64,UklGRiYAAABXQVZFZm10IBAAAAABAAEARKwAAIhYAQACABAAZGF0YQIAAAAAAA==';

async function poll() {
  const result = await fetch('/api/audio');
  if (result.status === 401 || result.status === 403) throw new Error('Bitte zuerst im Dashboard anmelden.');
  if (!result.ok) throw new Error('Wiedergabe ist nicht erreichbar.');
  return result.json();
}
function reportError() {
  void fetch('/api/player/error', { method: 'POST', headers: {
    'Content-Type': 'application/json', 'X-Bot-Request': '1',
  }, body: '{}' }).catch(() => {});
}
async function loop() {
  if (!running) return;
  try {
    const output = await poll();
    if (!running) return;
    if (output.reset !== reset || output.latest < cursor) {
      reset = output.reset;
      cursor = output.clips.length ? output.clips[0].id - 1 : output.latest;
      player.pause();
      player.removeAttribute('src');
      player.load();
    }
    if (player.paused || player.ended) {
      const next = output.clips.find(({ id }) => id > cursor);
      if (next) {
        cursor = next.id;
        player.volume = next.volume;
        player.src = '/api/audio/' + next.id;
        await player.play();
      }
    }
    status.textContent = 'Ausgabe aktiv.';
  } catch (error) {
    if (!running) return;
    status.textContent = error.message;
    reportError();
    stop();
  }
  if (running) setTimeout(loop, 750);
}
function stop() {
  generation++;
  running = false;
  player.pause();
  button.textContent = 'Ausgabe starten';
}
button.addEventListener('click', async () => {
  if (running) { stop(); status.textContent = 'Ausgabe gestoppt.'; return; }
  const revision = ++generation;
  button.disabled = true;
  try {
    const wav = Uint8Array.from(atob(silent.split(',')[1]), (character) => character.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([wav], { type: 'audio/wav' }));
    player.src = url;
    await player.play().finally(() => URL.revokeObjectURL(url));
    const output = await poll();
    if (revision !== generation) return;
    cursor = output.latest;
    reset = output.reset;
    running = true;
    button.textContent = 'Ausgabe stoppen';
    void loop();
  } catch (error) { status.textContent = error.message; reportError(); }
  finally { button.disabled = false; }
});
