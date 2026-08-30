import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { assertWithin, writeAtomic } from '@quick-app/core';
import { validateDesired } from '@quick-app/store-core';

export function readManifestFromPackage(packagePath) {
  const commands = process.platform === 'win32' ? [['tar.exe', ['-xOf', packagePath, 'AppxManifest.xml']], ['powershell.exe', ['-NoProfile', '-Command', `tar -xOf '${packagePath.replaceAll("'", "''")}' AppxManifest.xml`]]] : [['unzip', ['-p', packagePath, 'AppxManifest.xml']], ['tar', ['-xOf', packagePath, 'AppxManifest.xml']]];
  let last;
  for (const [command, args] of commands) {
    try { return execFileSync(command, args, { encoding: 'utf8', maxBuffer: 4 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] }); }
    catch (error) { last = error; }
  }
  throw new Error(`cannot read AppxManifest.xml from ${packagePath}: ${last?.message ?? 'archive tool missing'}`);
}

export function manifestValue(xml, name) {
  const identity = xml.match(/<Identity\b[^>]*>/i)?.[0] ?? '';
  const application = xml.match(/<Application\b[^>]*>/i)?.[0] ?? '';
  const props = xml.match(/<Properties\b[\s\S]*?<\/Properties>/i)?.[0] ?? '';
  if (name === 'identityName') return attr(identity, 'Name');
  if (name === 'publisher') return attr(identity, 'Publisher');
  if (name === 'version') return attr(identity, 'Version');
  if (name === 'executable') return attr(application, 'Executable');
  if (name === 'displayName') return text(props, 'DisplayName') || attr(application, 'DisplayName');
  if (name === 'publisherDisplayName') return text(props, 'PublisherDisplayName');
  if (name === 'runFullTrust') return /runFullTrust/i.test(xml);
  if (name === 'desktop') return !/Name\s*=\s*["']Windows\.Universal["']/i.test(xml) && (/windowsApp|packagedClassicApp|win32App/i.test(xml) || /EntryPoint\s*=\s*["']Windows\.FullTrustApplication["']/i.test(xml));
  if (name === 'resourceLanguages') return [...xml.matchAll(/<Resource\b[^>]*Language\s*=\s*["']([^"']+)["']/gi)].map(match => match[1]);
  return '';
}
function attr(tag, name) { return tag.match(new RegExp(`${name}\\s*=\\s*["']([^"']*)["']`, 'i'))?.[1] ?? ''; }
function text(xml, name) { return xml.match(new RegExp(`<${name}\\b[^>]*>([\\s\\S]*?)<\\/${name}>`, 'i'))?.[1]?.trim() ?? ''; }

export function pngSize(filePath) {
  const buffer = fs.readFileSync(filePath);
  if (buffer.length < 24 || buffer.readUInt32BE(0) !== 0x89504e47 || buffer.readUInt32BE(4) !== 0x0d0a1a0a) throw new Error(`invalid PNG: ${filePath}`);
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

export function runPreflight({ workspace, appRoot, desired, outputPath = null }) {
  const errors = [...validateDesired(desired, { strict: true, checkAge: false })];
  const packagePath = desired.package?.path ? assertWithin(appRoot, path.resolve(appRoot, desired.package.path), 'package') : '';
  if (!packagePath || !fs.existsSync(packagePath)) errors.push(`package.path does not exist: ${packagePath || '(empty)'}`);
  let manifest = '';
  const values = {};
  if (packagePath && fs.existsSync(packagePath)) {
    try {
      manifest = readManifestFromPackage(packagePath);
      for (const key of ['identityName', 'publisher', 'version', 'executable', 'displayName', 'publisherDisplayName', 'runFullTrust', 'desktop', 'resourceLanguages']) values[key] = manifestValue(manifest, key);
      if (!values.displayName) errors.push('MSIX DisplayName is missing');
      if (desired.productName && desired.productName !== 'PENDING' && values.displayName !== desired.productName) errors.push(`DisplayName mismatch: manifest=${values.displayName}, desired=${desired.productName}`);
      if (desired.package?.identityName && desired.package.identityName !== 'PENDING.IDENTITY' && values.identityName !== desired.package.identityName) errors.push(`Identity.Name mismatch: manifest=${values.identityName}, desired=${desired.package.identityName}`);
      if (desired.package?.publisher && desired.package.publisher !== 'CN=PENDING' && values.publisher !== desired.package.publisher) errors.push(`Publisher mismatch: manifest=${values.publisher}, desired=${desired.package.publisher}`);
      if (desired.package?.publisherDisplayName && values.publisherDisplayName && values.publisherDisplayName !== desired.package.publisherDisplayName) errors.push(`PublisherDisplayName mismatch: manifest=${values.publisherDisplayName}, desired=${desired.package.publisherDisplayName}`);
      if (!values.desktop) errors.push('package is not a Windows desktop application');
      if (desired.package?.executable && values.executable && path.basename(values.executable).toLowerCase() !== path.basename(desired.package.executable).toLowerCase()) errors.push(`Executable mismatch: manifest=${values.executable}, desired=${desired.package.executable}`);
      if (values.resourceLanguages.length && desired.site?.languageCode && !values.resourceLanguages.some(x => x.toLowerCase() === desired.site.languageCode.toLowerCase())) errors.push(`manifest lacks resource language ${desired.site.languageCode}`);
    } catch (error) { errors.push(error.message); }
  }
  if (desired.listing?.screenshot && desired.assets?.screenshot) checkImage(desired.assets.screenshot, 'screenshot', errors, appRoot);
  for (const [name, item] of Object.entries(desired.listing?.assets ?? {})) if (item?.enabled && item.path) checkImage(item.path, name, errors, appRoot);
  const result = { ok: errors.length === 0, errors, packagePath, manifest: values, checkedAt: new Date().toISOString() };
  if (outputPath) writeAtomic(workspace, outputPath, result);
  if (errors.length) { const error = new Error(`preflight failed with ${errors.length} issue(s)`); error.result = result; throw error; }
  return result;
}

function checkImage(input, label, errors, appRoot) {
  const file = path.resolve(appRoot, input);
  try { assertWithin(appRoot, file, label); if (!fs.existsSync(file)) throw new Error(`missing asset: ${file}`); const size = pngSize(file); if (size.width < 1 || size.height < 1) throw new Error(`empty asset: ${file}`); }
  catch (error) { errors.push(error.message); }
}
