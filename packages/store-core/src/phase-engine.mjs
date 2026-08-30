import { mark, markConverged } from './checkpoint.mjs';

export class Deadline {
  constructor(ms = 3_600_000) { this.end = Date.now() + ms; }
  remaining() { return Math.max(0, this.end - Date.now()); }
  assert(label = 'operation') { if (this.remaining() <= 0) { const error = new Error(`time budget exhausted: ${label}`); error.code = 'DEADLINE'; throw error; } }
}

export async function reconcilePhase({ phase, adapter, desired, checkpoint, evidence, logger, apply = false, deadline = new Deadline() }) {
  deadline.assert(`${phase}:start`);
  const page = await adapter.ensurePage();
  if (!adapter.acceptedPageKinds.includes(page.kind)) throw Object.assign(new Error(`unexpected PageKind for ${phase}: ${page.kind}`), { code: 'SCHEMA_DRIFT' });
  if (page.kind === 'SubmissionOverview') {
    if ((checkpoint.phaseStatuses[phase] ?? 'Unknown') === 'Unknown') mark(checkpoint, phase, 'Observed', { pageKind: page.kind });
    const overview = await adapter.overviewVerify(deadline);
    const evidenceIds = [...(await evidence?.writePhase(phase, { initial: { page }, cold: { pageKind: 'SubmissionOverview', observed: {}, diff: [] }, overview }) ?? []), ...(await adapter.captureEvidence?.(evidence, phase) ?? [])];
    const finalEvidence = { pageKind: 'SubmissionOverview', coldDiff: [], overviewStatus: overview.status, overviewUrl: overview.url, evidenceIds };
    markConverged(checkpoint, phase, finalEvidence);
    logger?.pass(`${phase} already Complete`, finalEvidence);
    return { phase, status: 'Converged', diff: [], cold: { diff: [] }, overview, exitCode: 0 };
  }
  const observed = await adapter.observe();
  mark(checkpoint, phase, 'Observed', { pageKind: page.kind, observed });
  const diff = await adapter.diff(desired, observed);
  logger?.event('DIFF', { phase, pageKind: page.kind, count: diff.length, diff });
  if (diff.length && !apply) {
    mark(checkpoint, phase, 'NeedsChanges', { pageKind: page.kind, diff });
    return { phase, status: 'NeedsChanges', diff, exitCode: 4 };
  }
  if (diff.length) {
    mark(checkpoint, phase, 'NeedsChanges', { pageKind: page.kind, diff });
    mark(checkpoint, phase, 'Applying', { pageKind: page.kind, diff });
    await adapter.apply(diff, desired, deadline);
    mark(checkpoint, phase, 'AppliedUnverified', { pageKind: page.kind, diff });
  }
  deadline.assert(`${phase}:cold-verify`);
  const cold = await adapter.coldVerify(desired, deadline);
  if (!Array.isArray(cold.diff) || cold.diff.length) {
    mark(checkpoint, phase, 'Failed', { pageKind: cold.pageKind, coldDiff: cold.diff ?? [], observed: cold.observed });
    throw Object.assign(new Error(`${phase} cold verification has differences`), { code: 'ERROR', evidence: cold });
  }
  const overview = await adapter.overviewVerify(deadline);
  const evidenceIds = [...(await evidence?.writePhase(phase, { initial: { page, observed, diff }, cold, overview }) ?? []), ...(await adapter.captureEvidence?.(evidence, phase) ?? [])];
  const finalEvidence = { pageKind: 'SubmissionOverview', coldDiff: cold.diff, overviewStatus: overview.status, overviewUrl: overview.url, evidenceIds, observed: cold.observed };
  markConverged(checkpoint, phase, finalEvidence);
  logger?.pass(`${phase} Converged`, finalEvidence);
  return { phase, status: 'Converged', diff, cold, overview, exitCode: 0 };
}
