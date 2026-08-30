import path from 'node:path';
import fs from 'node:fs';
import { assertWithin, writeAtomic } from '@quick-app/core';

export class EvidenceStore {
  constructor(workspace, runRoot) { this.workspace = path.resolve(workspace); this.runRoot = assertWithin(this.workspace, path.resolve(runRoot), 'evidence run'); this.dir = assertWithin(this.workspace, path.join(this.runRoot, 'evidence'), 'evidence'); fs.mkdirSync(this.dir, { recursive: true }); }
  async writePhase(phase, payload) {
    const safe = phase.replace(/[^a-z0-9-]/gi, '-');
    const file = path.join(this.dir, `${safe}.json`);
    writeAtomic(this.workspace, file, payload);
    return [path.relative(this.runRoot, file).replaceAll('\\', '/')];
  }
  write(name, value) { const file = assertWithin(this.workspace, path.join(this.dir, name), 'evidence file'); fs.mkdirSync(path.dirname(file), { recursive: true }); if (Buffer.isBuffer(value) || value instanceof Uint8Array) { const temp = `${file}.${process.pid}.${Date.now()}.tmp`; fs.writeFileSync(temp, value); fs.renameSync(temp, file); } else writeAtomic(this.workspace, file, value); return path.relative(this.runRoot, file).replaceAll('\\', '/'); }
  writeText(name, value) { return this.write(name, String(value)); }
}
