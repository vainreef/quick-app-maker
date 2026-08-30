#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { appRoot, assertWithin, ensureWorkspace, loadToolchain, configureNpmEnvironment, Logger, run, commandExists, writeAtomic, readJson } from '@quick-app/core';
import { createElectronApp } from '@quick-app/generator-electron';
import { EXIT, PHASES, normalizePhase, loadDesired, saveDesired, validateDesired, importListingMarkdown, runId, loadCheckpoint, saveCheckpoint, EvidenceStore, Deadline, reconcilePhase } from '@quick-app/store-core';
import { runPreflight } from '@quick-app/store-preflight';
import { EdgeSession, StoreDriver, phaseAdapters, capturePage, waitForPageKind, waitUntil, observeOverview, reserveProduct } from '@quick-app/store-playwright';

const ROOT = fileURLToPath(new URL('../', import.meta.url));
const command = process.argv[2] ?? 'help';
const args = parseArgs(process.argv.slice(3));
const workspace = ensureWorkspace(args['workspace-root'] ?? process.env.QAM_WORKSPACE_ROOT ?? process.cwd());

try {
  const code = await dispatch(command, args);
  process.exitCode = code ?? 0;
} catch (error) {
  const code = error.code === 'DEADLINE' ? EXIT.DEADLINE : error.code === 'SCHEMA_DRIFT' ? EXIT.SCHEMA_DRIFT : error.code === 'SESSION' ? EXIT.SESSION : error.code === 'CONFIG' ? EXIT.CONFIG : error.code === 'DIFF' ? EXIT.DIFF : EXIT.ERROR;
  if (args.json) process.stdout.write(JSON.stringify({ ok: false, code, error: error.message, result: error.result ?? undefined }, null, 2) + '\n');
  else process.stderr.write(`[ERROR ${code}] ${error.message}\n`);
  process.exitCode = code;
}

async function dispatch(name, options) {
  if (name === 'help' || name === '--help' || name === '-h') return help();
  if (name === 'doctor') return doctor();
  if (name === 'bootstrap') return bootstrap(options);
  if (name === 'check') return check();
  if (name === 'create') return create(options);
  if (name === 'dev') return appCommand('dev', options);
  if (name === 'test') return appCommand('test', options);
  if (name === 'package') return appCommand('package:store', options);
  if (name === 'store') return store(options);
  throw Object.assign(new Error(`unknown command: ${name}; run qam help`), { code: 'CONFIG' });
}

function help() {
  console.log(`Quick App Maker V2\n\nCommands:\n  doctor\n  bootstrap\n  create --name NAME --slug SLUG\n  dev APP\n  test APP\n  package APP --profile store\n  store launch|reserve|preflight|discover|inspect|plan|apply|run|verify|status|stop --app APP\n  check\n`);
  return 0;
}

async function doctor() {
  const lock = loadToolchain(ROOT); const node = process.versions.node; const major = Number(node.split('.')[0]);
  const report = { node, required: lock.node.version, npm: await npmVersion(), workspace, registry: lock.npm.registry, electron: lock.electron.version, playwright: lock.playwright.version, winapp: lock.winapp.version, platform: process.platform, ok: major === 24 };
  console.log(JSON.stringify(report, null, 2));
  return report.ok ? 0 : 1;
}

async function bootstrap(options) {
  const lock = loadToolchain(ROOT); const env = configureNpmEnvironment(workspace, lock); const logger = new Logger({ runId: runId('bootstrap'), root: path.join(workspace, '.cache/qam/bootstrap') });
  for (const dir of ['.cache/npm', '.cache/electron', '.cache/qam/runs']) fs.mkdirSync(path.join(workspace, dir), { recursive: true });
  const dependenciesReady = fs.existsSync(path.join(ROOT, 'node_modules', 'electron')) && fs.existsSync(path.join(ROOT, 'node_modules', 'playwright-core')) && (process.platform !== 'win32' || fs.existsSync(path.join(ROOT, 'node_modules', '@microsoft', 'winappcli')));
  if (fs.existsSync(path.join(ROOT, 'package-lock.json')) && !dependenciesReady) {
    const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'; logger.info('npm-ci'); await run(npm, ['ci', '--ignore-scripts', '--prefer-offline'], { cwd: ROOT, env, logger, timeoutMs: 900_000 });
  }
  await installElectron(ROOT, env, logger);
  logger.pass('bootstrap ready', { node: process.execPath, workspace });
  console.log(`BOOTSTRAP_READY\nNODE_PATH: ${process.execPath}\nWORKSPACE_ROOT: ${workspace}`);
  return 0;
}

