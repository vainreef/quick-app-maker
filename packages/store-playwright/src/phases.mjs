import path from 'node:path';
import { PHASES } from '@quick-app/store-core';
import { capturePage, waitForPageKind, waitUntil } from './inspector.mjs';
import { observeOverview } from './overview.mjs';

const accepted = {
  availability: ['AvailabilityForm', 'SubmissionOverview'], properties: ['PropertiesForm', 'SubmissionOverview'],
  'age-ratings': ['AgeRatingsQuestionnaire', 'AgeRatingsSummary', 'SubmissionOverview'], packages: ['PackagesForm', 'SubmissionOverview'],
  listing: ['ListingLanguageGrid', 'ListingForm', 'SubmissionOverview'], options: ['OptionsForm', 'SubmissionOverview']
};
const routes = { availability: 'availability', properties: 'properties', 'age-ratings': 'ageratings', packages: 'packages', listing: 'managelanguages?producttype=app', options: 'options' };

export class StoreDriver {
  constructor({ page, desired, checkpoint, logger }) { this.page = page; this.desired = desired; this.checkpoint = checkpoint; this.logger = logger; this.currentPhase = ''; }
  root(phase = this.currentPhase) {
    const discovered = this.checkpoint.routes?.[phase];
    if (discovered && discovered.includes(`/submissions/${this.checkpoint.submissionId}/`)) return discovered;
    const base = this.desired.site.baseUrl.replace(/\/$/, ''); const product = encodeURIComponent(this.desired.productId); const submission = this.checkpoint.submissionId || this.desired.submissionId;
    if (!product || product === 'PENDING' || !submission) throw new Error('productId and submissionId are required; run store discover first');
    return `${base}/${product}/submissions/${submission}/${routes[phase] ?? 'overview'}`;
  }
  async ensurePage(phase) {
    this.currentPhase = phase; const url = this.root(phase); this.logger?.info('navigate', { phase, url }); await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    let page = await waitForPageKind(this.page, accepted[phase], { operation: `${phase} PageKind` });
    if (phase === 'listing' && page.kind === 'ListingLanguageGrid') { await chooseLanguage(this.page, this.desired.site.languageCode); page = await waitForPageKind(this.page, ['ListingForm'], { operation: 'listing language form' }); }
    return page;
  }
  async observe(phase) {
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
    const url = this.page.url(); await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 }); const snapshot = await waitForPageKind(this.page, accepted[phase], { operation: `${phase} cold PageKind` });
    if (snapshot.kind === 'SubmissionOverview') return { pageKind: snapshot.kind, observed: {}, diff: [] };
    if (phase === 'listing' && snapshot.kind === 'ListingLanguageGrid') { await chooseLanguage(this.page, this.desired.site.languageCode); await waitForPageKind(this.page, ['ListingForm'], { operation: 'listing cold language form' }); }
    const observed = await this.observe(phase); const diff = await this.diff(phase, observed); return { pageKind: (await capturePage(this.page)).kind, observed, diff };
  }
  async overviewVerify() {
    const url = `${this.desired.site.baseUrl.replace(/\/$/, '')}/${encodeURIComponent(this.desired.productId)}/overview`; await this.page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 }); const overview = await observeOverview(this.page); const module = overview.modules[this.currentPhase];
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
async function readValue(page, selectors) { const item = await first(page, selectors); if (!item) return ''; try { return (await item.inputValue()).trim(); } catch {} try { return ((await item.getAttribute('value')) || '').trim(); } catch {} try { return ((await item.innerText()) || '').trim(); } catch {} return ''; }
async function fill(page, selectors, value, label) { const item = await first(page, selectors); if (!item) throw Object.assign(new Error(`field not found: ${label}`), { code: 'SCHEMA_DRIFT' }); await item.fill(String(value)); }
async function choose(page, selectors, value, label) { const item = await first(page, selectors); if (!item) throw Object.assign(new Error(`select not found: ${label}`), { code: 'SCHEMA_DRIFT' }); if ((await item.evaluate(el => el.tagName)).toLowerCase() === 'select') { await item.selectOption({ label: String(value) }).catch(() => item.selectOption(String(value))); return; } await item.click(); const option = page.getByRole('option', { name: new RegExp(escapeRegex(String(value)), 'i') }).first(); await option.click({ timeout: 15_000 }); }
async function check(page, selectors, want, label) { const item = await first(page, selectors); if (!item) throw Object.assign(new Error(`checkbox not found: ${label}`), { code: 'SCHEMA_DRIFT' }); const checked = await item.isChecked().catch(async () => (await item.getAttribute('aria-checked')) === 'true'); if (checked !== want) await item.click(); }
async function save(page) { const button = page.getByRole('button', { name: /^(保存|Save|保存更改|Save changes)$/i }).last(); if (!(await button.count())) throw Object.assign(new Error('save button not found'), { code: 'SCHEMA_DRIFT' }); await button.click(); let becameDisabled = false; await waitUntil(async () => { const alerts = (await page.locator('[role="alert"],.alert-error,.alert-danger').allTextContents()).join(' '); if (/error|失败|错误/i.test(alerts)) throw Object.assign(new Error(`save returned an error: ${alerts}`), { retryable: false }); const body = await page.locator('body').innerText(); const disabled = await button.isDisabled().catch(() => false); becameDisabled ||= disabled; return /saved|已保存|保存成功/i.test(body) || disabled || (becameDisabled && !disabled); }, { timeoutMs: 30_000, label: 'save action settled' }); }
async function chooseLanguage(page, languageCode) { const item = page.getByText(new RegExp(languageCode === 'zh-cn' ? '中文|Chinese|zh-cn' : escapeRegex(languageCode), 'i')).first(); if (await item.count()) await item.click(); else throw Object.assign(new Error(`listing language not found: ${languageCode}`), { code: 'SCHEMA_DRIFT' }); }

