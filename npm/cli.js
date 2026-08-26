#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const https = require('https');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const PACKAGE_VERSION = '1.2.0';
const ASSET_NAME = 'serial-loopback-test-v1.2.0.exe';
const DOWNLOAD_URL = 'https://github.com/user-zhaoqingsong/serial-loopback-test/releases/download/v1.2.0/' + ASSET_NAME;
const EXPECTED_SHA256 = '30fafa08d2e6389b4328782e333a045e10f91c49e4bf6b926725bf13d8270c52';
const args = process.argv.slice(2);

function printHelp() {
  console.log([
    'Serial Loopback Test v' + PACKAGE_VERSION,
    '',
    'Usage:',
    '  npx serial-loopback-test',
    '',
    'Options:',
    '  --download-only  Download and verify the EXE without launching it',
    '  --print-path     Print the cached EXE path without launching it',
    '  --version        Print the package version',
    '  --help           Show this help'
  ].join('\n'));
}

function hashFile(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('error', reject);
    stream.on('data', chunk => hash.update(chunk));
    stream.on('end', () => resolve(hash.digest('hex')));
  });
}

function download(url, destination, redirectsLeft) {
  return new Promise((resolve, reject) => {
    const request = https.get(url, {
      headers: { 'User-Agent': 'serial-loopback-test-npm/' + PACKAGE_VERSION }
    }, response => {
      if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
        response.resume();
        if (redirectsLeft <= 0) {
          reject(new Error('Too many redirects while downloading the EXE.'));
          return;
        }
        download(new URL(response.headers.location, url).toString(), destination, redirectsLeft - 1)
          .then(resolve, reject);
        return;
      }

      if (response.statusCode !== 200) {
        response.resume();
        reject(new Error('Download failed with HTTP status ' + response.statusCode + '.'));
        return;
      }

      const output = fs.createWriteStream(destination, { flags: 'wx' });
      output.on('error', reject);
      response.on('error', reject);
      output.on('finish', () => output.close(resolve));
      response.pipe(output);
    });
    request.on('error', reject);
  });
}

async function ensureExecutable() {
  const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  const installDirectory = process.env.SERIAL_LOOPBACK_TEST_HOME ||
    path.join(localAppData, 'SerialLoopbackTest');
  const executablePath = path.join(installDirectory, ASSET_NAME);

  await fs.promises.mkdir(installDirectory, { recursive: true });

  if (fs.existsSync(executablePath)) {
    const cachedHash = await hashFile(executablePath);
    if (cachedHash.toLowerCase() === EXPECTED_SHA256) {
      return executablePath;
    }
    console.warn('Cached EXE checksum mismatch; downloading a verified copy.');
    await fs.promises.unlink(executablePath);
  }

  const temporaryPath = executablePath + '.download-' + process.pid;
  try {
    console.log('Downloading Serial Loopback Test v' + PACKAGE_VERSION + '...');
    await download(DOWNLOAD_URL, temporaryPath, 8);
    const downloadedHash = await hashFile(temporaryPath);
    if (downloadedHash.toLowerCase() !== EXPECTED_SHA256) {
      throw new Error('SHA-256 verification failed. Expected ' + EXPECTED_SHA256 +
        ', received ' + downloadedHash + '.');
    }
    await fs.promises.rename(temporaryPath, executablePath);
    console.log('Verified SHA-256: ' + downloadedHash);
    return executablePath;
  } catch (error) {
    if (fs.existsSync(temporaryPath)) {
      await fs.promises.unlink(temporaryPath).catch(() => {});
    }
    throw error;
  }
}

async function main() {
  if (args.includes('--help') || args.includes('-h')) {
    printHelp();
    return;
  }
  if (args.includes('--version') || args.includes('-v')) {
    console.log(PACKAGE_VERSION);
    return;
  }
  if (process.platform !== 'win32') {
    throw new Error('This package only supports Windows.');
  }

  const executablePath = await ensureExecutable();
  if (args.includes('--print-path')) {
    console.log(executablePath);
    return;
  }
  if (args.includes('--download-only')) {
    console.log('Ready: ' + executablePath);
    return;
  }

  const child = spawn(executablePath, [], {
    detached: true,
    stdio: 'ignore',
    windowsHide: false
  });
  child.unref();
  console.log('Serial Loopback Test started.');
}

main().catch(error => {
  console.error('serial-loopback-test: ' + error.message);
  process.exitCode = 1;
});
