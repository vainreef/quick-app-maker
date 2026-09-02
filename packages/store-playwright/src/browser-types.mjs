export const SUPPORTED_BROWSERS = ['chrome', 'edge', 'safari'];
export const DEFAULT_BROWSER = 'chrome';

export const BROWSER_ALIASES = new Map([
  ['chrome', 'chrome'],
  ['google-chrome', 'chrome'],
  ['googlechrome', 'chrome'],
  ['chromium', 'chrome'],
  ['edge', 'edge'],
  ['msedge', 'edge'],
  ['microsoft-edge', 'edge'],
  ['safari', 'safari'],
  ['webkit', 'safari']
]);

export function normalizeBrowserType(value) {
  if (!value) return DEFAULT_BROWSER;
  const clean = String(value).trim().toLowerCase();
  const normalized = BROWSER_ALIASES.get(clean);
  if (!normalized) {
    throw new Error(`Unsupported browser: "${value}". Supported options are: ${SUPPORTED_BROWSERS.join(', ')}`);
  }
  return normalized;
}

export function resolveBrowserType({ option, env = process.env, config = null } = {}) {
  if (option) return normalizeBrowserType(option);
  if (env.QAM_BROWSER) return normalizeBrowserType(env.QAM_BROWSER);
  if (config?.browser) return normalizeBrowserType(config.browser);
  if (config?.site?.browser) return normalizeBrowserType(config.site.browser);
  return DEFAULT_BROWSER;
}
