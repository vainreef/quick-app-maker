import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createElectronApp, slugify } from './src/index.mjs';
const root = path.resolve('.cache', `generator-test-${process.pid}-${Date.now()}`); fs.mkdirSync(root, { recursive: true });
test('slugify is deterministic', () => { assert.equal(slugify('My App 2'), 'my-app-2'); assert.throws(() => slugify('中文'), /slug/); });
test('generator writes a runnable no-bundler app', () => { const app = createElectronApp({ workspace: root, name: 'Demo App', slug: 'demo-app' }); const pkg = JSON.parse(fs.readFileSync(path.join(app, 'package.json'), 'utf8')); assert.equal(pkg.main, 'src/main/main.cjs'); assert.equal(fs.existsSync(path.join(app, 'src/renderer/index.html')), true); assert.equal(JSON.parse(fs.readFileSync(path.join(app, 'store/desired-state.json'), 'utf8')).assets.screenshot, 'store/assets/Screenshot.png'); });
test('generator substitutes app identity in manifest', () => { const app = createElectronApp({ workspace: root, name: 'Demo Two', slug: 'demo-two' }); const manifest = fs.readFileSync(path.join(app, 'store/Package.appxmanifest'), 'utf8'); assert.match(manifest, /DisplayName>Demo Two</); assert.match(manifest, /Executable="demo-two\.exe"/); });
test('generator preserves existing living README.md in app directory', () => {
  const appDir = path.join(root, 'demo-living');
  fs.mkdirSync(appDir, { recursive: true });
  fs.writeFileSync(path.join(appDir, 'README.md'), '# Living Contract\nOriginal content', 'utf8');
  const app = createElectronApp({ workspace: root, name: 'Demo Living', slug: 'demo-living' });
  const readme = fs.readFileSync(path.join(app, 'README.md'), 'utf8');
  assert.equal(readme, '# Living Contract\nOriginal content');
  assert.equal(fs.existsSync(path.join(app, 'src/renderer/index.html')), true);
});
