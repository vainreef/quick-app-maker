#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { appRoot, assertWithin, ensureWorkspace, loadToolchain, configureNpmEnvironment, Logger, runNpm, runNodeScript, resolvePortableNode, sha256Sync, withWorkspaceLock, writeAtomic, readJson, openFileManager } from '@quick-app/core';
import { createElectronApp } from '@quick-app/generator-electron';
import { EXIT, PHASES, normalizePhase, loadDesired, saveDesired, validateDesired, importListingMarkdown, runId, loadCheckpoint, saveCheckpoint, EvidenceStore, Deadline, reconcilePhase } from '@quick-app/store-core';
import { runPreflight } from '@quick-app/store-preflight';
import { BrowserSession, resolveBrowserType, StoreDriver, phaseAdapters, capturePage, waitForPageKind, waitUntil, observeOverview, reserveProduct, verifyAppMount, captureAppScreenshot } from '@quick-app/store-playwright';

const ROOT = fileURLToPath(new URL('../', import.meta.url));
process.env.QAM_TOOLCHAIN_ROOT ||= ROOT;
process.env.QAM_ENGINE_ROOT ||= ROOT;
const TOOLCHAIN = loadToolchain(ROOT);
const command = process.argv[2] ?? 'help';
const args = parseArgs(process.argv.slice(3));
const targetCandidate = args.app ?? args._[0] ?? args._[1];
const deducedWorkspace = resolveWorkspace(args.workspaceRoot ?? process.env.QAM_WORKSPACE_ROOT, targetCandidate);
const workspace = ensureWorkspace(deducedWorkspace);

try {
  const code = await dispatch(command, args);
  process.exit(code ?? 0);
} catch (error) {
  const code = error.code === 'DEADLINE' ? EXIT.DEADLINE : error.code === 'SCHEMA_DRIFT' ? EXIT.SCHEMA_DRIFT : error.code === 'SESSION' || error.code === 'TOOLCHAIN' ? EXIT.SESSION : error.code === 'CONFIG' ? EXIT.CONFIG : error.code === 'DIFF' ? EXIT.DIFF : EXIT.ERROR;
  if (args.json) process.stdout.write(JSON.stringify({ ok: false, code, error: error.message, result: error.result ?? undefined }, null, 2) + '\n');
  else process.stderr.write(`[ERROR ${code}] ${error.message}\n`);
  process.exit(code);
}

async function dispatch(name, options) {
  if (name === 'help' || name === '--help' || name === '-h') return help();
  if (name === 'doctor') return doctor();
  if (name === 'bootstrap') return bootstrap(options);
  if (name === 'check') return check();
  if (name === 'self-test') return selfTest();
  if (name === 'create') return create(options);
  if (name === 'dev') return appCommand('dev', options);
  if (name === 'test') return appCommand('test', options);
  if (name === 'package') return appCommand('package:store', options);
  if (name === 'store') return store(options);
  if (name === 'screenshot' || name === 'capture') return screenshot(options);
  if (name === 'reveal' || name === 'open') return reveal(options);
  throw Object.assign(new Error(`unknown command: ${name}; run qam help`), { code: 'CONFIG' });
}

function help() {
  console.log(`Quick App Maker V2\n\nCommands:\n  doctor\n  bootstrap\n  self-test\n  create --name NAME --slug SLUG\n  dev APP\n  test APP\n  screenshot APP [--output PATH] [--width 1366] [--height 768]\n  package APP --profile store\n  store launch|reserve|preflight|discover|inspect|plan|apply|run|verify|status|stop --app APP [--browser chrome|edge|safari]\n  reveal PATH (opens in Finder / Explorer)\n  check\n`);
  return 0;
}

async function screenshot(options) {
  const target = options.app ?? options._[0] ?? '.';
  const root = appRoot(workspace, target);
  if (!fs.existsSync(path.join(root, 'package.json'))) throw Object.assign(new Error(`app package.json not found: ${root}`), { code: 'CONFIG' });
  const width = Number(options.width ?? 1366);
  const height = Number(options.height ?? 768);
  const defaultOut = path.join(root, 'store-submission-assets', `01_应用主界面高清截图_${width}x${height}.png`);
  const outputPath = path.resolve(root, options.output ?? options.o ?? defaultOut);
  console.log(`[QAM_SCREENSHOT] Capturing headless screenshot of ${path.basename(root)} (${width}x${height})...`);
  const result = await captureAppScreenshot(root, { width, height, outputPath });
  console.log(`[QAM_SCREENSHOT] Screenshot saved: ${result.outputPath} (${result.sizeBytes} bytes)`);
  return 0;
}

