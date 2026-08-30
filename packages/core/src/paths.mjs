import path from 'node:path';
import fs from 'node:fs';

export function absolute(value, base = process.cwd()) {
  if (!value || typeof value !== 'string') throw new TypeError('path must be a non-empty string');
  return path.resolve(base, value);
}

export function assertWithin(root, candidate, label = 'path') {
  const rootAbs = path.resolve(root);
  const candidateAbs = path.resolve(candidate);
  const rootKey = process.platform === 'win32' ? rootAbs.toLowerCase() : rootAbs;
  const candidateKey = process.platform === 'win32' ? candidateAbs.toLowerCase() : candidateAbs;
  const relative = path.relative(rootKey, candidateKey);
  if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    throw new Error(`${label} is outside WORKSPACE_ROOT: ${candidateAbs}`);
  }
  return candidateAbs;
}

export function workspaceRoot(value = process.env.QAM_WORKSPACE_ROOT ?? process.cwd()) {
  return path.resolve(value);
}

export function ensureWorkspace(root) {
  const workspace = workspaceRoot(root);
  fs.mkdirSync(workspace, { recursive: true });
  for (const name of ['.cache', '.cache/qam', '.cache/downloads', '.cache/npm', '.cache/electron', '.cache/runs']) {
    fs.mkdirSync(assertWithin(workspace, path.join(workspace, name), name), { recursive: true });
  }
  return workspace;
}

export function appRoot(workspace, value) {
  const candidate = absolute(value, workspace);
  assertWithin(workspace, candidate, 'app');
  if (path.basename(candidate).startsWith('.')) throw new Error('app directory cannot be hidden');
  return candidate;
}

export function runRoot(workspace, runId) {
  return assertWithin(workspace, path.join(workspace, '.cache', 'qam', 'runs', runId), 'run');
}

export function pathExists(value) {
  try { fs.accessSync(value); return true; } catch { return false; }
}
