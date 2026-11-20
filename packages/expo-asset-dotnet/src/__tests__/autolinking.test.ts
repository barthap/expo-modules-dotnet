import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

describe('dotnet autolinking metadata', () => {
  it('declares the ExpoAssetDotnet project at an existing package path', () => {
    const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
    const configPath = resolve(packageRoot, 'expo-module.config.json');

    expect(existsSync(configPath)).toBe(true);
    if (!existsSync(configPath)) {
      return;
    }

    const config = JSON.parse(readFileSync(configPath, 'utf8'));
    expect(config.platforms).toContain('dotnet');
    expect(config.dotnet.projects).toEqual([
      {
        path: 'dotnet/ExpoAssetDotnet/ExpoAssetDotnet.csproj',
        assemblyName: 'ExpoAssetDotnet',
      },
    ]);

    const projectPath = resolve(packageRoot, config.dotnet.projects[0].path);
    expect(existsSync(projectPath)).toBe(true);
  });

  it('declares that it can replace the expo-asset native-accessor file', () => {
    const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
    const config = JSON.parse(readFileSync(resolve(packageRoot, 'expo-module.config.json'), 'utf8'));

    expect(config.dotnet.substitutions).toEqual([
      {
        package: 'expo-asset',
        versionRange: '~12.0.13',
        accessor: 'build/ExpoAsset.js',
        replacement: 'src/index.ts',
      },
    ]);
    expect(existsSync(resolve(packageRoot, config.dotnet.substitutions[0].replacement))).toBe(true);
  });
});