async function reveal(options) {
  const target = options._[0] ?? '.';
  const abs = path.resolve(workspace, target);
  const isDir = fs.existsSync(abs) && fs.statSync(abs).isDirectory();
  await openFileManager(abs, { select: !isDir });
  console.log(`REVEALED: ${abs}`);
  return 0;
}

async function doctor() {
  const lock = TOOLCHAIN; const node = process.versions.node; const major = Number(node.split('.')[0]); const nodePath = resolvePortableNode(workspace); const workspaceNode = isWorkspaceNode(workspace, nodePath); const npm = await npmVersion(workspace); const nodeOk = process.platform === 'win32' ? node === lock.node.version : (major >= 24 && major < 27); const report = { node, required: lock.node.version, npm, nodePath, workspaceNode, workspace, registry: lock.npm.registry, electron: lock.electron.version, playwright: lock.playwright.version, winapp: lock.winapp.version, platform: process.platform, ok: nodeOk && Boolean(npm) && (process.platform !== 'win32' || workspaceNode) };
  console.log(JSON.stringify(report, null, 2));
  return report.ok ? 0 : 1;
}

async function bootstrap(options) {
  return withWorkspaceLock(workspace, 'bootstrap', async () => {
    const lock = TOOLCHAIN; const env = configureNpmEnvironment(workspace, lock); const bootstrapRunId = runId('bootstrap'); const logger = new Logger({ runId: bootstrapRunId, root: path.join(workspace, '.cache/qam/runs', bootstrapRunId) });
    for (const dir of ['.cache/npm', '.cache/electron', '.cache/qam/runs']) fs.mkdirSync(path.join(workspace, dir), { recursive: true });
    const dependenciesReady = packageDependenciesReady(ROOT);
    if (fs.existsSync(path.join(ROOT, 'package-lock.json')) && !dependenciesReady) {
      logger.info('npm-ci', { cwd: ROOT }); await runNpm(['ci', '--ignore-scripts', '--prefer-offline'], { cwd: ROOT, workspace, env, logger, timeoutMs: 900_000 });
    }
    await installElectron(ROOT, env, logger, workspace);
    const missing = dependencyStatus(ROOT).missing;
    if (missing.length) throw Object.assign(new Error(`bootstrap incomplete; missing: ${missing.join(', ')}`), { code: 'TOOLCHAIN' });
    writeAtomic(workspace, path.join(logger.root, 'bootstrap-result.json'), { ok: true, node: process.execPath, workspace, dependencies: dependencyStatus(ROOT) });
    logger.pass('bootstrap ready', { node: process.execPath, workspace });
    console.log(`BOOTSTRAP_READY\nNODE_PATH: ${process.execPath}\nWORKSPACE_ROOT: ${workspace}`);
    return 0;
  });
}

async function selfTest() {
  return withWorkspaceLock(workspace, 'self-test', async () => { const env = configureNpmEnvironment(workspace, TOOLCHAIN); const testRunId = runId('self-test'); const logger = new Logger({ runId: testRunId, root: path.join(workspace, '.cache/qam/runs', testRunId) }); const result = await runNpm(['test'], { cwd: ROOT, workspace, env, logger, timeoutMs: 900_000 }); return result.code; });
}

async function create(options) {
  const name = options.name ?? options._[0]; if (!name) throw Object.assign(new Error('create requires --name'), { code: 'CONFIG' }); const slug = options.slug;
  return withWorkspaceLock(workspace, 'create', async () => {
    const root = createElectronApp({ workspace, name, slug });
    const env = configureNpmEnvironment(workspace, TOOLCHAIN); const createRunId = runId('create'); const logger = new Logger({ runId: createRunId, root: path.join(workspace, '.cache/qam/runs', createRunId) });
    await runNpm(['install', '--ignore-scripts', '--prefer-offline'], { cwd: root, workspace, env, logger, timeoutMs: 900_000 }); await installElectron(root, env, logger, workspace); assertAppDependencies(root, false);
    console.log(`APP_CREATED: ${root}`); return 0;
  });
}

