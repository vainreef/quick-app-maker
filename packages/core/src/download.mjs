import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { assertWithin } from './paths.mjs';

export async function sha256(filePath) {
  const hash = crypto.createHash('sha256');
  await pipeline(fs.createReadStream(filePath), hash);
  return hash.digest('hex');
}

export function sha256Sync(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

export async function downloadVerified({ workspace, url, outputPath, expectedSha256 = '', retries = 1, logger = null }) {
  const target = assertWithin(workspace, outputPath, 'download output');
  fs.mkdirSync(path.dirname(target), { recursive: true });
  if (fs.existsSync(target) && (!expectedSha256 || (await sha256(target)) === expectedSha256.toLowerCase())) return target;
  let lastError;
  for (let attempt = 0; attempt <= retries; attempt += 1) {
    const part = `${target}.part`;
    try {
      logger?.info('download', { url, outputPath: target, attempt: attempt + 1 });
      const response = await fetch(url, { redirect: 'follow' });
      if (!response.ok || !response.body) throw new Error(`HTTP ${response.status} for ${url}`);
      await pipeline(Readable.fromWeb(response.body), fs.createWriteStream(part));
      const actual = await sha256(part);
      if (expectedSha256 && actual.toLowerCase() !== expectedSha256.toLowerCase()) throw new Error(`SHA-256 mismatch: expected ${expectedSha256}, got ${actual}`);
      fs.renameSync(part, target);
      return target;
    } catch (error) {
      lastError = error;
      try { fs.rmSync(`${target}.part`, { force: true }); } catch {}
      logger?.warn('download-failed', { message: error.message, attempt: attempt + 1 });
    }
  }
  throw lastError;
}
