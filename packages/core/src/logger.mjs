import fs from 'node:fs';
import path from 'node:path';

export class Logger {
  constructor({ runId = 'run', root = null, quiet = false } = {}) {
    this.runId = runId;
    this.quiet = quiet;
    this.root = root;
    this.eventsPath = root ? path.join(root, 'events.jsonl') : null;
    this.textPath = root ? path.join(root, 'run.log') : null;
    this.resultPath = root ? path.join(root, 'result.json') : null;
    if (root) fs.mkdirSync(root, { recursive: true });
  }

  event(type, data = {}) {
    const item = { ts: new Date().toISOString(), runId: this.runId, type, ...data };
    const line = JSON.stringify(item);
    if (this.eventsPath) fs.appendFileSync(this.eventsPath, line + '\n', 'utf8');
    if (!this.quiet) process.stdout.write(`[${item.ts.slice(11, 19)}] ${type}${data.message ? ` ${data.message}` : ''}\n`);
    if (this.textPath) fs.appendFileSync(this.textPath, `[${item.ts}] ${type} ${data.message ?? ''}\n`, 'utf8');
    if (this.resultPath) this.writeResult(type, data);
    return item;
  }
  info(message, data) { return this.event('INFO', { message, ...data }); }
  warn(message, data) { return this.event('WARN', { message, ...data }); }
  error(message, data) { return this.event('ERROR', { message, ...data }); }
  pass(message, data) { return this.event('PASS', { message, ...data }); }

  writeResult(type, data = {}) {
    const failed = type === 'ERROR' || type === 'PROCESS_ERROR' || type === 'PROCESS_FAIL' || type === 'PROCESS_TIMEOUT';
    const complete = type === 'PASS' || (type === 'PROCESS_EXIT' && data.code === 0);
    const result = {
      runId: this.runId,
      status: failed ? 'failed' : complete ? 'complete' : 'running',
      ok: complete ? true : failed ? false : null,
      updatedAt: new Date().toISOString(),
      lastEvent: { type, message: data.message ?? '', code: data.code ?? null, phase: data.phase ?? null, elapsedMs: data.elapsedMs ?? null }
    };
    try {
      const temp = `${this.resultPath}.${process.pid}.tmp`;
      fs.writeFileSync(temp, JSON.stringify(result, null, 2) + '\n', 'utf8');
      try { fs.renameSync(temp, this.resultPath); }
      catch (error) {
        if (error.code !== 'EEXIST' && error.code !== 'EPERM') throw error;
        fs.rmSync(this.resultPath, { force: true });
        fs.renameSync(temp, this.resultPath);
      }
    } catch {}
  }
}
