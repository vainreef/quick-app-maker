import path from 'node:path';
import { readJson, writeAtomic } from '@quick-app/core';
import { normalizePhase } from './constants.mjs';

export const DEFAULT_DESIRED = {
  productId: 'PENDING', productName: '', submissionId: '',
  site: { baseUrl: 'https://partner.microsoft.com/zh-cn/dashboard/products', languageId: '5', languageCode: 'zh-cn', supportedLanguageCodes: ['zh-cn'] },
  package: { path: '', executable: '', architecture: 'x64' },
  values: { description: '', shortDescription: '', features: [], keywords: [] },
  pricing: { currency: 'CN', priceTier: '0', markets: 'all', audience: 'Public' },
  properties: { category: 'Productivity', privacy: 'No', privacyPolicyText: '', privacyPolicyUrl: '', capabilities: {} },
  ageRatings: { mode: 'questionnaire', applicationType: '2558', answers: {}, defaultAnswer: 'No', requireConfirmation: true, confirmed: false, physicalMedia: false, iarcTerms: true },
  listing: { languageCode: 'zh-cn', screenshot: true, assets: {} },
  submissionOptions: { publishMode: 'Manual', runFullTrustReason: '' }
};

export function loadDesired(appRoot) {
  const filePath = path.join(appRoot, 'store', 'desired-state.json');
  const value = readJson(filePath, structuredClone(DEFAULT_DESIRED));
  return merge(DEFAULT_DESIRED, value);
}

export function saveDesired(appRoot, desired) {
  const filePath = path.join(appRoot, 'store', 'desired-state.json');
  writeAtomic(appRoot, filePath, desired);
  return filePath;
}

export function validateDesired(desired, { strict = false, checkAge = true } = {}) {
  const errors = [];
  required(desired.productName, 'productName', errors, strict);
  required(desired.values?.description, 'values.description', errors, strict);
  required(desired.values?.shortDescription, 'values.shortDescription', errors, strict);
  if ((desired.values?.keywords ?? []).length > 7) errors.push('values.keywords must contain at most 7 items');
  if ((desired.values?.keywords ?? []).some(value => String(value).length > 40)) errors.push('each keyword must be ≤40 characters');
  if (strict && !(desired.values?.features ?? []).length) errors.push('values.features is empty');
  if (strict && !(desired.values?.keywords ?? []).length) errors.push('values.keywords is empty');
  if (strict && desired.listing?.screenshot && !desired.assets?.screenshot) errors.push('assets.screenshot is required when listing.screenshot=true');
  if (desired.properties?.privacy === 'Yes' && !desired.properties.privacyPolicyUrl && !desired.properties.privacyPolicyText) errors.push('privacy=Yes requires privacyPolicyUrl or privacyPolicyText');
  const answers = desired.ageRatings?.answers ?? {};
  if (strict && checkAge && Object.keys(answers).length === 0) errors.push('ageRatings.answers is empty; confirm the questionnaire answers');
  if (strict && checkAge && !desired.ageRatings?.defaultAnswer) errors.push('ageRatings.defaultAnswer is required for newly revealed questionnaire items');
  if (strict && checkAge && desired.ageRatings?.requireConfirmation && !desired.ageRatings.confirmed) errors.push('ageRatings.confirmed must be true after reviewing the questionnaire answers');
  return errors;
}

function required(value, field, errors, strict) {
  if (strict && (!value || !String(value).trim())) errors.push(`${field} is required`);
}
function merge(base, value) {
  if (!value || typeof value !== 'object') return structuredClone(base);
  const result = structuredClone(base);
  for (const [key, item] of Object.entries(value)) {
    if (item && typeof item === 'object' && !Array.isArray(item) && result[key] && typeof result[key] === 'object') result[key] = merge(result[key], item);
    else result[key] = item;
  }
  return result;
}
