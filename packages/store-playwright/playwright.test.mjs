import test from 'node:test';
import assert from 'node:assert/strict';
import { PHASES, normalizePhase } from '@quick-app/store-core';
import { phaseAdapters } from './src/phases.mjs';
import { waitUntil } from './src/inspector.mjs';
test('phase names normalize once', () => { assert.equal(normalizePhase('ageRatings'), 'age-ratings'); assert.deepEqual(PHASES.length, 6); });
test('phase adapter registry exposes one uniform interface', () => { const driver = {}; const adapters = phaseAdapters(driver); for (const phase of PHASES) assert.ok(adapters[phase].acceptedPageKinds.length); });
test('non-retryable polling errors fail immediately', async () => { const started = Date.now(); await assert.rejects(waitUntil(async () => { throw Object.assign(new Error('fatal fixture error'), { retryable: false }); }, { timeoutMs: 5_000, intervalMs: 1_000 }), /fatal fixture error/); assert.ok(Date.now() - started < 1_000); });
