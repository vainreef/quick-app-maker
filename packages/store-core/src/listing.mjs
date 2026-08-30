import fs from 'node:fs';

const HEADINGS = new Map([
  ['short description', 'shortDescription'], ['简短摘要', 'shortDescription'], ['短描述', 'shortDescription'],
  ['description', 'description'], ['完整描述', 'description'],
  ['app features', 'features'], ['product features', 'features'], ['产品功能', 'features'],
  ['search terms', 'keywords'], ['search keywords', 'keywords'], ['搜索关键词', 'keywords'], ['搜索词', 'keywords']
]);

export function importListingMarkdown(desired, filePath) {
  const text = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '');
  const sections = new Map();
  let current = null;
  for (const line of text.split(/\r?\n/)) {
    const heading = line.match(/^#{1,6}\s+(.+?)\s*$/)?.[1];
    if (heading) {
      const key = heading.replace(/[（(].*?[）)]/g, '').trim().toLowerCase();
      current = HEADINGS.get(key) ?? null;
      if (current && !sections.has(current)) sections.set(current, []);
      continue;
    }
    if (current) sections.get(current).push(line);
  }
  const values = desired.values ?? (desired.values = {});
  if (sections.has('shortDescription')) values.shortDescription = clean(sections.get('shortDescription').join('\n'));
  if (sections.has('description')) values.description = clean(sections.get('description').join('\n'));
  if (sections.has('features')) values.features = sections.get('features').map(x => x.replace(/^\s*[-*+]\s+/, '').trim()).filter(Boolean);
  if (sections.has('keywords')) values.keywords = sections.get('keywords').join('\n').split(/[;,，、\n]/).map(x => x.trim()).filter(Boolean);
  return desired;
}
function clean(value) { return value.trim().replace(/\n{3,}/g, '\n\n'); }
