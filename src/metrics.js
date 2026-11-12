import { readFileSync, writeFileSync, renameSync } from 'node:fs';
import { join } from 'node:path';
import { availableParallelism } from 'node:os';

const hour = 3600000;
const sum = (a, b) => [a[0] + b[0], a[1] + b[1]];
export function metrics(directory, log) {
  const path = join(directory, 'traffic.json');
  let data = { total: [0, 0], hours: {} };
  try { data = JSON.parse(readFileSync(path, 'utf8')); } catch (error) {
    if (error.code !== 'ENOENT') log('system', 'Verbrauchszähler konnte nicht gelesen werden', error);
  }
  let dirty = false, cpu = null, previous;
  function flush() {
    if (!dirty) return;
    try {
      writeFileSync(path + '.tmp', JSON.stringify(data), { mode: 0o600 });
      renameSync(path + '.tmp', path);
      dirty = false;
    } catch (error) { log('system', 'Verbrauchszähler konnte nicht gespeichert werden', error); }
  }
  const cg = (file) => readFileSync('/sys/fs/cgroup/' + file, 'utf8').trim();
  function sample() {
    try {
      const now = process.hrtime.bigint();
      const usage = Number(cg('cpu.stat').match(/^usage_usec (\d+)$/m)?.[1]);
      const [quota, period] = cg('cpu.max').split(/\s+/);
      const cores = Math.min(availableParallelism(), quota === 'max' ? Infinity : Number(quota) / Number(period));
      if (previous && Number.isFinite(usage) && usage >= previous.usage && cores > 0) {
        cpu = Math.min(100, Math.round((usage - previous.usage) / (Number(now - previous.now) / 1000 * cores) * 100));
      }
      previous = { now, usage };
    } catch { cpu = null; }
  }
  sample();
  setInterval(sample, 2500).unref();
  setInterval(flush, 10000).unref();
  return {
    flush,
    add(sent, received) {
      if (![sent, received].every((n) => Number.isSafeInteger(n) && n >= 0)) return;
      const now = Math.floor(Date.now() / hour);
      data.hours[now] = sum(data.hours[now] ?? [0, 0], [sent, received]);
      data.total = sum(data.total, [sent, received]);
      for (const key of Object.keys(data.hours)) if (Number(key) < now - 168) delete data.hours[key];
      dirty = true;
    },
    traffic() {
      const now = Math.floor(Date.now() / hour);
      const since = (hours) => Object.entries(data.hours).filter(([key]) => +key > now - hours && +key <= now)
        .reduce((total, [, count]) => sum(total, count), [0, 0]);
      return { day: since(24), week: since(168), total: data.total };
    },
    resources() {
      try {
        const used = Number(cg('memory.current'));
        const limit = cg('memory.max');
        return { cpu, used, limit: limit === 'max' ? null : Number(limit) };
      } catch { return { cpu, used: null, limit: null }; }
    },
  };
}
