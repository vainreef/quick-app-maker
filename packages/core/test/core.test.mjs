import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { assertWithin, writeAtomic, readJson, configureNpmEnvironment } from '../src/index.mjs';

const root = path.resolve('.cache', `core-test-${process.pid}`);
fs.mkdirSync(root, { recursive: true });
test('path policy rejects escapes', () => { assert.throws(() => assertWithin(root, path.join(root, '..', 'outside')), /outside/); });
test('atomic JSON write is readable', () => { const file = writeAtomic(root, path.join(root, 'state.json'), { ok: true }); assert.deepEqual(readJson(file), { ok: true }); });
test('npm environment stays in workspace', () => { const env = configureNpmEnvironment(root, { npm: { registry: 'https://registry.npmmirror.com', cacheDirectory: '.cache/npm' }, electron: { mirror: 'https://npmmirror.com/mirrors/electron/' } }); assert.equal(env.npm_config_registry, 'https://registry.npmmirror.com'); assert.equal(path.dirname(env.npm_config_cache), path.join(root, '.cache')); });
