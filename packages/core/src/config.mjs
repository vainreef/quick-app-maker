import fs from 'node:fs';
import path from 'node:path';
import { readJson } from './atomic.mjs';

export function loadToolchain(root) {
  return readJson(path.join(root, 'qam-toolchain.lock.json'));
}

export function configureNpmEnvironment(root, lock = loadToolchain(root)) {
  const cache = path.resolve(root, lock.npm?.cacheDirectory ?? '.cache/npm');
  fs.mkdirSync(cache, { recursive: true });
  return {
    ...process.env,
    npm_config_registry: lock.npm?.registry ?? 'https://registry.npmmirror.com',
    npm_config_cache: cache,
    npm_config_userconfig: path.join(root, '.cache', 'npmrc'),
    ELECTRON_MIRROR: lock.electron?.mirror ?? 'https://npmmirror.com/mirrors/electron/',
    electron_config_cache: path.resolve(root, '.cache/electron'),
    PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
    npm_config_fund: 'false',
    npm_config_audit: 'false',
    npm_config_prefer_offline: 'true'
  };
}
