import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

/**
 * Cross-platform helper to open or reveal a file/directory in the native file manager (Finder on macOS, Explorer on Windows, xdg-open on Linux).
 * @param {string} targetPath - Absolute or relative path to file or directory
 * @param {object} options
 * @param {boolean} [options.select=true] - Whether to highlight/select the specific file
 * @param {boolean} [options.dryRun=false] - Whether to skip spawning the native process
 * @returns {Promise<{ ok: boolean, platform: string, command: string, args: string[] }>}
 */
export async function openFileManager(targetPath, { select = true, dryRun = false } = {}) {
  const absolutePath = path.resolve(targetPath);
  if (!fs.existsSync(absolutePath)) {
    throw new Error(`Target path does not exist: ${absolutePath}`);
  }

  const platform = process.platform;
  let command = '';
  let args = [];

  if (platform === 'darwin') {
    command = 'open';
    const isDir = fs.statSync(absolutePath).isDirectory();
    if (select && !isDir) {
      args = ['-R', absolutePath];
    } else {
      args = [absolutePath];
    }
  } else if (platform === 'win32') {
    command = 'explorer.exe';
    const isDir = fs.statSync(absolutePath).isDirectory();
    if (select && !isDir) {
      args = [`/select,${absolutePath}`];
    } else {
      args = [absolutePath];
    }
  } else {
    command = 'xdg-open';
    const isDir = fs.statSync(absolutePath).isDirectory();
    args = [isDir ? absolutePath : path.dirname(absolutePath)];
  }

  if (dryRun) {
    return { ok: true, platform, command, args };
  }

  return new Promise((resolve, reject) => {
    try {
      const child = spawn(command, args, {
        detached: true,
        stdio: 'ignore',
        windowsHide: false
      });
      child.unref();
      child.once('error', reject);
      resolve({ ok: true, platform, command, args });
    } catch (error) {
      reject(error);
    }
  });
}

export { openFileManager as revealInFileManager };
