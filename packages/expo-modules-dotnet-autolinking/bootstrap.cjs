'use strict';

const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');

const packageRoot = __dirname;
const buildEntryPath = path.join(packageRoot, 'build', 'index.js');

function fileMtimeMs(filePath) {
  try {
    return fs.statSync(filePath).mtimeMs;
  } catch {
    return 0;
  }
}

function newestMtimeMs(root) {
  if (!fs.existsSync(root)) {
    return 0;
  }

  let newest = fileMtimeMs(root);
  const entries = fs.readdirSync(root, { withFileTypes: true });
  for (const entry of entries) {
    const entryPath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      newest = Math.max(newest, newestMtimeMs(entryPath));
    } else if (entry.isFile()) {
      newest = Math.max(newest, fileMtimeMs(entryPath));
    }
  }
  return newest;
}

function shouldBuildPackage(root = packageRoot) {
  const sourceRoot = path.join(root, 'src');
  const tsconfigPath = path.join(root, 'tsconfig.json');
  const entryPath = path.join(root, 'build', 'index.js');

  if (!fs.existsSync(sourceRoot) || !fs.existsSync(tsconfigPath)) {
    return false;
  }
  if (!fs.existsSync(entryPath)) {
    return true;
  }

  const sourceMtime = Math.max(
    newestMtimeMs(sourceRoot),
    fileMtimeMs(tsconfigPath),
    fileMtimeMs(path.join(root, 'package.json'))
  );
  return sourceMtime > fileMtimeMs(entryPath);
}

function buildPackage(root = packageRoot) {
  console.log('[expo-modules-dotnet-autolinking] Refreshing local build output.');
  const command = process.platform === 'win32' ? process.env.ComSpec || 'cmd.exe' : 'pnpm';
  const args =
    process.platform === 'win32' ? ['/d', '/s', '/c', 'pnpm', 'run', 'build'] : ['run', 'build'];
  const result = childProcess.spawnSync(command, args, {
    cwd: root,
    stdio: 'inherit',
  });

  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`expo-modules-dotnet-autolinking build failed with exit code ${result.status}.`);
  }
}

function loadCompiledPackage() {
  if (shouldBuildPackage()) {
    buildPackage();
  }
  return require(buildEntryPath);
}

const compiledPackage = loadCompiledPackage();

module.exports = {
  ...compiledPackage,
  fileMtimeMs,
  newestMtimeMs,
  shouldBuildPackage,
  buildPackage,
  loadCompiledPackage,
};
