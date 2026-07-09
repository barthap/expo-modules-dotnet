import * as fs from 'fs/promises';
import * as os from 'os';
import * as path from 'path';
import { createRequire } from 'module';

import { afterEach, describe, expect, it } from 'vitest';

const require = createRequire(import.meta.url);
const packageRoot = path.resolve(process.cwd());
const bootstrapPath = path.join(packageRoot, 'bootstrap.cjs');
const tempRoots: string[] = [];

interface BootstrapModule {
  shouldBuildPackage(packageRoot: string): boolean;
}

async function makeTempRootAsync(): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'expo-dotnet-autolinking-bootstrap-'));
  tempRoots.push(root);
  return root;
}

async function writeFileAsync(filePath: string, content = ''): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, content);
}

async function setMtimeAsync(filePath: string, timestampMs: number): Promise<void> {
  const date = new Date(timestampMs);
  await fs.utimes(filePath, date, date);
}

function loadBootstrap(): BootstrapModule {
  delete require.cache[require.resolve(bootstrapPath)];
  return require(bootstrapPath) as BootstrapModule;
}

afterEach(async () => {
  await Promise.all(tempRoots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

describe('expo-modules-dotnet-autolinking bootstrap', () => {
  it('is the package main entry point so all require callers get the same dev guardrail', async () => {
    const packageJson = JSON.parse(await fs.readFile(path.join(packageRoot, 'package.json'), 'utf8'));

    expect(packageJson.main).toBe('bootstrap.cjs');
    expect(packageJson.files).toContain('bootstrap.cjs');
  });

  it('rebuilds a workspace package when source is newer than build output', async () => {
    const bootstrap = loadBootstrap();
    const root = await makeTempRootAsync();
    const now = Date.now();

    await writeFileAsync(path.join(root, 'package.json'), '{"scripts":{"build":"tsc"}}');
    await writeFileAsync(path.join(root, 'tsconfig.json'), '{}');
    await writeFileAsync(path.join(root, 'src', 'index.ts'), 'export {};');
    await writeFileAsync(path.join(root, 'build', 'index.js'), 'module.exports = {};');
    await setMtimeAsync(path.join(root, 'build', 'index.js'), now - 20_000);
    await setMtimeAsync(path.join(root, 'src', 'index.ts'), now);

    expect(bootstrap.shouldBuildPackage(root)).toBe(true);
  });

  it('rebuilds a workspace package when build output is missing', async () => {
    const bootstrap = loadBootstrap();
    const root = await makeTempRootAsync();

    await writeFileAsync(path.join(root, 'package.json'), '{"scripts":{"build":"tsc"}}');
    await writeFileAsync(path.join(root, 'tsconfig.json'), '{}');
    await writeFileAsync(path.join(root, 'src', 'index.ts'), 'export {};');

    expect(bootstrap.shouldBuildPackage(root)).toBe(true);
  });

  it('does not rebuild a published package without TypeScript source', async () => {
    const bootstrap = loadBootstrap();
    const root = await makeTempRootAsync();

    await writeFileAsync(path.join(root, 'package.json'), '{}');
    await writeFileAsync(path.join(root, 'build', 'index.js'), 'module.exports = {};');

    expect(bootstrap.shouldBuildPackage(root)).toBe(false);
  });
});
