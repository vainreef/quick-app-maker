import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const WINDOWS_EXECUTABLE = /\.(?:cmd|bat)$/i;

/** Resolve the Node executable shipped in WORKSPACE_ROOT/node. */
export function resolvePortableNode(workspace = process.cwd()) {
  const root = path.resolve(workspace);
  const candidate = process.platform === 'win32' ? path.join(root, 'node', 'node.exe') : path.join(root, 'node', 'bin', 'node');
  if (fs.existsSync(candidate)) return candidate;
  if (process.platform === 'win32' || process.env.QAM_REQUIRE_PORTABLE === '1') {
    const error = new Error(`portable Node was not found at ${candidate}`);
    error.code = 'TOOLCHAIN';
    throw error;
  }
  return process.execPath;
}

/** Locate npm bundled with the selected Node distribution, never a global npm binary. */
export function resolveBundledNpmCli(nodePath = process.execPath) {
  const nodeDir = path.dirname(nodePath);
  const candidates = [
    path.join(nodeDir, 'node_modules', 'npm', 'bin', 'npm-cli.js'),
    path.resolve(nodeDir, '..', 'lib', 'node_modules', 'npm', 'bin', 'npm-cli.js'),
    path.resolve(nodeDir, '..', '..', '..', 'lib', 'node_modules', 'npm', 'bin', 'npm-cli.js')
  ];
  const npmBin = path.join(nodeDir, 'npm');
  if (fs.existsSync(npmBin)) {
    try {
      const real = fs.realpathSync(npmBin);
      if (real.endsWith('npm-cli.js')) candidates.unshift(real);
    } catch {}
  }
  const found = candidates.find(file => fs.existsSync(file));
  if (found) return found;
  const error = new Error(`bundled npm CLI was not found beside ${nodePath}`);
  error.code = 'TOOLCHAIN';
  throw error;
}

export function npmInvocation(args = [], { workspace = process.cwd(), nodePath = null } = {}) {
  const node = nodePath ?? resolvePortableNode(workspace);
  return { command: node, args: [resolveBundledNpmCli(node), ...args], nodePath: node };
}

export function runNpm(args = [], options = {}) {
  const { workspace = options.cwd ?? process.cwd(), nodePath = null, ...runOptions } = options;
  const invocation = npmInvocation(args, { workspace, nodePath });
  const env = withNodePath(invocation.nodePath, runOptions.env ?? process.env);
  return run(invocation.command, invocation.args, { ...runOptions, env });
}

export function runNodeScript(script, args = [], options = {}) {
  const { workspace = options.cwd ?? process.cwd(), nodePath = null, ...runOptions } = options;
  const node = nodePath ?? resolvePortableNode(workspace);
  return run(node, [script, ...args], { ...runOptions, env: withNodePath(node, runOptions.env ?? process.env) });
}

export function commandExists(command) {
  const probe = process.platform === 'win32' ? 'where.exe' : 'which';
  return new Promise(resolve => {
    let child;
    try {
      child = spawn(probe, [command], { stdio: 'ignore', windowsHide: true });
    } catch {
      resolve(false);
      return;
    }
    child.once('close', code => resolve(code === 0));
    child.once('error', () => resolve(false));
  });
}

export function run(command, args = [], { cwd = process.cwd(), env = process.env, logger = null, timeoutMs = 0, allowFailure = false } = {}) {
  return new Promise((resolve, reject) => {
    let child;
    let settled = false;
    let timedOut = false;
    let stdout = '';
    let stderr = '';
    let timer = null;
    const startedAt = Date.now();
    const resultBase = { command, args, cwd };

    const finishError = (message, cause = null, result = resultBase) => {
      if (settled) return;
      settled = true;
      if (timer) clearTimeout(timer);
      const error = new Error(message, cause ? { cause } : undefined);
      error.result = { ...result, stdout, stderr, elapsedMs: Date.now() - startedAt };
      if (cause?.code) error.processCode = cause.code;
      logger?.event('PROCESS_ERROR', { command, args, cwd, elapsedMs: error.result.elapsedMs, message: error.message });
      reject(error);
    };

    try {
      const spawnOptions = { cwd, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] };
      if (process.platform === 'win32' && WINDOWS_EXECUTABLE.test(command)) spawnOptions.shell = true;
      logger?.event('PROCESS_START', { command, args, cwd });
      child = spawn(command, args, spawnOptions);
    } catch (error) {
      finishError(`failed to start ${formatCommand(command, args)} in ${cwd}: ${error.message}`, error);
      return;
    }

    timer = timeoutMs > 0 ? setTimeout(() => {
      timedOut = true;
      try { child.kill('SIGTERM'); } catch {}
    }, timeoutMs) : null;
    child.stdout?.on('data', chunk => {
      stdout += chunk;
      logger?.event('STDOUT', { message: chunk.toString().trimEnd() });
    });
    child.stderr?.on('data', chunk => {
      stderr += chunk;
      logger?.event('STDERR', { message: chunk.toString().trimEnd() });
    });
    child.once('error', error => finishError(`failed to start ${formatCommand(command, args)} in ${cwd}: ${error.message}`, error));
    child.once('close', code => {
      if (settled) return;
      if (timer) clearTimeout(timer);
      const result = { ...resultBase, code: code ?? 1, stdout, stderr, timedOut, elapsedMs: Date.now() - startedAt };
      if (timedOut) {
        const error = new Error(`${formatCommand(command, args)} exceeded ${timeoutMs}ms in ${cwd}`);
        error.code = 'DEADLINE';
        error.result = result;
        logger?.event('PROCESS_TIMEOUT', { command, args, cwd, elapsedMs: result.elapsedMs });
        settled = true;
        reject(error);
        return;
      }
      if (result.code !== 0 && !allowFailure) {
        const error = new Error(`${formatCommand(command, args)} exited with ${result.code} in ${cwd}: ${stderr.trim() || stdout.trim()}`);
        error.result = result;
        logger?.event('PROCESS_FAIL', { command, args, cwd, code: result.code, elapsedMs: result.elapsedMs });
        settled = true;
        reject(error);
      } else {
        logger?.event('PROCESS_EXIT', { command, args, cwd, code: result.code, elapsedMs: result.elapsedMs });
        settled = true;
        resolve(result);
      }
    });
  });
}

function withNodePath(nodePath, env) {
  const key = Object.keys(env).find(name => name.toLowerCase() === 'path') ?? 'PATH';
  const current = env[key] ?? '';
  const nodeDir = path.dirname(nodePath);
  return { ...env, [key]: `${nodeDir}${path.delimiter}${current}` };
}

function formatCommand(command, args) {
  return [command, ...args].map(value => {
    const text = String(value);
    return /[\s"]/.test(text) ? JSON.stringify(text) : text;
  }).join(' ');
}