async function create(options) {
  const name = options.name ?? options._[0]; if (!name) throw Object.assign(new Error('create requires --name'), { code: 'CONFIG' }); const slug = options.slug;
  const root = createElectronApp({ workspace, name, slug });
  const env = configureNpmEnvironment(workspace); const logger = new Logger({ runId: runId('create'), root: path.join(workspace, '.cache/qam/runs', runId('create')) });
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'; await run(npm, ['install', '--ignore-scripts', '--prefer-offline'], { cwd: root, env, logger, timeoutMs: 900_000 }); await installElectron(root, env, logger);
  console.log(`APP_CREATED: ${root}`); return 0;
}

async function installElectron(cwd, env, logger) {
  const ready = process.platform === 'win32' ? path.join(cwd, 'node_modules', 'electron', 'dist', 'electron.exe') : path.join(cwd, 'node_modules', 'electron', 'dist', 'Electron.app', 'Contents', 'MacOS', 'electron');
  if (fs.existsSync(ready)) return;
  const npx = process.platform === 'win32' ? 'npx.cmd' : 'npx'; const bin = path.join(cwd, 'node_modules', '.bin', process.platform === 'win32' ? 'install-electron.cmd' : 'install-electron');
  if (fs.existsSync(bin)) await run(npx, ['--no-install', 'install-electron', '--no'], { cwd, env, logger, timeoutMs: 900_000 });
}

async function appCommand(action, options) {
  const root = appRoot(workspace, options.app ?? options._[0] ?? '.'); if (!fs.existsSync(path.join(root, 'package.json'))) throw Object.assign(new Error(`app package.json not found: ${root}`), { code: 'CONFIG' });
  const env = configureNpmEnvironment(workspace); const logger = new Logger({ runId: runId(action), root: path.join(workspace, '.cache/qam/runs', runId(action)) }); const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  const appDepsReady = fs.existsSync(path.join(root, 'node_modules', 'electron')) && fs.existsSync(path.join(root, 'node_modules', 'vue'));
  if (!appDepsReady) await run(npm, ['install', '--ignore-scripts', '--prefer-offline'], { cwd: root, env, logger, timeoutMs: 900_000 });
  await installElectron(root, env, logger);
  if (action === 'package:store') { if (options.profile && options.profile !== 'store') throw Object.assign(new Error('only --profile store is supported'), { code: 'CONFIG' }); const desired = loadDesired(root); if (!desired.productId || desired.productId === 'PENDING' || !desired.package?.identityName || !desired.package?.publisher) throw Object.assign(new Error('reserve the Store name and identity before creating the Store package'), { code: 'CONFIG' }); }
  const script = action === 'package:store' ? 'package:store' : action; const result = await run(npm, ['run', script], { cwd: root, env, logger, timeoutMs: action === 'package:store' ? 1_200_000 : 0 }); return result.code;
}

