import * as fs from 'fs';

import { describe, expect, it } from 'vitest';

const { addExpoModulesDotnetPodfileHook } = require('../../plugin/ios.js');

const iosPodfile = [
  `require File.join(File.dirname(\`node --print "require.resolve('expo/package.json')"\`), "scripts/autolinking")`,
  `require File.join(File.dirname(\`node --print "require.resolve('react-native/package.json')"\`), "scripts/react_native_pods")`,
  '',
  "target 'app' do",
  '  use_expo_modules!',
  'end',
  '',
].join('\n');
const macosPodfile = fs.readFileSync(
  require.resolve('expo-desktop-template-bare-minimum/macos/Podfile'),
  'utf8'
);

describe('addExpoModulesDotnetPodfileHook', () => {
  it('wires the iOS Podfile with platform :ios', () => {
    const result = addExpoModulesDotnetPodfileHook(iosPodfile, 'ios');

    expect(result).toContain("use_expo_modules_dotnet!(platform: :ios, project_root: File.expand_path('..', __dir__))");
    expect(result).toContain("require.resolve('expo-modules-dotnet/package.json')");
  });

  it('wires the expo-desktop macOS template Podfile with platform :macos', () => {
    const result = addExpoModulesDotnetPodfileHook(macosPodfile, 'macos');

    expect(result).toContain("use_expo_modules_dotnet!(platform: :macos, project_root: File.expand_path('..', __dir__))");
    expect(result.indexOf('use_expo_modules_dotnet!')).toBeGreaterThan(result.indexOf('use_expo_modules!'));
    expect(result).not.toContain('platform: :ios');
  });

  it('is idempotent', () => {
    const once = addExpoModulesDotnetPodfileHook(macosPodfile, 'macos');

    expect(addExpoModulesDotnetPodfileHook(once, 'macos')).toBe(once);
  });

  it('names the platform Podfile when use_expo_modules! is missing', () => {
    const podfile = macosPodfile.replace('use_expo_modules!', '');

    expect(() => addExpoModulesDotnetPodfileHook(podfile, 'macos')).toThrow(/macos\/Podfile/);
  });
});
