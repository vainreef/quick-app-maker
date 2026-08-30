import test from 'node:test';
import assert from 'node:assert/strict';
import { PHASES, normalizePhase } from '@quick-app/store-core';
import { phaseAdapters } from './src/phases.mjs';
test('phase names normalize once', () => { assert.equal(normalizePhase('ageRatings'), 'age-ratings'); assert.deepEqual(PHASES.length, 6); });
test('phase adapter registry exposes one uniform interface', () => { const driver = {}; const adapters = phaseAdapters(driver); for (const phase of PHASES) assert.ok(adapters[phase].acceptedPageKinds.length); });
