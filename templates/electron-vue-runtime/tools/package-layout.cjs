const path = require('node:path');
const fs = require('node:fs');
const { packager } = require('@electron/packager');
const root = path.resolve(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
process.env.ELECTRON_MIRROR ||= 'https://npmmirror.com/mirrors/electron/';
process.env.electron_config_cache ||= path.join(root, '.cache', 'electron');
(async () => {
  const output = await packager({ dir: root, out: path.join(root, 'out'), platform: process.platform, arch: process.arch, overwrite: true, asar: true, prune: true, executableName: pkg.name, ignore: [/^\/store($|\/)/, /^\/tests($|\/)/, /^\/tools\/package-store/, /\.log$/] });
  console.log(JSON.stringify({ layout: output[0] }, null, 2));
})().catch(error => { console.error(error.stack || error.message); process.exit(1); });
