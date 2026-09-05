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

async function extractOverviewState(page) {
  const main = page.locator('main, [id="3ppmaincontent"], .product-overview, app-submission-overview').first();
  const scope = (await main.count()) ? main : page;
  const boxes = scope.locator('.accordion-body-list-itembox');
  let boxData = [];
  if (await boxes.count() > 0) {
    boxData = await boxes.evaluateAll(items => items.map((item, index) => {
      const a = item.querySelector('a');
      const aria = a ? a.getAttribute('aria-label') || '' : '';
      const innerText = item.innerText || '';
      const combined = `${innerText} ${aria}`.replace(/\s+/g, ' ').trim();
      return {
        index,
        text: combined,
        name: (item.querySelector('.module-name')?.innerText || innerText).replace(/\s+/g, ' ').trim(),
        href: (a?.getAttribute('href') || item.getAttribute('href') || '').toLowerCase(),
        ariaLabel: aria
      };
    }));
  } else {
    const links = scope.locator('a[href*="/submissions/"]');
    boxData = await links.evaluateAll(items => items.map((item, index) => {
      const aria = item.getAttribute('aria-label') || '';
      const innerText = item.innerText || item.textContent || '';
      const combined = `${innerText} ${aria}`.replace(/\s+/g, ' ').trim();
      return {
        index,
        text: combined,
        name: combined,
        href: (item.getAttribute('href') || '').toLowerCase(),
        ariaLabel: aria
      };
    }));
  }

  const modules = {};
  for (const phase of PHASES) {
    const found = boxData.find(item => {
      const lowerName = item.name.toLowerCase();
      const lowerText = item.text.toLowerCase();
      const lowerAria = (item.ariaLabel || '').toLowerCase();
      const hints = routeHints[phase];
      return hints.some(hint => lowerName === hint || lowerName.startsWith(hint) || lowerText.startsWith(hint) || item.href.includes(hint) || lowerAria.includes(hint));
    });

    if (!found) {
      modules[phase] = { status: 'Unknown', evidence: 'module box missing' };
      continue;
    }

    const row = found.text;
    const lower = row.toLowerCase();
    const failed = /error|failed|错误|失败/.test(lower);
    const incomplete = /incomplete|not started|未完成|未启动|未填写|填写调查表|提供准确答案/.test(lower);
    const processing = /processing|analyzing|验证中|处理中/.test(lower);
    const explicit = /complete|validated|完成|已验证/.test(lower);
    const badgeLess = phase === 'availability' || phase === 'properties' || phase === 'options';

    let status = 'Unknown';
    if (failed) {
      status = 'Error';
    } else if (processing) {
      status = 'Processing';
    } else if (incomplete) {
      status = 'Incomplete';
    } else if (explicit) {
      status = 'Complete';
    } else if (phase === 'age-ratings') {
      const hasRatingIndicator = /iarc|分级|评级|esrb|pegi|所有年龄|\b\d+\+/i.test(lower);
      status = hasRatingIndicator ? 'Complete' : 'Incomplete';
    } else if (badgeLess || !incomplete) {
      status = 'Complete';
    }

    modules[phase] = {
      status,
      evidence: {
        row: row.slice(0, 800),
        href: found.href,
        rule: explicit ? 'explicit' : badgeLess ? 'known-badge-less-v2' : 'standard'
      }
    };
  }
  return modules;
}