async function store(options) {
  const sub = options._[0] ?? 'help'; const root = appRoot(workspace, options.app ?? options._[1] ?? '.'); const desired = loadDesired(root); resolveDesiredPaths(root, desired); if (desired.listingMarkdown && fs.existsSync(desired.listingMarkdown)) importListingMarkdown(desired, desired.listingMarkdown); const stateDir = path.join(root, '.cache', 'store'); fs.mkdirSync(stateDir, { recursive: true }); const checkpoint = loadCheckpoint(stateDir, { productId: desired.productId }); const logger = new Logger({ runId: runId(`store-${sub}`), root: path.join(workspace, '.cache/qam/runs', runId(`store-${sub}`)) });
  if (sub === 'preflight') { const result = runPreflight({ workspace, appRoot: root, desired, outputPath: path.join(logger.root, 'preflight-result.json') }); console.log(JSON.stringify(result, null, 2)); return 0; }
  if (sub === 'status') { console.log(JSON.stringify({ checkpoint, session: readJson(path.join(stateDir, 'session.json'), null) }, null, 2)); return 0; }
  const session = new EdgeSession({ workspace, stateDir, baseUrl: appsOverviewUrl(desired.site.baseUrl), logger });
  if (sub === 'launch') { const result = await session.connect(); await waitForSignedIn(result.page); console.log(JSON.stringify({ ok: true, session: result.session, page: await capturePage(result.page) }, null, 2)); await result.browser.close(); return 0; }
  if (sub === 'stop') { if (fs.existsSync(session.statePath())) { session.session = readJson(session.statePath(), null); await session.close(); } else console.log('No active QAM Edge session.'); return 0; }
  const connected = await session.connect(); const { page } = connected;
  if (sub === 'reserve') { const name = options.name ?? desired.productName; if (!name) throw Object.assign(new Error('store reserve requires --name'), { code: 'CONFIG' }); await waitForSignedIn(page); const result = await reserveProduct({ page, appRoot: root, desired, name, logger }); desired.productId = result.productId; checkpoint.productId = result.productId; saveCheckpoint(workspace, stateDir, checkpoint); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return 0; }
  await waitForSignedIn(page);
  if (sub === 'discover') { const result = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = result.submissionId; checkpoint.routes = result.routes; saveCheckpoint(workspace, stateDir, checkpoint); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return 0; }
  if (sub === 'inspect') { console.log(JSON.stringify(await capturePage(page), null, 2)); await connected.browser.close(); return 0; }
  if (sub === 'verify') { const result = await verifyAll(page, desired, checkpoint, logger); console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return result.ok ? 0 : EXIT.DIFF; }
  if (sub === 'plan' || sub === 'apply') { const phase = normalizePhase(options.phase); if (phase === 'age-ratings' && options.confirmAgeRatings) { desired.ageRatings.confirmed = true; saveDesired(root, desired); } const validationErrors = validateDesired(desired, { strict: sub === 'apply', checkAge: phase === 'age-ratings' }); if (validationErrors.length) throw Object.assign(new Error(validationErrors.join('; ')), { code: 'CONFIG' }); if (!checkpoint.submissionId) { const discovery = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = discovery.submissionId; checkpoint.routes = discovery.routes; saveCheckpoint(workspace, stateDir, checkpoint); } const driver = new StoreDriver({ page, desired, checkpoint, logger }); const adapters = phaseAdapters(driver); const runRoot = path.join(workspace, '.cache/qam/runs', logger.runId); const evidence = new EvidenceStore(workspace, runRoot); let result; try { result = await reconcilePhase({ phase, adapter: adapters[phase], desired, checkpoint, evidence, logger, apply: sub === 'apply', deadline: new Deadline(Number(options.deadline ?? 3_600_000)) }); } finally { saveCheckpoint(workspace, stateDir, checkpoint); } console.log(JSON.stringify(result, null, 2)); await connected.browser.close(); return result.exitCode; }
  if (sub === 'run') { const apply = Boolean(options.apply); if (apply && options.confirmAgeRatings) { desired.ageRatings.confirmed = true; saveDesired(root, desired); } const validationErrors = validateDesired(desired, { strict: apply }); if (validationErrors.length) throw Object.assign(new Error(validationErrors.join('; ')), { code: 'CONFIG' }); if (!checkpoint.submissionId) { const discovery = await discoverSubmission(page, desired, checkpoint, logger); checkpoint.submissionId = discovery.submissionId; checkpoint.routes = discovery.routes; saveCheckpoint(workspace, stateDir, checkpoint); } let exit = 0; for (const phase of PHASES) { const driver = new StoreDriver({ page, desired, checkpoint, logger }); const adapters = phaseAdapters(driver); try { const result = await reconcilePhase({ phase, adapter: adapters[phase], desired, checkpoint, evidence: new EvidenceStore(workspace, path.join(logger.root, 'evidence')), logger, apply, deadline: new Deadline(Number(options.deadline ?? 3_600_000)) }); if (result.exitCode !== 0) { exit = result.exitCode; if (!apply) break; } } finally { saveCheckpoint(workspace, stateDir, checkpoint); } } await connected.browser.close(); return exit; }
  throw Object.assign(new Error(`unknown store command: ${sub}`), { code: 'CONFIG' });
}

function resolveDesiredPaths(root, desired) {
  const fields = [['package', 'path'], ['package', 'manifestPath'], ['assets', 'screenshot']]; for (const [parent, key] of fields) if (desired[parent]?.[key]) desired[parent][key] = assertWithin(root, path.resolve(root, desired[parent][key]), `${parent}.${key}`);
  for (const item of Object.values(desired.listing?.assets ?? {})) if (item?.path) item.path = assertWithin(root, path.resolve(root, item.path), 'listing asset');
  if (desired.listingMarkdown) desired.listingMarkdown = assertWithin(root, path.resolve(root, desired.listingMarkdown), 'listingMarkdown');
  return desired;
}

