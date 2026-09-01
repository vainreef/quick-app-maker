import path from 'node:path';
import fs from 'node:fs';
import { writeAtomic } from '@quick-app/core';
import { waitForPageKind, waitUntil } from './inspector.mjs';

export async function reserveProduct({ page, appRoot, desired, name, logger }) {
  const parsed = new URL(desired.site.baseUrl); const locale = parsed.pathname.split('/').filter(Boolean)[0] || 'zh-cn';
  const overviewUrl = `${parsed.origin}/${locale}/dashboard/apps-and-games/overview`;
  logger?.info('reserve-start', { overviewUrl, name, currentUrl: page.url() });

  if (/\/products\/[^/?#]+/i.test(page.url())) {
    logger?.info('already-on-product-page', { url: page.url() });
  } else {
    await page.goto(overviewUrl, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    if (/xboxconfig|devices/i.test(page.url())) {
      await page.goto(overviewUrl, { waitUntil: 'domcontentloaded', timeout: 45_000 });
    }
    await waitForPageKind(page, ['ProductOverview', 'SubmissionOverview'], { operation: 'apps and games overview' });

    // Check if an app with this name already exists in the list to avoid duplicate creation errors
    const existingAppLink = page.locator('main, [role="main"], body').locator(`a:has-text("${name}"), button:has-text("${name}")`).first();
    if (await existingAppLink.count() && await existingAppLink.isVisible().catch(() => false)) {
      logger?.info('existing-app-found', { name });
      await existingAppLink.click();
      await waitUntil(async () => /\/products\/[^/]+/i.test(page.url()), { timeoutMs: 30_000, label: 'navigate to existing product' });
    } else {
    // Click New product within main region to prevent sidebar menu misclicks
    const mainRegion = page.locator('main, [role="main"], .app-overview, #root').first();
    const newProduct = (await mainRegion.count()) ? mainRegion.getByRole('button', { name: /新产品|创建新应用|New product|Create new/i }).first() : page.getByRole('button', { name: /新产品|创建新应用|New product/i }).first();
    if (!(await newProduct.count())) throw Object.assign(new Error('“新产品”创建按钮未找到，请确认 Partner Center 已进入应用列表'), { code: 'SCHEMA_DRIFT' });
    await newProduct.click();
    const msix = page.getByRole('menuitem', { name: /MSIX.*应用|MSIX.*app/i }).or(page.getByRole('menuitem', { name: /MSIX/i })).or(page.locator('button[role="menuitem"]:has-text("MSIX")')).first();
    if (await msix.count()) await msix.click();

    const nameInput = page.getByRole('textbox', { name: /名称|name|产品/i }).first();
    if (!(await nameInput.count())) throw Object.assign(new Error('应用名称输入框未找到'), { code: 'SCHEMA_DRIFT' });
    await nameInput.fill(name);
    const checkBtn = page.getByRole('button', { name: /检查可用性|Check availability/i }).first();
    if (await checkBtn.count()) await checkBtn.click();

    // Check availability or conflict
    await waitUntil(async () => {
      const text = (await page.locator('body').innerText()).slice(-3000);
      if (/已被占用|不可用|not available|reserved by another/i.test(text)) {
        throw Object.assign(new Error(`应用名称「${name}」在微软商店已被占用，请修改应用名称后重新保留`), { code: 'CONFIG', retryable: false });
      }
      return /可用|available/i.test(text);
    }, { timeoutMs: 30_000, label: 'product name available' });

    const reserve = page.getByRole('button', { name: /保留产品名称|Reserve product name/i }).first();
    if (!(await reserve.count())) throw Object.assign(new Error('“保留产品名称”按钮未找到'), { code: 'SCHEMA_DRIFT' });
    await reserve.click();
    await waitUntil(async () => /\/products\/[^/]+|ProductId|产品标识/i.test(page.url() + (await page.locator('body').innerText()).slice(0, 4000)), { timeoutMs: 60_000, label: 'product created' });
    }
  }

  const productId = page.url().match(/\/products\/([^/?#]+)/i)?.[1] ?? page.url().match(/[?&](?:productId|id)=([^&#]+)/i)?.[1];
  if (!productId) throw new Error(`应用已保留但无法从 URL 中提取 ProductId: ${page.url()}`);
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
  const parsed = new URL(desired.site.baseUrl);
  const locale = parsed.pathname.split('/').filter(Boolean)[0] || 'zh-cn';
  const id = productId ?? desired.productId;
  const identityUrl = `${parsed.origin}/${locale}/dashboard/products/${id}/identity`;
  if (!page.url().includes('/identity')) {
    await page.goto(identityUrl, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  }
  await waitUntil(async () => /Identity\.Name|Package\/Identity\/Name|Package\/Identity\/Publisher/i.test(await page.locator('body').innerText()), { timeoutMs: 45_000, label: 'package identity table' });
  const text = await page.locator('body').innerText();
  const value = label => {
    const regex = new RegExp(`(?:${label})\\s*[:\\t\\n ]\\s*([^\\n\\r]+)`, 'i');
    const m = text.match(regex);
    if (m && m[1]) return m[1].trim();
    const line = text.split('\n').find(l => new RegExp(label, 'i').test(l));
    if (line) {
      const parts = line.split(/\s+/).filter(Boolean);
      return parts.slice(1).join(' ').trim();
    }
    return '';
  };
  return {
    identityName: value('Package/Identity/Name|Identity\\.Name'),
    publisher: value('Package/Identity/Publisher|Package/Publisher|Publisher(?!Display)'),
    publisherDisplayName: value('Package/Properties/PublisherDisplayName|PublisherDisplayName|Publisher Display Name'),
    manifestPath: desired.package.manifestPath ? path.resolve(appRoot, desired.package.manifestPath) : ''
  };
}
function updateManifest(filePath, values) { if (!filePath || !fs.existsSync(filePath)) return; let xml = fs.readFileSync(filePath, 'utf8'); xml = xml.replace(/(<Identity\b[^>]*\bName\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.identityName || 'PENDING.IDENTITY')}`).replace(/(<Identity\b[^>]*\bPublisher\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.publisher || 'CN=PENDING')}`).replace(/(<DisplayName>)[^<]*/i, `$1${escapeXml(values.displayName)}`).replace(/(<PublisherDisplayName>)[^<]*/i, `$1${escapeXml(values.publisherDisplayName || 'Quick App Maker')}`).replace(/(VisualElements\b[^>]*\bDisplayName\s*=\s*["'])[^"']*/i, `$1${escapeXml(values.displayName)}`); fs.writeFileSync(filePath, xml, 'utf8'); }
function escapeXml(value) { return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c])); }
