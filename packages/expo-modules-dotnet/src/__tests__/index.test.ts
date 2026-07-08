import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockNative = vi.hoisted(() => ({
  installer: {
    installModules: vi.fn(),
    getLastError: vi.fn(),
  },
}));

vi.mock('react-native', () => ({
  TurboModuleRegistry: {
    get: vi.fn(() => mockNative.installer),
  },
}));

describe('requireDotnetModule', () => {
  beforeEach(() => {
    vi.resetModules();
    mockNative.installer.installModules.mockReset();
    mockNative.installer.getLastError.mockReset();
    delete globalThis._expoDotnet;
  });

  it('returns the requested module after the native installer populates the registry', async () => {
    const exampleModule = { add: vi.fn() };
    mockNative.installer.installModules.mockImplementation(() => {
      globalThis._expoDotnet = {
        modules: {
          ExampleModule: exampleModule,
        },
      };
      return true;
    });

    const { requireDotnetModule } = await import('../index');

    expect(requireDotnetModule('ExampleModule')).toBe(exampleModule);
    expect(mockNative.installer.installModules).toHaveBeenCalledOnce();
  });

  it.each([undefined, null])(
    'throws autolinking guidance when the module registry entry is %s',
    async (moduleValue) => {
      mockNative.installer.installModules.mockImplementation(() => {
        globalThis._expoDotnet = {
          modules: {
            MissingModule: moduleValue,
          },
        };
        return true;
      });

      const { requireDotnetModule } = await import('../index');

      expect(() => requireDotnetModule('MissingModule')).toThrow(
        "Module 'MissingModule' is not registered. Check that it is autolinked correctly."
      );
    }
  );
});
