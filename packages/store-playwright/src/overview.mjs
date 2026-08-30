import { PHASES } from '@quick-app/store-core';
import { waitForPageKind } from './inspector.mjs';

const routeHints = { availability: ['availability', 'pricingandavailability'], properties: ['properties'], 'age-ratings': ['ageratings'], packages: ['packages'], listing: ['managelanguages', 'listings'], options: ['options', 'submissionoptions'] };

export async function observeOverview(page) {
  const snapshot = await waitForPageKind(page, ['SubmissionOverview', 'ProductOverview'], { operation: 'submission overview' });
  const modules = {};
  for (const phase of PHASES) {
    const links = page.locator('a[href*="/submissions/"],a[href],button,[role="link"]');
    const count = await links.count(); let match = null;
    for (let i = 0; i < count; i += 1) {
      const item = links.nth(i); const href = ((await item.getAttribute('href')) || '').toLowerCase(); const text = ((await item.innerText().catch(() => '')) || '').trim().toLowerCase();
      if (routeHints[phase].some(hint => href.includes(hint) || text === hint)) { match = item; break; }
    }
    if (!match) { modules[phase] = { status: 'Unknown', evidence: 'module link missing' }; continue; }
    const row = await match.evaluate(element => { let node = element; for (let i = 0; i < 5 && node; i += 1, node = node.parentElement) { if (/^(LI|TR)$/.test(node.tagName) || node.getAttribute('role') === 'listitem' || node.className?.toString().includes('module')) return node.innerText || ''; } return element.parentElement?.innerText || element.innerText || ''; });
    const lower = row.toLowerCase(); const failed = /error|failed|错误|失败/.test(lower); const incomplete = /incomplete|not started|未完成|未启动/.test(lower); const processing = /processing|analyzing|验证中|处理中/.test(lower); const explicit = /complete|validated|完成|已验证/.test(lower) || await match.locator('xpath=..').locator('[data-status="complete"],.checkmark,.win-icon-CheckMark').count().catch(() => 0) > 0;
    const badgeLess = phase === 'availability' || phase === 'age-ratings';
    const status = failed ? 'Error' : processing ? 'Processing' : incomplete ? 'Incomplete' : explicit || badgeLess ? 'Complete' : 'Unknown';
    modules[phase] = { status, evidence: { row: row.slice(0, 800), href: await match.getAttribute('href'), rule: explicit ? 'explicit' : badgeLess ? 'known-badge-less-v2' : 'unknown' } };
  }
  return { pageKind: snapshot.kind, url: snapshot.url, modules };
}
