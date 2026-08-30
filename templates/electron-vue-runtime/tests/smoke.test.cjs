const test = require('node:test');
const assert = require('node:assert/strict');
test('template contains a secure preload contract', () => { assert.match(require('node:fs').readFileSync(require('node:path').join(__dirname, '..', 'src/main/main.cjs'), 'utf8'), /contextIsolation:\s*true/); });