export async function observeOverview(page) {
  const snapshot = await waitForPageKind(page, ['SubmissionOverview', 'ProductOverview'], { operation: 'submission overview' });
  await page.locator('.accordion-body-list-itembox').nth(5).waitFor({ state: 'attached', timeout: 30_000 }).catch(() => {});
  await page.waitForTimeout(1500);

  let modules = await extractOverviewState(page);

  const checkSubmitEnabled = async () => {
    return await page.evaluate(() => {
      const candidates = [...document.querySelectorAll('he-button, button, [role="button"], input[type="submit"]')];
      const submitBtn = candidates.find(b => {
        const txt = (b.innerText || b.value || b.textContent || '').trim();
        return /提交进行认证|Submit for certification/i.test(txt);
      });

      if (!submitBtn) {
        return { found: false, enabled: false, reason: 'button not found' };
      }

      const hasDisabledAttr = submitBtn.hasAttribute('disabled');
      const hasDisabledProp = Boolean(submitBtn.disabled);
      const hasDisableClass = submitBtn.classList.contains('disable-submit') || submitBtn.classList.contains('disabled');
      const ariaDisabled = submitBtn.getAttribute('aria-disabled') === 'true';

      let innerDisabled = false;
      if (submitBtn.shadowRoot) {
        const innerBtn = submitBtn.shadowRoot.querySelector('button');
        if (innerBtn) {
          innerDisabled = innerBtn.hasAttribute('disabled') || innerBtn.disabled || innerBtn.getAttribute('aria-disabled') === 'true';
        }
      }

      const isEnabled = !hasDisabledAttr && !hasDisabledProp && !hasDisableClass && !ariaDisabled && !innerDisabled;

      return {
        found: true,
        enabled: isEnabled,
        hasDisabledAttr,
        hasDisableClass,
        ariaDisabled,
        innerDisabled,
        text: (submitBtn.innerText || submitBtn.textContent || '').trim(),
        className: submitBtn.className || '',
        outerHTML: submitBtn.outerHTML.slice(0, 300)
      };
    }).catch(() => ({ found: false, enabled: false, reason: 'evaluate failed' }));
  };

  let submitButtonState = await checkSubmitEnabled();

  // 关键特性：微软后台在 6 大阶段刚刚保存完成时，概览页初次挂载该按钮往往处于不可点态（disable-submit disabled=""）。
  // 必须启动异步状态翻转轮询门禁，等待 Angular 脏检查与后台校验完成；若超过 18s 仍未翻转，主动触发 reload 强制拉取最新状态。
  if (submitButtonState.found && !submitButtonState.enabled) {
    console.log('[BROWSER_ACTION] ⏳ Submit button is initially disabled. Waiting for backend validation & Angular state transition (up to 60s)...');
    const start = Date.now();
    let reloaded = false;
    while (Date.now() - start < 60_000) {
      await page.waitForTimeout(1500);
      submitButtonState = await checkSubmitEnabled();
      if (submitButtonState.enabled) {
        console.log('[BROWSER_ACTION] ✨ Submit button dynamically flipped to enabled in DOM!');
        break;
      }
      if (!reloaded && Date.now() - start > 18_000) {
        console.log('[BROWSER_ACTION] 🔄 Submit button still disabled after 18s; performing proactive reload to refresh Angular state from cloud...');
        reloaded = true;
        await page.reload({ waitUntil: 'domcontentloaded' }).catch(() => {});
        await page.locator('.accordion-body-list-itembox, a[href*="/submissions/"]').first().waitFor({ state: 'attached', timeout: 30_000 }).catch(() => {});
        await page.waitForTimeout(3000);
        modules = await extractOverviewState(page);
      }
    }
  }

  for (const phase of PHASES) {
    console.log(`[BROWSER_ACTION] Overview row for [${phase}]: status="${modules[phase]?.status}" text="${modules[phase]?.evidence?.row?.replace(/\s+/g, ' ')?.slice(0, 80)}"`);
  }

  console.log(`[BROWSER_ACTION] 🎯 Overview Submit Button [提交进行认证]: enabled=${submitButtonState.enabled} (found=${submitButtonState.found}, hasDisableClass=${submitButtonState.hasDisableClass}, hasDisabledAttr=${submitButtonState.hasDisabledAttr})`);

  return {
    pageKind: snapshot.kind,
    url: snapshot.url,
    modules,
    isSubmitEnabled: submitButtonState.enabled,
    submitButton: submitButtonState
  };
}

