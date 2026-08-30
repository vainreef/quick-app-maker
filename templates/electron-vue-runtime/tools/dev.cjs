const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const electron = require('electron');
const root = path.resolve(__dirname, '..');
let child;
let restarting = false;
function start() {
  child = spawn(electron, ['.'], { cwd: root, env: { ...process.env }, stdio: 'inherit', windowsHide: false });
  child.once('exit', code => { if (!restarting) process.exit(code ?? 0); });
}
function restart() {
  if (restarting) return;
  restarting = true;
  stopChild();
  setTimeout(() => { restarting = false; start(); }, 120);
}
function stopChild() { if (!child || child.exitCode !== null) return; if (process.platform === 'win32') spawnSync('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); else child.kill('SIGTERM'); }
start();
for (const dir of ['src/main', 'src/preload', 'src/renderer']) {
  try { fs.watch(path.join(root, dir), { recursive: true }, (_event, file) => { if (file) restart(); }); } catch { fs.watch(path.join(root, dir), (_event, file) => { if (file) restart(); }); }
}
process.once('SIGINT', () => { restarting = true; stopChild(); process.exit(130); });
