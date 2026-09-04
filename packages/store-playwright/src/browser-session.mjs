import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, execFileSync } from 'node:child_process';
import { assertWithin, writeAtomic } from '@quick-app/core';
import { resolveBrowserType } from './browser-types.mjs';
import { freePort, resolveBrowserPath, buildBrowserLaunchArgs } from './browser-launcher.mjs';

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

export class BrowserSession {
  constructor({
    workspace,
    stateDir,
    baseUrl = 'https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview',
    browserType = null,
    browserPath = null,
    logger = null
  } = {}) {
    this.workspace = workspace;
    this.browserType = resolveBrowserType({ option: browserType });
    this.stateDir = assertWithin(workspace, path.resolve(workspace, stateDir ?? '.cache/qam/session'), 'session');
    this.baseUrl = baseUrl;
    this.browserPath = resolveBrowserPath(this.browserType, browserPath);
    this.logger = logger;
    this.session = null;
    this.browser = null;
    this.context = null;
    this.page = null;
    this.diagnosticPage = null;
    fs.mkdirSync(this.stateDir, { recursive: true });
  }

  statePath() {
    return path.join(this.stateDir, 'session.json');
  }

  async existing() {
    if (!fs.existsSync(this.statePath())) throw new Error('No active session state file');
    const value = JSON.parse(fs.readFileSync(this.statePath(), 'utf8'));
    if (!value?.port) throw new Error('Invalid session state');
    const response = await fetch(`http://127.0.0.1:${value.port}/json/version`);
    if (!response.ok) throw new Error('Browser DevTools endpoint is stale');
    return value;
  }

  async ensure() {
    try {
      this.session = await this.existing();
    } catch {
      await this.cleanupStale();
      this.session = await this.start();
    }
    this.logger?.info('browser-session', {
      browserType: this.browserType,
      pid: this.session.pid,
      port: this.session.port,
      profile: this.session.profile
    });
    return this.session;
  }

