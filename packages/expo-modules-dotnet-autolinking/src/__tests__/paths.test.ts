import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it, vi } from 'vitest';

vi.unmock('../paths');

describe('defaultGenerateOutputDir', () => {
  it('keeps generated host files under the app .expo directory', async () => {
    const { defaultGenerateOutputDir } = await import('../paths');

    expect(defaultGenerateOutputDir('/app')).toBe(path.join('/app', '.expo', 'dotnet'));
  });
});

describe('findAdapterPackageRoot', () => {
  it('resolves the adapter package from the app root', async () => {
    const { findAdapterPackageRoot } = await import('../paths');
    const appRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-adapter-app-'));
    const adapterRoot = path.join(appRoot, 'node_modules', 'expo-modules-dotnet');
    fs.mkdirSync(adapterRoot, { recursive: true });
    fs.writeFileSync(path.join(adapterRoot, 'package.json'), '{"name":"expo-modules-dotnet"}');

    expect(findAdapterPackageRoot(appRoot)).toBe(fs.realpathSync(adapterRoot));
  });
});
