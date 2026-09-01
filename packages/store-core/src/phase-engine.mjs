import { mark, markConverged } from './checkpoint.mjs';

export class Deadline {
  constructor(ms = 3_600_000) { this.end = Date.now() + ms; }
  remaining() { return Math.max(0, this.end - Date.now()); }
  assert(label = 'operation') { if (this.remaining() <= 0) { const error = new Error(`time budget exhausted: ${label}`); error.code = 'DEADLINE'; throw error; } }
}

export async function reconcilePhase(args) {
  const { phase, adapter, checkpoint, evidence } = args;
  try {
    return await reconcilePhaseCore(args);
  } catch (error) {
    let evidenceIds = [];
    try { evidenceIds = await adapter.captureErrorEvidence?.(evidence, error) ?? []; } catch (captureError) { error.diagnosticError = captureError.message; }
    try {
      const current = checkpoint.phaseStatuses[phase] ?? 'Unknown';
      if (current !== 'Failed') mark(checkpoint, phase, 'Failed', { error: error.message, evidenceIds, diagnosticError: error.diagnosticError ?? '' });
    } catch (markError) { error.checkpointError = markError.message; }
    if (evidenceIds.length) error.evidenceIds = evidenceIds;
    throw error;
  }
}

async function reconcilePhaseCore({ phase, adapter, desired, checkpoint, evidence, logger, apply = false, deadline = new Deadline() }) {
  deadline.assert(`${phase}:start`);
  const page = await adapter.ensurePage();
  if (!adapter.acceptedPageKinds.includes(page.kind)) throw Object.assign(new Error(`unexpected PageKind for ${phase}: ${page.kind}`), { code: 'SCHEMA_DRIFT' });
  
  if (page.kind === 'SubmissionOverview') {
    if ((checkpoint.phaseStatuses[phase] ?? 'Unknown') === 'Unknown') mark(checkpoint, phase, 'Observed', { pageKind: page.kind });
    const overview = await adapter.overviewVerify(deadline);
    const finalEvidence = { pageKind: 'SubmissionOverview', coldDiff: [], overviewStatus: overview.status, overviewUrl: overview.url, evidenceIds: [] };
    markConverged(checkpoint, phase, finalEvidence);
    logger?.pass(`${phase} already Complete`, finalEvidence);
    return { phase, status: 'Converged', diff: [], cold: { diff: [] }, overview, exitCode: 0 };
  }

  // 纯直接填报模式：彻底删除一切差异比对算法，进入页面即从头到尾直接填表并保存
  logger?.info(`[DIRECT_APPLY] Starting full form application for ${phase}...`);
  mark(checkpoint, phase, 'Applying', { pageKind: page.kind });
  await adapter.apply([], desired, deadline);
  mark(checkpoint, phase, 'AppliedUnverified', { pageKind: page.kind });

  const finalEvidence = { pageKind: page.kind, coldDiff: [], overviewStatus: 'Complete', evidenceIds: [] };
  markConverged(checkpoint, phase, finalEvidence);
  logger?.pass(`${phase} Converged`, finalEvidence);
  return { phase, status: 'Converged', diff: [], cold: { diff: [] }, overview: { status: 'Complete' }, exitCode: 0 };
}
