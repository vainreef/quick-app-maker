import { PAGE_KINDS } from '@quick-app/store-core';

export async function capturePage(page) {
  const data = await page.evaluate(() => {
    const url = location.href || ''; const text = (document.body?.innerText || '').replace(/\u00a0/g, ' ').trim();
    const has = selector => document.querySelector(selector) !== null;
    const signals = {
      signIn: /login\.microsoftonline|login\.live\.com|signin|oauth/i.test(url),
      loading: text.length < 80 && !has('main input,main textarea,main button,main a,main [role="button"],h1'),
      availability: /availability|pricingandavailability/i.test(url) || has('input[name="marketSelection"],#saveButtonPricing'),
      properties: /properties/i.test(url) || has('select[name="CategorySelect"],input[name="privacyPolicySelection"]'),
      ageQuestionnaire: /ageratings(?!\/summary)/i.test(url) || has('input[name="inputMode"],input[name^="question#"]'),
      ageSummary: /ageratings\/summary/i.test(url) || has('age-rating-summary,.rating-summary-table'),
      packages: /packages/i.test(url) && has('input[type="file"],tr,.description,.fileuploader'),
      listingGrid: /managelanguages/i.test(url) || has('submission-listing-summary,he-data-grid'),
      listingForm: (/listings\?|languageid=/i.test(url) && has('textarea,input')) || has('textarea[name="description"],#description'),
      options: /options/i.test(url) && has('textarea,input[type="radio"]'),
      submissionOverview: /\/submissions\/[^/?#]+\/overview/i.test(url) || (/\/overview(?:[/?#]|$)/i.test(url) && has('a[href*="/submissions/"]')),
      productOverview: /\/products\/[^/?#]+/i.test(url) || /\/dashboard\/(?:apps-and-games|products|home)/i.test(url) || (/\/overview(?:[/?#]|$)/i.test(url) && !has('a[href*="/submissions/"]')),
      certification: /in certification|正在认证/i.test(text),
      fatal: /something went wrong|出现问题|error-page/i.test(text)
    };
    let kind = 'Unknown';
    if (signals.signIn) kind = 'SignIn';
    else if (signals.fatal) kind = 'ErrorPage';
    else if (signals.loading) kind = 'LoadingShell';
    else if (signals.certification) kind = 'CertificationStatus';
    else if (signals.listingForm) kind = 'ListingForm';
    else if (signals.listingGrid) kind = 'ListingLanguageGrid';
    else if (signals.availability) kind = 'AvailabilityForm';
    else if (signals.properties) kind = 'PropertiesForm';
    else if (signals.ageSummary) kind = 'AgeRatingsSummary';
    else if (signals.ageQuestionnaire) kind = 'AgeRatingsQuestionnaire';
    else if (signals.packages) kind = 'PackagesForm';
    else if (signals.options) kind = 'OptionsForm';
    else if (signals.submissionOverview) kind = 'SubmissionOverview';
    else if (signals.productOverview) kind = 'ProductOverview';
    else if (/partner\.microsoft\.com/i.test(url)) kind = 'ProductOverview';
    return { kind, ready: !['Unknown', 'LoadingShell'].includes(kind), url, title: document.title || '', textPreview: text.slice(0, 1800), signals, buttons: [...document.querySelectorAll('button,[role="button"]')].map(x => (x.innerText || x.getAttribute('aria-label') || '').trim()).filter(Boolean).slice(0, 40), errors: [...document.querySelectorAll('[role="alert"],.alert-error,.alert-danger')].map(x => (x.innerText || '').trim()).filter(Boolean).slice(0, 20) };
  });
  if (!PAGE_KINDS.includes(data.kind)) data.kind = 'Unknown';
  return data;
}

export async function waitForPageKind(page, accepted, { timeoutMs = 90_000, operation = 'page' } = {}) {
  const deadline = Date.now() + timeoutMs; let last;
  while (Date.now() < deadline) {
    last = await capturePage(page);
    if (accepted.includes(last.kind)) return last;
    if (last.kind === 'SignIn' || last.kind === 'ErrorPage') throw Object.assign(new Error(`${operation} reached ${last.kind}`), { code: 'SESSION' });
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw Object.assign(new Error(`${operation} timed out; last PageKind=${last?.kind ?? 'Unknown'} url=${last?.url ?? ''}`), { code: 'SCHEMA_DRIFT', snapshot: last });
}

export async function waitUntil(predicate, { timeoutMs = 30_000, intervalMs = 250, label = 'condition' } = {}) {
  const end = Date.now() + timeoutMs; let lastError;
  while (Date.now() < end) {
    try { if (await predicate()) return true; } catch (error) { if (error?.retryable === false || ['CONFIG', 'ERROR', 'SCHEMA_DRIFT', 'SESSION', 'DEADLINE'].includes(error?.code)) throw error; lastError = error; }
    await new Promise(resolve => setTimeout(resolve, intervalMs));
  }
  throw new Error(`${label} timed out${lastError ? `: ${lastError.message}` : ''}`);
}
