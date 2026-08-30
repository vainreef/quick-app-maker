import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { writeAtomic, readJson } from '@quick-app/core';
import { PHASES, PHASE_STATUS } from './constants.mjs';

const transitions = new Map([
  ['Unknown', new Set(['Observed', 'Failed'])], ['Observed', new Set(['NeedsChanges', 'Converged', 'Failed'])],
  ['NeedsChanges', new Set(['Applying', 'Converged', 'Failed'])], ['Applying', new Set(['AppliedUnverified', 'Converged', 'Failed'])],
  ['AppliedUnverified', new Set(['Observed', 'Converged', 'Failed'])], ['Converged', new Set(['Observed', 'Failed'])],
  ['Failed', new Set(['Observed', 'Applying', 'Failed'])]
]);

export function createCheckpoint({ productId = '', manifestHash = '', submissionId = '' } = {}) {
  return { schemaVersion: 2, productId, manifestHash, submissionId, phaseStatuses: Object.fromEntries(PHASES.map(name => [name, 'Unknown'])), convergedPhases: [], evidence: {}, updatedAt: new Date().toISOString() };
}

export function loadCheckpoint(stateDir, identity = {}) {
  const filePath = path.join(stateDir, 'checkpoint.json');
  const value = readJson(filePath, null);
  if (!value) return createCheckpoint(identity);
  if (value.schemaVersion !== 2) return createCheckpoint(identity);
  if (identity.productId && value.productId !== identity.productId) return createCheckpoint(identity);
  if (identity.manifestHash && value.manifestHash !== identity.manifestHash) return createCheckpoint(identity);
  if (identity.submissionId && value.submissionId !== identity.submissionId) return createCheckpoint(identity);
  return value;
}

export function saveCheckpoint(workspace, stateDir, checkpoint) {
  checkpoint.updatedAt = new Date().toISOString();
  return writeAtomic(workspace, path.join(stateDir, 'checkpoint.json'), checkpoint);
}

export function mark(checkpoint, phase, status, detail = {}) {
  if (!PHASE_STATUS.includes(status)) throw new Error(`invalid phase status: ${status}`);
  const current = checkpoint.phaseStatuses[phase] ?? 'Unknown';
  if (current !== status && !transitions.get(current)?.has(status)) throw new Error(`invalid checkpoint transition ${phase}: ${current} → ${status}`);
  checkpoint.phaseStatuses[phase] = status;
  if (status !== 'Converged') checkpoint.convergedPhases = checkpoint.convergedPhases.filter(item => item !== phase);
  checkpoint.evidence[phase] = { status, ...detail, verifiedAt: new Date().toISOString() };
  return checkpoint;
}

export function markConverged(checkpoint, phase, evidence) {
  if (!evidence || evidence.pageKind !== 'SubmissionOverview' || evidence.overviewStatus !== 'Complete' || !Array.isArray(evidence.coldDiff) || evidence.coldDiff.length !== 0 || !evidence.overviewUrl || !Array.isArray(evidence.evidenceIds) || !evidence.evidenceIds.length) {
    throw new Error(`phase ${phase} lacks complete cold-load and overview evidence`);
  }
  mark(checkpoint, phase, 'Converged', evidence);
  if (!checkpoint.convergedPhases.includes(phase)) checkpoint.convergedPhases.push(phase);
  return checkpoint;
}

export function runId(prefix = 'run') { return `${prefix}-${new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14)}-${crypto.randomBytes(3).toString('hex')}`; }