async function installElectron(cwd, env, logger, workspaceRoot) {
  const ready = process.platform === 'win32' ? path.join(cwd, 'node_modules', 'electron', 'dist', 'electron.exe') : path.join(cwd, 'node_modules', 'electron', 'dist', 'Electron.app', 'Contents', 'MacOS', 'electron');
  if (fs.existsSync(ready)) return;
  const installer = path.join(cwd, 'node_modules', 'electron', 'install.js');
  if (!fs.existsSync(installer)) throw Object.assign(new Error(`Electron installer not found: ${installer}`), { code: 'TOOLCHAIN' });
  await runNodeScript(installer, [], { cwd, workspace: workspaceRoot, env, logger, timeoutMs: 900_000 });
  if (!fs.existsSync(ready)) throw Object.assign(new Error(`Electron runtime was not installed at ${ready}`), { code: 'TOOLCHAIN' });
}

async function appCommand(action, options) {
  const root = appRoot(workspace, options.app ?? options._[0] ?? '.'); if (!fs.existsSync(path.join(root, 'package.json'))) throw Object.assign(new Error(`app package.json not found: ${root}`), { code: 'CONFIG' });
  const lockName = `app-${path.relative(workspace, root)}`;
  return withWorkspaceLock(workspace, lockName, async () => {
    const env = configureNpmEnvironment(workspace, TOOLCHAIN); const actionRunId = runId(action); const logger = new Logger({ runId: actionRunId, root: path.join(workspace, '.cache/qam/runs', actionRunId) }); const needsWinApp = action === 'package:store';
    const appDepsReady = appDependencyStatus(root, needsWinApp).packagesReady;
    if (!appDepsReady) await runNpm(['install', '--ignore-scripts', '--prefer-offline'], { cwd: root, workspace, env, logger, timeoutMs: 900_000 });
    await installElectron(root, env, logger, workspace); assertAppDependencies(root, needsWinApp);
    if (action === 'package:store') { if (options.profile && options.profile !== 'store') throw Object.assign(new Error('only --profile store is supported'), { code: 'CONFIG' }); const desired = loadDesired(root); if (!desired.productId || desired.productId === 'PENDING' || !desired.package?.identityName || !desired.package?.publisher) throw Object.assign(new Error('reserve the Store name and identity before creating the Store package'), { code: 'CONFIG' }); }
    const script = action === 'package:store' ? 'package:store' : action; const result = await runNpm(['run', script], { cwd: root, workspace, env, logger, timeoutMs: action === 'package:store' ? 1_200_000 : 0 });
    if (result.code !== 0) return result.code;
    if (action === 'test') {
      try {
        const verifyRes = await verifyAppMount(root, { logger });
        if (verifyRes?.skipped) {
          console.log(`[QAM_TEST] Headless UI mount verification skipped: ${verifyRes.reason}`);
        } else {
          console.log('[QAM_TEST] Headless UI mount verification passed: #app mounted cleanly without errors.');
        }
      } catch (mountError) {
        console.error(`[QAM_TEST_ERROR] Headless UI mount verification failed:\n  ${mountError.message}`);
        return 1;
      }
    }
    return result.code;
  });
}

async function store(options) {
  const sub = options._[0] ?? 'help';
  if (sub !== 'status') {
    const root = appRoot(workspace, options.app ?? options._[1] ?? '.');
    return withWorkspaceLock(workspace, `app-${path.relative(workspace, root)}`, () => storeUnlocked(options));
  }
  return storeUnlocked(options);
}

