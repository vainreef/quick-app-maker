const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { packager } = require('@electron/packager');
const root = path.resolve(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
process.env.ELECTRON_MIRROR ||= 'https://npmmirror.com/mirrors/electron/';
process.env.electron_config_cache ||= path.join(root, '.cache', 'electron');
const outRoot = path.join(root, 'out');
const storeRoot = path.join(root, 'store');
const manifest = path.join(storeRoot, 'Package.appxmanifest');
if (process.platform !== 'win32') throw new Error('Store MSIX packaging runs on Windows; use a Windows workspace for package:store.');
const winappCli = path.join(root, 'node_modules', '@microsoft', 'winappcli', 'dist', 'cli.js');
if (!fs.existsSync(winappCli)) throw new Error(`WinApp CLI is missing: ${winappCli}. Run the portable bootstrap first.`);
const workspaceNode = process.env.QAM_WORKSPACE_ROOT ? path.join(process.env.QAM_WORKSPACE_ROOT, 'node', 'node.exe') : '';
const nodeRuntime = workspaceNode && fs.existsSync(workspaceNode) ? workspaceNode : process.execPath;
function run(args) {
  const result = spawnSync(nodeRuntime, [winappCli, ...args], { cwd: root, stdio: 'inherit', env: { ...process.env, PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1' } });
  if (result.error) throw new Error(`WinApp CLI failed to start: ${result.error.message}`);
  if (result.status !== 0) throw new Error(`WinApp CLI exited with ${result.status ?? 1}`);
}
(async () => {
  const layouts = await packager({ dir: root, out: outRoot, platform: 'win32', arch: 'x64', overwrite: true, asar: true, prune: true, executableName: pkg.name, ignore: [/^\/store($|\/)/, /^\/tests($|\/)/, /^\/tools\/package-store/, /\.log$/] });
  const layoutRoot = layouts[0];
  const assets = path.join(layoutRoot, 'Assets'); fs.mkdirSync(assets, { recursive: true });
  for (const file of fs.readdirSync(path.join(storeRoot, 'assets'))) fs.copyFileSync(path.join(storeRoot, 'assets', file), path.join(assets, file));
  const packageOut = path.join(root, 'build', 'store-package'); fs.rmSync(packageOut, { recursive: true, force: true }); fs.mkdirSync(packageOut, { recursive: true });
  run(['pack', layoutRoot, '--output', packageOut, '--manifest', manifest]);
  const artifact = fs.readdirSync(packageOut).filter(x => /\.(msix|appx|msixbundle)$/i.test(x)).map(x => path.join(packageOut, x)).sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs)[0];
  if (!artifact) throw new Error(`MSIX artifact not found in ${packageOut}`);
  const desiredPath = path.join(storeRoot, 'desired-state.json'); const desired = JSON.parse(fs.readFileSync(desiredPath, 'utf8')); desired.package.path = path.relative(root, artifact).replaceAll('\\', '/'); desired.package.executable = `${pkg.name}.exe`; fs.writeFileSync(desiredPath, JSON.stringify(desired, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify({ layout: layoutRoot, artifact, desired: desiredPath }, null, 2));
})().catch(error => { console.error(error.stack || error.message); process.exit(1); });