  async start() {
    const port = await freePort();
    const runId = `${Date.now()}-${process.pid}`;
    let profile = null;
    if (fs.existsSync(this.statePath())) {
      try {
        const oldSession = JSON.parse(fs.readFileSync(this.statePath(), 'utf8'));
        if (oldSession?.profile && fs.existsSync(oldSession.profile)) {
          profile = oldSession.profile;
        }
      } catch {}
    }
    if (!profile && fs.existsSync(this.stateDir)) {
      const candidates = fs.readdirSync(this.stateDir)
        .filter(f => f.startsWith(`profile-${this.browserType}-`))
        .sort().reverse();
      if (candidates.length > 0) {
        profile = path.join(this.stateDir, candidates[0]);
      }
    }
    if (!profile) {
      profile = assertWithin(
        this.workspace,
        path.join(this.stateDir, `profile-${this.browserType}-${runId}`),
        'browser profile'
      );
      fs.mkdirSync(profile, { recursive: true });
    }

    const args = buildBrowserLaunchArgs({
      browserType: this.browserType,
      port,
      profile,
      url: this.baseUrl
    });

    let childPid = 0;
    if (process.platform === 'win32' && process.env.QAM_DEFAULT_DESKTOP !== '0') {
      const pidFile = path.join(this.stateDir, `browser-${runId}.pid`);
      const bridge = resolveBridge(this.workspace);
      execFileSync(
        'powershell.exe',
        [
          '-NoProfile',
          '-ExecutionPolicy',
          'Bypass',
          '-File',
          bridge,
          '-EdgePath',
          this.browserPath,
          '-Port',
          String(port),
          '-Profile',
          profile,
          '-Url',
          this.baseUrl,
          '-PidFile',
          pidFile
        ],
        { stdio: 'inherit' }
      );
      await waitForFile(pidFile, 15_000);
      childPid = Number(fs.readFileSync(pidFile, 'utf8').trim());
    } else {
      const child = spawn(this.browserPath, args, { detached: true, stdio: 'ignore', windowsHide: false });
      child.unref();
      childPid = child.pid ?? 0;
    }

    const startedAt = Date.now();
    let lastError = null;
    while (Date.now() - startedAt < 45_000) {
      try {
        const response = await fetch(`http://127.0.0.1:${port}/json/version`);
        if (response.ok) {
          const value = {
            version: 1,
            browserType: this.browserType,
            browserPath: this.browserPath,
            pid: childPid,
            port,
            profile,
            owner: runId,
            startedAt: new Date().toISOString()
          };
          writeAtomic(this.workspace, this.statePath(), value);
          return value;
        }
      } catch (err) {
        lastError = err;
      }
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(
      `${this.browserType} DevTools endpoint did not start on port ${port}: ${lastError?.message ?? 'timeout'}`
    );
  }

  async connect() {
    await this.ensure();
    let playwright;
    try {
      playwright = await import('playwright-core');
    } catch (error) {
      throw new Error(`playwright-core is not installed: ${error.message}`);
    }

    this.browser = await playwright.chromium.connectOverCDP(`http://127.0.0.1:${this.session.port}`, {
      timeout: 30_000,
      isLocal: true
    });
    this.context = this.browser.contexts()[0];
    if (!this.context) throw new Error('Browser has no active browser context');

    const pages = this.context.pages();
    this.page = pages.find(item => item.url().includes('partner.microsoft.com')) || pages[0];
    if (!this.page) this.page = await this.context.newPage();
    this.attachDiagnostics(this.page);
    return { browser: this.browser, context: this.context, page: this.page, session: this.session };
  }

  async openTab(url) {
    if (!this.context) await this.connect();
    const newPage = await this.context.newPage();
    this.attachDiagnostics(newPage);
    if (url) await newPage.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    this.page = newPage;
    return newPage;
  }

  findTab(urlPattern) {
    if (!this.context) return null;
    const regex = typeof urlPattern === 'string' ? new RegExp(urlPattern, 'i') : urlPattern;
    const pages = this.context.pages();
    return pages.find(p => regex.test(p.url())) ?? null;
  }

  async ensureTab(urlPattern, defaultUrl) {
    if (!this.context) await this.connect();
    const existing = this.findTab(urlPattern);
    if (existing) {
      await existing.bringToFront();
      this.page = existing;
      return existing;
    }
    return await this.openTab(defaultUrl);
  }

  async closeTab(pageToClose = this.page) {
    if (pageToClose) {
      try { await pageToClose.close(); } catch {}
      if (this.page === pageToClose) {
        this.page = this.context?.pages()[0] ?? null;
      }
    }
  }

  attachDiagnostics(page) {
    if (!page || this.diagnosticPage === page) return;
    this.diagnosticPage = page;
    page.on('pageerror', error =>
      this.logger?.event('BROWSER_PAGE_ERROR', { message: redact(error.message), stack: redact(error.stack || '') })
    );
    page.on('requestfailed', request =>
      this.logger?.event('BROWSER_REQUEST_FAILED', {
        url: redact(request.url()),
        error: redact(request.failure()?.errorText || '')
      })
    );
  }

  async close() {
    try { await this.browser?.close(); } catch {}
    const pid = Number(this.session?.pid || 0);
    if (pid > 0) {
      try {
        if (process.platform === 'win32') {
          execFileSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
        } else {
          process.kill(pid, 'SIGTERM');
        }
      } catch {}
    }
    try { fs.rmSync(this.statePath(), { force: true }); } catch {}
  }

  async cleanupStale() {
    let value;
    try {
      value = JSON.parse(fs.readFileSync(this.statePath(), 'utf8'));
    } catch {
      value = null;
    }
    const pid = Number(value?.pid || 0);
    if (pid > 0) {
      try {
        if (process.platform === 'win32') {
          execFileSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
        } else {
          process.kill(pid, 'SIGTERM');
        }
      } catch {}
    }
    try { fs.rmSync(this.statePath(), { force: true }); } catch {}
  }
}

export { BrowserSession as EdgeSession };

async function waitForFile(file, timeoutMs) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    if (fs.existsSync(file)) return;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(`Desktop bridge did not write PID file: ${file}`);
}

function redact(value) {
  return String(value ?? '').replace(/(authorization|token|cookie|secret|password)(\s*[:=]\s*)[^\s,;]+/gi, '$1$2[REDACTED]');
}
