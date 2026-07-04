import * as path from 'path';
import { describe, expect, it, vi } from 'vitest';

const expoAutolinking = vi.hoisted(() => ({
  findProjectRootSync: vi.fn(),
  makeCachedDependenciesLinker: vi.fn(),
  scanExpoModuleResolutionsForPlatform: vi.fn(),
}));

vi.mock('expo-modules-autolinking/exports', () => expoAutolinking);

describe('resolveAppRoot', () => {
  it('uses the directory when Expo autolinking returns a package.json path', async () => {
    const packageJsonPath = path.resolve('apps/desktop-app/package.json');
    expoAutolinking.findProjectRootSync.mockReturnValue(packageJsonPath);
    const { resolveAppRoot } = await import('../discovery');

    expect(resolveAppRoot()).toBe(path.dirname(packageJsonPath));
  });
});
