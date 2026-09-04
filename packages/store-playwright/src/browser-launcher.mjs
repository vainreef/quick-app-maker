import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { execFileSync } from 'node:child_process';
import { normalizeBrowserType, DEFAULT_BROWSER } from './browser-types.mjs';

export async function freePort() {
  return await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

export function resolveBrowserPath(browserType = DEFAULT_BROWSER, customPath = null) {
  const type = normalizeBrowserType(browserType);
  if (customPath) {
    if (fs.existsSync(customPath)) return customPath;
    throw new Error(`Custom browser path not found: ${customPath}`);
  }

  const platform = process.platform;

  if (type === 'chrome') {
    if (platform === 'darwin') {
      const candidates = [
        '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        `${process.env.HOME}/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`,
        '/Applications/Google Chrome Canary.app/Contents/MacOS/Google Chrome Canary',
        '/Applications/Chromium.app/Contents/MacOS/Chromium'
      ];
      for (const c of candidates) if (fs.existsSync(c)) return c;
      const which = probeCommand('which', ['google-chrome', 'google-chrome-stable', 'chromium']);
      if (which) return which;
      return '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
    }
    if (platform === 'win32') {
      const roots = [
        process.env.ProgramFiles,
        process.env['ProgramFiles(x86)'],
        process.env.LocalAppData ? path.join(process.env.LocalAppData, 'Google', 'Chrome', 'Application') : null
      ].filter(Boolean);
      for (const root of roots) {
        const candidate = root.endsWith('chrome.exe') ? root : path.join(root, 'Google', 'Chrome', 'Application', 'chrome.exe');
        if (fs.existsSync(candidate)) return candidate;
      }
      const found = probeCommand('where.exe', ['chrome.exe']);
      if (found) return found;
      return 'chrome.exe';
    }
    const found = probeCommand('which', ['google-chrome', 'google-chrome-stable', 'chromium-browser', 'chromium']);
    if (found) return found;
    return 'google-chrome';
  }

  if (type === 'edge') {
    if (platform === 'win32') {
      const roots = [
        process.env.ProgramFiles,
        process.env['ProgramFiles(x86)'],
        process.env.LocalAppData
      ].filter(Boolean);
      for (const root of roots) {
        const candidate = root.endsWith('msedge.exe') ? root : path.join(root, 'Microsoft', 'Edge', 'Application', 'msedge.exe');
        if (fs.existsSync(candidate)) return candidate;
      }
      const found = probeCommand('where.exe', ['msedge.exe']);
      if (found) return found;
      return 'msedge.exe';
    }
    if (platform === 'darwin') {
      const candidates = [
        '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
        `${process.env.HOME}/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge`
      ];
      for (const c of candidates) if (fs.existsSync(c)) return c;
      const which = probeCommand('which', ['msedge', 'microsoft-edge']);
      if (which) return which;
      return '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge';
    }
    const found = probeCommand('which', ['msedge', 'microsoft-edge']);
    if (found) return found;
    return 'msedge';
  }

  if (type === 'safari') {
    if (platform === 'darwin') {
      const safariApp = '/Applications/Safari.app/Contents/MacOS/Safari';
      if (fs.existsSync(safariApp)) return safariApp;
    }
    throw new Error('Safari is only available on macOS platforms');
  }

  throw new Error(`Unsupported browser type: ${type}`);
}

function probeCommand(runner, binaries) {
  for (const bin of binaries) {
    try {
      const out = execFileSync(runner, [bin], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).split(/\r?\n/).find(Boolean);
      if (out && fs.existsSync(out.trim())) return out.trim();
    } catch {}
  }
  return null;
}

export function buildBrowserLaunchArgs({ browserType = DEFAULT_BROWSER, port, profile, url, extraArgs = [] }) {
  const type = normalizeBrowserType(browserType);
  if (type === 'chrome' || type === 'edge') {
    return [
      `--user-data-dir=${profile}`,
      `--remote-debugging-port=${port}`,
      '--remote-debugging-address=127.0.0.1',
      '--remote-allow-origins=http://localhost,http://127.0.0.1',
      '--no-first-run',
      '--no-default-browser-check',
      '--start-maximized',
      ...extraArgs,
      url
    ];
  }
  return [...extraArgs, url];
}
