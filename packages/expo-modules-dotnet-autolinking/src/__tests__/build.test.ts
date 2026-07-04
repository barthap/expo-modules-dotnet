import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

import { describe, expect, it } from 'vitest';

import {
  buildOutputDir,
  defaultConfiguration,
  defaultLoaderMode,
  defaultRid,
  dotnetArgsForBuild,
  locateAndroidNdkClangSync,
  sanitizedDotnetEnv,
} from '../build';

function makeTempRoot(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-autolinking-build-'));
}

function writeFile(filePath: string): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, '');
}

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

  it('returns mobile runtime-pack args for iOS nativeaot mode', () => {
    expect(
      dotnetArgsForBuild({
        csprojPath: 'generated/ExpoDotnetHost.csproj',
        mode: 'nativeaot',
        configuration: 'Release',
        rid: 'iossimulator-arm64',
      })
    ).toEqual([
      'publish',
      'generated/ExpoDotnetHost.csproj',
      '-c',
      'Release',
      '-r',
      'iossimulator-arm64',
      '/p:PublishAot=true',
      '/p:NativeLib=Shared',
      '/p:PublishAotUsingRuntimePack=true',
      '--self-contained',
      'true',
    ]);
  });

  it('returns android nativeaot args with injected clang path', () => {
    expect(
      dotnetArgsForBuild({
        csprojPath: 'generated/ExpoDotnetHost.csproj',
        mode: 'nativeaot',
        configuration: 'Release',
        rid: 'android-arm64',
        androidNdkClangPath: '<ndk>/toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android35-clang',
      })
    ).toEqual([
      'publish',
      'generated/ExpoDotnetHost.csproj',
      '-c',
      'Release',
      '-r',
      'android-arm64',
      '/p:PublishAot=true',
      '/p:NativeLib=Shared',
      '/p:PublishAotUsingRuntimePack=true',
      '--self-contained',
      'true',
      '/p:CppCompilerAndLinker=<ndk>/toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android35-clang',
      '/p:StripSymbols=false',
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

  it('returns mobile RIDs', () => {
    expect(defaultRid('ios', 'arm64', {})).toBe('iossimulator-arm64');
    expect(defaultRid('ios', 'arm64', { PLATFORM_NAME: 'iphoneos' })).toBe('ios-arm64');
    expect(defaultRid('android', 'arm64', {})).toBe('android-arm64');
  });
});

describe('locateAndroidNdkClangSync', () => {
  it('picks the newest NDK clang under ANDROID_HOME', () => {
    const root = makeTempRoot();
    const oldClang = path.join(
      root,
      'ndk/26.1.1/toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android34-clang'
    );
    const newClang = path.join(
      root,
      'ndk/27.0.1/toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android35-clang'
    );
    writeFile(oldClang);
    writeFile(newClang);

    expect(locateAndroidNdkClangSync({ ANDROID_HOME: root })).toBe(newClang);
  });

  it('prefers ANDROID_NDK_HOME over ANDROID_HOME', () => {
    const sdkRoot = makeTempRoot();
    const ndkRoot = makeTempRoot();
    const sdkClang = path.join(
      sdkRoot,
      'ndk/27.0.1/toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android35-clang'
    );
    const ndkHomeClang = path.join(
      ndkRoot,
      'toolchains/llvm/prebuilt/darwin-arm64/bin/aarch64-linux-android35-clang'
    );
    writeFile(sdkClang);
    writeFile(ndkHomeClang);

    expect(locateAndroidNdkClangSync({ ANDROID_HOME: sdkRoot, ANDROID_NDK_HOME: ndkRoot })).toBe(
      ndkHomeClang
    );
  });

  it('throws the documented error when no NDK is configured', () => {
    expect(() => locateAndroidNdkClangSync({})).toThrow(
      '[expo-modules-dotnet-autolinking] Android NDK not found. Set ANDROID_NDK_HOME or ANDROID_HOME.'
    );
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