async function observeAvailability(page) { return { currency: await readValue(page, ['select[name="currency"]', 'he-select[name="currency"]', '#currency']), priceTier: await readValue(page, ['select[name="pricingTier"]', 'he-select[name="pricingTier"]', '#pricingTier']), allMarkets: (await page.locator('input[name="marketSelection"]:checked,[name="marketSelection"][aria-checked="true"]').count()) > 0 || (await page.getByText(/所有可能的市场|All possible markets/i).count() > 0) }; }
async function observeProperties(page) { return { category: await readValue(page, ['select[name="CategorySelect"]', 'he-select[name="category"]', '#category-select']), privacy: await readValue(page, ['input[name="privacyPolicySelection"]:checked', 'input[name="privacy"]:checked']), capabilities: { storage: await checkedValue(page, /存储|storage/i), backups: await checkedValue(page, /备份|backup/i), windows: await checkedValue(page, /Windows/i), usesGenAI: await checkedValue(page, /生成式|GenAI/i) } }; }
async function checkedValue(page, name) { const item = page.getByRole('checkbox', { name }).first(); return await item.isChecked().catch(() => false); }
async function observeAgeRatings(page) { const answers = await page.locator('input[type="radio"]:checked').evaluateAll(items => Object.fromEntries(items.map(item => [item.name || item.id, item.value]))); return { mode: await readValue(page, ['input[name="inputMode"]:checked']), applicationType: await readValue(page, ['input[name="question#1109"]:checked']), answers, physicalMedia: await checkedValue(page, /物理介质|physical media/i), terms: await checkedValue(page, /IARC|条款|terms/i), complete: /summary/i.test(page.url()) }; }
async function observePackages(page) { const entries = await page.locator('tr').evaluateAll(rows => rows.map(row => { const text = (row.innerText || '').replace(/\s+/g, ' ').trim(); const file = text.match(/[^\s]+\.(?:msix|appx|msixbundle|appxbundle)/i)?.[0] || ''; const status = /error|错误|failed|失败/i.test(text) ? 'Error' : /processing|analyzing|上传中|处理中|正在分析/i.test(text) ? 'Processing' : /validated|已验证|已完成/i.test(text) ? 'Validated' : file ? 'Unknown' : ''; return file ? { fileName: file, status, text } : null; }).filter(Boolean)); return { entries, desktop: await checkedValue(page, /Desktop|桌面/i), mobile: await checkedValue(page, /Mobile|移动/i), xbox: await checkedValue(page, /Xbox/i), future: await checkedValue(page, /future|未来/i) }; }
async function observeListing(page) { return { description: await readValue(page, ['textarea[name="description"]', '#description']), shortDescription: await readValue(page, ['textarea[name="shortDescription"]', '#shortDescription', 'input[name="shortDescription"]']), features: await page.locator('[data-feature], .feature-item, input[name="features"]').evaluateAll(items => items.map(x => (x.value || x.innerText || '').trim()).filter(Boolean)), keywords: await page.locator('[data-keyword], .keyword, input[name="keywords"]').evaluateAll(items => items.map(x => (x.value || x.innerText || '').trim()).filter(Boolean)), assetCount: await page.locator('img[alt*="logo" i],img[alt*="screenshot" i],.asset-thumbnail img').count() }; }
async function observeOptions(page) { const raw = await readValue(page, ['input[name="releaseDate"]:checked', 'input[name^="radioReleaseDate_"]:checked']); return { publishMode: /manual|手动/i.test(raw) ? 'Manual' : /asap|尽快/i.test(raw) ? 'AsSoonAsPossible' : raw, runFullTrustReason: await readValue(page, ['textarea[name="runFullTrustReason"]', '#runFullTrustReason']) }; }

