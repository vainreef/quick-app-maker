import { spawn } from 'node:child_process';

export function commandExists(command) {
  const probe = process.platform === 'win32' ? 'where.exe' : 'which';
  return new Promise(resolve => {
    const child = spawn(probe, [command], { stdio: 'ignore', windowsHide: true });
    child.once('close', code => resolve(code === 0));
    child.once('error', () => resolve(false));
  });
}

export function run(command, args = [], { cwd = process.cwd(), env = process.env, logger = null, timeoutMs = 0, allowFailure = false } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    const timer = timeoutMs > 0 ? setTimeout(() => child.kill('SIGTERM'), timeoutMs) : null;
    child.stdout.on('data', chunk => { stdout += chunk; logger?.event('STDOUT', { message: chunk.toString().trimEnd() }); });
    child.stderr.on('data', chunk => { stderr += chunk; logger?.event('STDERR', { message: chunk.toString().trimEnd() }); });
    child.once('error', error => { if (timer) clearTimeout(timer); reject(error); });
    child.once('close', code => {
      if (timer) clearTimeout(timer);
      const result = { command, args, code: code ?? 1, stdout, stderr };
      if (result.code !== 0 && !allowFailure) {
        const error = new Error(`${command} exited with ${result.code}: ${stderr.trim() || stdout.trim()}`);
        error.result = result;
        reject(error);
      } else resolve(result);
    });
  });
}
