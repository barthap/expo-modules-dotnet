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
});
