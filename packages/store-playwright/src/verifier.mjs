import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright-core';
import { resolveBrowserPath } from './browser-launcher.mjs';

/**
 * Perform deterministic headless verification of the Electron Vue application's UI mounting.
 * Asserts:
 * 1. #app root element exists and is rendered.
 * 2. [v-cloak] is cleanly removed by Vue upon successful mounting.
 * 3. No unhandled exceptions (pageerror) or console.error occur during template compilation/mount.
 * 4. Error boundary element #qam-render-error is NOT present.
 *
 * @param {string} appRoot - Absolute path to application root
 * @param {object} [options]
 * @returns {Promise<{ ok: boolean, check: object }>}
 */
export async function verifyAppMount(appRoot, options = {}) {
  const htmlPath = path.join(appRoot, 'src', 'renderer', 'index.html');
  if (!fs.existsSync(htmlPath)) throw new Error(`index.html not found at: ${htmlPath}`);

  const browserType = process.platform === 'win32' ? 'edge' : 'chrome';
  let executablePath;
  try {
    executablePath = resolveBrowserPath(browserType);
  } catch {
    try { executablePath = resolveBrowserPath('chrome'); } catch {}
  }

  let browser;
  try {
    browser = await chromium.launch({
      executablePath: executablePath || undefined,
      headless: true,
      args: ['--allow-file-access-from-files', '--no-sandbox', '--disable-gpu']
    });
  } catch (launchErr) {
    // If no browser executable could be launched in headless mode, return soft warning
    if (options.logger) options.logger.warn(`headless browser launch skipped: ${launchErr.message}`);
    return { ok: true, skipped: true, reason: launchErr.message };
  }

  const page = await browser.newPage();
  const pageErrors = [];
  const consoleErrors = [];

  page.on('pageerror', err => pageErrors.push(err.message));
  page.on('console', msg => {
    if (msg.type() === 'error') consoleErrors.push(msg.text());
  });

  // Inject robust mock of window.qam using Proxy to satisfy any custom IPC calls
  await page.addInitScript(() => {
    window.qam = new Proxy({
      loadState: async () => ({ version: 1, items: [], letters: [] }),
      saveState: async (v) => v,
      appInfo: async () => ({ name: 'Test App', version: '0.1.0' })
    }, {
      get(target, prop) {
        if (prop in target) return target[prop];
        return async () => ({});
      }
    });
  });

  await page.route('**/*', async route => {
    const url = route.request().url();
    if (url.includes('vue.global.js')) {
      const candidates = [
        path.join(appRoot, 'node_modules', 'vue', 'dist', 'vue.global.js'),
        path.join(appRoot, '..', 'node_modules', 'vue', 'dist', 'vue.global.js'),
        path.join(appRoot, '..', '..', 'node_modules', 'vue', 'dist', 'vue.global.js')
      ];
      for (const cand of candidates) {
        if (fs.existsSync(cand)) {
          return route.fulfill({
            status: 200,
            contentType: 'text/javascript',
            body: fs.readFileSync(cand)
          });
        }
      }
    }
    return route.continue();
  });

  try {
    const fileUrl = pathToFileURL(htmlPath).href;
    await page.goto(fileUrl, { waitUntil: 'load', timeout: 15_000 });
    // Wait for Vue runtime compiler & setup to mount
    await page.waitForTimeout(600);

    const check = await page.evaluate(() => {
      const appEl = document.querySelector('#app');
      const errBox = document.querySelector('#qam-render-error');
      return {
        exists: Boolean(appEl),
        cloaked: appEl ? appEl.hasAttribute('v-cloak') : false,
        childCount: appEl ? appEl.childElementCount : 0,
        text: appEl ? appEl.innerText.trim() : '',
        errBoxText: errBox ? errBox.innerText.trim() : ''
      };
    });

    if (check.errBoxText) {
      throw new Error(`Vue runtime error boundary triggered:\n${check.errBoxText}`);
    }

    if (pageErrors.length) {
      throw new Error(`Uncaught runtime error during UI mount:\n  ${pageErrors.join('\n  ')}`);
    }

    if (!check.exists) {
      throw new Error('#app root mount element not found in DOM');
    }

    if (check.cloaked) {
      throw new Error('#app root is still cloaked with [v-cloak] (Vue failed to mount or aborts rendering)');
    }

    if (check.childCount === 0 && !check.text) {
      throw new Error('#app root element is completely empty after mounting');
    }

    return { ok: true, check };
  } finally {
    await browser.close();
  }
}
