import * as path from 'path';

import { describe, expect, it } from 'vitest';

import { configureMixedModeDebugger, findPackageProjectPath } from '../windows/packageDebugger';

describe('packageDebugger', () => {
  it('finds the single WAP project and enables mixed debugging', () => {
    const solutionPath = path.join('app', 'windows', 'App.sln');
    const solution = [
      'Microsoft Visual Studio Solution File, Format Version 12.00',
      'Project("{C7167F0D-BC9F-4E6E-AFE1-012C56B48DB5}") = "App.Package", "App.Package\\App.Package.wapproj", "{11111111-1111-1111-1111-111111111111}"',
      'EndProject',
    ].join('\r\n');
    const project = [
      '<Project>',
      '  <PropertyGroup>',
      '    <DebuggerType>NativeOnly</DebuggerType>',
      '    <BackgroundTaskDebugEngines>NativeOnly</BackgroundTaskDebugEngines>',
      '  </PropertyGroup>',
      '</Project>',
    ].join('\r\n');

    expect(findPackageProjectPath(solution, solutionPath)).toBe(
      path.resolve('app', 'windows', 'App.Package', 'App.Package.wapproj')
    );
    expect(configureMixedModeDebugger(project)).toEqual({
      changed: true,
      content: project.replace('<DebuggerType>NativeOnly</DebuggerType>', '<DebuggerType>Mixed</DebuggerType>'),
    });
  });
});
