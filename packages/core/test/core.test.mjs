import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { assertWithin, writeAtomic, readJson, configureNpmEnvironment, npmInvocation, run, WorkspaceLock, Logger } from '../src/index.mjs';

const root = path.resolve('.cache', `core-test-${process.pid}`);
fs.mkdirSync(root, { recursive: true });
test('path policy rejects escapes', () => { assert.throws(() => assertWithin(root, path.join(root, '..', 'outside')), /outside/); });
test('atomic JSON write is readable', () => { const file = writeAtomic(root, path.join(root, 'state.json'), { ok: true }); assert.deepEqual(readJson(file), { ok: true }); });
test('npm environment stays in workspace', () => { const env = configureNpmEnvironment(root, { npm: { registry: 'https://registry.npmmirror.com', cacheDirectory: '.cache/npm' }, electron: { mirror: 'https://npmmirror.com/mirrors/electron/' } }); assert.equal(env.npm_config_registry, 'https://registry.npmmirror.com'); assert.equal(path.dirname(env.npm_config_cache), path.join(root, '.cache')); });
test('npm environment requires the engine toolchain lock', () => { assert.throws(() => configureNpmEnvironment(root), /toolchain lock/); });
test('npm invocation uses the bundled Node CLI instead of a command shim', () => { const nodePath = path.join(root, 'portable-node', process.platform === 'win32' ? 'node.exe' : 'bin/node'); const npmCli = path.join(path.dirname(nodePath), 'node_modules', 'npm', 'bin', 'npm-cli.js'); fs.mkdirSync(path.dirname(npmCli), { recursive: true }); fs.writeFileSync(npmCli, ''); const invocation = npmInvocation(['ci'], { workspace: root, nodePath }); assert.equal(invocation.command, nodePath); assert.deepEqual(invocation.args, [npmCli, 'ci']); });
test('process start errors include the exact command and working directory', async () => { const missing = path.join(root, 'missing-command'); await assert.rejects(run(missing, ['--probe'], { cwd: root }), error => error.message.includes(missing) && error.message.includes(root) && error.result?.command === missing); });
test('workspace lock prevents overlapping writers and releases cleanly', () => { const lockRoot = path.join(root, `lock-${Date.now()}`); const first = new WorkspaceLock(lockRoot, 'app-demo').acquire(); assert.throws(() => new WorkspaceLock(lockRoot, 'app-demo').acquire(), /workspace is busy/); first.release(); const second = new WorkspaceLock(lockRoot, 'app-demo').acquire(); second.release(); });
test('logger writes a compact result artifact beside events', () => { const logRoot = path.join(root, `logger-${Date.now()}`); const logger = new Logger({ runId: 'logger-test', root: logRoot, quiet: true }); logger.pass('done', { code: 0 }); const result = readJson(path.join(logRoot, 'result.json')); assert.equal(result.runId, 'logger-test'); assert.equal(result.status, 'complete'); assert.equal(result.ok, true); });
