import { spawn } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

import type { DotnetLinkingManifest } from './types';

/** The staged file name that upstream expo-constants also uses. */
export const appConfigBasename = 'app.config';

/**
 * The app config to stage for a platform: computed for windows and macos when a
 * resolved module declares it, otherwise undefined (no computation, no file).
 */
export async function appConfigForPlatformAsync(
  platform: string,
  projectRoot: string,
  manifest: DotnetLinkingManifest
): Promise<string | undefined> {
  if ((platform !== 'windows' && platform !== 'macos') || !manifestNeedsAppConfig(manifest)) {
    return undefined;
  }
  return computeAppConfigAsync(projectRoot);
}

/** True when a resolved module declares that it reads the app config. */
export function manifestNeedsAppConfig(manifest: DotnetLinkingManifest): boolean {
  return manifest.modules.some((module) => module.appConfig);
}

function autolinkingError(message: string): Error {
  return new Error(`[expo-modules-dotnet-autolinking] ${message}`);
}

// Finds a package the way Node does from a directory, in each enclosing
// node_modules folder, but without NODE_PATH or the global folders: pnpm points
// NODE_PATH at its hoisted store, which can hold another SDK's copy.
function findPackageRoot(packageName: string, fromDirectory: string): string | null {
  let directory = path.resolve(fromDirectory);
  for (;;) {
    if (path.basename(directory) !== 'node_modules') {
      const candidate = path.join(directory, 'node_modules', ...packageName.split('/'));
      if (fs.existsSync(path.join(candidate, 'package.json'))) {
        return fs.realpathSync(candidate);
      }
    }
    const parent = path.dirname(directory);
    if (parent === directory) {
      return null;
    }
    directory = parent;
  }
}

// Runs in a child process so env loading and the working-directory change stay
// out of the CLI. It writes the result to a file, because @expo/env and the app
// config may print to stdout.
const childScript = `
const fs = require('fs');
const [configRoot, envRoot, projectRoot, outputFile] = process.argv.slice(1);
require(envRoot).load(projectRoot);
process.chdir(projectRoot);
const { exp } = require(configRoot).getConfig(projectRoot, {
  isPublicConfig: true,
  skipSDKVersionRequirement: true,
});
for (const platform of ['macos', 'windows']) {
  if (exp[platform] && typeof exp[platform] === 'object') {
    delete exp[platform].config;
  }
}
fs.writeFileSync(outputFile, JSON.stringify(exp));
`;

/**
 * Computes the project's public Expo config as upstream expo-constants does,
 * with the @expo/config and @expo/env that the app's expo-constants uses.
 *
 * @returns The serialized config, a JSON object.
 */
// @ref docs/specs/dotnet-autolinking.md "Staging Writes The App Config For Modules That Declare It"
export async function computeAppConfigAsync(projectRoot: string): Promise<string> {
  const constantsRoot = findPackageRoot('expo-constants', projectRoot);
  if (constantsRoot === null) {
    throw autolinkingError(
      `A dotnet module reads the app config, but expo-constants does not resolve from "${projectRoot}". ` +
        "Add expo-constants to the app's dependencies."
    );
  }
  // Under pnpm, expo-constants' own dependencies are siblings of its real folder
  // in the store, not of the app's link to it, so the walk starts from the realpath.
  const packageRoots = ['@expo/config', '@expo/env'].map((name) => {
    const root = findPackageRoot(name, constantsRoot);
    if (root === null) {
      throw autolinkingError(`${name} does not resolve from expo-constants at "${constantsRoot}".`);
    }
    return root;
  });

  const outputDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'expo-dotnet-app-config-'));
  const outputFile = path.join(outputDirectory, appConfigBasename);
  try {
    await runChildAsync([...packageRoots, projectRoot, outputFile], projectRoot);
    const content = fs.readFileSync(outputFile, 'utf8');
    const parsed: unknown = JSON.parse(content);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      throw autolinkingError('The computed app config is not a JSON object.');
    }
    return content;
  } finally {
    fs.rmSync(outputDirectory, { recursive: true, force: true });
  }
}

function runChildAsync(args: string[], cwd: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['-e', childScript, ...args], {
      cwd,
      stdio: ['ignore', 'ignore', 'pipe'],
    });
    let stderr = '';
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk: string) => {
      stderr += chunk;
    });
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) {
        resolve();
        return;
      }
      reject(autolinkingError(`Computing the app config failed (exit code ${code}):\n${stderr.trim()}`));
    });
  });
}
