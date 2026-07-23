import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

import { afterEach, describe, expect, it } from 'vitest';

const plugin = require('../../app.plugin.js');

const temporaryDirectories: string[] = [];

function createTemporaryDirectory() {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'expo-dotnet-react-native-dir-'));
  temporaryDirectories.push(directory);
  return directory;
}

afterEach(() => {
  while (temporaryDirectories.length > 0) {
    fs.rmSync(temporaryDirectories.pop()!, { force: true, recursive: true });
  }
});

describe('Windows React Native directory config plugin helpers', () => {
  it('resolves react-native/package.json from the supplied app root', () => {
    const appRoot = path.join('fixture', 'app');
    const packageJsonPath = path.join(appRoot, 'node_modules', 'react-native', 'package.json');

    const resolved = plugin.resolveReactNativeDir(appRoot, (request: string, options: unknown) => {
      expect(request).toBe('react-native/package.json');
      expect(options).toEqual({ paths: [appRoot] });
      return packageJsonPath;
    });

    expect(resolved).toBe(path.dirname(packageJsonPath));
  });

  it('resolves independently selected package locations for separate app roots', () => {
    const desktopAppRoot = path.join('fixture', 'desktop-app');
    const mobileAppRoot = path.join('fixture', 'mobile-app');
    const selectedPackages = new Map([
      [desktopAppRoot, path.join('packages', 'react-native-0.81', 'package.json')],
      [mobileAppRoot, path.join('packages', 'react-native-0.86', 'package.json')],
    ]);
    const resolvePackage = (_request: string, options: { paths: string[] }) => {
      const packageJsonPath = selectedPackages.get(options.paths[0]);
      if (!packageJsonPath) {
        throw new Error('Cannot find module');
      }
      return packageJsonPath;
    };

    expect(plugin.resolveReactNativeDir(desktopAppRoot, resolvePackage)).toBe(
      path.join('packages', 'react-native-0.81')
    );
    expect(plugin.resolveReactNativeDir(mobileAppRoot, resolvePackage)).toBe(
      path.join('packages', 'react-native-0.86')
    );
  });

  it('fails before writing generated props when React Native cannot be resolved', () => {
    const appRoot = createTemporaryDirectory();

    expect(() =>
      plugin.writeExpoDotnetReactNativeProps(
        appRoot,
        plugin.resolveReactNativeDir(appRoot, () => {
          throw new Error('Cannot find module');
        })
      )
    ).toThrow(/react-native.*app root/i);

    expect(fs.existsSync(path.join(appRoot, '.expo'))).toBe(false);
  });

  it('adds one generated-props import without replacing existing directory props', () => {
    const existing = '<Project>\n  <PropertyGroup><Custom>true</Custom></PropertyGroup>\n</Project>\n';

    const once = plugin.mergeExpoDotnetReactNativePropsImport(existing);

    expect(once).toContain('<Custom>true</Custom>');
    expect(once.match(/expo-modules-dotnet-react-native-dir/g)).toHaveLength(1);
    expect(plugin.mergeExpoDotnetReactNativePropsImport(once)).toBe(once);
  });

  it('writes app-local props that preserve an explicit ReactNativeDir override', () => {
    const appRoot = createTemporaryDirectory();
    const reactNativeDir = path.join(appRoot, 'node_modules', 'react-native');

    const propsPath = plugin.writeExpoDotnetReactNativeProps(appRoot, reactNativeDir);
    const props = fs.readFileSync(propsPath, 'utf8');

    expect(propsPath).toBe(
      path.join(appRoot, '.expo', 'dotnet', 'windows', 'ExpoDotnetReactNativeDir.props')
    );
    expect(props).toContain('<ExpoDotnetReactNativeDir>');
    expect(props).toContain("<ReactNativeDir Condition=\"'$(ReactNativeDir)' == ''\">");
  });

  it('writes generated props and merges one import into existing directory props', () => {
    const appRoot = createTemporaryDirectory();
    const directoryBuildPropsPath = path.join(appRoot, 'Directory.Build.props');
    const packageJsonPath = path.join(appRoot, 'node_modules', 'react-native', 'package.json');
    fs.writeFileSync(
      directoryBuildPropsPath,
      '<Project>\n  <PropertyGroup><Custom>true</Custom></PropertyGroup>\n</Project>\n'
    );

    plugin.configureWindowsReactNativeDir(appRoot, () => packageJsonPath);
    plugin.configureWindowsReactNativeDir(appRoot, () => packageJsonPath);

    const directoryBuildProps = fs.readFileSync(directoryBuildPropsPath, 'utf8');
    expect(directoryBuildProps).toContain('<Custom>true</Custom>');
    expect(directoryBuildProps.match(/expo-modules-dotnet-react-native-dir/g)).toHaveLength(1);
    expect(
      fs.existsSync(
        path.join(appRoot, '.expo', 'dotnet', 'windows', 'ExpoDotnetReactNativeDir.props')
      )
    ).toBe(true);
  });

  it('does not create directory props when app-root package resolution fails', () => {
    const appRoot = createTemporaryDirectory();

    expect(() =>
      plugin.configureWindowsReactNativeDir(appRoot, () => {
        throw new Error('Cannot find module');
      })
    ).toThrow(/react-native.*app root/i);

    expect(fs.existsSync(path.join(appRoot, 'Directory.Build.props'))).toBe(false);
    expect(fs.existsSync(path.join(appRoot, '.expo'))).toBe(false);
  });

  it('does not create generated props when existing directory props are invalid', () => {
    const appRoot = createTemporaryDirectory();
    const packageJsonPath = path.join(appRoot, 'node_modules', 'react-native', 'package.json');
    fs.writeFileSync(path.join(appRoot, 'Directory.Build.props'), '<Project>');

    expect(() => plugin.configureWindowsReactNativeDir(appRoot, () => packageJsonPath)).toThrow(
      /closing <\/Project>/i
    );

    expect(fs.existsSync(path.join(appRoot, '.expo'))).toBe(false);
  });
});