function diffValues(phase, desired, observed) {
  const diff = []; const add = (field, current, target) => { if (JSON.stringify(current) !== JSON.stringify(target)) diff.push({ field, current, desired: target }); };
  if (phase === 'availability') { add('currency', observed.currency, desired.pricing.currency); add('priceTier', observed.priceTier, desired.pricing.priceTier); add('allMarkets', observed.allMarkets, desired.pricing.markets === 'all'); }
  if (phase === 'properties') { add('category', observed.category, desired.properties.category); add('privacy', observed.privacy, desired.properties.privacy); for (const [key, value] of Object.entries(desired.properties.capabilities ?? {})) add(`capabilities.${key}`, observed.capabilities?.[key], value); }
  if (phase === 'age-ratings') { add('mode', observed.mode, desired.ageRatings.mode); add('applicationType', observed.applicationType, desired.ageRatings.applicationType); for (const [key, value] of Object.entries(desired.ageRatings.answers ?? {})) add(`answers.${key}`, observed.answers?.[`question#${key}`] ?? observed.answers?.[key], value); add('physicalMedia', observed.physicalMedia, desired.ageRatings.physicalMedia); add('terms', observed.terms, desired.ageRatings.iarcTerms); if (!observed.complete) diff.push({ field: 'complete', current: false, desired: true }); }
  if (phase === 'packages') { const target = path.basename(desired.package.path || ''); const same = observed.entries.filter(entry => entry.fileName.toLowerCase() === target.toLowerCase()); if (!same.length) diff.push({ field: 'package', current: 'absent', desired: target }); else if (same.length !== 1 || same[0].status === 'Error') diff.push({ field: 'packageConflict', current: same.map(item => item.status), desired: 'one Validated' }); else if (same[0].status !== 'Validated') diff.push({ field: 'packageStatus', current: same[0].status, desired: 'Validated' }); add('desktop', observed.desktop, true); add('mobile', observed.mobile, false); add('xbox', observed.xbox, false); add('future', observed.future, true); }
  if (phase === 'listing') { add('description', observed.description, desired.values.description); add('shortDescription', observed.shortDescription, desired.values.shortDescription); add('features', observed.features, desired.values.features); add('keywords', observed.keywords, desired.values.keywords); }
  if (phase === 'options') { add('publishMode', observed.publishMode, desired.submissionOptions.publishMode); if (desired.submissionOptions.runFullTrustReason) add('runFullTrustReason', observed.runFullTrustReason, desired.submissionOptions.runFullTrustReason); }
  return diff;
}

