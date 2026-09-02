import test from 'node:test';
import assert from 'node:assert/strict';
import { PHASES, normalizePhase } from '@quick-app/store-core';
import { phaseAdapters } from './src/phases.mjs';
import { waitUntil } from './src/inspector.mjs';
import { resolveBrowserType, normalizeBrowserType, SUPPORTED_BROWSERS } from './src/browser-types.mjs';
import { resolveBrowserPath, buildBrowserLaunchArgs } from './src/browser-launcher.mjs';
import { BrowserSession, EdgeSession } from './src/browser-session.mjs';

test('phase names normalize once', () => { assert.equal(normalizePhase('ageRatings'), 'age-ratings'); assert.deepEqual(PHASES.length, 6); });
test('phase adapter registry exposes one uniform interface', () => { const driver = {}; const adapters = phaseAdapters(driver); for (const phase of PHASES) assert.ok(adapters[phase].acceptedPageKinds.length); });
test('non-retryable polling errors fail immediately', async () => { const started = Date.now(); await assert.rejects(waitUntil(async () => { throw Object.assign(new Error('fatal fixture error'), { retryable: false }); }, { timeoutMs: 5_000, intervalMs: 1_000 }), /fatal fixture error/); assert.ok(Date.now() - started < 1_000); });

test('browser type resolver defaults to chrome and handles configuration', () => {
  assert.equal(resolveBrowserType(), 'chrome');
  assert.equal(resolveBrowserType({ option: 'edge' }), 'edge');
  assert.equal(resolveBrowserType({ option: 'google-chrome' }), 'chrome');
  assert.equal(resolveBrowserType({ option: 'safari' }), 'safari');
  assert.equal(resolveBrowserType({ env: { QAM_BROWSER: 'edge' } }), 'edge');
  assert.equal(resolveBrowserType({ config: { site: { browser: 'edge' } } }), 'edge');
  assert.throws(() => normalizeBrowserType('firefox'), /Unsupported browser/);
  assert.deepEqual(SUPPORTED_BROWSERS, ['chrome', 'edge', 'safari']);
});

test('browser launcher resolves path and builds launch args', () => {
  const chromePath = resolveBrowserPath('chrome');
  assert.ok(typeof chromePath === 'string' && chromePath.length > 0);
  const args = buildBrowserLaunchArgs({
    browserType: 'chrome',
    port: 9222,
    profile: '/tmp/test-profile',
    url: 'https://example.com'
  });
  assert.ok(args.includes('--remote-debugging-port=9222'));
  assert.ok(args.includes('--user-data-dir=/tmp/test-profile'));
  assert.ok(args.includes('https://example.com'));
});

test('browser session instantiates with resolved browser and supports edge alias', () => {
  const session = new BrowserSession({ workspace: process.cwd(), browserType: 'chrome' });
  assert.equal(session.browserType, 'chrome');
  assert.equal(EdgeSession, BrowserSession);
});
