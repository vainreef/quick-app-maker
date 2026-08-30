import fs from 'node:fs';
import path from 'node:path';
import { readJson } from './atomic.mjs';

export function loadToolchain(root) {
  return readJson(path.join(root, 'qam-toolchain.lock.json'));
}

export function configureNpmEnvironment(root, lock) {
  if (!lock || typeof lock !== 'object') {
    const error = new Error('toolchain lock must be supplied by the engine');
    error.code = 'TOOLCHAIN';
    throw error;
  }
  const cache = path.resolve(root, lock.npm?.cacheDirectory ?? '.cache/npm');
  const userconfig = path.join(root, '.cache', 'npmrc');
  const electronCache = path.resolve(root, '.cache/electron');
  fs.mkdirSync(cache, { recursive: true });
  fs.mkdirSync(electronCache, { recursive: true });
  if (!fs.existsSync(userconfig)) fs.writeFileSync(userconfig, ['registry=' + (lock.npm?.registry ?? 'https://registry.npmmirror.com'), 'fund=false', 'audit=false', 'progress=false', 'prefer-offline=true'].join('\n') + '\n', 'utf8');
  const inherited = { ...process.env };
  for (const key of Object.keys(inherited)) if (/^npm_config_(prefix|global|globalconfig|userconfig|cache|registry)$/i.test(key)) delete inherited[key];
  return {
    ...inherited,
    npm_config_registry: lock.npm?.registry ?? 'https://registry.npmmirror.com',
    npm_config_cache: cache,
    npm_config_userconfig: userconfig,
    ELECTRON_MIRROR: lock.electron?.mirror ?? 'https://npmmirror.com/mirrors/electron/',
    electron_config_cache: electronCache,
    QAM_WORKSPACE_ROOT: path.resolve(root),
    QAM_REQUIRE_PORTABLE: process.platform === 'win32' ? '1' : (inherited.QAM_REQUIRE_PORTABLE ?? '0'),
    PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
    npm_config_fund: 'false',
    npm_config_audit: 'false',
    npm_config_prefer_offline: 'true'
  };
}
