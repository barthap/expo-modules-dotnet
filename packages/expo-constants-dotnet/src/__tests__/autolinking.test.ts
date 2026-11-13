import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

describe('dotnet autolinking metadata', () => {
  it('declares the ExpoConstantsDotnet project at an existing package path', () => {
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
        path: 'dotnet/ExpoConstantsDotnet/ExpoConstantsDotnet.csproj',
        assemblyName: 'ExpoConstantsDotnet',
      },
    ]);

    const projectPath = resolve(packageRoot, config.dotnet.projects[0].path);
    expect(existsSync(projectPath)).toBe(true);
  });

  it('declares that it reads the app config', () => {
    const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
    const config = JSON.parse(readFileSync(resolve(packageRoot, 'expo-module.config.json'), 'utf8'));

    expect(config.dotnet.appConfig).toBe(true);
  });

  it('declares that it can replace the expo-constants native-accessor file', () => {
    const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
    const config = JSON.parse(readFileSync(resolve(packageRoot, 'expo-module.config.json'), 'utf8'));

    expect(config.dotnet.substitutions).toEqual([
      {
        package: 'expo-constants',
        versionRange: '~18.0.13',
        accessor: 'build/ExponentConstants.js',
        replacement: 'src/index.ts',
      },
    ]);
    expect(existsSync(resolve(packageRoot, config.dotnet.substitutions[0].replacement))).toBe(true);
  });
});
