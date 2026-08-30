import fs from 'node:fs';
import path from 'node:path';
import { assertWithin } from './paths.mjs';

export class WorkspaceLock {
  constructor(workspace, name, { staleAfterMs = 24 * 60 * 60 * 1000 } = {}) {
    this.workspace = path.resolve(workspace);
    this.name = String(name).replace(/[^a-z0-9._-]+/gi, '-').replace(/^-+|-+$/g, '') || 'workspace';
    this.staleAfterMs = staleAfterMs;
    this.file = assertWithin(this.workspace, path.join(this.workspace, '.cache', 'qam', 'locks', `${this.name}.lock`), 'lock');
    this.handle = null;
  }

  acquire() {
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        this.handle = fs.openSync(this.file, 'wx');
        try {
          fs.writeFileSync(this.handle, JSON.stringify({ pid: process.pid, name: this.name, startedAt: new Date().toISOString() }) + '\n', 'utf8');
        } catch (writeError) {
          try { fs.closeSync(this.handle); } catch {}
          this.handle = null;
          try { fs.rmSync(this.file, { force: true }); } catch {}
          throw writeError;
        }
        return this;
      } catch (error) {
        if (error.code !== 'EEXIST' || !this.removeStale()) {
          const owner = readOwner(this.file);
          const detail = owner ? ` (pid ${owner.pid}, started ${owner.startedAt})` : '';
          const lockError = new Error(`workspace is busy: ${this.name}${detail}`);
          lockError.code = 'LOCK';
          lockError.lockPath = this.file;
          throw lockError;
        }
      }
    }
    throw new Error(`could not acquire workspace lock: ${this.file}`);
  }

  release() {
    if (this.handle !== null) {
      try { fs.closeSync(this.handle); } catch {}
      this.handle = null;
      try { fs.rmSync(this.file, { force: true }); } catch {}
    }
  }

  removeStale() {
    const owner = readOwner(this.file);
    if (owner?.pid && isAlive(owner.pid)) return false;
    try {
      const age = Date.now() - fs.statSync(this.file).mtimeMs;
      if (owner || age > this.staleAfterMs) {
        fs.rmSync(this.file, { force: true });
        return true;
      }
    } catch {}
    return false;
  }
}

export async function withWorkspaceLock(workspace, name, fn, options = {}) {
  const lock = new WorkspaceLock(workspace, name, options).acquire();
  try { return await fn(); } finally { lock.release(); }
}

function readOwner(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return null; }
}

function isAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try { process.kill(pid, 0); return true; } catch (error) { return error.code === 'EPERM'; }
}
