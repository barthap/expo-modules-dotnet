import * as fs from 'fs';
import * as path from 'path';

import { afterEach, describe, expect, it } from 'vitest';

import { createWindowsTestApp } from './windowsTestApp';

const { getPrebuildConfigAsync } = require('expo-desktop-prebuild-config');
const { compileModsAsync } = require('expo-desktop-config-plugins');

const templateMacosDir = path.join(
  path.dirname(require.resolve('expo-desktop-template-bare-minimum/package.json')),
  'macos'
);
const packageRoot = path.resolve(__dirname, '..', '..');
const platforms = ['macos', 'windows'];
const appRoots: string[] = [];

// expo-desktop prebuild renames the template's HelloWorld to the sanitized app
// name before it runs mods; "Desktop App" becomes "DesktopApp".
function copyRenamed(source: string, destination: string) {
  fs.mkdirSync(destination, { recursive: true });
  for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
    const from = path.join(source, entry.name);
    const to = path.join(destination, entry.name.replace(/HelloWorld/g, 'DesktopApp'));
    if (entry.isSymbolicLink()) {
      continue;
    } else if (entry.isDirectory()) {
      copyRenamed(from, to);
    } else if (/\.(pbxproj|plist|xcscheme|entitlements|swift|h|m|mm)$|^Podfile$/.test(entry.name)) {
      fs.writeFileSync(to, fs.readFileSync(from, 'utf8').replace(/HelloWorld/g, 'DesktopApp'));
    } else {
      fs.copyFileSync(from, to);
    }
  }
}

function createDesktopApp() {
  const appRoot = createWindowsTestApp();
  appRoots.push(appRoot);
  copyRenamed(templateMacosDir, path.join(appRoot, 'macos'));
  fs.writeFileSync(path.join(appRoot, 'package.json'), '{"name":"desktop-app","version":"1.0.0"}');
  fs.writeFileSync(
    path.join(appRoot, 'app.json'),
    JSON.stringify({
      expo: {
        name: 'Desktop App',
        slug: 'desktop-app',
        macos: { bundleIdentifier: 'dev.expo.modules.dotnet.desktopapp' },
        windows: {
          namespace: 'DesktopApp',
          projectGuid: 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB',
          packageGuid: 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        },
        plugins: ['expo-modules-dotnet'],
      },
    })
  );
  fs.symlinkSync(packageRoot, path.join(appRoot, 'node_modules', 'expo-modules-dotnet'), 'junction');
  return appRoot;
}

async function prebuildAsync(appRoot: string) {
  const { exp } = await getPrebuildConfigAsync(appRoot, {
    platforms,
    bundleIdentifier: 'dev.expo.modules.dotnet.desktopapp',
    bundleIdentifierMacos: 'dev.expo.modules.dotnet.desktopapp',
    displayName: 'Desktop App',
    filesafeName: 'Desktop App',
    windowsNamespace: 'DesktopApp',
    windowsPackageGuid: 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
    windowsProjectGuid: 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB',
  });
  await compileModsAsync(exp, { projectRoot: appRoot, platforms, assertMissingModProviders: false });
}

function snapshot(appRoot: string): Record<string, string> {
  const files: Record<string, string> = {};
  const walk = (directory: string) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const entryPath = path.join(directory, entry.name);
      if (entry.name === 'node_modules' || entry.name === 'prepare-calls.log') continue;
      if (entry.isDirectory()) walk(entryPath);
      else files[path.relative(appRoot, entryPath)] = fs.readFileSync(entryPath, 'utf8');
    }
  };
  walk(appRoot);
  return files;
}

afterEach(() => {
  while (appRoots.length > 0) {
    fs.rmSync(appRoots.pop()!, { force: true, recursive: true });
  }
});

describe('expo-desktop prebuild with expo-modules-dotnet', () => {
  it('wires the macOS project without a projectName', async () => {
    const appRoot = createDesktopApp();

    await prebuildAsync(appRoot);

    const podfile = fs.readFileSync(path.join(appRoot, 'macos', 'Podfile'), 'utf8');
    expect(podfile).toContain('use_expo_modules_dotnet!(platform: :macos');
    const pbxproj = fs.readFileSync(
      path.join(appRoot, 'macos', 'DesktopApp.xcodeproj', 'project.pbxproj'),
      'utf8'
    );
    expect(pbxproj.match(/EXPO_DOTNET_LOADER = hostfxr;/g)).toHaveLength(2);
    const infoPlist = fs.readFileSync(path.join(appRoot, 'macos', 'DesktopApp-macOS', 'Info.plist'), 'utf8');
    expect(infoPlist).toMatch(/<key>ExpoModulesDotnetLoader<\/key>\s*<string>\$\(EXPO_DOTNET_LOADER\)<\/string>/);
  });

  it('keeps our Directory.Build.props import after expo-desktop Windows mods run', async () => {
    const appRoot = createDesktopApp();

    await prebuildAsync(appRoot);

    expect(fs.readFileSync(path.join(appRoot, 'Directory.Build.props'), 'utf8')).toContain(
      'expo-modules-dotnet-react-native-dir'
    );
    const vcxproj = fs.readFileSync(path.join(appRoot, 'windows', 'MyApp', 'MyApp.vcxproj'), 'utf8');
    expect(vcxproj.match(/ExpoDotnetApp\.targets/g)).toHaveLength(1);
  });

  it('changes no file when prebuild runs again without --clean', async () => {
    const appRoot = createDesktopApp();
    await prebuildAsync(appRoot);
    const first = snapshot(appRoot);

    await prebuildAsync(appRoot);

    expect(snapshot(appRoot)).toEqual(first);
  });
});
