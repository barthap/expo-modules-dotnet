import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

const templateWindowsDir = path.join(
  path.dirname(require.resolve('expo-desktop-template-bare-minimum/package.json')),
  'windows'
);

// Copies the SDK 54 template windows/ folder with the placeholders that
// expo-desktop prebuild renders, and fakes the two packages the Windows mod
// resolves from the app root. The fake autolinking package records its calls.
export function createWindowsTestApp(): string {
  const appRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'expo-dotnet-windows-mods-'));
  fs.cpSync(templateWindowsDir, path.join(appRoot, 'windows'), { recursive: true });
  for (const file of [
    'MyApp.sln',
    path.join('MyApp', 'MyApp.vcxproj'),
    path.join('MyApp.Package', 'MyApp.Package.wapproj'),
  ]) {
    const filePath = path.join(appRoot, 'windows', file);
    const rendered = fs
      .readFileSync(filePath, 'utf8')
      .replace(/\{\{ name \}\}/g, 'MyApp')
      .replace(/\{\{ packageGuidUpper \}\}/g, '{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}')
      .replace(/\{\{ projectGuidUpper \}\}/g, '{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}');
    fs.writeFileSync(filePath, rendered);
  }

  const reactNativeDir = path.join(appRoot, 'node_modules', 'react-native');
  fs.mkdirSync(reactNativeDir, { recursive: true });
  fs.writeFileSync(path.join(reactNativeDir, 'package.json'), '{"name":"react-native","version":"0.81.6"}');

  const autolinkingDir = path.join(appRoot, 'node_modules', 'expo-modules-dotnet-autolinking');
  fs.mkdirSync(autolinkingDir, { recursive: true });
  fs.writeFileSync(path.join(autolinkingDir, 'package.json'), '{"name":"expo-modules-dotnet-autolinking","main":"index.js"}');
  fs.writeFileSync(
    path.join(autolinkingDir, 'index.js'),
    [
      "const fs = require('fs');",
      "const path = require('path');",
      'exports.prepareWindowsSolutionAsync = async (options) => {',
      "  fs.appendFileSync(path.join(options.appRoot, 'prepare-calls.log'), JSON.stringify(options) + '\\n');",
      '  return { changed: true };',
      '};',
      '',
    ].join('\n')
  );
  return appRoot;
}

export function readPrepareCalls(appRoot: string): Array<{ appRoot: string; solutionPath: string }> {
  const logPath = path.join(appRoot, 'prepare-calls.log');
  if (!fs.existsSync(logPath)) return [];
  return fs.readFileSync(logPath, 'utf8').trim().split('\n').map((line) => JSON.parse(line));
}
