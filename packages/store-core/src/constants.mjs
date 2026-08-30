export const PHASES = ['availability', 'properties', 'age-ratings', 'packages', 'listing', 'options'];
export const PHASE_ALIASES = new Map([
  ['ageRatings', 'age-ratings'], ['ageratings', 'age-ratings'], ['age-ratings', 'age-ratings'],
  ['availability', 'availability'], ['properties', 'properties'], ['packages', 'packages'],
  ['listing', 'listing'], ['options', 'options']
]);
export const PHASE_STATUS = ['Unknown', 'Observed', 'NeedsChanges', 'Applying', 'AppliedUnverified', 'Converged', 'Failed'];
export const EXIT = { OK: 0, ERROR: 1, CONFIG: 2, SESSION: 3, DIFF: 4, SCHEMA_DRIFT: 5, DEADLINE: 6 };
export const PAGE_KINDS = ['LoadingShell', 'SignIn', 'ErrorPage', 'ProductOverview', 'SubmissionOverview', 'AvailabilityForm', 'PropertiesForm', 'AgeRatingsQuestionnaire', 'AgeRatingsSummary', 'PackagesForm', 'ListingLanguageGrid', 'ListingForm', 'OptionsForm', 'SubmissionConfirmation', 'CertificationStatus', 'Unknown'];
export function normalizePhase(value) {
  const normalized = PHASE_ALIASES.get(value);
  if (!normalized) throw new Error(`unknown phase: ${value}`);
  return normalized;
}
