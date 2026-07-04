import * as path from 'path';

import { describe, expect, it } from 'vitest';

import {
  buildOutputDir,
  defaultConfiguration,
  defaultLoaderMode,
  defaultRid,
  dotnetArgsForBuild,
  sanitizedDotnetEnv,
} from '../build';

describe('dotnetArgsForBuild', () => {
  it('returns dotnet build args for hostfxr mode', () => {
    expect(
      dotnetArgsForBuild({
        csprojPath: 'generated/ExpoDotnetHost.csproj',
        mode: 'hostfxr',
        configuration: 'Debug',
      })
    ).toEqual(['build', 'generated/ExpoDotnetHost.csproj', '-c', 'Debug']);
  });

  it('returns dotnet publish args for nativeaot mode', () => {
    expect(
      dotnetArgsForBuild({
        csprojPath: 'generated/ExpoDotnetHost.csproj',
        mode: 'nativeaot',
        configuration: 'Release',
        rid: 'osx-arm64',
      })
    ).toEqual([
      'publish',
      'generated/ExpoDotnetHost.csproj',
      '-c',
      'Release',
      '-r',
      'osx-arm64',
      '/p:PublishAot=true',
      '/p:NativeLib=Shared',
    ]);
  });

  it('throws when nativeaot mode has no RID', () => {
    expect(() =>
      dotnetArgsForBuild({
        csprojPath: 'generated/ExpoDotnetHost.csproj',
        mode: 'nativeaot',
        configuration: 'Release',
      })
    ).toThrow(/rid/i);
  });
});

describe('defaultLoaderMode', () => {
  it.each([
    ['macos', 'hostfxr'],
    ['windows', 'hostfxr'],
    ['ios', 'nativeaot'],
    ['android', 'nativeaot'],
  ] as const)('returns %s default mode', (platform, expectedMode) => {
    expect(defaultLoaderMode(platform)).toBe(expectedMode);
  });
});

describe('defaultRid', () => {
  it.each([
    ['macos', 'arm64', 'osx-arm64'],
    ['macos', 'x64', 'osx-x64'],
    ['windows', 'arm64', 'win-arm64'],
    ['windows', 'x64', 'win-x64'],
  ])('returns %s %s RID', (platform, arch, expectedRid) => {
    expect(defaultRid(platform, arch)).toBe(expectedRid);
  });
});

describe('defaultConfiguration', () => {
  it('defaults hostfxr builds to Debug', () => {
    expect(defaultConfiguration('hostfxr')).toBe('Debug');
  });

  it('defaults nativeaot builds to Release', () => {
    expect(defaultConfiguration('nativeaot')).toBe('Release');
  });
});

describe('sanitizedDotnetEnv', () => {
  it('strips Xcode variables that break dotnet and keeps unrelated variables', () => {
    const env = {
      ACTION: 'build',
      ARCHS: 'arm64',
      CURRENT_ARCH: 'arm64',
      PLATFORM_NAME: 'macosx',
      PRODUCT_NAME: 'App',
      PROJECT_NAME: 'App',
      TARGET_NAME: 'App',
      TARGETNAME: 'App',
      PATH: '/usr/bin',
    };

    expect(sanitizedDotnetEnv(env)).toEqual({ PATH: '/usr/bin' });
  });
});

describe('buildOutputDir', () => {
  it('returns the hostfxr build output directory', () => {
    const csprojPath = path.join('generated', 'ExpoDotnetHost.csproj');

    expect(
      buildOutputDir({
        csprojPath,
        mode: 'hostfxr',
        configuration: 'Debug',
      })
    ).toBe(path.join('generated', 'bin', 'Debug', 'net10.0'));
  });

  it('returns the nativeaot publish output directory', () => {
    const csprojPath = path.join('generated', 'ExpoDotnetHost.csproj');

    expect(
      buildOutputDir({
        csprojPath,
        mode: 'nativeaot',
        configuration: 'Release',
        rid: 'osx-arm64',
      })
    ).toBe(path.join('generated', 'bin', 'Release', 'net10.0', 'osx-arm64', 'publish'));
  });
});
