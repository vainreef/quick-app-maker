import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(fileURLToPath(new URL('../../../', import.meta.url)));

test('bootstrap contract installs dependencies before qam and uses portable Git', () => {
  const text = fs.readFileSync(path.join(root, 'bootstrap', 'entry.ps1'), 'utf8');
  assert.doesNotMatch(text, /Get-Command\s+git\.exe/);
  assert.doesNotMatch(text, /Get-Command\s+curl\.exe/);
  assert.match(text, /cmd\\git\.exe/);
  assert.match(text, /git version 2\.47\.1\.windows\.1/);
  assert.match(text, /npm-cli\.js/);
  assert.match(text, /--prefix\s+\$Destination\s+ci/);
  assert.match(text, /QAM_REQUIRE_PORTABLE/);
});

test('qam resolves the toolchain lock from the engine, not the workspace', () => {
  const text = fs.readFileSync(path.join(root, 'bin', 'qam.mjs'), 'utf8');
  assert.match(text, /const TOOLCHAIN = loadToolchain\(ROOT\)/);
  assert.doesNotMatch(text, /configureNpmEnvironment\(workspace\);/);
  assert.match(text, /configureNpmEnvironment\(workspace, TOOLCHAIN\)/);
});

test('store packaging does not launch npx command shims directly', () => {
  const text = fs.readFileSync(path.join(root, 'templates', 'electron-vue-runtime', 'tools', 'package-store.cjs'), 'utf8');
  assert.doesNotMatch(text, /npx\.cmd|npm\.cmd|--no-install/);
  assert.doesNotMatch(text, /process\.execPath/);
  assert.match(text, /workspaceNode/);
  assert.match(text, /@microsoft.*winappcli.*dist.*cli\.js/);
});

test('portable command wrapper bootstraps workspace dependencies before qam', () => {
  const text = fs.readFileSync(path.join(root, 'bootstrap', 'qam.cmd'), 'utf8');
  assert.match(text, /node\\node\.exe/i);
  assert.match(text, /npm-cli\.js/i);
  assert.match(text, /--prefix/);
  assert.match(text, /ci --ignore-scripts --prefer-offline/i);
  assert.match(text, /QAM_REQUIRE_PORTABLE/);
});

test('development wrapper leaves DevTools opt-in', () => {
  const text = fs.readFileSync(path.join(root, 'templates', 'electron-vue-runtime', 'tools', 'dev.cjs'), 'utf8');
  assert.doesNotMatch(text, /QAM_DEVTOOLS:\s*['"]1['"]/);
});

test('store run shares one deadline across all phases', () => {
  const text = fs.readFileSync(path.join(root, 'bin', 'qam.mjs'), 'utf8');
  assert.match(text, /const deadline = new Deadline\([^;]+\);\s*for\s*\(const phase of PHASES\)/);
  assert.match(text, /apply,\s*deadline\s*\}\)/);
});
