import * as fs from 'fs';
import * as path from 'path';

import { describe, expect, it } from 'vitest';

describe('Windows MSBuild autolinking target', () => {
  it('runs before PrepareForBuild so loader mode switches refresh managed artifacts', () => {
    const targetPath = path.resolve(
      __dirname,
      '..',
      '..',
      '..',
      'expo-modules-dotnet',
      'windows',
      'ExpoDotnetAutolink.targets'
    );
    const target = fs.readFileSync(targetPath, 'utf8');

    expect(target).toContain('<Target Name="ExpoDotnetLink" BeforeTargets="PrepareForBuild">');
  });

  it('leaves React Native core resolution to RNW property sheets', () => {
    const projectPath = path.resolve(
      __dirname,
      '..',
      '..',
      '..',
      'expo-modules-dotnet',
      'windows',
      'ExpoModulesDotnet',
      'ExpoModulesDotnet.vcxproj'
    );
    const project = fs.readFileSync(projectPath, 'utf8');

    expect(project).not.toContain('<ReactNativeDir ');
    expect(project).not.toContain('$(ReactNativeDir)\\ReactCommon');
  });

  it('uses the selected JSI declaration for ArrayBuffer capabilities', () => {
    const bridgePath = path.resolve(
      __dirname,
      '..',
      '..',
      '..',
      'expo-modules-dotnet',
      'native',
      'packages',
      'jsi',
      'src',
      'ExpoJsiBridge.cpp'
    );
    const capabilitiesPath = path.resolve(
      __dirname,
      '..',
      '..',
      '..',
      'expo-modules-dotnet',
      'native',
      'packages',
      'jsi',
      'src',
      'ArrayBufferCapabilities.h'
    );
    const testhostCmakePath = path.resolve(
      __dirname,
      '..',
      '..',
      '..',
      'expo-modules-dotnet',
      'native',
      'testhost',
      'CMakeLists.txt'
    );
    const bridge = fs.readFileSync(bridgePath, 'utf8');
    const capabilities = fs.readFileSync(capabilitiesPath, 'utf8');
    const testhostCmake = fs.readFileSync(testhostCmakePath, 'utf8');

    expect(capabilities).toContain('requires(');
    expect(bridge).not.toContain('ReactNativeVersion.h');
    expect(bridge).not.toContain('REACT_NATIVE_VERSION_');
    expect(capabilities).not.toContain('REACT_NATIVE_VERSION_');
    expect(capabilities).not.toContain('EXPO_DOTNET_HAS_ARRAY_BUFFER_INTROSPECTION');
    expect(testhostCmake).not.toContain('REACT_NATIVE_VERSION_');
  });
});
