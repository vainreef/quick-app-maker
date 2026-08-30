import fs from 'node:fs';
import path from 'node:path';
import { assertWithin } from './paths.mjs';

export function writeAtomic(root, filePath, value) {
  const target = assertWithin(root, filePath, 'atomic output');
  fs.mkdirSync(path.dirname(target), { recursive: true });
  const temp = `${target}.${process.pid}.${Date.now()}.tmp`;
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n';
  fs.writeFileSync(temp, text, 'utf8');
  fs.renameSync(temp, target);
  return target;
}

export function readJson(filePath, fallback = undefined) {
  try { return JSON.parse(fs.readFileSync(filePath, 'utf8')); }
  catch (error) {
    if (fallback !== undefined && (error.code === 'ENOENT' || error instanceof SyntaxError)) return fallback;
    throw error;
  }
}
