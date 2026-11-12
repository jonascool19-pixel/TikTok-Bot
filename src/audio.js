import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { styles } from './settings.js';

const exec = promisify(execFile);
export const naturalVoices = process.env.BOT_PIPER && process.env.BOT_PIPER_MODELS ? {
  'de_DE-thorsten-medium': 'Deutsch · Thorsten (natürlich)',
  'de_DE-kerstin-low': 'Deutsch · Kerstin (natürlich)',
  'en_US-amy-low': 'Englisch · Amy (natürlich)',
} : {};

export async function speak(text, config) {
  if (Object.hasOwn(naturalVoices, config.voice)) {
    const directory = await mkdtemp(join(tmpdir(), 'ttb-voice-'));
    try {
      const input = join(directory, 'text.txt');
      const output = join(directory, 'voice.wav');
      await writeFile(input, text, 'utf8');
      await exec(process.env.BOT_PIPER, [
        '--model', join(process.env.BOT_PIPER_MODELS, config.voice + '.onnx'),
        '--input-file', input, '--output-file', output, '--length-scale', String(165 / config.speed),
      ], { timeout: 20000, windowsHide: true, env: { ...process.env, OMP_NUM_THREADS: '1' } });
      return await readFile(output);
    } finally { await rm(directory, { recursive: true, force: true }); }
  }
  const { stdout } = await exec(process.env.BOT_ESPEAK || 'espeak-ng', [
    '--stdout', '-v', config.voice + styles[config.style], '-s', String(config.speed), '--', text,
  ], { encoding: 'buffer', timeout: 10000, maxBuffer: 3 * 1024 * 1024, windowsHide: true });
  return stdout;
}
