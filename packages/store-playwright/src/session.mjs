import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { spawn, execFileSync } from 'node:child_process';
import { assertWithin, writeAtomic } from '@quick-app/core';

export async function freePort() {
  return await new Promise((resolve, reject) => { const server = net.createServer(); server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); }); server.on('error', reject); });
}

function resolveBridge(workspace) {
  const candidates = [
    path.resolve(workspace, 'bootstrap', 'launch-default-desktop.ps1'),
    path.resolve(workspace, 'quick-app-maker', 'bootstrap', 'launch-default-desktop.ps1'),
    fileURLToPath(new URL('../../../bootstrap/launch-default-desktop.ps1', import.meta.url))
  ];
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  return candidates[0];
}

export class EdgeSession {
  constructor({ workspace, stateDir, baseUrl = 'https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview', edgePath = process.env.EDGE_PATH, logger = null } = {}) {
    this.workspace = workspace; this.stateDir = assertWithin(workspace, path.resolve(workspace, stateDir ?? '.cache/qam/session'), 'session'); this.baseUrl = baseUrl; this.edgePath = resolveEdgePath(edgePath); this.logger = logger; this.session = null; this.browser = null; this.context = null; this.page = null;
    fs.mkdirSync(this.stateDir, { recursive: true });
  }
  statePath() { return path.join(this.stateDir, 'session.json'); }
  async existing() {
    const value = JSON.parse(fs.readFileSync(this.statePath(), 'utf8'));
    const response = await fetch(`http://127.0.0.1:${value.port}/json/version`);
    if (!response.ok) throw new Error('Edge DevTools endpoint is stale');
    return value;
  }
  async ensure() {
    try { this.session = await this.existing(); }
    catch { await this.cleanupStale(); this.session = await this.start(); }
    this.logger?.info('edge-session', { pid: this.session.pid, port: this.session.port, profile: this.session.profile });
    return this.session;
  }
  async start() {
    const port = await freePort(); const runId = `${Date.now()}-${process.pid}`; const profile = assertWithin(this.workspace, path.join(this.stateDir, `profile-${runId}`), 'edge profile'); fs.mkdirSync(profile, { recursive: true });
    const args = [`--user-data-dir=${profile}`, `--remote-debugging-port=${port}`, '--remote-debugging-address=127.0.0.1', '--remote-allow-origins=http://localhost,http://127.0.0.1', '--no-first-run', '--no-default-browser-check', '--start-maximized', this.baseUrl];
    let childPid = 0;
    if (process.platform === 'win32' && process.env.QAM_DEFAULT_DESKTOP !== '0') {
      const pidFile = path.join(this.stateDir, `edge-${runId}.pid`); const bridge = resolveBridge(this.workspace);
      execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', bridge, '-EdgePath', this.edgePath, '-Port', String(port), '-Profile', profile, '-Url', this.baseUrl, '-PidFile', pidFile], { stdio: 'inherit' });
      await waitForFile(pidFile, 15_000); childPid = Number(fs.readFileSync(pidFile, 'utf8').trim());
    } else {
      const child = spawn(this.edgePath, args, { detached: true, stdio: 'ignore', windowsHide: false }); child.unref(); childPid = child.pid ?? 0;
    }
    const startedAt = Date.now(); let last;
    while (Date.now() - startedAt < 45_000) {
        try { const response = await fetch(`http://127.0.0.1:${port}/json/version`); if (response.ok) { const value = { version: 1, pid: childPid, port, profile, owner: runId, startedAt: new Date().toISOString() }; writeAtomic(this.workspace, this.statePath(), value); return value; } }
      catch (error) { last = error; }
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`Edge DevTools endpoint did not start: ${last?.message ?? 'timeout'}`);
  }
  async connect() {
    await this.ensure();
    let playwright;
    try { playwright = await import('playwright-core'); }
    catch (error) { throw new Error(`playwright-core is not installed: ${error.message}`); }
    this.browser = await playwright.chromium.connectOverCDP(`http://127.0.0.1:${this.session.port}`, { timeout: 30_000, isLocal: true });
    this.context = this.browser.contexts()[0];
    if (!this.context) throw new Error('Edge has no browser context');
    const pages = this.context.pages(); this.page = pages.find(item => item.url().includes('partner.microsoft.com')) || pages[0];
    if (!this.page) this.page = await this.context.newPage();
    this.attachDiagnostics(this.page);
    return { browser: this.browser, context: this.context, page: this.page, session: this.session };
  }
  attachDiagnostics(page) {
    if (this.diagnosticPage === page) return;
    this.diagnosticPage = page;
    page.on('console', message => this.logger?.event('BROWSER_CONSOLE', { level: message.type(), text: redact(message.text()) }));
    page.on('pageerror', error => this.logger?.event('BROWSER_PAGE_ERROR', { message: redact(error.message), stack: redact(error.stack || '') }));
    page.on('requestfailed', request => this.logger?.event('BROWSER_REQUEST_FAILED', { url: redact(request.url()), error: redact(request.failure()?.errorText || '') }));
  }
  async close() {
    try { await this.browser?.close(); } catch {}
    const pid = Number(this.session?.pid || 0);
    if (pid > 0) {
      try { if (process.platform === 'win32') execFileSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' }); else process.kill(pid, 'SIGTERM'); } catch {}
    }
    try { fs.rmSync(this.statePath(), { force: true }); } catch {}
  }
  async cleanupStale() {
    let value; try { value = JSON.parse(fs.readFileSync(this.statePath(), 'utf8')); } catch { value = null; }
    const pid = Number(value?.pid || 0); if (pid > 0) { try { if (process.platform === 'win32') execFileSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' }); else process.kill(pid, 'SIGTERM'); } catch {} }
    try { fs.rmSync(this.statePath(), { force: true }); } catch {}
  }
}

async function waitForFile(file, timeoutMs) { const end = Date.now() + timeoutMs; while (Date.now() < end) { if (fs.existsSync(file)) return; await new Promise(resolve => setTimeout(resolve, 100)); } throw new Error(`desktop bridge did not write PID file: ${file}`); }

function resolveEdgePath(value) {
  if (value) return value;
  try { const found = execFileSync(process.platform === 'win32' ? 'where.exe' : 'which', ['msedge.exe'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).split(/\r?\n/).find(Boolean); if (found) return found.trim(); } catch {}
  if (process.platform === 'win32') {
    for (const root of [process.env.ProgramFiles, process.env['ProgramFiles(x86)']]) { if (root) { const candidate = path.join(root, 'Microsoft', 'Edge', 'Application', 'msedge.exe'); if (fs.existsSync(candidate)) return candidate; } }
  }
  return 'msedge.exe';
}

function redact(value) {
  return String(value ?? '').replace(/(authorization|token|cookie|secret|password)(\s*[:=]\s*)[^\s,;]+/gi, '$1$2[REDACTED]');
}
