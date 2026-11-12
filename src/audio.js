import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { characterVoices, styles } from './settings.js';

const exec = promisify(execFile);
const piperCatalog = {
  'de_DE-thorsten-medium': 'Deutsch · Thorsten (natürlich)',
  'de_DE-kerstin-low': 'Deutsch · Kerstin (natürlich)',
  'en_US-amy-low': 'Englisch · Amy (natürlich)',
  ...Object.fromEntries(Object.entries(characterVoices).map(([id, [label]]) => [id, label])),
};
const model = (id) => Object.hasOwn(characterVoices, id) ? 'de_DE-thorsten_emotional-medium' : id;
export const naturalVoices = Object.fromEntries(Object.entries(piperCatalog).filter(([id]) =>
  process.env.BOT_PIPER && existsSync(process.env.BOT_PIPER) && process.env.BOT_PIPER_MODELS &&
  ['.onnx', '.onnx.json'].every((suffix) => existsSync(join(process.env.BOT_PIPER_MODELS, model(id) + suffix)))));

export async function speak(text, config) {
  if (Object.hasOwn(naturalVoices, config.voice)) {
    const directory = await mkdtemp(join(tmpdir(), 'ttb-voice-'));
    try {
      const input = join(directory, 'text.txt');
      const output = join(directory, 'voice.wav');
      await writeFile(input, text, 'utf8');
      await exec(process.env.BOT_PIPER, [
        '--model', join(process.env.BOT_PIPER_MODELS, model(config.voice) + '.onnx'),
        ...(Object.hasOwn(characterVoices, config.voice) ? ['--speaker', String(characterVoices[config.voice][1])] : []),
        '--input-file', input, '--output-file', output, '--length-scale', String(165 / config.speed),
      ], { timeout: 20000, windowsHide: true, env: { ...process.env, OMP_NUM_THREADS: '1' } });
      return await readFile(output);
    } finally { await rm(directory, { recursive: true, force: true }); }
  }
  if (Object.hasOwn(piperCatalog, config.voice)) throw new Error('Piper-Stimme ist nicht installiert');
  const { stdout } = await exec(process.env.BOT_ESPEAK || 'espeak-ng', [
    '--stdout', '-v', config.voice + styles[config.style], '-s', String(config.speed), '--', text,
  ], { encoding: 'buffer', timeout: 10000, maxBuffer: 3 * 1024 * 1024, windowsHide: true });
  return stdout;
}
