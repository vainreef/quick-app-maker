import fs from 'node:fs';
import path from 'node:path';
import { readJson, writeAtomic } from '@quick-app/core';
import { normalizePhase } from './constants.mjs';

export const DEFAULT_DESIRED = {
  productId: 'PENDING', productName: '', submissionId: '',
  site: { baseUrl: 'https://partner.microsoft.com/zh-cn/dashboard/products', languageId: '5', languageCode: 'zh-cn', supportedLanguageCodes: ['zh-cn'] },
  package: { path: '', executable: '', architecture: 'x64' },
  values: { description: '', shortDescription: '', features: [], keywords: [] },
  pricing: { currency: 'CN', priceTier: '0', markets: 'all', audience: 'Public' },
  properties: { category: 'Productivity', privacy: 'Yes', privacyPolicyText: '本应用为纯单机离线运行的个人工具软件，不收集、不存储、不传输亦不共享任何用户个人信息或设备数据。所有数据均安全保存在用户本地设备中。', privacyPolicyUrl: '', capabilities: {} },
  ageRatings: { mode: 'questionnaire', applicationType: '2558', answers: {}, defaultAnswer: 'No', requireConfirmation: true, confirmed: false, physicalMedia: false, iarcTerms: true },
  listing: { languageCode: 'zh-cn', screenshot: true, assets: {} },
  submissionOptions: { publishMode: 'Asap', runFullTrustReason: '本产品为基于 Windows 本地独立运行的桌面应用程序。需要使用 runFullTrust 权限以读写本地用户数据存储文件，实现数据本地安全持久化保存，不依赖也不连接任何外部云端网络服务。' }
};

export function parseSubmissionTxt(content) {
  if (!content || typeof content !== 'string') return null;
  const sections = {};
  let currentHeader = null;
  const lines = content.split(/\r?\n/);
  for (const line of lines) {
    const match = line.match(/^【([^】]+)】/);
    if (match) {
      currentHeader = match[1].trim();
      sections[currentHeader] = [];
    } else if (currentHeader) {
      sections[currentHeader].push(line);
    }
  }

  const getSection = (patterns) => {
    for (const key of Object.keys(sections)) {
      for (const p of patterns) {
        if (p instanceof RegExp ? p.test(key) : key.includes(p)) {
          return sections[key].join('\n').trim();
        }
      }
    }
    return '';
  };

  const name = getSection([/全称|名称/]);
  const shortDesc = getSection([/一句话|简短/]);
  const desc = getSection([/详细描述|详细介绍|产品说明/]);
  const rawFeatures = getSection([/特性列表|核心特性|核心亮点|功能列表|产品功能/]);
  const rawKeywords = getSection([/关键字|关键词/]);
  const privacy = getSection([/隐私/]);
  const fullTrust = getSection([/runFullTrust|受限权限/]);

  const features = rawFeatures
    ? rawFeatures.split(/\r?\n/)
        .map(l => l.replace(/^[-*•\d.]+\s*/, '').trim())
        .filter(l => l.length > 2)
    : [];

  const keywords = rawKeywords
    ? rawKeywords.split(/[,，、|\n]+/).map(k => k.trim()).filter(Boolean)
    : [];

  return {
    productName: name || undefined,
    shortDescription: shortDesc || undefined,
    description: desc || undefined,
    features: features.length ? features : undefined,
    keywords: keywords.length ? keywords : undefined,
    privacyPolicyText: privacy || undefined,
    runFullTrustReason: fullTrust || undefined
  };
}

