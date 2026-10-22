const fs = require('fs');
const path = require('path');
const { withDangerousMod, withMod } = require('expo/config-plugins');

const { addExpoModulesDotnetPodfileHook } = require('./ios');

const MANAGED_GITIGNORE = '*\n!.gitignore\n!.gitkeep\n';

function unquote(value) {
  return String(value).replace(/^"(.*)"$/, '$1');
}

// The expo-desktop template project holds both a `<name>-iOS` and a
// `<name>-macOS` application target; only the macOS one loads .NET.
function findMacosApplicationTarget(project) {
  const targets = project.pbxNativeTargetSection();
  const matches = Object.entries(targets).filter(
    ([key, target]) =>
      !key.endsWith('_comment') &&
      unquote(target.productType) === 'com.apple.product-type.application' &&
      unquote(target.name).endsWith('-macOS')
  );
  if (matches.length !== 1) {
    const names = matches.map(([, target]) => unquote(target.name)).join(', ');
    throw new Error(
      `expo-modules-dotnet config plugin expected exactly one "-macOS" application target in the macOS Xcode project, found ${matches.length}${names ? `: ${names}` : ''}.`
    );
  }
  return matches[0][1];
}

function setLoaderBuildSetting(project, loader) {
  const target = findMacosApplicationTarget(project);
  const configurationList = project.pbxXCConfigurationList()[target.buildConfigurationList];
  const configurations = project.pbxXCBuildConfigurationSection();
  for (const { value } of configurationList.buildConfigurations) {
    configurations[value].buildSettings.EXPO_DOTNET_LOADER = loader;
  }
  return project;
}

function setLoaderInfoPlist(plist) {
  return { ...plist, ExpoModulesDotnetLoader: '$(EXPO_DOTNET_LOADER)' };
}

// HostFXR loads the machine-wide .NET install (/etc/dotnet,
// /usr/local/share/dotnet), which the App Sandbox denies. HostFXR is the
// development loader; NativeAOT needs no machine-wide runtime and keeps the
// sandbox that the template turns on.
function setSandboxForLoader(entitlements, loader) {
  if (loader !== 'hostfxr') {
    return entitlements;
  }
  return { ...entitlements, 'com.apple.security.app-sandbox': false };
}

function ensureManagedDirectory(platformProjectRoot) {
  const managedDirectory = path.join(platformProjectRoot, 'Managed');
  const gitignorePath = path.join(managedDirectory, '.gitignore');
  fs.mkdirSync(managedDirectory, { recursive: true });
  if (!fs.existsSync(gitignorePath)) {
    fs.writeFileSync(gitignorePath, MANAGED_GITIGNORE);
  }
}

// Registered through withMod so the plugin needs no expo-desktop dependency:
// expo-desktop prebuild supplies the `macos` providers, and any other host
// skips these mods.
function withExpoModulesDotnetMacos(config, { loader }) {
  config = withMod(config, {
    platform: 'macos',
    mod: 'podfile',
    action: async (modConfig) => {
      modConfig.modResults.contents = addExpoModulesDotnetPodfileHook(
        modConfig.modResults.contents,
        'macos'
      );
      return modConfig;
    },
  });
  config = withMod(config, {
    platform: 'macos',
    mod: 'xcodeproj',
    action: async (modConfig) => {
      setLoaderBuildSetting(modConfig.modResults, loader);
      return modConfig;
    },
  });
  config = withMod(config, {
    platform: 'macos',
    mod: 'infoPlist',
    action: async (modConfig) => {
      modConfig.modResults = setLoaderInfoPlist(modConfig.modResults);
      return modConfig;
    },
  });
  config = withMod(config, {
    platform: 'macos',
    mod: 'entitlements',
    action: async (modConfig) => {
      modConfig.modResults = setSandboxForLoader(modConfig.modResults, loader);
      return modConfig;
    },
  });
  return withDangerousMod(config, [
    'macos',
    async (modConfig) => {
      ensureManagedDirectory(modConfig.modRequest.platformProjectRoot);
      return modConfig;
    },
  ]);
}

module.exports = {
  setLoaderBuildSetting,
  setLoaderInfoPlist,
  setSandboxForLoader,
  withExpoModulesDotnetMacos,
};