async function storeUnlocked(options) {
  const sub = options._[0] ?? 'help'; const root = appRoot(workspace, options.app ?? options._[1] ?? '.'); const desired = loadDesired(root); resolveDesiredPaths(root, desired); if (desired.listingMarkdown && fs.existsSync(desired.listingMarkdown)) importListingMarkdown(desired, desired.listingMarkdown); const stateDir = path.join(root, '.cache', 'store'); fs.mkdirSync(stateDir, { recursive: true }); const storeRunId = runId(`store-${sub}`); const manifestFile = desired.package?.manifestPath && fs.existsSync(desired.package.manifestPath) ? desired.package.manifestPath : ''; const manifestHash = manifestFile ? sha256Sync(manifestFile) : ''; const checkpoint = loadCheckpoint(stateDir, { productId: desired.productId, submissionId: desired.submissionId, manifestHash }); checkpoint.manifestHash = manifestHash || checkpoint.manifestHash; const logger = new Logger({ runId: storeRunId, root: path.join(workspace, '.cache/qam/runs', storeRunId) });
  if (sub === 'preflight') { const result = runPreflight({ workspace, appRoot: root, desired, outputPath: path.join(logger.root, 'preflight-result.json') }); console.log(JSON.stringify(result, null, 2)); return 0; }
  if (sub === 'status') { console.log(JSON.stringify({ checkpoint, session: readJson(path.join(stateDir, 'session.json'), null) }, null, 2)); return 0; }
  const browserType = resolveBrowserType({ option: options.browser, config: desired });
  const session = new BrowserSession({ workspace, stateDir, baseUrl: appsOverviewUrl(desired.site.baseUrl), browserType, browserPath: options['browser-path'] ?? options.browserPath, logger });
  if (sub === 'launch') { const sessionInfo = await session.ensure(); console.log(JSON.stringify({ ok: true, launched: true, browser: sessionInfo.browserType, session: sessionInfo, message: `${sessionInfo.browserType} launched. Please sign in to Microsoft Partner Center, then reply when ready.` }, null, 2)); return 0; }
  if (sub === 'stop') { if (fs.existsSync(session.statePath())) { session.session = readJson(session.statePath(), null); await session.close(); console.log(`Active ${session.session?.browserType ?? 'browser'} session stopped.`); } else console.log('No active QAM browser session.'); return 0; }
  let connected = null;
  try {
    connected = await session.connect(); const { page } = connected;
    if (sub === 'inspect') { console.log(JSON.stringify(await capturePage(page), null, 2)); await connected.browser.close(); return 0; }
    if (sub === 'reserve') { const name = options.name ?? desired.productName; if (!name) throw Object.assign(new Error('store reserve requires --name'), { code: 'CONFIG' }); await waitForSignedIn(page); const result = await reserveProduct({ page, appRoot: root, desired, name, logger }); desired.productId = result.productId; checkpoint.productId = result.productId; saveDesired(root, desired); saveCheckpoint(workspace, stateDir, checkpoint); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return 0; }
    await waitForSignedIn(page);
    if (sub === 'discover') { const result = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = result.submissionId; checkpoint.routes = result.routes; desired.submissionId = result.submissionId; saveDesired(root, desired); saveCheckpoint(workspace, stateDir, checkpoint); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return 0; }
    if (sub === 'verify') { const result = await verifyAll(page, desired, checkpoint, logger); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return result.ok ? 0 : EXIT.DIFF; }
    if (sub === 'plan' || sub === 'apply') { const phase = normalizePhase(options.phase); const apply = sub === 'apply'; if (apply && phase === 'age-ratings' && options.confirmAgeRatings) { desired.ageRatings.confirmed = true; saveDesired(root, desired); } const validationErrors = validateDesired(desired, { strict: apply, checkAge: phase === 'age-ratings' }); if (validationErrors.length) throw Object.assign(new Error(validationErrors.join('; ')), { code: 'CONFIG' }); if (!checkpoint.submissionId) { if (!apply) throw Object.assign(new Error('store plan requires an existing submission checkpoint; run store discover first'), { code: 'CONFIG' }); const discovery = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = discovery.submissionId; checkpoint.routes = discovery.routes; saveCheckpoint(workspace, stateDir, checkpoint); } const driver = new StoreDriver({ page, desired, checkpoint, logger }); const adapters = phaseAdapters(driver); const runRoot = path.join(workspace, '.cache/qam/runs', logger.runId); const evidence = new EvidenceStore(workspace, runRoot); let result; try { result = await reconcilePhase({ phase, adapter: adapters[phase], desired, checkpoint, evidence, logger, apply, deadline: new Deadline(Number(options.deadline ?? 3_600_000)) }); } finally { if (apply) saveCheckpoint(workspace, stateDir, checkpoint); } console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return result.exitCode; }
    if (sub === 'run') { const apply = Boolean(options.apply); if (apply && options.confirmAgeRatings) { desired.ageRatings.confirmed = true; saveDesired(root, desired); } const validationErrors = validateDesired(desired, { strict: apply }); if (validationErrors.length) throw Object.assign(new Error(validationErrors.join('; ')), { code: 'CONFIG' }); if (!checkpoint.submissionId) { if (!apply) throw Object.assign(new Error('read-only store run requires an existing submission checkpoint; run store discover first'), { code: 'CONFIG' }); const discovery = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = discovery.submissionId; checkpoint.routes = discovery.routes; saveCheckpoint(workspace, stateDir, checkpoint); } let exit = 0; const deadline = new Deadline(Number(options.deadline ?? 3_600_000)); for (const phase of PHASES) { const driver = new StoreDriver({ page, desired, checkpoint, logger }); const adapters = phaseAdapters(driver); try { const result = await reconcilePhase({ phase, adapter: adapters[phase], desired, checkpoint, evidence: new EvidenceStore(workspace, path.join(logger.root, 'evidence')), logger, apply, deadline }); if (result.exitCode !== 0) { exit = result.exitCode; if (!apply) break; } } finally { if (apply) saveCheckpoint(workspace, stateDir, checkpoint); } } await connected.browser.close(); return exit; }
    throw Object.assign(new Error(`unknown store command: ${sub}`), { code: 'CONFIG' });
  } finally {
    try { if (connected?.browser) await connected.browser.close(); else if (session.session) await session.close(); } catch {}
  }
}

