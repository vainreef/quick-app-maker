import test from 'node:test';
import assert from 'node:assert/strict';
import { manifestValue, pngSize } from './src/index.mjs';
import { solidPng } from '../generator-electron/src/png.mjs';
import fs from 'node:fs';
import path from 'node:path';
const xml = `<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="Acme.App" Publisher="CN=Acme" Version="1.2.3.4"/><Properties><DisplayName>Demo</DisplayName><PublisherDisplayName>Acme</PublisherDisplayName></Properties><Resources><Resource Language="zh-CN"/></Resources><Applications><Application Executable="demo.exe" EntryPoint="Windows.FullTrustApplication"/></Applications><Capabilities><rescap:Capability Name="runFullTrust"/></Capabilities></Package>`;
test('manifest values are read by tag, not guessed from page text', () => { assert.equal(manifestValue(xml, 'identityName'), 'Acme.App'); assert.equal(manifestValue(xml, 'publisher'), 'CN=Acme'); assert.equal(manifestValue(xml, 'displayName'), 'Demo'); assert.equal(manifestValue(xml, 'executable'), 'demo.exe'); assert.equal(manifestValue(xml, 'runFullTrust'), true); assert.deepEqual(manifestValue(xml, 'resourceLanguages'), ['zh-CN']); });
test('PNG assets are checked without an image runtime', () => { const file = path.resolve('.cache', `preflight-${process.pid}.png`); fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, solidPng(12, 8)); assert.deepEqual(pngSize(file), { width: 12, height: 8 }); });