async function waitForSignedIn(page) { await waitUntil(async () => { const snap = await capturePage(page); return snap.ready && snap.kind !== 'SignIn' && snap.kind !== 'ErrorPage' && /partner\.microsoft\.com/i.test(snap.url); }, { timeoutMs: 900_000, label: 'Partner Center sign-in' }); }
function appsOverviewUrl(baseUrl) { const parsed = new URL(baseUrl); const locale = parsed.pathname.split('/').filter(Boolean)[0] || 'zh-cn'; return `${parsed.origin}/${locale}/dashboard/apps-and-games/overview`; }
async function discoverSubmission(page, desired, checkpoint, logger) { const base = desired.site.baseUrl.replace(/\/$/, ''); const url = `${base}/${encodeURIComponent(desired.productId)}/overview`; await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 }); await waitForPageKind(page, ['ProductOverview', 'SubmissionOverview'], { operation: 'product overview' }); let links = await page.locator('a[href*="/submissions/"]').evaluateAll(items => items.map(item => item.href).filter(Boolean)); let id = links.map(x => x.match(/\/submissions\/([^/]+)/i)?.[1]).find(Boolean) ?? checkpoint.submissionId ?? desired.submissionId; if (!id) { const start = page.getByRole('button', { name: /开始提交|Start submission|新建提交/i }).first(); if (!(await start.count())) throw Object.assign(new Error('active submission not found and start button is missing'), { code: 'SCHEMA_DRIFT' }); await start.click(); await waitUntil(async () => page.url().includes('/submissions/'), { timeoutMs: 60_000, label: 'submission draft creation' }); id = page.url().match(/\/submissions\/([^/]+)/i)?.[1]; }
  if (!id) throw new Error('submissionId was not found'); links = await page.locator('a[href*="/submissions/"]').evaluateAll(items => items.map(item => item.href).filter(Boolean)); const routes = {}; for (const phase of PHASES) { const hint = phase === 'age-ratings' ? 'ageratings' : phase === 'listing' ? 'managelanguages|listings' : phase; routes[phase] = links.find(x => new RegExp(hint, 'i').test(x)) ?? ''; } logger?.pass('submission discovered', { submissionId: id, routes }); return { submissionId: id, routes, url: page.url() }; }
async function verifyAll(page, desired, checkpoint, logger) { const url = `${desired.site.baseUrl.replace(/\/$/, '')}/${encodeURIComponent(desired.productId)}/overview`; await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 }); const overview = await observeOverview(page); const incomplete = PHASES.filter(phase => overview.modules[phase]?.status !== 'Complete'); const result = { ok: incomplete.length === 0, url: overview.url, pageKind: overview.pageKind, modules: overview.modules, incomplete }; logger?.event(result.ok ? 'PRODUCT_VERIFIED' : 'PRODUCT_INCOMPLETE', result); writeAtomic(workspace, path.join(logger.root, 'verify-result.json'), result); return result; }

async function check() {
  const stale = []; const roots = ['toolchain', 'bootstrap/workers', 'skills/vainreef-fast-publish/references/toolchain/v1']; for (const item of roots) if (fs.existsSync(path.join(ROOT, item))) stale.push(item);
  const required = ['bin/qam.mjs', 'qam-toolchain.lock.json', 'packages/core/src/index.mjs', 'packages/store-core/src/index.mjs', 'packages/store-playwright/src/index.mjs', 'packages/store-preflight/src/index.mjs', 'templates/electron-vue-runtime/package.json', 'skills/vainreef-fast-publish/SKILL.md']; const missing = required.filter(item => !fs.existsSync(path.join(ROOT, item)));
  const legacyFiles = []; for (const file of walk(ROOT)) if (!file.includes(`${path.sep}node_modules${path.sep}`) && /\.(cs|csproj|sln)$/i.test(file)) legacyFiles.push(path.relative(ROOT, file));
  const lockText = fs.existsSync(path.join(ROOT, 'package-lock.json')) ? fs.readFileSync(path.join(ROOT, 'package-lock.json'), 'utf8') : '';
  const errors = [...stale.map(x => `stale tree exists: ${x}`), ...missing.map(x => `missing: ${x}`), ...legacyFiles.map(x => `legacy source exists: ${x}`)]; if (/git\+|ssh:\/\/git|github\.com\/electron\/node-gyp/i.test(lockText)) errors.push('package-lock contains a non-mirror git dependency'); const result = { ok: errors.length === 0, errors, checkedAt: new Date().toISOString() }; console.log(JSON.stringify(result, null, 2)); return result.ok ? 0 : 1;
}

function parseArgs(raw) { const result = { _: [] }; for (let i = 0; i < raw.length; i += 1) { const token = raw[i]; if (!token.startsWith('-')) { result._.push(token); continue; } const key = token.replace(/^-+/, '').replace(/-([a-z])/g, (_, c) => c.toUpperCase()); const next = raw[i + 1]; if (next && !next.startsWith('-')) { result[key] = next; i += 1; } else result[key] = true; } return result; }
async function npmVersion() { const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'; try { return (await run(npm, ['--version'], { cwd: ROOT, allowFailure: true })).stdout.trim(); } catch { return ''; } }
function* walk(root) { for (const entry of fs.readdirSync(root, { withFileTypes: true })) { if (['.git', 'node_modules', '.cache'].includes(entry.name)) continue; const file = path.join(root, entry.name); if (entry.isDirectory()) yield* walk(file); else yield file; } }
