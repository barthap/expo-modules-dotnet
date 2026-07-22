import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DotnetEventEmitter, DotnetModule } from '../index';

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

  it('includes native installer diagnostics when installation does not create the registry', async () => {
    mockNative.installer.installModules.mockReturnValue(false);
    mockNative.installer.getLastError.mockReturnValue(
      'Expo JSI ABI version mismatch: native=21 managed=22.'
    );

    const { requireDotnetModule } = await import('../index');

    expect(() => requireDotnetModule('ExampleModule')).toThrow(
      'expo-modules-dotnet native installer failed: ' +
        'Expo JSI ABI version mismatch: native=21 managed=22.'
    );
  });
});

describe('typed facade base classes', () => {
  it.each([
    [
      'DotnetEventEmitter',
      DotnetEventEmitter,
      'DotnetEventEmitter instances are created by the native module registry. Use requireDotnetModule() to obtain a module.',
    ],
    [
      'DotnetModule',
      DotnetModule,
      'DotnetModule instances are created by the native module registry. Use requireDotnetModule() to obtain a module.',
    ],
  ])('%s is an exported value whose constructor directs callers to module lookup', (_, Facade, message) => {
    expect(typeof Facade).toBe('function');
    expect(() => new Facade()).toThrow(message);
  });

  it('keeps only the five modern event methods on the event-emitter prototype', () => {
    expect(Object.getOwnPropertyNames(DotnetEventEmitter.prototype).sort()).toEqual(
      [
        'addListener',
        'constructor',
        'emit',
        'listenerCount',
        'removeAllListeners',
        'removeListener',
      ].sort()
    );
    expect(Object.getOwnPropertyNames(DotnetModule.prototype)).toEqual(['constructor']);

    expect(DotnetEventEmitter.prototype.addListener).toHaveLength(2);
    expect(DotnetEventEmitter.prototype.removeListener).toHaveLength(2);
    expect(DotnetEventEmitter.prototype.removeAllListeners).toHaveLength(1);
    expect(DotnetEventEmitter.prototype.emit).toHaveLength(1);
    expect(DotnetEventEmitter.prototype.listenerCount).toHaveLength(1);

    for (const name of ['removeSubscription', 'startObserving', 'stopObserving', 'unavailable']) {
      expect(DotnetEventEmitter.prototype).not.toHaveProperty(name);
      expect(DotnetModule.prototype).not.toHaveProperty(name);
    }
  });
});

describe('DotnetSharedObject', () => {
  it('is an exported class value whose direct construction throws facade guidance', async () => {
    const { DotnetSharedObject } = await import('../index');
    expect(typeof DotnetSharedObject).toBe('function');
    expect(() => new DotnetSharedObject()).toThrowError(
      /generated class|module return/
    );
  });

  it('exposes only constructor and release on its prototype', async () => {
    const { DotnetSharedObject } = await import('../index');
    expect(Object.getOwnPropertyNames(DotnetSharedObject.prototype).sort()).toEqual([
      'constructor',
      'release',
    ]);
  });

  it('throws the same facade guidance from the placeholder release()', async () => {
    const { DotnetSharedObject } = await import('../index');
    expect(() => DotnetSharedObject.prototype.release.call({})).toThrowError(
      /generated class|module return/
    );
  });

  it('lets subclasses inherit release(): void', async () => {
    const { DotnetSharedObject } = await import('../index');
    class Handle extends DotnetSharedObject {}
    expect(Handle.prototype.release).toBe(DotnetSharedObject.prototype.release);
    expect(Object.getOwnPropertyNames(Handle.prototype)).toEqual(['constructor']);
  });
});
