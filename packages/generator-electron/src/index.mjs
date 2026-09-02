import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { appRoot, assertWithin, writeAtomic } from '@quick-app/core';
import { DEFAULT_DESIRED } from '@quick-app/store-core';
import { solidPng } from './png.mjs';

const ROOT = path.resolve(fileURLToPath(new URL('../../../', import.meta.url)));
const TEMPLATE = path.join(ROOT, 'templates', 'electron-vue-runtime');

export function slugify(value) {
  const slug = String(value ?? '').normalize('NFKD').replace(/[^\w\s-]/g, '').trim().toLowerCase().replace(/[\s_-]+/g, '-').replace(/^-+|-+$/g, '');
  if (!slug) throw new Error('slug must contain ASCII letters or digits');
  return slug;
}

export function createElectronApp({ workspace, name, slug = slugify(name), profile = 'electron-vue-runtime' }) {
  if (profile !== 'electron-vue-runtime') throw new Error(`unknown generator profile: ${profile}`);
  const root = appRoot(workspace, slug);
  const conflicting = fs.existsSync(root)
    ? fs.readdirSync(root).filter(file => file !== 'README.md' && !file.startsWith('.'))
    : [];
  if (conflicting.length) throw new Error(`app directory contains conflicting files: ${conflicting.join(', ')}`);
  fs.mkdirSync(root, { recursive: true });
  copyTemplate(TEMPLATE, root, { app_name: name, name, slug });
  const desired = structuredClone(DEFAULT_DESIRED);
  desired.productName = name;
  desired.listingMarkdown = 'store/listing.zh-CN.md';
  desired.package.executable = `${slug}.exe`;
  desired.package.manifestPath = 'store/Package.appxmanifest';
  desired.assets = { screenshot: 'store/assets/Screenshot.png' };
  desired.listing.languageCode = 'zh-cn';
  desired.ageRatings.answers = { '1109': '2558' };
  desired.ageRatings.defaultAnswer = 'No';
  desired.ageRatings.requireConfirmation = true;
  desired.ageRatings.confirmed = false;
  desired.values.description = `${name} 的完整描述。请在 store/listing.zh-CN.md 中替换为真实文案。`;
  desired.values.shortDescription = `${name} 的简短描述`;
  desired.values.features = ['清晰的核心功能', '快速上手', '本地数据优先'];
  desired.values.keywords = [name, '工具'];
  desired.listing.assets = {
    storeLogo: { enabled: true, path: 'store/assets/StoreLogo.png', width: 50, height: 50 },
    square44: { enabled: true, path: 'store/assets/Square44x44Logo.png', width: 44, height: 44 },
    square150: { enabled: true, path: 'store/assets/Square150x150Logo.png', width: 150, height: 150 },
    wide310: { enabled: true, path: 'store/assets/Wide310x150Logo.png', width: 310, height: 150 }
  };
  writeAtomic(root, path.join(root, 'store', 'desired-state.json'), desired);
  writeAtomic(root, path.join(root, 'quickapp.config.json'), { schemaVersion: 2, name, slug, profile, createdAt: new Date().toISOString(), framework: 'electron-vue-runtime', bundler: 'none' });
  const assetRoot = path.join(root, 'store', 'assets'); fs.mkdirSync(assetRoot, { recursive: true });
  for (const [file, width, height, color] of [['StoreLogo.png', 50, 50, [37, 99, 235]], ['Square44x44Logo.png', 44, 44, [37, 99, 235]], ['Square150x150Logo.png', 150, 150, [37, 99, 235]], ['Wide310x150Logo.png', 310, 150, [37, 99, 235]], ['Screenshot.png', 1366, 768, [15, 23, 42]]]) fs.writeFileSync(path.join(assetRoot, file), solidPng(width, height, color));
  fs.copyFileSync(path.join(root, 'store', 'listing.template.md'), path.join(root, 'store', 'listing.zh-CN.md'));
  return root;
}

function copyTemplate(source, destination, replacements) {
  for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
    const from = path.join(source, entry.name); const to = path.join(destination, entry.name);
    if (entry.isDirectory()) { fs.mkdirSync(to, { recursive: true }); copyTemplate(from, to, replacements); continue; }
    if (entry.name === 'README.md' && fs.existsSync(to)) continue;
    let data = fs.readFileSync(from);
    if (entry.name.endsWith('.json') || entry.name.endsWith('.cjs') || entry.name.endsWith('.md') || entry.name.endsWith('.js') || entry.name.endsWith('.html') || entry.name.endsWith('.xml') || entry.name.endsWith('.appxmanifest')) {
      let text = data.toString('utf8'); for (const [key, value] of Object.entries(replacements)) text = text.replaceAll(`__${key.toUpperCase()}__`, value); data = Buffer.from(text, 'utf8');
    }
    fs.mkdirSync(path.dirname(to), { recursive: true }); fs.writeFileSync(to, data);
  }
}
