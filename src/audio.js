import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { styles } from './settings.js';

const exec = promisify(execFile);
export async function speak(text, config) {
  const { stdout } = await exec(process.env.BOT_ESPEAK || 'espeak-ng', [
    '--stdout', '-v', config.voice + styles[config.style], '-s', String(config.speed), '--', text,
  ], { encoding: 'buffer', timeout: 10000, maxBuffer: 3 * 1024 * 1024, windowsHide: true });
  return stdout;
}
