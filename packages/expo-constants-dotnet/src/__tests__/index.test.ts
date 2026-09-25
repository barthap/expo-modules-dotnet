import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockDotnet = vi.hoisted(() => ({
  requireDotnetModule: vi.fn(),
}));

vi.mock('expo-modules-dotnet', () => ({
  DotnetModule: class {},
  requireDotnetModule: mockDotnet.requireDotnetModule,
}));

describe('Expo constants facade', () => {
  beforeEach(() => {
    vi.resetModules();
    mockDotnet.requireDotnetModule.mockReset();
    delete (globalThis as { expo?: unknown }).expo;
  });

  it('returns the registered ExponentConstants object without using the Expo registry', async () => {
    const nativeConstants = {
      platform: 'macos',
      executionEnvironment: 'bare',
      sessionId: 'c27ce6d5-3f2c-4a7c-9fe0-552d30471d28',
      nativeAppVersion: '1.0',
      nativeBuildVersion: '1',
      expoVersion: null,
    };
    mockDotnet.requireDotnetModule.mockReturnValue(nativeConstants);

    const facade = await import('../index');

    expect(mockDotnet.requireDotnetModule).toHaveBeenCalledExactlyOnceWith('ExponentConstants');
    expect(facade.default).toBe(nativeConstants);
    expect(facade.default.nativeBuildVersion).toBe('1');
    expect((globalThis as { expo?: unknown }).expo).toBeUndefined();
  });
});
