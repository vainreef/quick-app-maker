const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
function read(name) { return fs.readFileSync(path.join(root, name), 'utf8'); }

test('template contains a secure preload contract', () => {
  const main = read('src/main/main.cjs');
  const preload = read('src/preload/preload.cjs');
  assert.match(main, /contextIsolation:\s*true/);
  assert.match(main, /sandbox:\s*true/);
  assert.match(main, /nodeIntegration:\s*false/);
  assert.match(preload, /contextBridge\.exposeInMainWorld/);
});

test('template keeps every Vue binding inside the mount root', () => {
  const html = read('src/renderer/index.html');
  const mount = html.match(/<main id="app"[\s\S]*?<\/main>/)?.[0];
  assert.ok(mount, '#app mount root is required');
  assert.match(mount, /v-model/);
  assert.match(mount, /@click/);
  const outside = html.slice(html.indexOf('</main>') + '</main>'.length, html.lastIndexOf('<script'));
  assert.doesNotMatch(outside, /\{\{|v-|@\w+=/);
});

test('template CSP allows Vue runtime compiler', () => {
  const html = read('src/renderer/index.html');
  const csp = html.match(/<meta http-equiv="Content-Security-Policy" content="([^"]+)"/)?.[1] || '';
  assert.ok(csp, 'Content-Security-Policy meta tag is required');
  assert.match(csp, /'unsafe-eval'/, 'CSP must allow unsafe-eval for in-browser Vue runtime template compilation');
});

test('template has explicit loading and persistence error paths', () => {
  const html = read('src/renderer/index.html');
  const js = read('src/renderer/app.js');
  assert.match(html, /v-if="loading"/);
  assert.match(html, /role="alert"/);
  assert.match(js, /await window\.qam\.loadState/);
  assert.match(js, /await window\.qam\.saveState/);
  assert.match(js, /失败/);
});

test('development wrapper leaves DevTools opt-in', () => {
  assert.doesNotMatch(read('tools/dev.cjs'), /QAM_DEVTOOLS:\s*['"]1['"]/);
});
