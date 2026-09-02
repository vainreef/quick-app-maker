import path from 'node:path';
import fs from 'node:fs';
import { writeAtomic } from '@quick-app/core';
import { waitForPageKind, waitUntil } from './inspector.mjs';

export async function openReserveModal(page, logger) {
  const newProductBtn = page.getByRole('button', { name: /新产品|New product/i }).or(page.locator('button:has-text("新产品")')).first();
  if (await newProductBtn.count() && await newProductBtn.isVisible().catch(() => false)) {
    await newProductBtn.click();
    await page.waitForTimeout(500);
    const msixItem = page.getByRole('menuitem', { name: /MSIX.*应用|MSIX.*app/i })
      .or(page.getByRole('button', { name: /MSIX.*应用|MSIX.*app/i }))
      .or(page.locator('button:has-text("MSIX")'))
      .or(page.locator('[role="menuitem"]:has-text("MSIX")'))
      .first();
    if (await msixItem.count() && await msixItem.isVisible().catch(() => false)) {
      await msixItem.click();
    }
  }
  const nameInput = page.getByRole('textbox', { name: /名称|name|产品/i }).or(page.locator('input[type="text"]')).first();
  if (await nameInput.count()) {
    await nameInput.waitFor({ state: 'visible', timeout: 20_000 }).catch(() => {});
    await nameInput.focus().catch(() => {});
  }
  logger?.info('reserve-modal-opened');
}

