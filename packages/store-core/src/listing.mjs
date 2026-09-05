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
  const importedDesc = sections.has('description') ? clean(sections.get('description').join('\n')) : '';
  const importedShort = sections.has('shortDescription') ? clean(sections.get('shortDescription').join('\n')) : '';
  const importedFeatures = sections.has('features') ? sections.get('features').map(x => x.replace(/^\s*[-*+]\s+/, '').trim()).filter(Boolean) : [];
  const importedKeywords = sections.has('keywords') ? sections.get('keywords').join('\n').split(/[;,，、\n]/).map(x => x.trim()).filter(Boolean) : [];

  const isTemplateDesc = /是一款专为 Windows 平台打造的高性能纯单机个人桌面应用|模板应用|默认应用描述/i.test(importedDesc);
  if (importedDesc && (!values.description || (!isTemplateDesc && importedDesc.length > 30))) {
    values.description = importedDesc;
  }
  if (importedShort && (!values.shortDescription || importedShort.length > 10)) {
    values.shortDescription = importedShort;
  }
  if (importedFeatures.length && (!values.features?.length || importedFeatures.length >= values.features.length)) {
    values.features = importedFeatures;
  }
  if (importedKeywords.length && (!values.keywords?.length || importedKeywords.length >= values.keywords.length)) {
    values.keywords = importedKeywords;
  }
  return desired;
}
function clean(value) { return value.trim().replace(/\n{3,}/g, '\n\n'); }
