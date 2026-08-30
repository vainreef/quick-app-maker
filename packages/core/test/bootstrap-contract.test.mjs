import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(fileURLToPath(new URL('../../../', import.meta.url)));

test('bootstrap contract installs dependencies before qam and uses portable Git', () => {
  const text = fs.readFileSync(path.join(root, 'bootstrap', 'entry.ps1'), 'utf8');
  assert.doesNotMatch(text, /Get-Command\s+git\.exe/);
  assert.match(text, /cmd\\git\.exe/);
  assert.match(text, /git version 2\.47\.1\.windows\.1/);
  assert.match(text, /npm-cli\.js/);
  assert.match(text, /--prefix\s+\$Destination\s+ci/);
  assert.match(text, /QAM_REQUIRE_PORTABLE/);
});

test('store packaging does not launch npx command shims directly', () => {
  const text = fs.readFileSync(path.join(root, 'templates', 'electron-vue-runtime', 'tools', 'package-store.cjs'), 'utf8');
  assert.doesNotMatch(text, /npx\.cmd|npm\.cmd|--no-install/);
  assert.match(text, /process\.execPath/);
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
