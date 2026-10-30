import { describe, expect, it } from 'vitest';

const { resolvePluginOptions } = require('../../plugin/options.js');
const withExpoModulesDotnet = require('../../app.plugin.js');

describe('resolvePluginOptions', () => {
  it('defaults both desktop loaders to hostfxr when no options are given', () => {
    const expected = { macos: { loader: 'hostfxr' }, windows: { loader: 'hostfxr' } };
    expect(resolvePluginOptions(undefined)).toEqual(expected);
    expect(resolvePluginOptions({})).toEqual(expected);
  });

  it('keeps the default for the platform that is not configured', () => {
    expect(resolvePluginOptions({ macos: { loader: 'nativeaot' } })).toEqual({
      macos: { loader: 'nativeaot' },
      windows: { loader: 'hostfxr' },
    });
  });

  it('rejects a shared loader key because iOS and Android only support nativeaot', () => {
    expect(() => resolvePluginOptions({ loader: 'nativeaot' })).toThrow(/"loader".*macos.*windows/s);
  });

  it('rejects an unknown loader value and names the allowed values', () => {
    expect(() => resolvePluginOptions({ windows: { loader: 'aot' } })).toThrow(
      /windows\.loader.*hostfxr.*nativeaot/s
    );
  });

  it('rejects an unknown key inside a platform', () => {
    expect(() => resolvePluginOptions({ windows: { loader: 'hostfxr', extra: 1 } })).toThrow(
      /windows\.extra/
    );
  });

  it('rejects a platform value that is not an object', () => {
    expect(() => resolvePluginOptions({ macos: 'nativeaot' })).toThrow(/"macos"/);
  });
});

describe('withExpoModulesDotnet options', () => {
  it('validates options before it registers any mod', () => {
    expect(() => withExpoModulesDotnet({ name: 'app', slug: 'app' }, { loader: 'nativeaot' })).toThrow(
      /"loader"/
    );
  });

  it('works on a config with no desktop mod providers', () => {
    const config = withExpoModulesDotnet({ name: 'app', slug: 'app' });

    expect(config.mods.ios.dangerous).toBeTypeOf('function');
  });
});
