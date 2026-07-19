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

  it('resolves React Native beside the consuming React Native Windows package', () => {
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

    expect(project).toContain(
      "<ReactNativeDir Condition=\"'$(ReactNativeDir)' == ''\">$(ReactNativeWindowsDir)..\\react-native\\</ReactNativeDir>"
    );
    expect(project).not.toContain(
      "GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory), 'node_modules\\react-native\\package.json')"
    );
  });
});
