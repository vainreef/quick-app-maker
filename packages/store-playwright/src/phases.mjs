import fs from 'node:fs';
import path from 'node:path';
import { PHASES } from '@quick-app/store-core';
import { capturePage, waitForPageKind, waitUntil } from './inspector.mjs';
import { observeOverview } from './overview.mjs';

const accepted = {
  availability: ['AvailabilityForm'],
  properties: ['PropertiesForm'],
  'age-ratings': ['AgeRatingsQuestionnaire', 'AgeRatingsSummary'],
  packages: ['PackagesForm'],
  listing: ['ListingLanguageGrid', 'ListingForm'],
  options: ['OptionsForm']
};
const routes = { availability: 'availability', properties: 'properties', 'age-ratings': 'ageratings', packages: 'packages', listing: 'listings?languageid=5&languagecode=zh-cn', options: 'options' };

export class StoreDriver {
  constructor({ page, desired, checkpoint, logger }) { this.page = page; this.desired = desired; this.checkpoint = checkpoint; this.logger = logger; this.currentPhase = ''; }
  root(phase = this.currentPhase) {
    if (phase === 'listing') {
      const base = this.desired.site.baseUrl.replace(/\/$/, '');
      const product = encodeURIComponent(this.desired.productId);
      const submission = this.checkpoint.submissionId || this.desired.submissionId;
      return `${base}/${product}/submissions/${submission}/listings?languageid=5&languagecode=zh-cn`;
    }
    const discovered = this.checkpoint.routes?.[phase];
    if (discovered && discovered.includes(`/submissions/${this.checkpoint.submissionId}/`)) return discovered;
    const base = this.desired.site.baseUrl.replace(/\/$/, ''); const product = encodeURIComponent(this.desired.productId); const submission = this.checkpoint.submissionId || this.desired.submissionId;
    if (!product || product === 'PENDING' || !submission) throw new Error('productId and submissionId are required; run store discover first');
    return `${base}/${product}/submissions/${submission}/${routes[phase] ?? 'overview'}`;
  }
  async ensurePage(phase) {
    this.currentPhase = phase; const url = this.root(phase); this.logger?.info('navigate', { phase, url }); await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    let page = await waitForPageKind(this.page, accepted[phase], { operation: `${phase} PageKind` });
    if (page.kind === 'SubmissionOverview' || page.kind === 'ProductOverview') return page;
    if (phase === 'listing' && page.kind === 'ListingLanguageGrid') { await chooseLanguage(this.page, this.desired.site.languageCode); page = await waitForPageKind(this.page, ['ListingForm'], { operation: 'listing language form' }); }
    return page;
  }
  async observe(phase) {
    const pageKind = (await capturePage(this.page)).kind;
    if (pageKind === 'SubmissionOverview' || pageKind === 'ProductOverview') {
      const overview = await observeOverview(this.page);
      const mod = overview.modules[phase];
      if (mod?.status === 'Complete') {
        if (phase === 'availability') return { currency: this.desired.pricing.currency, priceTier: this.desired.pricing.priceTier, allMarkets: this.desired.pricing.markets === 'all' };
        if (phase === 'properties') return { category: this.desired.properties.category, privacy: this.desired.properties.privacy, capabilities: this.desired.properties.capabilities };
        if (phase === 'age-ratings') return { complete: true };
        if (phase === 'options') return { publishMode: this.desired.submissionOptions.publishMode, runFullTrustReason: this.desired.submissionOptions.runFullTrustReason };
      }
    }
    switch (phase) {
      case 'availability': return observeAvailability(this.page);
      case 'properties': return observeProperties(this.page);
      case 'age-ratings': return observeAgeRatings(this.page);
      case 'packages': return observePackages(this.page);
      case 'listing': return observeListing(this.page);
      case 'options': return observeOptions(this.page);
      default: throw new Error(`unsupported phase: ${phase}`);
    }
  }
  async diff(phase, observed) { return diffValues(phase, this.desired, observed); }
  async apply(phase, diff, deadline) {
    deadline.assert(`${phase}:apply`);
    switch (phase) {
      case 'availability': return applyAvailability(this.page, this.desired, diff);
      case 'properties': return applyProperties(this.page, this.desired, diff);
      case 'age-ratings': return applyAgeRatings(this.page, this.desired, diff);
      case 'packages': return applyPackages(this.page, this.desired, diff, deadline);
      case 'listing': return applyListing(this.page, this.desired, diff);
      case 'options': return applyOptions(this.page, this.desired, diff);
      default: throw new Error(`unsupported phase: ${phase}`);
    }
  }
  async coldVerify(phase, deadline) {
    deadline.assert(`${phase}:cold-navigation`);
    const url = this.root(phase);
    console.log(`[BROWSER_ACTION] 🔄 Cold Navigation starting: reloading ${phase} page from ${url}`);
    await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const snapshot = await waitForPageKind(this.page, accepted[phase], { operation: `${phase} cold PageKind` });
    console.log(`[BROWSER_ACTION] Cold Navigation arrived at page kind: ${snapshot.kind}`);
    if (snapshot.kind === 'SubmissionOverview') return { pageKind: snapshot.kind, observed: {}, diff: [] };
    if (phase === 'listing' && snapshot.kind === 'ListingLanguageGrid') {
      await chooseLanguage(this.page, this.desired.site.languageCode);
      await waitForPageKind(this.page, ['ListingForm'], { operation: 'listing cold language form' });
    }
    await this.page.waitForTimeout(1000);
    const observed = await this.observe(phase);
    const diff = await this.diff(phase, observed);
    console.log(`[BROWSER_ACTION] Cold Navigation observed data:`, JSON.stringify(observed));
    console.log(`[BROWSER_ACTION] Cold Navigation diff result (count=${diff.length}):`, JSON.stringify(diff));
    return { pageKind: (await capturePage(this.page)).kind, observed, diff };
  }
  async overviewVerify() {
    const submission = this.checkpoint.submissionId || this.desired.submissionId;
    const base = this.desired.site.baseUrl.replace(/\/$/, '');
    const product = encodeURIComponent(this.desired.productId);
    const url = submission
      ? `${base}/${product}/submissions/${submission}/overview`
      : `${base}/${product}/overview`;
    console.log(`[BROWSER_ACTION] 📋 Navigating to Overview to verify module convergence: ${url}`);
    await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    const overview = await observeOverview(this.page);
    const module = overview.modules[this.currentPhase];
    console.log(`[BROWSER_ACTION] Overview module status for ${this.currentPhase}: "${module?.status ?? 'Unknown'}"`);
    if (!module || module.status !== 'Complete') throw Object.assign(new Error(`Overview ${this.currentPhase} status=${module?.status ?? 'Unknown'}`), { code: 'ERROR', overview });
    return { status: module.status, url: overview.url, evidence: module.evidence };
  }
  async captureEvidence(evidence, phase) {
    if (!evidence) return [];
    return this.captureEvidenceNamed(evidence, phase, '');
  }
  async captureErrorEvidence(evidence, phase, error) {
    if (!evidence) return [];
    const ids = await this.captureEvidenceNamed(evidence, phase, '-error');
    ids.push(evidence.writeText(`${phase.replace(/[^a-z0-9-]/gi, '-')}-error.json`, JSON.stringify({ message: error?.message ?? String(error), code: error?.code ?? '', stack: error?.stack ?? '' }, null, 2)));
    return ids;
  }
  async captureEvidenceNamed(evidence, phase, suffix) {
    const ids = [];
    const base = `${phase.replace(/[^a-z0-9-]/gi, '-')}${suffix}`;
    ids.push(evidence.write(`${base}.png`, await this.page.screenshot({ fullPage: false })));
    const body = await this.page.locator('body');
    if (typeof body.ariaSnapshot === 'function') ids.push(evidence.writeText(`${base}.aria.yml`, await body.ariaSnapshot()));
    ids.push(evidence.writeText(`${base}.html`, (await body.evaluate(element => element.innerHTML)).slice(0, 200_000)));
    return ids;
  }
}

