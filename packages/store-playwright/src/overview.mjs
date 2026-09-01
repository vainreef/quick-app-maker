import { PHASES } from '@quick-app/store-core';
import { waitForPageKind } from './inspector.mjs';

const routeHints = {
  availability: ['availability', 'pricingandavailability', '定价和可用性', '定价与可用性', '定价'],
  properties: ['properties', '属性'],
  'age-ratings': ['ageratings', 'age-ratings', '年龄分级'],
  packages: ['packages', '程序包', '软件包', '包'],
  listing: ['managelanguages', 'listings', 'store 一览', '一览', '应用商店一览', '应用商店列表'],
  options: ['options', 'submissionoptions', '提交选项']
};

export async function observeOverview(page) {
  const snapshot = await waitForPageKind(page, ['SubmissionOverview', 'ProductOverview'], { operation: 'submission overview' });
  await page.locator('.accordion-body-list-itembox, .module-name, [class*="module"], a[href*="submissions"]').first().waitFor({ state: 'attached', timeout: 15_000 }).catch(() => {});
  await page.waitForTimeout(1000);
  const modules = {};

  const boxes = page.locator('.accordion-body-list-itembox');
  let boxData = [];
  if (await boxes.count() > 0) {
    boxData = await boxes.evaluateAll(items => items.map((item, index) => ({
      index,
      text: (item.innerText || '').replace(/\s+/g, ' ').trim(),
      name: (item.querySelector('.module-name')?.innerText || item.innerText || '').replace(/\s+/g, ' ').trim(),
      href: (item.querySelector('a')?.getAttribute('href') || item.getAttribute('href') || '').toLowerCase()
    })));
  } else {
    const links = page.locator('a, button, [role="link"], .module-name, span.module-name');
    boxData = await links.evaluateAll(items => items.map((item, index) => ({
      index,
      text: (item.innerText || item.textContent || '').trim(),
      name: (item.innerText || item.textContent || '').trim(),
      href: (item.getAttribute('href') || item.closest('a')?.getAttribute('href') || '').toLowerCase()
    })));
  }

  for (const phase of PHASES) {
    const found = boxData.find(item => {
      const lowerName = item.name.toLowerCase();
      const lowerText = item.text.toLowerCase();
      const hints = routeHints[phase];
      return hints.some(hint => lowerName === hint || lowerName.startsWith(hint) || lowerText.startsWith(hint) || item.href.includes(hint));
    });

    if (!found) {
      modules[phase] = { status: 'Unknown', evidence: 'module box missing' };
      continue;
    }

    const row = found.text;
    const lower = row.toLowerCase();
    const failed = /error|failed|错误|失败/.test(lower);
    const incomplete = /incomplete|not started|未完成|未启动/.test(lower);
    const processing = /processing|analyzing|验证中|处理中/.test(lower);
    const explicit = /complete|validated|完成|已验证/.test(lower);
    const badgeLess = phase === 'availability' || phase === 'properties' || phase === 'age-ratings' || phase === 'options';

    const status = failed ? 'Error' : processing ? 'Processing' : incomplete ? 'Incomplete' : (explicit || badgeLess || !incomplete) ? 'Complete' : 'Unknown';
    console.log(`[BROWSER_ACTION] Overview row for [${phase}]: status="${status}" text="${row.replace(/\s+/g, ' ').slice(0, 80)}"`);

    modules[phase] = {
      status,
      evidence: {
        row: row.slice(0, 800),
        href: found.href,
        rule: explicit ? 'explicit' : badgeLess ? 'known-badge-less-v2' : 'standard'
      }
    };
  }
  return { pageKind: snapshot.kind, url: snapshot.url, modules };
}

