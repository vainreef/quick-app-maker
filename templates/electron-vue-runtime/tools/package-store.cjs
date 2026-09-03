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

(async () => {
  console.log(`Packaging Windows layout for ${pkg.name}...`);
  const layouts = await packager({
    dir: root,
    out: outRoot,
    platform: 'win32',
    arch: 'x64',
    overwrite: true,
    asar: true,
    prune: true,
    executableName: pkg.name,
    ignore: [/^\/\.cache($|\/)/, /^\/build($|\/)/, /^\/out($|\/)/, /^\/store($|\/)/, /^\/tests($|\/)/, /^\/tools($|\/)/, /\.log$/]
  });

  const layoutRoot = layouts[0];
  console.log(`✓ Windows layout generated at: ${layoutRoot}`);

  // 1. Inject Assets
  const assets = path.join(layoutRoot, 'Assets');
  fs.mkdirSync(assets, { recursive: true });
  for (const file of fs.readdirSync(path.join(storeRoot, 'assets'))) {
    fs.copyFileSync(path.join(storeRoot, 'assets', file), path.join(assets, file));
  }

  // 2. Inject AppxManifest.xml
  fs.copyFileSync(manifest, path.join(layoutRoot, 'AppxManifest.xml'));

  // 3. Remove any conflicting manual manifest files if present
  try { fs.rmSync(path.join(layoutRoot, '[Content_Types].xml'), { force: true }); } catch {}
  try { fs.rmSync(path.join(layoutRoot, 'AppxBlockMap.xml'), { force: true }); } catch {}

  // 4. Build MSIX package using official makemsix / winappcli tool
  const packageOut = path.join(root, 'build', 'store-package');
  fs.rmSync(packageOut, { recursive: true, force: true });
  fs.mkdirSync(packageOut, { recursive: true });

  const msixName = `${pkg.name}-${pkg.version}-x64.msix`;
  const msixPath = path.join(packageOut, msixName);

  const workspaceRoot = process.env.QAM_WORKSPACE_ROOT || path.resolve(root, '..', '..');
  const engineRoot = process.env.QAM_TOOLCHAIN_ROOT || process.env.QAM_ENGINE_ROOT || '';
  const makemsixCandidates = [
    engineRoot ? path.join(engineRoot, 'tools', 'makemsix', 'makemsix') : '',
    path.join(workspaceRoot, 'tools', 'makemsix', 'makemsix'),
    path.resolve(root, '..', '..', 'tools', 'makemsix', 'makemsix'),
    path.resolve(root, '..', 'tools', 'makemsix', 'makemsix')
  ].filter(Boolean);

  const makemsixBin = makemsixCandidates.find(p => fs.existsSync(p));

  if (makemsixBin) {
    console.log(`Packing official MSIX via makemsix: ${makemsixBin}...`);
    const res = spawnSync(makemsixBin, ['pack', '-d', layoutRoot, '-p', msixPath], { cwd: root, stdio: 'inherit' });
    if (res.error) throw res.error;
    if (res.status !== 0) throw new Error(`makemsix pack failed with status ${res.status}`);
  } else if (process.platform === 'win32') {
    const winappCli = path.join(root, 'node_modules', '@microsoft', 'winappcli', 'dist', 'cli.js');
    const workspaceNode = process.env.QAM_WORKSPACE_ROOT ? path.join(process.env.QAM_WORKSPACE_ROOT, 'node', 'node.exe') : '';
    const nodeRuntime = (workspaceNode && fs.existsSync(workspaceNode)) ? workspaceNode : 'node';
    if (fs.existsSync(winappCli)) {
      const res = spawnSync(nodeRuntime, [winappCli, 'pack', layoutRoot, '--output', packageOut, '--manifest', manifest], { cwd: root, stdio: 'inherit' });
      if (res.status !== 0) throw new Error(`winappcli pack failed: ${res.status}`);
    } else {
      throw new Error(`WinApp CLI is missing at ${winappCli}`);
    }
  } else {
    throw new Error(`Official makemsix tool not found at ${makemsixBin}`);
  }

  const artifact = fs.readdirSync(packageOut).filter(x => /\.(msix|appx|msixbundle)$/i.test(x)).map(x => path.join(packageOut, x)).sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs)[0];
  if (!artifact) throw new Error(`MSIX artifact not found in ${packageOut}`);

  const desiredPath = path.join(storeRoot, 'desired-state.json');
  const desired = JSON.parse(fs.readFileSync(desiredPath, 'utf8'));
  desired.package.path = path.relative(root, artifact).replaceAll('\\', '/');
  desired.package.executable = `${pkg.name}.exe`;
  fs.writeFileSync(desiredPath, JSON.stringify(desired, null, 2) + '\n', 'utf8');

  console.log(JSON.stringify({
    ok: true,
    layout: layoutRoot,
    artifact,
    size: fs.statSync(artifact).size,
    desired: desiredPath
  }, null, 2));
})().catch(error => {
  console.error(error.stack || error.message);
  process.exit(1);
});