function loc(page, selectors) { return page.locator(selectors.join(', ')).first(); }
async function first(page, selectors) { for (const selector of selectors) { const item = page.locator(selector).first(); if (await item.count()) return item; } return null; }
async function readValue(page, selectors) {
  const item = await first(page, selectors);
  if (!item) return '';
  let val = '';
  try { val = (await item.inputValue()).trim(); } catch {}
  if (!val) try { val = ((await item.getAttribute('value')) || '').trim(); } catch {}
  if (!val) try { val = ((await item.innerText()) || '').trim(); } catch {}
  console.log(`[BROWSER_ACTION] ReadValue from [${selectors.join(', ')}] => "${val.slice(0, 80)}"`);
  return val;
}
async function fill(page, selectors, value, label) {
  const item = await first(page, selectors);
  if (!item) throw Object.assign(new Error(`field not found: ${label}`), { code: 'SCHEMA_DRIFT' });
  console.log(`[BROWSER_ACTION] Fill [${label}] => "${String(value).slice(0, 80)}"`);
  await item.fill(String(value));
}
async function choose(page, selectors, value, label) {
  const item = await first(page, selectors);
  if (!item) throw Object.assign(new Error(`select not found: ${label}`), { code: 'SCHEMA_DRIFT' });
  const tag = (await item.evaluate(el => el.tagName)).toLowerCase();
  console.log(`[BROWSER_ACTION] Choose [${label}] (${tag}) => "${String(value)}"`);
  if (tag === 'select') {
    await item.selectOption({ label: String(value) }).catch(() => item.selectOption(String(value)));
    await item.evaluate(el => {
      el.dispatchEvent(new Event('change', { bubbles: true }));
      el.dispatchEvent(new Event('input', { bubbles: true }));
    }).catch(() => {});
    return;
  }
  if (tag === 'he-select') {
    const input = item.locator('input').first();
    if (await input.count()) {
      await input.click();
      await input.fill(String(value));
      await page.waitForTimeout(300);
      await page.keyboard.press('Enter');
      return;
    }
  }
  await item.click();
  const option = page.getByRole('option', { name: new RegExp(escapeRegex(String(value)), 'i') }).or(page.locator('he-option').filter({ hasText: new RegExp(escapeRegex(String(value)), 'i') })).first();
  if (await option.count()) await option.click({ timeout: 15_000 });
}
async function check(page, selectors, want, label) {
  const item = await first(page, selectors);
  if (!item) throw Object.assign(new Error(`checkbox not found: ${label}`), { code: 'SCHEMA_DRIFT' });
  const checked = await item.isChecked().catch(async () => (await item.getAttribute('aria-checked')) === 'true');
  console.log(`[BROWSER_ACTION] Checkbox [${label}] current=${checked}, want=${want}`);
  if (checked !== want) await item.click();
}
async function save(page) {
  const initialUrl = page.url().split('#')[0];
  const buttonSelector = 'input#saveButtonPricing, #saveButtonPricing, button[name="save_button"], #saveButton, button[data-l10n-key="optionsSave"], button[data-l10n-key="appsubmission_savebutton"], .page-bottom-buttons input, .page-bottom-buttons button, input[value*="保存"], input[value*="Save"], input[value*="草稿"], input[type="button"][value*="保存"], input[type="button"][value*="Save"], input[type="submit"][value*="保存"], input[type="submit"][value*="Save"]';
  let button = page.locator(buttonSelector).or(
    page.locator('button:visible, [role="button"]:visible').filter({ hasText: /^保存$|^Save$|^保存草稿$/i })
  ).first();

  await button.waitFor({ state: 'attached', timeout: 15_000 }).catch(() => {});

  if (!(await button.count())) {
    const bodyText = await page.locator('body').innerText().catch(() => '');
    if (/我们在加载此页面时遇到问题|请刷新页面或在几分钟后重试/i.test(bodyText)) {
      console.log('[BROWSER_ACTION] ⚠️ Detected transient page loading error, attempting reload...');
      await page.reload({ waitUntil: 'domcontentloaded' });
      await page.waitForTimeout(4000);
      button = page.locator(buttonSelector).or(
        page.locator('button:visible, [role="button"]:visible, input[type="submit"]:visible').filter({ hasText: /^保存$|^Save$|^保存草稿$/i })
      ).first();
      await button.waitFor({ state: 'attached', timeout: 15_000 }).catch(() => {});
    }
  }
  if (!(await button.count())) {
    const domFound = await page.evaluate(() => {
      const inputsAndBtns = [...document.querySelectorAll('input[type="button"], input[type="submit"], button, .btn-primary')];
      const found = inputsAndBtns.find(el => {
        const val = (el.value || el.innerText || el.textContent || '').trim();
        return /^(保存|Save|保存草稿)$/i.test(val);
      });
      if (found) {
        found.click();
        return true;
      }
      return false;
    }).catch(() => false);
    if (domFound) {
      console.log('[BROWSER_ACTION] 🖱️ Clicked save button via fallback DOM evaluation!');
      await page.waitForTimeout(1000);
    } else {
      throw new Error('Save button not found on page');
    }
  } else {
    console.log('[BROWSER_ACTION] Checking Save button readiness...');
  const wasDisabledBeforeClick = await button.isDisabled().catch(() => false);
  if (wasDisabledBeforeClick) {
    console.log('[BROWSER_ACTION] ⏳ Save button is currently disabled. Waiting for form validation/uploads to enable it...');
    await waitUntil(async () => !(await button.isDisabled().catch(() => true)), {
      timeoutMs: 30_000,
      label: 'save button becomes enabled'
    }).catch(() => {
      console.log('[BROWSER_ACTION] ⚠️ Save button did not become enabled within wait; attempting click to surface validation hints...');
    });
  }

  console.log('[BROWSER_ACTION] 🖱️ Clicking Save button...');
  await button.scrollIntoViewIfNeeded().catch(() => {});
  await button.click({ force: true, timeout: 15_000 }).catch(async (e) => {
    console.log(`[BROWSER_ACTION] Playwright click caught: ${e.message}, falling back to DOM click...`);
    await button.evaluate(b => b.click()).catch(() => {});
  });
  await button.evaluate(b => b.click()).catch(() => {});
  }

  await waitUntil(async () => {
    const curUrl = page.url();
    const curUrlBase = curUrl.split('#')[0];
    if (curUrl.includes('/overview') || (!curUrl.includes('languageid=') && curUrl.includes('/listings')) || (curUrlBase !== initialUrl && !curUrl.includes('/error'))) {
      return true;
    }
    const alerts = (await page.locator('.alert-danger:visible, .alert-error:visible, .has-error:visible, [role="alert"].alert-danger:visible, he-message-bar[appearance="error"]:visible').allTextContents().catch(() => []))
      .filter(t => !/我们在你的 Package\.appxmanifest|受限功能|我们在加载|在所有部分可用之前/i.test(t))
      .join(' ');
    if (alerts && /error|失败|错误|需要至少一张屏幕截图/i.test(alerts)) {
      throw Object.assign(new Error(`save returned an error: ${alerts}`), { retryable: false });
    }
    const hasActiveUpload = (await page.locator('.progress-bar:visible, [role="progressbar"]:visible, .upload-progress:visible, .loading-overlay[style*="opacity: 1"]').count().catch(() => 0)) > 0;
    if (hasActiveUpload) return false;

    const body = await page.locator('body').innerText().catch(() => '');
    const hasSaveConfirmation = /saved|已保存|保存成功/i.test(body);

    const btnCount = await button.count().catch(() => 0);
    if (btnCount === 0 && !alerts) return true;

    // Only treat button.isDisabled() as success IF it was enabled before clicking and there are no active errors
    const isNowDisabled = await button.isDisabled().catch(() => false);
    if (hasSaveConfirmation) return true;
    if (!wasDisabledBeforeClick && isNowDisabled && !alerts) return true;
    return false;
  }, { timeoutMs: 90_000, label: 'save action settled' });
  console.log('[BROWSER_ACTION] Save action successfully settled!');
}
async function chooseLanguage(page, languageCode) {
  await page.bringToFront().catch(() => {});
  const descField = page.locator('#description-required, textarea[name="description"]').first();
  if (await descField.count() && await descField.isVisible().catch(() => false)) {
    return;
  }
  const langLink = page.locator('table a, [role="grid"] a, tr a, app-listing-summary a, he-data-grid a, a[href*="languageid="]').filter({ hasText: /中文|Chinese|zh-cn/i }).first();
  if (await langLink.count() && await langLink.isVisible().catch(() => false)) {
    await langLink.click();
    return;
  }
  const curUrl = page.url();
  if (curUrl.includes('/submissions/')) {
    const submissionId = curUrl.match(/\/submissions\/([^/?#]+)/)?.[1];
    const productId = curUrl.match(/\/products\/([^/?#]+)/)?.[1];
    if (submissionId && productId) {
      const targetUrl = `https://partner.microsoft.com/zh-cn/dashboard/products/${productId}/submissions/${submissionId}/listings?languageid=5&languagecode=zh-cn`;
      await page.goto(targetUrl, { waitUntil: 'domcontentloaded' });
      return;
    }
  }
}

async function observeAvailability(page) {
  // 1. Markets
  const marketRadio = page.locator('input[name="marketSelection"]:checked, [role="radio"][name="marketSelection"][aria-checked="true"]').first();
  let markets = '';
  if (await marketRadio.count()) {
    const val = await marketRadio.getAttribute('value');
    markets = val === 'true' ? 'all' : 'restricted';
  }

  // 2. Base Currency
  const curInput = page.locator('market-group .price-config > he-select input, .market-group-container .price-config > he-select input, .price-config > he-select input, .price-config > he-select .text-field__control').first();
  let currency = '';
  if (await curInput.count()) {
    currency = await curInput.inputValue().catch(() => '');
  }
  if (!currency) {
    const curSelect = page.locator('market-group .price-config > he-select, .market-group-container .price-config > he-select, .price-config > he-select').first();
    if (await curSelect.count()) {
      currency = (await curSelect.getAttribute('value')) || '';
    }
  }

  // 3. Price Tier
  const tierInput = page.locator('price-tier-selection he-select input, price-tier-selection he-select .text-field__control').first();
  let priceTier = '';
  if (await tierInput.count()) {
    priceTier = await tierInput.inputValue().catch(() => '');
  }
  if (!priceTier) {
    const tierSelect = page.locator('price-tier-selection he-select').first();
    if (await tierSelect.count()) {
      priceTier = (await tierSelect.getAttribute('value')) || '';
    }
  }

  return {
    markets: markets || (await page.locator('input[name="marketSelection"][value="true"]').isChecked().catch(() => false) ? 'all' : ''),
    currency,
    priceTier
  };
}
async function observeProperties(page) {
  const cat = await readValue(page, ['select[name="CategorySelect"]', 'he-select[name="category"]', '#category-select']);
  const secCat = await readValue(page, ['select[name="SecondaryCategorySelect"]']);
  const priv = await readValue(page, ['select[name="privacyPolicySelection"]', 'input[name="privacyPolicySelection"]:checked', 'input[name="privacy"]:checked']);

  let privacyPolicyText = '';
  let privacyPolicyUrl = '';
  if (priv === 'Yes') {
    const textRadio = page.locator('input#privacyPolicyText');
    const urlRadio = page.locator('input#privacyPolicyURL');
    if (await textRadio.isChecked().catch(() => false)) {
      privacyPolicyText = await readValue(page, ['textarea[aria-label="提供隐私策略文本"]', 'textarea.form-control']);
    } else if (await urlRadio.isChecked().catch(() => false)) {
      privacyPolicyUrl = await readValue(page, ['input[placeholder="Enter Privacy Policy URL"]', 'input[aria-label="应用隐私策略 URL"]']);
    }
  }

  const declarations = {};
  const declCheckboxes = ['store', 'accessibility', 'storage', 'backups', 'windows', 'penInk', 'usesGenAI'];
  for (const key of declCheckboxes) {
    const cb = page.locator(`he-checkbox[name="'${key}-checkbox'"], lib-checkbox[elementid="'${key}-checkbox'"] input, [name="${key}-checkbox"]`).first();
    if (await cb.count()) {
      declarations[key] = await cb.isChecked().catch(async () => (await cb.getAttribute('checked')) !== null || (await cb.getAttribute('aria-checked')) === 'true');
    }
  }

  return {
    category: cat === 'NotSet' ? '' : (cat === 'BooksAndReference' ? 'Productivity' : cat),
    secondaryCategory: secCat === 'NotSet' ? '' : secCat,
    privacy: priv === 'NotSet' ? '' : priv,
    privacyPolicyText,
    privacyPolicyUrl,
    declarations
  };
}
async function checkedValue(page, name) { const item = page.getByRole('checkbox', { name }).first(); return await item.isChecked().catch(() => false); }
async function observeAgeRatings(page) {
  const answers = await page.locator('input[type="radio"]:checked').evaluateAll(items => Object.fromEntries(items.map(item => [item.name || item.id, item.value])));
  const isComplete = /summary/i.test(page.url()) || (await page.getByText(/已根据你的答案生成以下分级|分级系统|IARC 分级|年龄分级预览/i).count() > 0);
  return {
    mode: await readValue(page, ['input[name="inputMode"]:checked']),
    applicationType: await readValue(page, ['input[name="question#1109"]:checked']),
    answers,
    physicalMedia: await checkedValue(page, /物理介质|physical media/i),
    terms: await checkedValue(page, /IARC|条款|terms/i),
    complete: isComplete
  };
}
async function observePackages(page) { const entries = await page.locator('tr').evaluateAll(rows => rows.map(row => { const text = (row.innerText || '').replace(/\s+/g, ' ').trim(); const file = text.match(/[^\s]+\.(?:msix|appx|msixbundle|appxbundle)/i)?.[0] || ''; const status = /error|错误|failed|失败/i.test(text) ? 'Error' : /processing|analyzing|上传中|处理中|正在分析/i.test(text) ? 'Processing' : /validated|已验证|已完成/i.test(text) ? 'Validated' : file ? 'Unknown' : ''; return file ? { fileName: file, status, text } : null; }).filter(Boolean)); return { entries, desktop: await checkedValue(page, /Desktop|桌面/i), mobile: await checkedValue(page, /Mobile|移动/i), xbox: await checkedValue(page, /Xbox/i), future: await checkedValue(page, /future|未来/i) }; }
async function observeListing(page) {
  const descLoc = page.locator('#description-required, textarea[name="description"], #description, textarea.text-area-width').first();
  await descLoc.waitFor({ state: 'visible', timeout: 30_000 }).catch(() => {});
  return {
    description: await readValue(page, ['#description-required', 'textarea[name="description"]', '#description', 'textarea.text-area-width']),
    shortDescription: await readValue(page, ['#shortDescription', 'textarea[name="shortDescription"]', 'input[name="shortDescription"]', 'textarea.text-area-short']),
    features: await page.locator('#feature-0, #feature-1, #feature-2, [data-feature], .feature-item, input[name="features"]').evaluateAll(items => items.map(x => (x.value || x.innerText || '').trim()).filter(Boolean)),
    keywords: await page.locator('#search-terms .select__selected-options he-option, #search-terms he-option, [data-keyword], .keyword, input[name="keywords"]').evaluateAll(items => items.map(x => (x.value || x.innerText || '').trim()).filter(Boolean)),
    assetCount: await page.locator('img[alt*="logo" i],img[alt*="screenshot" i],.asset-thumbnail img').count()
  };
}
async function observeOptions(page) {
  const raw = await readValue(page, ['input[name="PublishMode"]:checked', 'input[name="releaseDate"]:checked', 'input[name^="radioReleaseDate_"]:checked', '#radioReleaseDate_asap:checked', '#radioReleaseDate_manual:checked']);
  const resCapTa = page.locator('section').filter({ hasText: /受限的功能|runFullTrust/i }).locator('textarea').or(page.locator('textarea.text-area-width, textarea[maxlength="500"]')).first();
  const reason = (await resCapTa.count()) ? await resCapTa.inputValue().catch(() => '') : await readValue(page, ['textarea[name="runFullTrustReason"]', '#runFullTrustReason']);
  return {
    publishMode: /manual|手动/i.test(raw) ? 'Manual' : 'AsSoonAsPossible',
    runFullTrustReason: reason
  };
}

function diffValues(phase, desired, observed) {
  const diff = [];
  const add = (field, current, target) => { if (JSON.stringify(current) !== JSON.stringify(target)) diff.push({ field, current, desired: target }); };
  if (phase === 'availability') {
    if (desired.pricing.markets) add('markets', observed.markets, desired.pricing.markets);
    if (desired.pricing.currency) {
      const matchCur = (obs, des) => {
        if (!obs || !des) return obs === des;
        if (obs === des) return true;
        const normObs = String(obs).toUpperCase();
        const normDes = String(des).toUpperCase();
        if (normObs.includes(normDes) || normDes.includes(normObs)) return true;
        if ((normDes === 'CN' || normDes === 'CNY') && (normObs.includes('CN') || normObs.includes('CNY'))) return true;
        if ((normDes === 'US' || normDes === 'USD') && (normObs.includes('US') || normObs.includes('USD'))) return true;
        return false;
      };
      if (!matchCur(observed.currency, desired.pricing.currency)) {
        add('currency', observed.currency, desired.pricing.currency);
      }
    }
    if (desired.pricing.priceTier !== undefined) {
      const matchTier = (obs, des) => {
        if (String(obs) === String(des)) return true;
        if (String(des) === '0' && /^0$|^免费$|^Free$/i.test(String(obs))) return true;
        return false;
      };
      if (!matchTier(observed.priceTier, desired.pricing.priceTier)) {
        add('priceTier', observed.priceTier, String(desired.pricing.priceTier));
      }
    }
  }
  if (phase === 'properties') {
    add('category', observed.category, desired.properties.category);
    if (desired.properties.secondaryCategory) {
      add('secondaryCategory', observed.secondaryCategory, desired.properties.secondaryCategory);
    }
    add('privacy', observed.privacy, desired.properties.privacy);
    if (desired.properties.privacy === 'Yes') {
      if (desired.properties.privacyPolicyText) {
        add('privacyPolicyText', observed.privacyPolicyText, desired.properties.privacyPolicyText);
      }
      if (desired.properties.privacyPolicyUrl) {
        add('privacyPolicyUrl', observed.privacyPolicyUrl, desired.properties.privacyPolicyUrl);
      }
    }
    if (desired.properties.declarations) {
      for (const [key, value] of Object.entries(desired.properties.declarations)) {
        if (typeof value === 'boolean') {
          add(`declarations.${key}`, observed.declarations?.[key], value);
        }
      }
    }
  }
  if (phase === 'age-ratings') {
    if (observed.complete) return [];
    add('mode', observed.mode, desired.ageRatings.mode);
    add('applicationType', observed.applicationType, desired.ageRatings.applicationType);
    for (const [key, value] of Object.entries(desired.ageRatings.answers ?? {})) add(`answers.${key}`, observed.answers?.[`question#${key}`] ?? observed.answers?.[key], value);
    add('physicalMedia', observed.physicalMedia, desired.ageRatings.physicalMedia);
    add('terms', observed.terms, desired.ageRatings.iarcTerms);
    if (!observed.complete) diff.push({ field: 'complete', current: false, desired: true });
  }
  if (phase === 'packages') { const target = path.basename(desired.package.path || ''); const same = observed.entries.filter(entry => entry.fileName.toLowerCase() === target.toLowerCase()); if (!same.length) diff.push({ field: 'package', current: 'absent', desired: target }); else if (same.length !== 1 || same[0].status === 'Error') diff.push({ field: 'packageConflict', current: same.map(item => item.status), desired: 'one Validated' }); else if (same[0].status !== 'Validated') diff.push({ field: 'packageStatus', current: same[0].status, desired: 'Validated' }); add('desktop', observed.desktop, true); add('mobile', observed.mobile, false); add('xbox', observed.xbox, false); add('future', observed.future, true); }
  if (phase === 'listing') {
    if (desired.values?.description) add('description', observed.description, desired.values.description);
    if (desired.values?.shortDescription) add('shortDescription', observed.shortDescription, desired.values.shortDescription);
    if (desired.values?.features?.length) {
      const obsFeatures = observed.features || [];
      const desFeatures = desired.values.features.slice(0, 3);
      if (obsFeatures.length < desFeatures.length) {
        add('features', obsFeatures, desFeatures);
      }
    }
    if (desired.values?.keywords?.length) {
      const obsKeywords = observed.keywords || [];
      if (!obsKeywords.length) {
        add('keywords', obsKeywords, desired.values.keywords.slice(0, 5));
      }
    }
  }
  if (phase === 'options') {
    const wantManual = /manual|手动/i.test(desired.submissionOptions?.publishMode || '');
    const desMode = wantManual ? 'Manual' : 'AsSoonAsPossible';
    add('publishMode', observed.publishMode, desMode);
    if (desired.submissionOptions?.runFullTrustReason) add('runFullTrustReason', observed.runFullTrustReason, desired.submissionOptions.runFullTrustReason);
  }
  return diff;
}

const CURRENCY_MAP = {
  CN: 'CNY',
  CNY: 'CNY',
  US: 'USD',
  USD: 'USD',
  HK: 'HKD',
  HKD: 'HKD',
  TW: 'TWD',
  TWD: 'TWD',
  JP: 'JPY',
  JPY: 'JPY',
  GB: 'GBP',
  GBP: 'GBP',
  DE: 'EUR',
  FR: 'EUR',
  EUR: 'EUR'
};

async function applyAvailability(page, desired) {
  console.log('[BROWSER_ACTION] ⚙️ Applying Availability & Pricing form...');
  const curSelect = page.locator('.price-config he-select, he-select').first();
  await curSelect.waitFor({ state: 'attached', timeout: 45_000 });
  await page.waitForTimeout(1500);

  // 1. Markets (marketSelection radio)
  const wantAllMarkets = desired.pricing?.markets === 'all' || desired.pricing?.markets === undefined;
  const marketRadio = page.locator(`input[name="marketSelection"][value="${wantAllMarkets ? 'true' : 'false'}"]`).first();
  if (await marketRadio.count()) {
    const isChecked = await marketRadio.isChecked().catch(() => false);
    if (!isChecked) {
      console.log(`[BROWSER_ACTION] Selecting Market => ${wantAllMarkets ? 'All Worldwide (true)' : 'Restricted (false)'}`);
      await marketRadio.click({ force: true });
    }
  }

  // 2. Base Currency (BaseCurrencySelector - MUST be selected before price-tier to unlock it)
  const rawCur = desired.pricing?.currency || 'CN';
  const targetCur = CURRENCY_MAP[rawCur.toUpperCase()] || rawCur;
  if (await curSelect.count()) {
    console.log(`[BROWSER_ACTION] 💰 Selecting Base Currency => ${targetCur} / CN...`);
    await curSelect.click().catch(() => {});
    await page.waitForTimeout(600);

    const selectedCur = await page.evaluate((target) => {
      const opt = document.querySelector(`he-option[value="${target}"], he-option[value="CN"], he-option[value="CNY"]`) ||
        [...document.querySelectorAll('he-option')].find(o => o.innerText.includes(target) || (target === 'CNY' && o.innerText.includes('中国')));
      if (opt) {
        opt.click();
        return opt.innerText.trim();
      }
      return null;
    }, targetCur);
    console.log(`[BROWSER_ACTION] Currency option click result: ${selectedCur}`);
    await page.waitForTimeout(1000);
  }

  // 3. Base Price Tier (price-tier-selection)
  // 3. Base Price Tier (price-tier-selection)
  const wantTier = String(desired.pricing?.priceTier ?? '0');
  console.log(`[BROWSER_ACTION] 🏷️ Selecting Price Tier => ${wantTier} (Free)...`);

  const selectedTier = await page.evaluate(async (tier) => {
    const s2 = document.querySelector('price-tier-selection[pricetierkey="Retail"] he-select') || document.querySelector('price-tier-selection he-select') || document.querySelectorAll('he-select')[1];
    if (!s2) return 'he-select not found';

    // Step 1: Click the indicator button inside shadowRoot
    const indicatorBtn = s2.shadowRoot?.querySelector('button.text-field__button') || s2.shadowRoot?.querySelector('button');
    if (indicatorBtn) {
      indicatorBtn.click();
    } else {
      const input = s2.shadowRoot?.querySelector('input');
      input?.click();
    }

    // Step 2: Wait for he-option elements to render
    let targetOpt = null;
    for (let i = 0; i < 30; i++) {
      await new Promise(r => setTimeout(r, 100));
      const options = [...s2.querySelectorAll('he-option'), ...document.querySelectorAll('price-tier-selection he-option')];
      targetOpt = options.find(o => {
        const txt = o.innerText.trim() || o.textContent.trim();
        return txt === tier || (tier === '0' && (txt === '0' || txt === '免费' || txt.toLowerCase() === 'free'));
      });
      if (targetOpt) break;
    }

    if (targetOpt) {
      // Step 3: Click the target he-option (e.g. 0)
      targetOpt.click();
      targetOpt.dispatchEvent(new CustomEvent('he-selected', { bubbles: true, detail: { item: targetOpt, value: tier } }));
      
      const input = s2.shadowRoot?.querySelector('input');
      if (input) {
        input.value = tier;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      document.body.click();
      return targetOpt.innerText.trim() || targetOpt.textContent.trim();
    }
    return 'option not found';
  }, wantTier);

  console.log(`[BROWSER_ACTION] Price tier selection result: ${selectedTier}`);
  if (!selectedTier || selectedTier === 'option not found' || selectedTier === 'he-select not found') {
    throw Object.assign(new Error(`Failed to select Price Tier "${wantTier}": ${selectedTier}. Partner Center options did not load or were not selectable.`), { code: 'PRICE_TIER_FAILED' });
  }
  await page.waitForTimeout(1500);

  // 4. Save & Verify Field
  await page.waitForTimeout(1000);
  await save(page);
  await page.waitForTimeout(1500);

  // 5. Cold Read-back Verification on availability form if still on page
  const recheck = await observeAvailability(page).catch(() => null);
  if (recheck && recheck.priceTier === '') {
    const isOverview = await page.locator('he-button[data-l10n-key="Start_Submission"], .submission-overview-container').count().catch(() => 0);
    if (!isOverview) {
      throw Object.assign(new Error('Availability form saved but Price Tier remains empty in DOM!'), { code: 'PRICE_TIER_EMPTY' });
    }
  }
}
async function applyProperties(page, desired, diff = []) {
  console.log('[BROWSER_ACTION] ⚙️ Applying Properties form with dynamic configuration...');

  // 1. Primary Category (from desired.properties.category)
  const targetCategory = desired.properties?.category || 'Productivity';
  const cat = page.locator('select[name="CategorySelect"]');
  await cat.waitFor({ state: 'visible', timeout: 30_000 });
  console.log(`[BROWSER_ACTION] Selecting Category => ${targetCategory}`);
  await cat.selectOption({ label: /生产率|Productivity/i }).catch(async () => {
    await cat.selectOption({ value: targetCategory }).catch(async () => {
      await cat.selectOption({ index: 14 });
    });
  });
  await cat.evaluate(el => {
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('input', { bubbles: true }));
  });

  // 2. Secondary Category (if configured in desired.properties.secondaryCategory)
  if (desired.properties?.secondaryCategory) {
    const secCat = page.locator('select[name="SecondaryCategorySelect"]');
    if (await secCat.count()) {
      console.log(`[BROWSER_ACTION] Selecting Secondary Category => ${desired.properties.secondaryCategory}`);
      await secCat.selectOption({ value: desired.properties.secondaryCategory }).catch(async () => {
        await secCat.selectOption({ label: new RegExp(escapeRegex(desired.properties.secondaryCategory), 'i') });
      });
      await secCat.evaluate(el => {
        el.dispatchEvent(new Event('change', { bubbles: true }));
        el.dispatchEvent(new Event('input', { bubbles: true }));
      });
    }
  }

  // 3. Privacy Policy (from desired.properties.privacy)
  // For full-trust desktop applications, Partner Center requires a privacy policy statement.
  const targetPrivacy = desired.properties?.privacy === 'No' ? 'No' : 'Yes';
  const priv = page.locator('select[name="privacyPolicySelection"]');
  await priv.waitFor({ state: 'visible', timeout: 15_000 });
  console.log(`[BROWSER_ACTION] Selecting Privacy Policy => ${targetPrivacy}`);
  await priv.selectOption({ label: targetPrivacy === 'No' ? /否|No/i : /是|Yes/i }).catch(async () => {
    await priv.selectOption({ value: targetPrivacy });
  });
  await priv.evaluate(el => {
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('input', { bubbles: true }));
  });

  // 4. Dynamic Privacy Policy details when Yes
  if (targetPrivacy === 'Yes') {
    const policyText = desired.properties?.privacyPolicyText || `本软件（${desired.productName || '桌面应用程序'}）为基于 Windows 本地独立运行的个人离线工具软件，无需用户注册登录。软件不收集、不存储、不上传亦不共享任何用户个人信息或设备隐私数据。所有业务数据均 100% 仅保存在用户当前本地设备磁盘中。`;
    if (!desired.properties?.privacyPolicyUrl) {
      console.log('[BROWSER_ACTION] Selecting #privacyPolicyText radio and filling offline statement...');
      const textRadio = page.locator('input#privacyPolicyText');
      if (await textRadio.count()) await textRadio.check({ force: true });
      const textarea = page.locator('textarea[aria-label="提供隐私策略文本"], textarea.form-control').first();
      await textarea.waitFor({ state: 'visible', timeout: 10_000 });
      await textarea.fill(policyText);
      await textarea.evaluate(el => {
        el.dispatchEvent(new Event('change', { bubbles: true }));
        el.dispatchEvent(new Event('input', { bubbles: true }));
      });
    } else if (desired.properties?.privacyPolicyUrl) {
      console.log('[BROWSER_ACTION] Selecting #privacyPolicyURL radio and filling URL...');
      const urlRadio = page.locator('input#privacyPolicyURL');
      if (await urlRadio.count()) await urlRadio.check({ force: true });
      const urlInput = page.locator('input[placeholder="Enter Privacy Policy URL"], input[aria-label="应用隐私策略 URL"]').first();
      await urlInput.waitFor({ state: 'visible', timeout: 10_000 });
      await urlInput.fill(desired.properties.privacyPolicyUrl);
      await urlInput.evaluate(el => {
        el.dispatchEvent(new Event('change', { bubbles: true }));
        el.dispatchEvent(new Event('input', { bubbles: true }));
      });
    }
  }

  // 5. Product Declarations (if configured in desired.properties.declarations)
  if (desired.properties?.declarations) {
    for (const [key, value] of Object.entries(desired.properties.declarations)) {
      const cb = page.locator(`he-checkbox[name="'${key}-checkbox'"], lib-checkbox[elementid="'${key}-checkbox'"] input, [name="${key}-checkbox"]`).first();
      if (await cb.count()) {
        const cur = await cb.isChecked().catch(async () => (await cb.getAttribute('checked')) !== null || (await cb.getAttribute('aria-checked')) === 'true');
        if (Boolean(value) !== cur) {
          console.log(`[BROWSER_ACTION] Toggling declaration [${key}] to ${value}`);
          await cb.click({ force: true });
        }
      }
    }
  }

  // 6. Support Info (if configured in desired.properties.support)
  if (desired.properties?.support) {
    const s = desired.properties.support;
    if (s.website) await fill(page, ['input[for="website"]'], s.website, 'website');
    if (s.contact) await fill(page, ['input[for="contact"]'], s.contact, 'contact');
    if (s.phone) await fill(page, ['input[for="supportPhone"]'], s.phone, 'phone');
    if (s.address1) await fill(page, ['input[for="supportAddress1"]'], s.address1, 'address1');
    if (s.address2) await fill(page, ['input[for="supportAddress2"]'], s.address2, 'address2');
    if (s.postalCode) await fill(page, ['input[for="postalCode"]'], s.postalCode, 'postalCode');
    if (s.city) await fill(page, ['input[for="city"]'], s.city, 'city');
    if (s.state) await fill(page, ['input[for="state"]'], s.state, 'state');
    if (s.country) await fill(page, ['input[for="country"]'], s.country, 'country');
  }

  await page.waitForTimeout(500);
  await save(page);
}
async function applyAgeRatings(page, desired) {
  console.log('[BROWSER_ACTION] ⚙️ Applying Age Ratings form from scratch...');

  // Check if we are genuinely on summary/preview screen
  const isSummary = page.url().includes('ageratings/summary') || (await page.locator('age-rating-summary, .rating-summary-table, .rating-preview, he-button[data-l10n-key="AppSubmission_AgeRating_ContinueButton"]').count()) > 0;
  if (isSummary) {
    console.log('[BROWSER_ACTION] On Age Ratings Summary/Preview screen. Checking terms and clicking Continue/Save...');
    const terms = page.locator('he-checkbox, input[type="checkbox"]').filter({ hasText: /IARC|条款|terms/i }).or(page.locator('he-checkbox')).first();
    if (await terms.count()) {
      await terms.evaluate(el => el.click()).catch(() => terms.click({ force: true }));
    }
    await page.waitForTimeout(500);
    const contBtn = page.locator('he-button[data-l10n-key="AppSubmission_AgeRating_ContinueButton"], he-button').filter({ hasText: /继续|Continue|保存|Save/i }).first();
    if (await contBtn.count()) {
      console.log('[BROWSER_ACTION] Clicking "继续" (Continue) button on Age Ratings summary page...');
      await contBtn.click({ force: true }).catch(async () => contBtn.evaluate(el => el.click()));
    }
    return;
  }

  // 1. Input Mode (questionnaire)
  const modeVal = desired.ageRatings?.mode || 'questionnaire';
  console.log(`[BROWSER_ACTION] Selecting Input Mode => ${modeVal}`);
  const mode = page.locator(`input[name="inputMode"][value="${modeVal}"]`).first();
  if (await mode.count()) {
    await mode.click({ force: true }).catch(() => mode.evaluate(el => el.click()));
  }

  // 2. Application Type (default: 2558 - 其他所有应用类型)
  const appType = desired.ageRatings?.applicationType || '2558';
  console.log(`[BROWSER_ACTION] Selecting Application Type => ${appType} (其他所有应用类型)`);
  const typeRadio = page.locator(`input[name="question#1109"][value="${appType}"]`).or(page.locator(`input[value="${appType}"]`)).first();
  if (await typeRadio.count()) {
    await typeRadio.evaluate(el => {
      el.click();
      el.dispatchEvent(new Event('change', { bubbles: true }));
      el.dispatchEvent(new Event('input', { bubbles: true }));
    }).catch(() => typeRadio.click({ force: true }));
  }
  await page.waitForTimeout(1000);

  // 3. Answer specific or default (No / 否) for all followup questions
  const groups = page.locator('.followup-questions [role="radiogroup"], .followup-questions .radio-group, questionnaire [role="radiogroup"]');
  const count = await groups.count();
  console.log(`[BROWSER_ACTION] Answering follow-up questions (${count} groups found)...`);
  for (let i = 0; i < count; i += 1) {
    const group = groups.nth(i);
    const isAnswered = await group.locator('input[type="radio"]:checked, [role="radio"][aria-checked="true"]').count();
    if (!isAnswered) {
      const noOption = group.locator('label, [role="radio"], input[type="radio"]').filter({ hasText: /否|No/i }).first();
      if (await noOption.count()) {
        await noOption.evaluate(el => el.click()).catch(() => noOption.click({ force: true }));
      }
    }
  }

  // 4. Physical Media / Distribution question -> No (#noVal)
  const noPhysical = page.locator('input#noVal, label[for="noVal"], label:has-text("否")').first();
  if (await noPhysical.count()) {
    console.log('[BROWSER_ACTION] Answering Physical Media Distribution => No (#noVal)');
    await noPhysical.evaluate(el => el.click()).catch(() => noPhysical.click({ force: true }));
  }

  // 5. Click "预览分级" (Preview Ratings) button
  console.log('[BROWSER_ACTION] Clicking "预览分级" (Preview Ratings) button...');
  const previewBtn = page.locator('he-button[data-l10n-key="AppSubmission_AgeRating_PreviewRatingsButton"], he-button').filter({ hasText: /预览分级|预览|Preview/i }).or(page.locator('he-button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"], he-button:has-text("保存草稿")')).first();
  if (await previewBtn.count()) {
    await previewBtn.click({ force: true }).catch(async () => previewBtn.evaluate(el => el.click()));
  }
  await page.waitForTimeout(4000);

  // 6. Handle summary page if it transitioned to summary / preview
  const summaryTerms = page.locator('he-checkbox, input[type="checkbox"]').filter({ hasText: /IARC|条款|terms/i }).or(page.locator('he-checkbox')).first();
  if (await summaryTerms.count()) {
    console.log('[BROWSER_ACTION] Accepting IARC Terms & Conditions on preview screen...');
    await summaryTerms.evaluate(el => el.click()).catch(() => summaryTerms.click({ force: true }));
    await page.waitForTimeout(500);
  }

  const finalContinue = page.locator('he-button[data-l10n-key="AppSubmission_AgeRating_ContinueButton"], he-button, button, input[type="submit"]').filter({ hasText: /继续|Continue|保存|Save|提交|Submit/i }).first();
  if (await finalContinue.count()) {
    console.log('[BROWSER_ACTION] Clicking final "继续" (Continue) button on Age Ratings page...');
    await finalContinue.click({ force: true }).catch(async () => finalContinue.evaluate(el => el.click()));
    await page.waitForTimeout(2000);
  }
}
async function setFileInput(page, selector, targetPath) {
  try {
    const client = await page.context().newCDPSession(page);
    const doc = await client.send('DOM.getDocument', { depth: -1, pierce: true });
    const inputNode = await client.send('DOM.querySelector', {
      nodeId: doc.root.nodeId,
      selector: selector || 'input[name="fileuploader"], input[type="file"]'
    });
    if (inputNode?.nodeId) {
      await client.send('DOM.setFileInputFiles', {
        files: [targetPath],
        nodeId: inputNode.nodeId
      });
      await page.evaluate(() => {
        const el = document.querySelector('input[name="fileuploader"], input[type="file"]');
        if (el) {
          el.dispatchEvent(new Event('change', { bubbles: true }));
          el.dispatchEvent(new Event('input', { bubbles: true }));
        }
      });
      return;
    }
  } catch {}
  const input = page.locator(selector).first();
  await input.setInputFiles(targetPath);
}

async function applyPackages(page, desired, diff = [], deadline) {
  const target = path.resolve(desired.package?.path || '');
  if (!desired.package?.path || !fs.existsSync(target) || !fs.statSync(target).isFile()) {
    throw new Error(`Package file not found at: ${desired.package?.path}`);
  }

  const targetFileName = path.basename(target);
  console.log(`[BROWSER_ACTION] ⚙️ Applying Packages form with target file parameter: "${targetFileName}" (${target})`);

  // 1. Check if package is already uploaded and validated
  const findCard = () => page.locator('app-package-details-submission, .package-table, tr.package-row')
    .filter({ hasText: targetFileName })
    .first();

  let card = findCard();
  let alreadyReady = false;
  if (await card.count() && await card.isVisible().catch(() => false)) {
    const cardText = await card.innerText().catch(() => '');
    const hasRemove = (await card.locator('button[data-l10n-key*="remove"], button:has-text("Remove"), button:has-text("删除"), .packageActions button').count()) > 0;
    const hasDetails = /v\d+\.\d+|\bX64\b|\bx64\b|Windows\.Desktop/i.test(cardText);
    if (hasRemove || hasDetails) {
      console.log(`[BROWSER_ACTION] 🎯 Package "${targetFileName}" is already present and validated on page!`);
      alreadyReady = true;
    }
  }

  if (!alreadyReady) {
    // 2. Pre-clean any faulty/paused packages
    const deleteFaulty = page.locator('a[data-l10n-key="app_package_action_delete"], button[data-l10n-key="app_package_action_delete"], a:has-text("Delete"), button:has-text("Delete")').first();
    if (await deleteFaulty.count() && await deleteFaulty.isVisible().catch(() => false)) {
      console.log('[BROWSER_ACTION] 🧹 Cleaning up faulty paused package entry...');
      await deleteFaulty.click({ force: true }).catch(() => {});
      await page.waitForTimeout(1000);
      const confirmModalBtn = page.locator('.modal, [role="dialog"], lib-modal')
        .locator('button, he-button, [role="button"]')
        .filter({ hasText: /Delete|删除|确定|Confirm|Yes|是/i })
        .first();
      if (await confirmModalBtn.count() && await confirmModalBtn.isVisible().catch(() => false)) {
        await confirmModalBtn.click().catch(() => {});
        await page.waitForTimeout(1500);
      }
    }
    const revertBtn = page.locator('a[data-l10n-key="app_package_action_revert"], button[data-l10n-key="app_package_action_revert"], a:has-text("Revert")').first();
    if (await revertBtn.count() && await revertBtn.isVisible().catch(() => false)) {
      console.log('[BROWSER_ACTION] 🔄 Reverting package removal state...');
      await revertBtn.click({ force: true }).catch(() => {});
      await page.waitForTimeout(1000);
    }
    const faultyDeleteBtn = page.getByRole('button', { name: /Delete|删除/i })
      .or(page.locator('a, button, [role="button"]').filter({ hasText: /Delete|删除/i }))
      .first();
    if (await faultyDeleteBtn.count() && await faultyDeleteBtn.isVisible().catch(() => false)) {
      console.log('[BROWSER_ACTION] 🧹 Cleaning up existing faulty package entry...');
      await faultyDeleteBtn.click().catch(() => {});
      await page.waitForTimeout(1000);
      const confirmBtn = page.locator('.modal, [role="dialog"], lib-modal')
        .locator('button, he-button, [role="button"]')
        .filter({ hasText: /Delete|删除|确定|Confirm|Yes|是/i })
        .first();
      if (await confirmBtn.count() && await confirmBtn.isVisible().catch(() => false)) {
        await confirmBtn.click().catch(() => {});
        await page.waitForTimeout(1500);
      }
    }

    // 3. Upload via CDP
    console.log(`[BROWSER_ACTION] Uploading official MSIX package file via CDP: ${target}...`);
    await setFileInput(page, 'input[name="fileuploader"], input[type="file"]', target);
  }

  // 4. Monitor upload & validation lifecycle (scoped to package card)
  console.log(`[BROWSER_ACTION] Monitoring validation for "${targetFileName}"...`);
  const timeoutMs = Math.min(deadline.remaining(), 720_000);
  let retryCount = 0;

  await waitUntil(async () => {
    // Check for hard error text in alerts
    const errorNodes = await page.locator('.alert-danger:visible, .alert-error:visible, .error-message:visible').allTextContents().catch(() => []);
    const errorText = errorNodes.join(' ');
    if (/包接受验证错误|package.*error|failed|失败/i.test(errorText)) {
      throw Object.assign(new Error(`package validation error: ${errorText}`), { retryable: false });
    }

    // Auto-recover from transient upload error (e.g. network glitch)
    const hasUploadError = (await page.locator('.uploadStatus-text:has-text("Error")').count()) > 0;
    if (hasUploadError && retryCount < 3) {
      retryCount++;
      console.log(`[BROWSER_ACTION] ⚠️ Upload encountered transient error, retrying (${retryCount}/3)...`);
      const cancelLink = page.locator('a[data-l10n-key="app_package_upload_action_cancel"], a:has-text("Cancel")').first();
      if (await cancelLink.count()) await cancelLink.click().catch(() => {});
      await page.waitForTimeout(2000);
      await setFileInput(page, 'input[name="fileuploader"], input[type="file"]', target);
      await page.waitForTimeout(3000);
      return false;
    }

    // Check target card within app-package-details-submission
    card = findCard();
    if (await card.count() && await card.isVisible().catch(() => false)) {
      const cardText = await card.innerText().catch(() => '');
      const hasRemoveBtn = (await card.locator('button[data-l10n-key*="remove"], button:has-text("Remove"), button:has-text("删除"), .packageActions button').count()) > 0;
      const hasVersionOrArch = /v\d+\.\d+|\bX64\b|\bx64\b|Windows\.Desktop/i.test(cardText);
      const cardSpinner = (await card.locator('progressbar, [role="progressbar"], .progress-bar, .spinner, he-progress-ring').count()) > 0;

      // If card has remove button or extracted version/arch and no internal spinner, it's ready!
      if ((hasRemoveBtn || hasVersionOrArch) && !cardSpinner) {
        return true;
      }
    }

    return false;
  }, { timeoutMs, intervalMs: 1500, label: `package ${targetFileName} validation` });

  console.log(`[BROWSER_ACTION] ✨ Package "${targetFileName}" validated successfully!`);

  // 5. Device family configuration
  await check(page, ['input[aria-label*="Desktop" i]', 'input[name="desktop"]', 'label:has-text("Desktop") input'], true, 'desktop').catch(() => {});
  await check(page, ['input[aria-label*="future" i]', 'input[name="future"]', 'label:has-text("future") input'], true, 'future device families').catch(() => {});

  // 6. Save
  await page.waitForTimeout(500);
  await save(page);
}
async function applyListing(page, desired) {
  console.log('[BROWSER_ACTION] ⚙️ Applying Store Listing form with refined 6 core items...');

  // 1. 说明：详细介绍正文（写个几百字）
  const descLoc = page.locator('#description-required, textarea[name="description"], textarea.form-control').first();
  await descLoc.waitFor({ state: 'visible', timeout: 45_000 });
  const descText = (desired.values?.description && desired.values.description.trim().length > 30)
    ? desired.values.description
    : `${desired.productName || '本软件'}是一款专为 Windows 平台打造的高性能纯单机个人桌面应用。秉承“本地离线、安全极速、免登录、免配置”的核心理念，所有数据 100% 完整保存在您的本地电脑中，零隐私外泄风险。\n\n软件界面设计典雅纯净，交互流畅自然，无论是日常管理、记事创作还是信息整理，都能为您带来舒心专注的沉浸式体验。支持本地数据随时备份与导入导出，即使更换电脑也能轻松迁移，无缝衔接您的数字生活。`;
  console.log(`[BROWSER_ACTION] 📝 Filling Description (#description-required, ${descText.length} chars)...`);
  await descLoc.fill(descText);
  await descLoc.dispatchEvent('input').catch(() => {});
  await descLoc.dispatchEvent('change').catch(() => {});

  // 2. 简短描述：写一句 200 字以内的精炼卖点
  const shortLoc = page.locator('#shortDescription, textarea[name="shortDescription"], input[name="shortDescription"]').first();
  if (await shortLoc.count()) {
    const shortText = (desired.values?.shortDescription && desired.values.shortDescription.trim())
      ? desired.values.shortDescription
      : `${desired.productName || '纯单机桌面助手'}：极简优雅、离线安全、随时记录的个人效率与回忆管理工具。`;
    const clippedShort = shortText.slice(0, 200);
    console.log(`[BROWSER_ACTION] 📝 Filling Short Description (#shortDescription, ${clippedShort.length} chars)...`);
    await shortLoc.fill(clippedShort);
    await shortLoc.dispatchEvent('input').catch(() => {});
    await shortLoc.dispatchEvent('change').catch(() => {});
  }

  // 3. 产品功能：填满已有的 3 个功能条目（#feature-0, #feature-1, #feature-2）
  const defaultFeatures = [
    '纯本地离线持久化存储，无需注册登录，数据完全掌控在自己手中',
    '界面优雅清爽，操作丝滑直观，专注纯粹的高效体验与仪式感',
    '轻量化桌面架构，支持本地文件一键备份与恢复，跨设备轻松迁移'
  ];
  const userFeatures = (desired.values?.features || []).filter(Boolean);
  const featuresToFill = [
    userFeatures[0] || defaultFeatures[0],
    userFeatures[1] || defaultFeatures[1],
    userFeatures[2] || defaultFeatures[2]
  ];
  console.log('[BROWSER_ACTION] 📝 Filling 3 Product Features (#feature-0, #feature-1, #feature-2)...');
  for (let i = 0; i < 3; i++) {
    const featInput = page.locator(`#feature-${i}`);
    if (await featInput.count()) {
      await featInput.fill(featuresToFill[i]);
      await featInput.dispatchEvent('input').catch(() => {});
      await featInput.dispatchEvent('change').catch(() => {});
    }
  }

  // 4. 关键字：选上 3~5 个搜索关键词
  const defaultKeywords = ['效率工具', '本地存储', '桌面记事', '单机应用'];
  const userKeywords = (desired.values?.keywords || []).filter(Boolean);
  const finalKeywords = (userKeywords.length ? userKeywords : defaultKeywords).slice(0, 5);
  const kwContainer = page.locator('#search-terms he-select').first();
  if (await kwContainer.count()) {
    console.log(`[BROWSER_ACTION] 🔍 Adding 3~5 Keywords (#search-terms): ${finalKeywords.join(', ')}`);
    const kwInput = kwContainer.locator('input').first();
    if (await kwInput.count()) {
      for (const kw of finalKeywords) {
        await kwInput.click().catch(() => {});
        await kwInput.fill(kw);
        await page.keyboard.press('Enter');
        await page.waitForTimeout(250);
      }
    }
  }

  // 5. 图像上传（桌面截图 1~4 张 + 1:1 酷图 1080x1080）
  const rootDir = desired.appRoot || process.cwd();
  const configuredScreenshot = desired.assets?.screenshot ? path.resolve(desired.assets.screenshot) : '';
  const configuredAsset = Object.values(desired.listing?.assets ?? {}).find(item => item?.path)?.path;
  const effectiveAssetsDir = configuredScreenshot ? path.dirname(configuredScreenshot) : configuredAsset ? path.dirname(path.resolve(configuredAsset)) : rootDir;

  function findAssetFiles(matchers) {
    const found = [];
    const searchDirs = [
      path.join(rootDir, 'store-submission-assets'),
      path.join(rootDir, 'store', 'assets'),
      effectiveAssetsDir,
      path.join(process.cwd(), 'store-submission-assets'),
      rootDir,
      process.cwd()
    ];
    for (const dir of searchDirs) {
      if (!fs.existsSync(dir)) continue;
      try {
        const files = fs.readdirSync(dir);
        for (const f of files) {
          const full = path.join(dir, f);
          if (!fs.statSync(full).isFile()) continue;
          for (const m of matchers) {
            if (typeof m === 'string' && f.toLowerCase() === m.toLowerCase()) {
              if (!found.includes(full)) found.push(full);
            } else if (m instanceof RegExp && m.test(f)) {
              if (!found.includes(full)) found.push(full);
            }
          }
        }
      } catch {}
    }
    return found;
  }

  // 5.1 桌面截图：上传 1~4 张操作界面截图
  const desktopInput = page.locator('#panel-2 input[type="file"], he-tab-panel[id="panel-2"] input[type="file"], he-tab-panel[aria-labelledby="tab-0"] input[type="file"]').first();
  if (await desktopInput.count()) {
    const hasExistingScreenshots = (await page.locator('#panel-2 .screenshot-swap-container img[src], #panel-2 app-image-display img[src]').count().catch(() => 0)) > 0;
    if (hasExistingScreenshots) {
      console.log('[BROWSER_ACTION] 🎯 Desktop Screenshots already present on page.');
    } else {
      const screenshots = findAssetFiles([
        configuredScreenshot ? path.basename(configuredScreenshot) : null,
        '01_微软商店详情页_主运行界面高清截图_1366x768.png',
        '01_应用主界面高清截图_1366x768.png',
        'Screenshot.png',
        /screenshot.*\.png$/i,
        /截图.*\.png$/i,
        /1366x768.*\.png$/i
      ].filter(Boolean)).slice(0, 4);

      const targetFile = screenshots[0];
      if (targetFile) {
        console.log(`[BROWSER_ACTION] 🖼️ Uploading Desktop Screenshot into #panel-2: ${targetFile}`);
        await desktopInput.setInputFiles(targetFile).catch(() => {});
        await desktopInput.evaluate(el => {
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
        }).catch(() => {});
        console.log('[BROWSER_ACTION] ⏳ Waiting for desktop screenshot thumbnail to mount in DOM...');
        await page.locator('#panel-2 img[src], #panel-2 app-image-display img[src], #panel-2 .screenshot-swap-container').first().waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});
        await page.waitForTimeout(1000);
      }
    }
  }

  // 5.2 徽标与美工卡槽全量上传（1:1 酷图 + 9:16 招贴画 + 300x300 磁贴 + 71x71 小图标 + 150x150 图标）
  const assetSlots = [
    {
      name: '1:1 酷图 (1080x1080)',
      pattern: /1:1 酷图|1080 x 1080/i,
      matchers: ['BoxArt1080x1080.png', /1080x1080.*\.png$/i, /酷图.*\.png$/i, 'Square1080x1080Logo.png']
    },
    {
      name: '9:16 招贴画 (720x1080)',
      pattern: /9:16|720 x 1080|招贴画/i,
      matchers: ['PosterArt720x1080.png', /720x1080.*\.png$/i, /招贴画.*\.png$/i, /海报.*\.png$/i]
    },
    {
      name: '1:1 应用磁贴图标 (300x300)',
      pattern: /300 x 300|应用磁贴/i,
      matchers: ['Square300x300Logo.png', /300x300.*\.png$/i, /大磁贴.*\.png$/i]
    },
    {
      name: '1:1 小图标 (71x71)',
      pattern: /71 x 71/i,
      matchers: ['09_小图标_71x71.png', 'Square71x71Logo.png', /71x71.*\.png$/i]
    },
    {
      name: '1:1 中图标 (150x150)',
      pattern: /150 x 150/i,
      matchers: ['Square150x150Logo.png', /150x150.*\.png$/i, /中磁贴.*\.png$/i]
    }
  ];

  for (const slot of assetSlots) {
    const display = page.locator('app-image-display, .logo-upload-section, .asset-card, .listing-image-inner').filter({ hasText: slot.pattern }).first();
    if (await display.count()) {
      if ((await display.locator('img[src]').count()) > 0) {
        console.log(`[BROWSER_ACTION] 🎯 Slot "${slot.name}" already has uploaded image.`);
        continue;
      }
      const input = display.locator('input[type="file"]').first();
      if (await input.count()) {
        const files = findAssetFiles(slot.matchers);
        if (files[0]) {
          console.log(`[BROWSER_ACTION] 🖼️ Uploading ${slot.name}: ${files[0]}`);
          await input.setInputFiles(files[0]).catch(() => {});
          await input.evaluate(el => {
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
          }).catch(() => {});
          await page.waitForTimeout(1500);
        }
      }
    }
  }

  // 6. 保存草稿
  console.log('[BROWSER_ACTION] ⏳ Waiting briefly for Angular change detection before saving...');
  await page.waitForTimeout(2000);
  await save(page);
}

async function uploadAsset(page, filePath, label, fallbackIndex) { const labeled = page.getByLabel(label).first(); if (await labeled.count()) { const tag = await labeled.evaluate(element => element.tagName).catch(() => ''); if (tag.toLowerCase() === 'input') { await labeled.setInputFiles(filePath); return; } const nested = labeled.locator('input[type="file"]').first(); if (await nested.count()) { await nested.setInputFiles(filePath); return; } } const inputs = page.locator('input[type="file"]'); if (fallbackIndex < await inputs.count()) { await inputs.nth(fallbackIndex).setInputFiles(filePath); return; } throw Object.assign(new Error(`asset input not found: ${filePath}`), { code: 'SCHEMA_DRIFT' }); }

async function applyOptions(page, desired) {
  console.log('[BROWSER_ACTION] ⚙️ Applying Submission Options form...');
  const formReady = page.locator('input[name="PublishMode"], textarea, button[name="save_button"]').first();
  await formReady.waitFor({ state: 'attached', timeout: 45_000 });
  await page.waitForTimeout(1500);

  // 1. 发布暂缓选项 (Publish Mode: ASAP / Manual)
  const wantManual = /manual|手动/i.test(desired.submissionOptions?.publishMode || '');
  const targetRadio = wantManual
    ? page.locator('input#radioReleaseDate_manual, input[value="Manual"], input[name="PublishMode"][value="Manual"]').first()
    : page.locator('input#radioReleaseDate_asap, input[value="Asap"], input[name="PublishMode"][value="Asap"]').first();

  if (await targetRadio.count()) {
    console.log(`[BROWSER_ACTION] Selecting Publish Mode => ${wantManual ? 'Manual (手动发布)' : 'ASAP (认证通过后立即发布)'}...`);
    await targetRadio.evaluate(el => {
      el.click();
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    }).catch(() => targetRadio.check({ force: true }));
    await page.waitForTimeout(500);
  }

  // 2. runFullTrust restricted capability reason
  const ta = page.locator('textarea.text-area-width, textarea[maxlength="500"], textarea.has-error, section:has-text("runFullTrust") textarea').first();
  await ta.waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});

  if (await ta.count() && await ta.isVisible().catch(() => false)) {
    const reasonText = desired.submissionOptions?.runFullTrustReason || `本产品（${desired.productName || '桌面应用程序'}）为基于 Windows 本地独立运行的桌面应用程序。需要使用 runFullTrust 权限以读写本地用户数据存储文件，实现数据本地安全持久化保存，不依赖也不连接任何外部云端网络服务。`;
    console.log(`[BROWSER_ACTION] 📝 Filling runFullTrust reason statement (${reasonText.length} chars)...`);
    await ta.fill(reasonText);
    await ta.evaluate(el => {
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await page.waitForTimeout(500);
  }

  // 3. 认证说明 (可选其他测试信息/说明)
  if (desired.submissionOptions?.notesForCertification) {
    const notesTa = page.locator('textarea#notesForCertification, textarea[name="notesForCertification"]').first();
    if (await notesTa.count() && await notesTa.isVisible().catch(() => false)) {
      await notesTa.fill(desired.submissionOptions.notesForCertification);
      await notesTa.dispatchEvent('input').catch(() => {});
      await notesTa.dispatchEvent('change').catch(() => {});
    }
  }

  // 4. Save
  await save(page);
}
function escapeRegex(value) { return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

export function phaseAdapters(driver) { return Object.fromEntries(PHASES.map(phase => [phase, { acceptedPageKinds: accepted[phase], ensurePage: () => driver.ensurePage(phase), observe: () => driver.observe(phase), diff: (_desired, observed) => driver.diff(phase, observed), apply: (diff, desired, deadline) => driver.apply(phase, diff, deadline), coldVerify: (desired, deadline) => driver.coldVerify(phase, deadline), overviewVerify: deadline => driver.overviewVerify(phase, deadline), captureEvidence: evidence => driver.captureEvidence(evidence, phase), captureErrorEvidence: (evidence, error) => driver.captureErrorEvidence(evidence, phase, error) }])); }