async function applyAvailability(page, desired) { await choose(page, ['select[name="currency"]', 'he-select[name="currency"]', '#currency'], desired.pricing.currency, 'currency'); await choose(page, ['select[name="pricingTier"]', 'he-select[name="pricingTier"]', '#pricingTier'], desired.pricing.priceTier, 'price tier'); await check(page, ['input[name="marketSelection"]', '[role="checkbox"][aria-label*="市场"]'], desired.pricing.markets === 'all', 'markets'); await save(page); }
async function applyProperties(page, desired) { await choose(page, ['select[name="CategorySelect"]', 'he-select[name="category"]', '#category-select'], desired.properties.category, 'category'); const privacy = page.getByRole('radio', { name: new RegExp(escapeRegex(desired.properties.privacy), 'i') }).first(); if (await privacy.count()) await privacy.check(); else await check(page, [`input[name="privacyPolicySelection"][value="${desired.properties.privacy}"]`], true, 'privacy'); for (const [key, value] of Object.entries(desired.properties.capabilities ?? {})) await check(page, [`input[name="${key}"]`, `input[name="${key}-checkbox"]`, `[aria-label*="${key}" i]`], Boolean(value), key); await save(page); }
async function applyAgeRatings(page, desired) { const mode = page.getByRole('radio', { name: new RegExp(escapeRegex(desired.ageRatings.mode), 'i') }).first(); if (await mode.count()) await mode.check(); const type = page.locator(`input[name="question#1109"][value="${desired.ageRatings.applicationType}"]`); if (await type.count()) await type.check(); for (const [id, value] of Object.entries(desired.ageRatings.answers ?? {})) { const exact = page.locator(`input[name="question#${id}"][value="${value}"]`); if (await exact.count()) await exact.check(); else { const label = page.locator('label').filter({ hasText: new RegExp(escapeRegex(String(value)), 'i') }).first(); if (await label.count()) await label.click(); else throw Object.assign(new Error(`age answer not found: ${id}=${value}`), { code: 'SCHEMA_DRIFT' }); } } const defaultPattern = String(desired.ageRatings.defaultAnswer || '').toLowerCase() === 'no' ? /否|No/i : new RegExp(escapeRegex(String(desired.ageRatings.defaultAnswer || '')), 'i'); const groups = page.locator('[role="radiogroup"]'); for (let i = 0; i < await groups.count(); i += 1) { const group = groups.nth(i); if (await group.locator('input[type="radio"]:checked').count()) continue; const option = group.locator('label').filter({ hasText: defaultPattern }).first(); if (await option.count()) await option.click(); else throw Object.assign(new Error(`unanswered age-rating group ${i} has no default answer`), { code: 'SCHEMA_DRIFT' }); }
  await check(page, ['input#noVal', 'input[name="physicalMedia"]'], Boolean(desired.ageRatings.physicalMedia), 'physical media'); if (desired.ageRatings.iarcTerms) { const terms = page.getByRole('checkbox', { name: /IARC|条款|terms/i }).first(); if (await terms.count() && !(await terms.isChecked().catch(() => false))) await terms.check(); } const preview = page.getByRole('button', { name: /预览|Preview/i }).last(); if (await preview.count()) await preview.click(); else await page.getByRole('button', { name: /保存|Save/i }).last().click(); await waitForPageKind(page, ['AgeRatingsSummary', 'SubmissionOverview'], { operation: 'age ratings summary' }); if (await page.getByRole('button', { name: /保存|Save/i }).count()) await page.getByRole('button', { name: /保存|Save/i }).last().click(); }
