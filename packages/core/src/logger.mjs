import fs from 'node:fs';
import path from 'node:path';

export class Logger {
  constructor({ runId = 'run', root = null, quiet = false } = {}) {
    this.runId = runId;
    this.quiet = quiet;
    this.root = root;
    this.eventsPath = root ? path.join(root, 'events.jsonl') : null;
    this.textPath = root ? path.join(root, 'run.log') : null;
    if (root) fs.mkdirSync(root, { recursive: true });
  }

  event(type, data = {}) {
    const item = { ts: new Date().toISOString(), runId: this.runId, type, ...data };
    const line = JSON.stringify(item);
    if (this.eventsPath) fs.appendFileSync(this.eventsPath, line + '\n', 'utf8');
    if (!this.quiet) process.stdout.write(`[${item.ts.slice(11, 19)}] ${type}${data.message ? ` ${data.message}` : ''}\n`);
    if (this.textPath) fs.appendFileSync(this.textPath, `[${item.ts}] ${type} ${data.message ?? ''}\n`, 'utf8');
    return item;
  }
  info(message, data) { return this.event('INFO', { message, ...data }); }
  warn(message, data) { return this.event('WARN', { message, ...data }); }
  error(message, data) { return this.event('ERROR', { message, ...data }); }
  pass(message, data) { return this.event('PASS', { message, ...data }); }
}