export function loadDesired(appRoot) {
  const filePath = path.join(appRoot, 'store', 'desired-state.json');
  const value = readJson(filePath, structuredClone(DEFAULT_DESIRED));
  const merged = merge(DEFAULT_DESIRED, value);

  // Auto-sniff store-submission-assets/00_*.txt if available
  const sniffDirs = [
    path.join(appRoot, 'store-submission-assets'),
    path.join(process.cwd(), 'store-submission-assets')
  ];
  for (const dir of sniffDirs) {
    if (fs.existsSync(dir)) {
      try {
        const files = fs.readdirSync(dir);
        const txtFile = files.find(f => f.startsWith('00_') && f.endsWith('.txt')) ||
                        files.find(f => f.endsWith('.txt') && (f.includes('文案') || f.includes('说明')));
        if (txtFile) {
          const content = fs.readFileSync(path.join(dir, txtFile), 'utf8');
          const parsed = parseSubmissionTxt(content);
          if (parsed) {
            if (parsed.description && (!merged.values.description || merged.values.description.length < 30)) {
              merged.values.description = parsed.description;
            }
            if (parsed.shortDescription && !merged.values.shortDescription) {
              merged.values.shortDescription = parsed.shortDescription;
            }
            if (parsed.features?.length && (!merged.values.features || !merged.values.features.length)) {
              merged.values.features = parsed.features;
            }
            if (parsed.keywords?.length && (!merged.values.keywords || !merged.values.keywords.length)) {
              merged.values.keywords = parsed.keywords;
            }
            if (parsed.privacyPolicyText && (!merged.properties.privacyPolicyText || merged.properties.privacyPolicyText.includes('个人工具软件'))) {
              merged.properties.privacyPolicyText = parsed.privacyPolicyText;
            }
            if (parsed.runFullTrustReason && (!merged.submissionOptions.runFullTrustReason || merged.submissionOptions.runFullTrustReason.includes('基于 Windows 本地独立运行'))) {
              merged.submissionOptions.runFullTrustReason = parsed.runFullTrustReason;
            }
          }
          break;
        }
      } catch {}
    }
  }

  return merged;
}

export function saveDesired(appRoot, desired) {
  const filePath = path.join(appRoot, 'store', 'desired-state.json');
  writeAtomic(appRoot, filePath, desired);
  return filePath;
}

export function validateDesired(desired, { strict = false, checkAge = true } = {}) {
  const errors = [];
  required(desired.productName, 'productName', errors, strict);
  required(desired.values?.description, 'values.description', errors, strict);
  required(desired.values?.shortDescription, 'values.shortDescription', errors, strict);
  if ((desired.values?.keywords ?? []).length > 7) errors.push('values.keywords must contain at most 7 items');
  if ((desired.values?.keywords ?? []).some(value => String(value).length > 40)) errors.push('each keyword must be ≤40 characters');
  if (strict && !(desired.values?.features ?? []).length) errors.push('values.features is empty');
  if (strict && !(desired.values?.keywords ?? []).length) errors.push('values.keywords is empty');
  if (strict && desired.listing?.screenshot && !desired.assets?.screenshot) errors.push('assets.screenshot is required when listing.screenshot=true');
  if (desired.properties?.privacy === 'Yes' && !desired.properties.privacyPolicyUrl && !desired.properties.privacyPolicyText) errors.push('privacy=Yes requires privacyPolicyUrl or privacyPolicyText');
  const answers = desired.ageRatings?.answers ?? {};
  if (strict && checkAge && Object.keys(answers).length === 0) errors.push('ageRatings.answers is empty; confirm the questionnaire answers');
  if (strict && checkAge && !desired.ageRatings?.defaultAnswer) errors.push('ageRatings.defaultAnswer is required for newly revealed questionnaire items');
  if (strict && checkAge && desired.ageRatings?.requireConfirmation && !desired.ageRatings.confirmed) errors.push('ageRatings.confirmed must be true after reviewing the questionnaire answers');
  return errors;
}

function required(value, field, errors, strict) {
  if (strict && (!value || !String(value).trim())) errors.push(`${field} is required`);
}
function merge(base, value) {
  if (!value || typeof value !== 'object') return structuredClone(base);
  const result = structuredClone(base);
  for (const [key, item] of Object.entries(value)) {
    if (item && typeof item === 'object' && !Array.isArray(item) && result[key] && typeof result[key] === 'object') result[key] = merge(result[key], item);
    else result[key] = item;
  }
  return result;
}
