import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { createCipheriv, createDecipheriv, randomBytes, scryptSync, timingSafeEqual } from 'node:crypto';

export const dataDir = process.env.BOT_DATA || 'data';
mkdirSync(dataDir, { recursive: true, mode: 0o700 });
const stateFile = join(dataDir, 'state.json');

export function passwordHash(password) {
  const salt = randomBytes(16).toString('hex');
  return salt + ':' + scryptSync(password, salt, 32).toString('hex');
}

export function passwordMatches(password, hash) {
  const [salt, expected] = hash.split(':');
  return timingSafeEqual(scryptSync(password, salt, 32), Buffer.from(expected, 'hex'));
}

export function load() {
  try { return JSON.parse(readFileSync(stateFile, 'utf8')); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
}

export function save(state) {
  const temporary = stateFile + '.tmp';
  writeFileSync(temporary, JSON.stringify(state), { mode: 0o600 });
  renameSync(temporary, stateFile);
}

const magic = Buffer.from('TIKTOK-BOT-BACKUP-1\n');
export function encryptBackup(data, password) {
  const salt = randomBytes(16);
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', scryptSync(password, salt, 32), iv);
  cipher.setAAD(magic);
  const encrypted = Buffer.concat([cipher.update(JSON.stringify(data)), cipher.final()]);
  return Buffer.concat([magic, salt, iv, cipher.getAuthTag(), encrypted]).toString('base64');
}

export function decryptBackup(input, password) {
  const bytes = Buffer.from(input, 'base64');
  const offset = magic.length;
  if (bytes.length <= offset + 44 || !bytes.subarray(0, offset).equals(magic)) throw new Error('Ungültiges Backupformat');
  const decipher = createDecipheriv('aes-256-gcm',
    scryptSync(password, bytes.subarray(offset, offset + 16), 32), bytes.subarray(offset + 16, offset + 28));
  decipher.setAAD(magic);
  decipher.setAuthTag(bytes.subarray(offset + 28, offset + 44));
  return JSON.parse(Buffer.concat([decipher.update(bytes.subarray(offset + 44)), decipher.final()]).toString('utf8'));
}