async function applyPackages(page, desired, diff, deadline) { const conflict = diff.find(item => item.field === 'packageConflict'); if (conflict) throw Object.assign(new Error('package upload blocked by conflict/error rows; repair first'), { code: 'ERROR' }); const target = path.resolve(desired.package.path); const input = page.locator('input[type="file"]').first(); if (diff.some(item => item.field === 'package')) { if (!(await input.count())) throw Object.assign(new Error('package file input not found'), { code: 'SCHEMA_DRIFT' }); await input.setInputFiles(target); }
  const fileName = path.basename(target); await waitUntil(async () => { const row = page.getByText(fileName, { exact: false }).first(); if (!(await row.count())) return false; const text = (await row.innerText().catch(() => '')).toLowerCase(); if (/error|错误|failed|失败/.test(text)) throw Object.assign(new Error(`package validation error: ${text}`), { retryable: false }); return /validated|已验证|已完成/.test(text); }, { timeoutMs: Math.min(deadline.remaining(), 720_000), label: `package ${fileName} validated` }); await check(page, ['input[aria-label*="Desktop" i]', 'input[name="desktop"]'], true, 'desktop'); await check(page, ['input[aria-label*="Mobile" i]', 'input[name="mobile"]'], false, 'mobile'); await check(page, ['input[aria-label*="Xbox" i]', 'input[name="xbox"]'], false, 'xbox'); await check(page, ['input[aria-label*="future" i]', 'input[name="future"]'], true, 'future device families'); await save(page); }
async function applyListing(page, desired) { await fill(page, ['textarea[name="description"]', '#description'], desired.values.description, 'description'); await fill(page, ['textarea[name="shortDescription"]', '#shortDescription', 'input[name="shortDescription"]'], desired.values.shortDescription, 'short description'); const features = page.locator('input[name="features"], textarea[name="features"]'); if (await features.count()) { for (let i = 0; i < desired.values.features.length; i += 1) { const item = features.nth(i); if (await item.count()) await item.fill(desired.values.features[i]); } } const keywords = page.locator('input[name="keywords"], input[placeholder*="关键词" i]'); if (await keywords.count()) { await keywords.first().fill(desired.values.keywords.join(';')); } let index = 0; if (desired.assets?.screenshot) await uploadAsset(page, desired.assets.screenshot, /screenshot|屏幕截图/i, index++); for (const [name, item] of Object.entries(desired.listing.assets ?? {})) if (item.enabled && item.path) await uploadAsset(page, item.path, new RegExp(`${escapeRegex(name)}|logo|图标|${name.includes('square') ? '方形' : ''}`, 'i'), index++); await save(page); }
async function uploadAsset(page, filePath, label, fallbackIndex) { const labeled = page.getByLabel(label).first(); if (await labeled.count()) { const tag = await labeled.evaluate(element => element.tagName).catch(() => ''); if (tag.toLowerCase() === 'input') { await labeled.setInputFiles(filePath); return; } const nested = labeled.locator('input[type="file"]').first(); if (await nested.count()) { await nested.setInputFiles(filePath); return; } } const inputs = page.locator('input[type="file"]'); if (fallbackIndex < await inputs.count()) { await inputs.nth(fallbackIndex).setInputFiles(filePath); return; } throw Object.assign(new Error(`asset input not found: ${filePath}`), { code: 'SCHEMA_DRIFT' }); }
async function applyOptions(page, desired) { const mode = page.getByRole('radio', { name: new RegExp(escapeRegex(desired.submissionOptions.publishMode), 'i') }).first(); if (await mode.count()) await mode.check(); if (desired.submissionOptions.runFullTrustReason) await fill(page, ['textarea[name="runFullTrustReason"]', '#runFullTrustReason'], desired.submissionOptions.runFullTrustReason, 'runFullTrustReason'); await save(page); }
function escapeRegex(value) { return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

export function phaseAdapters(driver) { return Object.fromEntries(PHASES.map(phase => [phase, { acceptedPageKinds: accepted[phase], ensurePage: () => driver.ensurePage(phase), observe: () => driver.observe(phase), diff: (_desired, observed) => driver.diff(phase, observed), apply: (diff, desired, deadline) => driver.apply(phase, diff, deadline), coldVerify: (desired, deadline) => driver.coldVerify(phase, deadline), overviewVerify: deadline => driver.overviewVerify(phase, deadline), captureEvidence: evidence => driver.captureEvidence(evidence, phase), captureErrorEvidence: (evidence, error) => driver.captureErrorEvidence(evidence, phase, error) }])); }
