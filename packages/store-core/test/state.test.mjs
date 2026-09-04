import test from 'node:test';
import assert from 'node:assert/strict';
import { createCheckpoint, loadCheckpoint, mark, markConverged, validateDesired, importListingMarkdown, PHASES, EXIT, EvidenceStore, parseSubmissionTxt, loadDesired } from '../src/index.mjs';
import fs from 'node:fs';
import path from 'node:path';

test('checkpoint refuses evidence-free convergence', () => { const cp = createCheckpoint({ productId: 'P' }); assert.throws(() => markConverged(cp, 'listing', {}), /evidence/); });
test('checkpoint requires full evidence chain', () => { const cp = createCheckpoint({ productId: 'P' }); mark(cp, 'listing', 'Observed'); mark(cp, 'listing', 'NeedsChanges', { diff: [{ field: 'x' }] }); mark(cp, 'listing', 'Applying'); mark(cp, 'listing', 'AppliedUnverified'); markConverged(cp, 'listing', { pageKind: 'SubmissionOverview', coldDiff: [], overviewStatus: 'Complete', overviewUrl: 'https://example.test', evidenceIds: ['listing.json'] }); assert.equal(cp.phaseStatuses.listing, 'Converged'); assert.deepEqual(cp.convergedPhases, ['listing']); });
test('all phases and exit codes are explicit', () => { assert.deepEqual(PHASES, ['availability', 'properties', 'age-ratings', 'packages', 'listing', 'options']); assert.equal(EXIT.DIFF, 4); });
test('listing import handles mixed Chinese and English headings', () => { const root = path.resolve('.cache', `listing-test-${process.pid}`); fs.mkdirSync(root, { recursive: true }); const file = path.join(root, 'listing.md'); fs.writeFileSync(file, '## 简短摘要（Short Description）\n短文\n## Description\n长文\n## App Features\n- A\n- B\n## Search Terms\na;b'); const desired = { values: {} }; importListingMarkdown(desired, file); assert.equal(desired.values.shortDescription, '短文'); assert.equal(desired.values.description, '长文'); assert.deepEqual(desired.values.features, ['A', 'B']); assert.deepEqual(desired.values.keywords, ['a', 'b']); });
test('strict desired validation catches missing age answers and assets', () => { const errors = validateDesired({ productName: 'P', values: { description: 'd', shortDescription: 's', features: ['f'], keywords: ['k'] }, listing: { screenshot: true }, assets: {}, ageRatings: { answers: {} } }, { strict: true }); assert.ok(errors.some(error => error.includes('ageRatings.answers'))); assert.ok(errors.some(error => error.includes('assets.screenshot'))); });
test('evidence store keeps binary screenshots binary', () => { const root = path.resolve('.cache', `evidence-test-${process.pid}-${Date.now()}`); const store = new EvidenceStore(root, root); const file = store.write('shot.png', Buffer.from([0x89, 0x50, 0x4e, 0x47])); assert.deepEqual([...fs.readFileSync(path.join(root, 'evidence', 'shot.png'))], [0x89, 0x50, 0x4e, 0x47]); assert.match(file, /shot\.png$/); });
test('checkpoint resets when product, manifest, or submission identity changes', () => { const root = path.resolve('.cache', `checkpoint-test-${process.pid}-${Date.now()}`); fs.mkdirSync(root, { recursive: true }); const file = path.join(root, 'checkpoint.json'); fs.writeFileSync(file, JSON.stringify(createCheckpoint({ productId: 'P1', manifestHash: 'H1', submissionId: 'S1' }))); assert.equal(loadCheckpoint(root, { productId: 'P2' }).productId, 'P2'); fs.writeFileSync(file, JSON.stringify(createCheckpoint({ productId: 'P1', manifestHash: 'H1', submissionId: 'S1' }))); assert.equal(loadCheckpoint(root, { productId: 'P1', manifestHash: 'H2' }).manifestHash, 'H2'); fs.writeFileSync(file, JSON.stringify(createCheckpoint({ productId: 'P1', manifestHash: 'H1', submissionId: 'S1' }))); assert.equal(loadCheckpoint(root, { productId: 'P1', submissionId: 'S2' }).submissionId, 'S2'); });
test('parseSubmissionTxt parses Chinese store submission text assets', () => {
  const sample = `【应用全称】\n时光回忆录\n\n【一句话亮点摘要】\n优雅的私人回忆录\n\n【产品详细描述】\n时光回忆录是一款专为记录人生珍贵点滴而设计的桌面应用。\n\n【核心特性】\n1. 典雅排版\n2. 纯本地保存\n\n【搜索关键字】\n回忆录, 记事本, 日记\n\n【隐私合规声明】\n数据完全保存在本地。\n\n【受限权限（runFullTrust）使用理由】\n读写本地数据。`;
  const parsed = parseSubmissionTxt(sample);
  assert.equal(parsed.productName, '时光回忆录');
  assert.equal(parsed.shortDescription, '优雅的私人回忆录');
  assert.equal(parsed.description, '时光回忆录是一款专为记录人生珍贵点滴而设计的桌面应用。');
  assert.deepEqual(parsed.features, ['典雅排版', '纯本地保存']);
  assert.deepEqual(parsed.keywords, ['回忆录', '记事本', '日记']);
  assert.equal(parsed.privacyPolicyText, '数据完全保存在本地。');
  assert.equal(parsed.runFullTrustReason, '读写本地数据。');
});
test('loadDesired auto-sniffs and adopts store-submission-assets 00 txt file', () => {
  const root = path.resolve('.cache', `sniff-test-${process.pid}-${Date.now()}`);
  const assetsDir = path.join(root, 'store-submission-assets');
  fs.mkdirSync(assetsDir, { recursive: true });
  fs.writeFileSync(path.join(assetsDir, '00_文案说明.txt'), '【产品详细描述】\n真实且详细的软件中文介绍文案，超过三十个汉字说明本软件的核心功能与特色。');
  const desired = loadDesired(root);
  assert.equal(desired.values.description, '真实且详细的软件中文介绍文案，超过三十个汉字说明本软件的核心功能与特色。');
});
