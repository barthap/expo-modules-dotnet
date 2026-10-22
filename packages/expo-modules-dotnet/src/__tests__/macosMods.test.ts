import { describe, expect, it } from 'vitest';

const xcode = require('xcode');

const { setLoaderBuildSetting, setLoaderInfoPlist } = require('../../plugin/macos.js');

const templatePbxprojPath = require.resolve(
  'expo-desktop-template-bare-minimum/macos/HelloWorld.xcodeproj/project.pbxproj'
);

function loadTemplateProject() {
  const project = xcode.project(templatePbxprojPath);
  project.parseSync();
  return project;
}

function loaderSettingsByTarget(project: any): Record<string, unknown[]> {
  const targets = project.pbxNativeTargetSection();
  const lists = project.pbxXCConfigurationList();
  const configurations = project.pbxXCBuildConfigurationSection();
  const result: Record<string, unknown[]> = {};
  for (const [key, target] of Object.entries<any>(targets)) {
    if (key.endsWith('_comment')) continue;
    const list = lists[target.buildConfigurationList];
    result[String(target.name).replace(/"/g, '')] = list.buildConfigurations.map(
      (entry: { value: string }) => configurations[entry.value].buildSettings.EXPO_DOTNET_LOADER
    );
  }
  return result;
}

describe('setLoaderBuildSetting', () => {
  it('sets EXPO_DOTNET_LOADER on every configuration of the -macOS target only', () => {
    const project = loadTemplateProject();

    setLoaderBuildSetting(project, 'nativeaot');

    const settings = loaderSettingsByTarget(project);
    expect(settings['HelloWorld-macOS']).toEqual(['nativeaot', 'nativeaot']);
    expect(settings['HelloWorld-iOS']).toEqual([undefined, undefined]);
  });

  it('gives the same project when it runs twice', () => {
    const project = loadTemplateProject();
    setLoaderBuildSetting(project, 'hostfxr');
    const once = project.writeSync();

    setLoaderBuildSetting(project, 'hostfxr');

    expect(project.writeSync()).toBe(once);
  });

  it('throws and names both targets when two -macOS application targets exist', () => {
    const project = loadTemplateProject();
    const targets = project.pbxNativeTargetSection();
    const iosKey = Object.keys(targets).find(
      (key) => !key.endsWith('_comment') && targets[key].name === '"HelloWorld-iOS"'
    )!;
    targets[iosKey].name = '"Other-macOS"';

    expect(() => setLoaderBuildSetting(project, 'hostfxr')).toThrow(/HelloWorld-macOS/);
    expect(() => setLoaderBuildSetting(project, 'hostfxr')).toThrow(/Other-macOS/);
  });
});

describe('setLoaderInfoPlist', () => {
  it('points ExpoModulesDotnetLoader at the build setting', () => {
    expect(setLoaderInfoPlist({ CFBundleName: 'x' })).toEqual({
      CFBundleName: 'x',
      ExpoModulesDotnetLoader: '$(EXPO_DOTNET_LOADER)',
    });
  });

  it('is a no-op when the key is already set', () => {
    const plist = setLoaderInfoPlist({});

    expect(setLoaderInfoPlist({ ...plist })).toEqual(plist);
  });
});