function resolveDesiredPaths(root, desired) {
  const fields = [['package', 'path'], ['package', 'manifestPath'], ['assets', 'screenshot']]; for (const [parent, key] of fields) if (desired[parent]?.[key]) desired[parent][key] = assertWithin(root, path.resolve(root, desired[parent][key]), `${parent}.${key}`);
  for (const item of Object.values(desired.listing?.assets ?? {})) if (item?.path) item.path = assertWithin(root, path.resolve(root, item.path), 'listing asset');
  if (desired.listingMarkdown) desired.listingMarkdown = assertWithin(root, path.resolve(root, desired.listingMarkdown), 'listingMarkdown');
  return desired;
}

async function waitForSignedIn(page) { await waitUntil(async () => { const snap = await capturePage(page); return !snap.signals?.signIn && snap.kind !== 'SignIn' && snap.kind !== 'ErrorPage' && /partner\.microsoft\.com/i.test(snap.url); }, { timeoutMs: 900_000, label: 'Partner Center sign-in' }); }
function appsOverviewUrl(baseUrl) { const parsed = new URL(baseUrl); const locale = parsed.pathname.split('/').filter(Boolean)[0] || 'zh-cn'; return `${parsed.origin}/${locale}/dashboard/apps-and-games/overview`; }
async function discoverSubmission(page, desired, checkpoint, logger) {
  const prodId = desired.productId && desired.productId !== 'PENDING' ? desired.productId : (desired.product?.productId || 'PENDING');
  desired.productId = prodId;
  const base = desired.site.baseUrl.replace(/\/$/, '');
  const overviewUrl = `${base}/${encodeURIComponent(prodId)}/overview`;
  if (!page.url().includes('/submissions/') && !page.url().includes(`/products/${prodId}/overview`)) {
    await page.goto(overviewUrl, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  }
  await waitForPageKind(page, ['ProductOverview', 'SubmissionOverview'], { operation: 'product overview' });

  let id = page.url().match(/\/submissions\/([^/?#]+)/i)?.[1] ?? checkpoint.submissionId ?? desired.submissionId;
  const getSubmissionHrefs = async () => {
    return await page.evaluate(() => {
      const anchors = [...document.querySelectorAll('a')];
      return anchors.map(a => a.getAttribute('href') || a.href).filter(h => h && h.includes('/submissions/'));
    });
  };

  let foundHrefs = await getSubmissionHrefs();
  for (const href of foundHrefs) {
    const match = href.match(/\/submissions\/([^/?#]+)/i);
    if (match && match[1]) { id = match[1]; break; }
  }

  if (!id) {
    const startBtn = page.locator('he-button[data-l10n-key="Start_Submission"], he-button:has-text("开始提交"), button:has-text("开始提交"), a:has-text("开始提交")').first();
    if (await startBtn.count()) {
      await startBtn.click().catch(() => {});
    } else {
      await page.evaluate(() => {
        const heBtn = document.querySelector('he-button[data-l10n-key="Start_Submission"]') || document.querySelector('he-button');
        const innerBtn = heBtn?.shadowRoot?.querySelector('button') || heBtn?.querySelector('button') || heBtn;
        innerBtn?.click();
      });
    }

    await waitUntil(async () => {
      foundHrefs = await getSubmissionHrefs();
      return foundHrefs.length > 0 || page.url().includes('/submissions/');
    }, { timeoutMs: 30_000, label: 'submission draft generation' });

    id = page.url().match(/\/submissions\/([^/?#]+)/i)?.[1];
    for (const href of foundHrefs) {
      const match = href.match(/\/submissions\/([^/?#]+)/i);
      if (match && match[1]) { id = match[1]; break; }
    }
  }

  if (!id) throw new Error('未能从当前产品页面中识别到 submissionId');

  const routes = {};
  for (const phase of PHASES) {
    const hint = phase === 'age-ratings' ? 'ageratings' : phase === 'listing' ? 'managelanguages|listings' : phase;
    const found = foundHrefs.find(x => new RegExp(hint, 'i').test(x));
    routes[phase] = found ? (found.startsWith('http') ? found : `${new URL(desired.site.baseUrl).origin}${found}`) : `${base}/${encodeURIComponent(desired.productId)}/submissions/${id}/${phase === 'age-ratings' ? 'ageratings' : phase === 'listing' ? 'managelanguages?producttype=app' : phase}`;
  }

  desired.submissionId = id;
  checkpoint.submissionId = id;
  checkpoint.routes = routes;
  logger?.pass('submission discovered', { submissionId: id, routes });
  return { submissionId: id, routes, url: page.url() };
}
async function verifyAll(page, desired, checkpoint, logger) { const url = `${desired.site.baseUrl.replace(/\/$/, '')}/${encodeURIComponent(desired.productId)}/overview`; await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 }); const overview = await observeOverview(page); const incomplete = PHASES.filter(phase => overview.modules[phase]?.status !== 'Complete'); const result = { ok: incomplete.length === 0, url: overview.url, pageKind: overview.pageKind, modules: overview.modules, incomplete }; logger?.event(result.ok ? 'PRODUCT_VERIFIED' : 'PRODUCT_INCOMPLETE', result); writeAtomic(workspace, path.join(logger.root, 'verify-result.json'), result); return result; }

async function check() {
  const stale = []; const roots = ['toolchain', 'bootstrap/workers', 'skills/vainreef-fast-publish/references/toolchain/v1']; for (const item of roots) if (fs.existsSync(path.join(ROOT, item))) stale.push(item);
  const required = ['bin/qam.mjs', 'qam-toolchain.lock.json', 'bootstrap/entry.ps1', 'bootstrap/qam.cmd', 'packages/core/src/index.mjs', 'packages/core/src/process.mjs', 'packages/core/src/lock.mjs', 'packages/store-core/src/index.mjs', 'packages/store-playwright/src/index.mjs', 'packages/store-preflight/src/index.mjs', 'templates/electron-vue-runtime/package.json', 'templates/electron-vue-runtime/tools/package-store.cjs', 'skills/vainreef-fast-publish/SKILL.md']; const missing = required.filter(item => !fs.existsSync(path.join(ROOT, item)));
  const legacyFiles = []; for (const file of walk(ROOT)) if (!file.includes(`${path.sep}node_modules${path.sep}`) && /\.(cs|csproj|sln)$/i.test(file)) legacyFiles.push(path.relative(ROOT, file));
  const lockText = fs.existsSync(path.join(ROOT, 'package-lock.json')) ? fs.readFileSync(path.join(ROOT, 'package-lock.json'), 'utf8') : '';
  const bootstrapText = fs.existsSync(path.join(ROOT, 'bootstrap/entry.ps1')) ? fs.readFileSync(path.join(ROOT, 'bootstrap/entry.ps1'), 'utf8') : ''; const processText = fs.existsSync(path.join(ROOT, 'packages/core/src/process.mjs')) ? fs.readFileSync(path.join(ROOT, 'packages/core/src/process.mjs'), 'utf8') : ''; const qamText = fs.readFileSync(path.join(ROOT, 'bin/qam.mjs'), 'utf8'); const devText = fs.existsSync(path.join(ROOT, 'templates/electron-vue-runtime/tools/dev.cjs')) ? fs.readFileSync(path.join(ROOT, 'templates/electron-vue-runtime/tools/dev.cjs'), 'utf8') : ''; const storeScript = fs.existsSync(path.join(ROOT, 'templates/electron-vue-runtime/tools/package-store.cjs')) ? fs.readFileSync(path.join(ROOT, 'templates/electron-vue-runtime/tools/package-store.cjs'), 'utf8') : ''; const manifest = readJson(path.join(ROOT, 'package.json'), {}); const toolchain = readJson(path.join(ROOT, 'qam-toolchain.lock.json'), {}); const versionChecks = [['playwright-core', manifest.dependencies?.['playwright-core'], toolchain.playwright?.version], ['electron', manifest.devDependencies?.electron, toolchain.electron?.version], ['@electron/packager', manifest.devDependencies?.['@electron/packager'], toolchain.packager?.version], ['vue', manifest.devDependencies?.vue, toolchain.ui?.version], ['@microsoft/winappcli', manifest.optionalDependencies?.['@microsoft/winappcli'], toolchain.winapp?.version]]; const errors = [...stale.map(x => `stale tree exists: ${x}`), ...missing.map(x => `missing: ${x}`), ...legacyFiles.map(x => `legacy source exists: ${x}`)]; for (const [name, actual, expected] of versionChecks) if (!expected || actual !== expected) errors.push(`toolchain version mismatch: ${name}=${actual ?? ''}, lock=${expected ?? ''}`); if (/git\+|ssh:\/\/git|github\.com\/electron\/node-gyp/i.test(lockText)) errors.push('package-lock contains a non-mirror git dependency'); if (/Get-Command\s+git(?:\.exe)?|\$gitPath\s*=\s*\$git\.Source/i.test(bootstrapText)) errors.push('bootstrap selects system Git'); if (/Get-Command\s+curl(?:\.exe)?/i.test(bootstrapText)) errors.push('bootstrap selects system curl'); if (!/cmd\\git\.exe/.test(bootstrapText)) errors.push('bootstrap does not validate portable Git'); if (!/npm-cli\.js/.test(bootstrapText) || !/--prefix\s+\$Destination\s+ci/.test(bootstrapText)) errors.push('bootstrap does not install dependencies before starting qam'); if (!/runNpm|resolveBundledNpmCli/.test(processText)) errors.push('process runner lacks bundled npm execution'); if (/configureNpmEnvironment\(workspace\);/.test(qamText)) errors.push('commands load the toolchain lock from the workspace instead of the engine'); if (/QAM_DEVTOOLS\s*:\s*['"]1['"]/.test(devText)) errors.push('dev wrapper opens DevTools by default'); if (/npx\.cmd|npm\.cmd/.test(qamText) || /npx\.cmd|npm\.cmd|--no-install/.test(storeScript)) errors.push('project directly invokes a command shim'); const result = { ok: errors.length === 0, errors, checkedAt: new Date().toISOString() }; console.log(JSON.stringify(result, null, 2)); return result.ok ? 0 : 1;
}

function parseArgs(raw) { const result = { _: [] }; for (let i = 0; i < raw.length; i += 1) { const token = raw[i]; if (!token.startsWith('-')) { result._.push(token); continue; } const key = token.replace(/^-+/, '').replace(/-([a-z])/g, (_, c) => c.toUpperCase()); const next = raw[i + 1]; if (next && !next.startsWith('-')) { result[key] = next; i += 1; } else result[key] = true; } return result; }
async function npmVersion(workspaceRoot) { try { return (await runNpm(['--version'], { cwd: ROOT, workspace: workspaceRoot, allowFailure: true })).stdout.trim(); } catch { return ''; } }
function packageDependenciesReady(root) { return dependencyStatus(root).packagesReady; }
function dependencyStatus(root) {
  const lock = TOOLCHAIN;
  const packages = {
    electron: packageVersionMatches(path.join(root, 'node_modules', 'electron', 'package.json'), lock.electron.version),
    packager: packageVersionMatches(path.join(root, 'node_modules', '@electron', 'packager', 'package.json'), lock.packager.version),
    playwrightCore: packageVersionMatches(path.join(root, 'node_modules', 'playwright-core', 'package.json'), lock.playwright.version),
    vue: packageVersionMatches(path.join(root, 'node_modules', 'vue', 'package.json'), lock.ui.version),
    workspaceCore: packageVersionMatches(path.join(root, 'node_modules', '@quick-app', 'core', 'package.json'), '2.0.0'),
    workspaceGenerator: packageVersionMatches(path.join(root, 'node_modules', '@quick-app', 'generator-electron', 'package.json'), '2.0.0'),
    workspaceStoreCore: packageVersionMatches(path.join(root, 'node_modules', '@quick-app', 'store-core', 'package.json'), '2.0.0'),
    workspaceStorePlaywright: packageVersionMatches(path.join(root, 'node_modules', '@quick-app', 'store-playwright', 'package.json'), '2.0.0'),
    workspaceStorePreflight: packageVersionMatches(path.join(root, 'node_modules', '@quick-app', 'store-preflight', 'package.json'), '2.0.0'),
    winapp: process.platform !== 'win32' || packageVersionMatches(path.join(root, 'node_modules', '@microsoft', 'winappcli', 'package.json'), lock.winapp.version) && fs.existsSync(path.join(root, 'node_modules', '@microsoft', 'winappcli', 'bin', process.arch === 'arm64' ? 'win-arm64' : 'win-x64', 'winapp.exe'))
  };
  const packagesReady = Object.values(packages).every(Boolean);
  const runtime = process.platform === 'win32' ? path.join(root, 'node_modules', 'electron', 'dist', 'electron.exe') : path.join(root, 'node_modules', 'electron', 'dist', 'Electron.app', 'Contents', 'MacOS', 'electron');
  const missing = Object.entries(packages).filter(([, ok]) => !ok).map(([name]) => name); if (!fs.existsSync(runtime)) missing.push('electron-runtime');
  return { packagesReady, ok: missing.length === 0, missing, runtime };
}
function appDependencyStatus(root, needsWinApp) {
  const lock = TOOLCHAIN; const packages = { electron: packageVersionMatches(path.join(root, 'node_modules', 'electron', 'package.json'), lock.electron.version), packager: packageVersionMatches(path.join(root, 'node_modules', '@electron', 'packager', 'package.json'), lock.packager.version), vue: packageVersionMatches(path.join(root, 'node_modules', 'vue', 'package.json'), lock.ui.version) };
  if (needsWinApp) packages.winapp = packageVersionMatches(path.join(root, 'node_modules', '@microsoft', 'winappcli', 'package.json'), lock.winapp.version) && fs.existsSync(path.join(root, 'node_modules', '@microsoft', 'winappcli', 'bin', process.arch === 'arm64' ? 'win-arm64' : 'win-x64', 'winapp.exe'));
  return { packagesReady: Object.values(packages).every(Boolean), packages };
}
function assertAppDependencies(root, needsWinApp) { const status = appDependencyStatus(root, needsWinApp); if (!status.packagesReady) { const missing = Object.entries(status.packages).filter(([, ok]) => !ok).map(([name]) => name); throw Object.assign(new Error(`app dependencies missing: ${missing.join(', ')}`), { code: 'TOOLCHAIN' }); } }
function packageVersionMatches(file, expected) { const value = readJson(file, null); return Boolean(value && value.version === expected); }
function isWorkspaceNode(root, nodePath) { const expected = process.platform === 'win32' ? path.join(root, 'node', 'node.exe') : path.join(root, 'node', 'bin', 'node'); return path.resolve(nodePath) === path.resolve(expected); }
function* walk(root) { for (const entry of fs.readdirSync(root, { withFileTypes: true })) { if (['.git', 'node_modules', '.cache'].includes(entry.name)) continue; const file = path.join(root, entry.name); if (entry.isDirectory()) yield* walk(file); else yield file; } }
function resolveWorkspace(explicitRoot, targetCandidate) {
  if (explicitRoot) return path.resolve(explicitRoot);
  if (process.env.QAM_WORKSPACE_ROOT) return path.resolve(process.env.QAM_WORKSPACE_ROOT);
  if (targetCandidate && typeof targetCandidate === 'string') {
    const candidate = path.resolve(targetCandidate);
    if (fs.existsSync(candidate)) {
      const isDir = fs.statSync(candidate).isDirectory();
      if (isDir && fs.existsSync(path.join(candidate, 'package.json'))) {
        const rel = path.relative(process.cwd(), candidate);
        if (rel.startsWith('..') || path.isAbsolute(rel)) {
          return path.dirname(candidate);
        }
      }
    }
  }
  return process.cwd();
}