export async function reserveProduct({ page, appRoot, desired, name, logger }) {
  const parsed = new URL(desired.site.baseUrl); const locale = parsed.pathname.split('/').filter(Boolean)[0] || 'zh-cn';
  const overviewUrl = `${parsed.origin}/${locale}/dashboard/apps-and-games/overview`;
  logger?.info('reserve-start', { overviewUrl, name, currentUrl: page.url() });

  let productId = page.url().match(/\/products\/([^/?#]+)/i)?.[1] ?? page.url().match(/[?&](?:productId|id)=([^&#]+)/i)?.[1];

  if (!productId) {
    if (!page.url().includes('/overview')) {
      await page.goto(overviewUrl, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    }
    await waitForPageKind(page, ['ProductOverview', 'SubmissionOverview'], { operation: 'apps and games overview' });

    // Search for existing app in the user's products table
    const targetName = name || desired.productName;
    const existingAppLink = page.locator('main, [role="main"], body').locator(`a[href*="/products/"]`).filter({ hasText: new RegExp(targetName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') }).first();
    if (await existingAppLink.count() && await existingAppLink.isVisible().catch(() => false)) {
      const href = await existingAppLink.getAttribute('href');
      productId = href?.match(/\/products\/([^/?#]+)/i)?.[1];
      logger?.info('existing-app-found', { targetName, productId });
      await existingAppLink.click();
      await waitUntil(async () => /\/products\/[^/]+/i.test(page.url()), { timeoutMs: 30_000, label: 'navigate to existing product' });
    } else {
      // If still not found, check the first product link on page if only one product exists
      const firstProductLink = page.locator('main, [role="main"]').locator('a[href*="/products/"]').first();
      if (await firstProductLink.count() && await firstProductLink.isVisible().catch(() => false)) {
        const href = await firstProductLink.getAttribute('href');
        productId = href?.match(/\/products\/([^/?#]+)/i)?.[1];
        await firstProductLink.click();
        await waitUntil(async () => /\/products\/[^/]+/i.test(page.url()), { timeoutMs: 30_000, label: 'navigate to product' });
      }
    }
  }

  productId = productId ?? page.url().match(/\/products\/([^/?#]+)/i)?.[1] ?? page.url().match(/[?&](?:productId|id)=([^&#]+)/i)?.[1];
  if (!productId) throw new Error(`未能获取到已保留的 ProductId。请确认已在浏览器中点击「保留产品名称」进入产品页面。当前 URL: ${page.url()}`);
  desired.productId = productId;

  const identity = await scrapeIdentity(page, desired, appRoot, productId);
  if (!identity.identityName || !identity.publisher || !identity.publisherDisplayName) throw Object.assign(new Error('Product Identity 字段未能在页面中完整识别'), { code: 'SCHEMA_DRIFT', identity });
  desired.productId = productId; desired.productName = name; desired.package.identityName = identity.identityName; desired.package.publisher = identity.publisher; desired.package.publisherDisplayName = identity.publisherDisplayName;
  const stored = structuredClone(desired);
  for (const key of ['path', 'manifestPath']) if (stored.package?.[key]) stored.package[key] = path.relative(appRoot, stored.package[key]).replaceAll('\\', '/');
  if (stored.assets?.screenshot) stored.assets.screenshot = path.relative(appRoot, stored.assets.screenshot).replaceAll('\\', '/');
  for (const item of Object.values(stored.listing?.assets ?? {})) if (item.path) item.path = path.relative(appRoot, item.path).replaceAll('\\', '/');
  if (stored.listingMarkdown) stored.listingMarkdown = path.relative(appRoot, stored.listingMarkdown).replaceAll('\\', '/');
  writeAtomic(appRoot, path.join(appRoot, 'store', 'desired-state.json'), stored);
  if (identity.manifestPath) updateManifest(identity.manifestPath, { ...identity, displayName: name });
  logger?.pass('product reserved', { productId, identity });
  return { productId, identity };
}

async function scrapeIdentity(page, desired, appRoot, productId) {
  const extractFromDom = async () => {
    return await page.evaluate(() => {
      const getField = (labelText) => {
        const lis = [...document.querySelectorAll('li, tr, .micro-row, div')];
        for (const li of lis) {
          const keyEl = li.querySelector('.key, td:first-child, .field-label');
          const valEl = li.querySelector('.app-id-contents, .text, td:last-child');
          if (keyEl && valEl && (keyEl.innerText || '').includes(labelText)) {
            return (valEl.innerText || '').trim();
          }
        }
        return '';
      };
      return {
        identityName: getField('Package/Identity/Name'),
        publisher: getField('Package/Identity/Publisher'),
        publisherDisplayName: getField('PublisherDisplayName') || getField('Publisher Display Name')
      };
    });
  };

  let domResult = await extractFromDom();
  if (domResult.identityName && domResult.publisher && domResult.publisherDisplayName) {
    return {
      ...domResult,
      manifestPath: desired.package.manifestPath ? path.resolve(appRoot, desired.package.manifestPath) : ''
    };
  }

  const toggleBtn = page.getByRole('button', { name: /切换产品标识|查看产品标识/i }).or(page.locator('a[aria-label*="产品标识"]')).first();
  if (await toggleBtn.count() && await toggleBtn.isVisible().catch(() => false)) {
    await toggleBtn.click().catch(() => {});
    await page.waitForTimeout(500);
  }

  domResult = await extractFromDom();
  if (domResult.identityName && domResult.publisher && domResult.publisherDisplayName) {
    return {
      ...domResult,
      manifestPath: desired.package.manifestPath ? path.resolve(appRoot, desired.package.manifestPath) : ''
    };
  }

  // Fallback text parser
  const text = await page.locator('body').innerText();
  const value = label => {
    const lines = text.split('\n').map(l => l.trim()).filter(Boolean);
    const matched = lines.find(l => new RegExp(`^${label}\\b`, 'i').test(l));
    if (matched) {
      if (matched.includes('\t')) return matched.split('\t').slice(1).join(' ').trim();
      const parts = matched.split(/\s{2,}|\t/);
      if (parts.length > 1) return parts[1].trim();
      const regex = new RegExp(`^${label}\\s+(.+)`, 'i');
      const m = matched.match(regex);
      if (m && m[1]) return m[1].trim();
    }
    const regex = new RegExp(`(?:${label})[\\t: ]+([^\\r\\n]+)`, 'i');
    const m = text.match(regex);
    return m ? m[1].trim() : '';
  };
  return {
    identityName: domResult.identityName || value('Package/Identity/Name|Identity\\.Name'),
    publisher: domResult.publisher || value('Package/Identity/Publisher|Package/Publisher|Publisher(?!Display)'),
    publisherDisplayName: domResult.publisherDisplayName || value('Package/Properties/PublisherDisplayName|PublisherDisplayName|Publisher Display Name'),
    manifestPath: desired.package.manifestPath ? path.resolve(appRoot, desired.package.manifestPath) : ''
  };
}
function updateManifest(filePath, values) { if (!filePath || !fs.existsSync(filePath)) return; let xml = fs.readFileSync(filePath, 'utf8'); xml = xml.replace(/(<Identity\b[^>]*\bName\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.identityName || 'PENDING.IDENTITY')}`).replace(/(<Identity\b[^>]*\bPublisher\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.publisher || 'CN=PENDING')}`).replace(/(<DisplayName>)[^<]*/i, `$1${escapeXml(values.displayName)}`).replace(/(<PublisherDisplayName>)[^<]*/i, `$1${escapeXml(values.publisherDisplayName || 'Quick App Maker')}`).replace(/(VisualElements\b[^>]*\bDisplayName\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.displayName)}`); fs.writeFileSync(filePath, xml, 'utf8'); }
function escapeXml(value) { return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c])); }
