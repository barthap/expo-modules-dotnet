import { spawnSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

import { afterEach, describe, expect, it } from 'vitest';

import { createWindowsTestApp, readPrepareCalls } from './windowsTestApp';

const { addProjectImport, configureWindowsProjectAsync, writeExpoDotnetWindowsProps } = require('../../plugin/windows.js');

const appRoots: string[] = [];

function createApp() {
  const appRoot = createWindowsTestApp();
  appRoots.push(appRoot);
  return appRoot;
}

afterEach(() => {
  while (appRoots.length > 0) {
    fs.rmSync(appRoots.pop()!, { force: true, recursive: true });
  }
});

describe('writeExpoDotnetWindowsProps', () => {
  it('sets the configured loader and managed folder only when MSBuild has no value yet', () => {
    const appRoot = createApp();

    const propsPath = writeExpoDotnetWindowsProps(appRoot, {
      reactNativeDir: path.join(appRoot, 'node_modules', 'react-native'),
      loader: 'nativeaot',
    });

    const props = fs.readFileSync(propsPath, 'utf8');
    expect(props).toContain("<ExpoDotnetLoader Condition=\"'$(ExpoDotnetLoader)' == ''\">nativeaot</ExpoDotnetLoader>");
    expect(props).toContain(
      "<ExpoDotnetManagedDir Condition=\"'$(ExpoDotnetManagedDir)' == ''\">$(MSBuildThisFileDirectory)..\\..\\..\\windows\\Managed\\</ExpoDotnetManagedDir>"
    );
    expect(props).toContain("<ReactNativeDir Condition=\"'$(ReactNativeDir)' == ''\">");
  });
});

describe('addProjectImport', () => {
  const project = '<?xml version="1.0"?>\n<Project>\n  <Import Project="a.targets" />\n</Project>\n';

  it('adds one marked import before </Project>', () => {
    const result = addProjectImport(project, 'x\\y.targets', 'expo-modules-dotnet-app');

    expect(result).toContain(
      '  <!-- expo-modules-dotnet-app -->\n  <Import Project="$(MSBuildThisFileDirectory)x\\y.targets" />\n</Project>'
    );
  });

  it('is idempotent', () => {
    const once = addProjectImport(project, 'x\\y.targets', 'expo-modules-dotnet-app');

    expect(addProjectImport(once, 'x\\y.targets', 'expo-modules-dotnet-app')).toBe(once);
  });
});

describe('configureWindowsProjectAsync', () => {
  it('wires the app and package projects that the solution names', async () => {
    const appRoot = createApp();

    await configureWindowsProjectAsync(appRoot, { loader: 'hostfxr' });

    const vcxproj = fs.readFileSync(path.join(appRoot, 'windows', 'MyApp', 'MyApp.vcxproj'), 'utf8');
    const wapproj = fs.readFileSync(
      path.join(appRoot, 'windows', 'MyApp.Package', 'MyApp.Package.wapproj'),
      'utf8'
    );
    expect(vcxproj.match(/ExpoDotnetApp\.targets/g)).toHaveLength(1);
    expect(wapproj.match(/ExpoDotnetPackage\.targets/g)).toHaveLength(1);
    expect(fs.readFileSync(path.join(appRoot, 'windows', 'Managed', '.gitignore'), 'utf8')).toContain('*');
    expect(fs.readFileSync(path.join(appRoot, 'Directory.Build.props'), 'utf8')).toContain(
      'expo-modules-dotnet-react-native-dir'
    );
    expect(readPrepareCalls(appRoot)).toEqual([
      { appRoot, solutionPath: path.join(appRoot, 'windows', 'MyApp.sln') },
    ]);
  });

  it('points the imports at files that exist in this package', async () => {
    const appRoot = createApp();

    await configureWindowsProjectAsync(appRoot, { loader: 'hostfxr' });

    for (const [project, file] of [
      [path.join('MyApp', 'MyApp.vcxproj'), 'ExpoDotnetApp.targets'],
      [path.join('MyApp.Package', 'MyApp.Package.wapproj'), 'ExpoDotnetPackage.targets'],
    ]) {
      const projectPath = path.join(appRoot, 'windows', project);
      const relative = fs
        .readFileSync(projectPath, 'utf8')
        .match(new RegExp(`\\$\\(MSBuildThisFileDirectory\\)([^"]*${file.replace('.', '\\.')})`))![1];
      expect(fs.existsSync(path.resolve(path.dirname(projectPath), relative.split('\\').join(path.sep)))).toBe(true);
    }
  });

  it('leaves one copy of each edit when it runs twice', async () => {
    const appRoot = createApp();
    await configureWindowsProjectAsync(appRoot, { loader: 'hostfxr' });
    const files = ['MyApp/MyApp.vcxproj', 'MyApp.Package/MyApp.Package.wapproj'].map((file) =>
      path.join(appRoot, 'windows', file)
    );
    const first = files.map((file) => fs.readFileSync(file, 'utf8'));

    await configureWindowsProjectAsync(appRoot, { loader: 'hostfxr' });

    expect(files.map((file) => fs.readFileSync(file, 'utf8'))).toEqual(first);
  });

  it('does not hard-code the template project name', () => {
    const source = fs.readFileSync(require.resolve('../../plugin/windows.js'), 'utf8');

    expect(source).not.toMatch(/MyApp|DesktopApp/);
  });

  it('tells the developer to add the autolinking package when it is missing', () => {
    const appRoot = createApp();
    fs.rmSync(path.join(appRoot, 'node_modules', 'expo-modules-dotnet-autolinking'), { recursive: true });
    // pnpm scripts set NODE_PATH to the workspace store, where Node would still
    // find the real package; an app outside the workspace has no such path.
    const script =
      `require(${JSON.stringify(require.resolve('../../plugin/windows.js'))})` +
      `.configureWindowsProjectAsync(${JSON.stringify(appRoot)}, { loader: 'hostfxr' })` +
      '.then(() => process.exit(0), (error) => { console.error(error.message); process.exit(1); });';

    const result = spawnSync(process.execPath, ['-e', script], {
      encoding: 'utf8',
      env: { ...process.env, NODE_PATH: '' },
    });

    expect(result.status).toBe(1);
    expect(result.stderr).toMatch(/Add expo-modules-dotnet-autolinking as a dependency of the app/);
  });
});
