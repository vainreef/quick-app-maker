const { app, BrowserWindow, protocol, ipcMain, session } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const APP_ROOT = app.getAppPath();
const DATA_ROOT = process.env.QAM_DATA_DIR ? path.resolve(process.env.QAM_DATA_DIR) : app.getPath('userData');
const STATE_FILE = path.join(DATA_ROOT, 'state.json');
const MAX_ITEMS = 500;
protocol.registerSchemesAsPrivileged([{ scheme: 'app', privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } }]);

function inside(root, candidate) {
  const relative = path.relative(path.resolve(root), path.resolve(candidate));
  return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

function readState() {
  try { return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return { version: 1, items: [] }; throw error; }
}
function saveState(value) {
  const normalized = normalizeState(value);
  fs.mkdirSync(DATA_ROOT, { recursive: true });
  const temp = `${STATE_FILE}.${process.pid}.${crypto.randomBytes(3).toString('hex')}.tmp`;
  fs.writeFileSync(temp, JSON.stringify(normalized, null, 2) + '\n', 'utf8');
  fs.renameSync(temp, STATE_FILE);
  return normalized;
}

function normalizeState(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !Array.isArray(value.items)) throw new TypeError('state.items must be an array');
  if (value.items.length > MAX_ITEMS) throw new RangeError(`state.items exceeds ${MAX_ITEMS} records`);
  const items = value.items.map((item, index) => {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new TypeError(`state.items[${index}] must be an object`);
    const id = typeof item.id === 'string' ? item.id.trim() : '';
    const text = typeof item.text === 'string' ? item.text.trim() : '';
    if (!id || id.length > 128 || !text || text.length > 10_000) throw new TypeError(`state.items[${index}] is invalid`);
    return { id, text, ...(typeof item.createdAt === 'string' && item.createdAt ? { createdAt: item.createdAt } : {}) };
  });
  if (new Set(items.map(item => item.id)).size !== items.length) throw new TypeError('state.items contains duplicate ids');
  return { version: 1, items };
}

function registerProtocol() {
  protocol.handle('app', async request => {
    const url = new URL(request.url);
    const relative = decodeURIComponent(url.pathname).replace(/^\/+/, '');
    const file = path.resolve(APP_ROOT, relative || 'src/renderer/index.html');
    if (!inside(APP_ROOT, file)) return new Response('Forbidden', { status: 403 });
    try {
      const data = await fs.promises.readFile(file);
      const ext = path.extname(file).toLowerCase();
      const type = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml' }[ext] || 'application/octet-stream';
      return new Response(data, { headers: { 'content-type': type } });
    } catch { return new Response('Not found', { status: 404 }); }
  });
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1120, height: 760, minWidth: 760, minHeight: 520,
    title: '__APP_NAME__',
    webPreferences: {
      preload: path.join(APP_ROOT, 'src/preload/preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false
    }
  });
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.loadURL('app://local/src/renderer/index.html');
  if (process.env.QAM_DEVTOOLS === '1') window.webContents.openDevTools({ mode: 'detach' });
  return window;
}

app.whenReady().then(() => {
  registerProtocol();
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  ipcMain.handle('state:load', event => { if (!event.senderFrame.url.startsWith('app://')) throw new Error('invalid sender'); return readState(); });
  ipcMain.handle('state:save', (event, value) => { if (!event.senderFrame.url.startsWith('app://')) throw new Error('invalid sender'); return saveState(value); });
  ipcMain.handle('app:info', event => { if (!event.senderFrame.url.startsWith('app://')) throw new Error('invalid sender'); return { name: '__APP_NAME__', version: app.getVersion(), dataRoot: DATA_ROOT }; });
  createWindow();
  app.on('activate', () => { if (!BrowserWindow.getAllWindows().length) createWindow(); });
});
app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
