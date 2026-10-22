const fs = require('fs');
const path = require('path');
const { withDangerousMod, withMod } = require('expo/config-plugins');

// RNW 0.81 dependency projects declare the v143 toolset. VS 2026 ships v145
// only, so without this override those projects do not build.
const DIRECTORY_BUILD_TARGETS = `<Project>
  <PropertyGroup Condition="'$(PlatformToolset)' == 'v143' and Exists('$(VCTargetsPath)\\Platforms\\$(Platform)\\PlatformToolsets\\v145\\Toolset.props')">
    <PlatformToolset>v145</PlatformToolset>
  </PropertyGroup>
</Project>
`;

// RNW NuGet restore needs the react-native Azure feed. expo-desktop prebuild
// copies only the platform folders, so it never writes this app-root file.
const NUGET_CONFIG = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="react-native" value="https://pkgs.dev.azure.com/ms/react-native/_packaging/react-native-public/nuget/v3/index.json" />
    <add key="Nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
</configuration>
`;

// The template derives the react-native-macos path from the react-native
// path. Under pnpm, react-native resolves to its own store folder, which has
// no react-native-macos sibling, so pod install fails. Resolve it through Node.
const TEMPLATE_RN_MACOS_PATH = ':path => "#{config[:reactNativePath]}-macos",';
const RESOLVED_RN_MACOS_PATH =
  ':path => Pathname.new(File.dirname(`node --print "require.resolve(\'react-native-macos/package.json\')"`)).relative_path_from(Pathname.pwd).to_s,';

function withResolvedReactNativeMacosPath(config) {
  return withMod(config, {
    platform: 'macos',
    mod: 'podfile',
    action: async (modConfig) => {
      const { contents } = modConfig.modResults;
      if (!contents.includes(RESOLVED_RN_MACOS_PATH)) {
        if (!contents.includes(TEMPLATE_RN_MACOS_PATH)) {
          throw new Error('withDesktopAppFixes: macos/Podfile no longer has the template react-native-macos path.');
        }
        modConfig.modResults.contents = contents.replace(TEMPLATE_RN_MACOS_PATH, RESOLVED_RN_MACOS_PATH);
      }
      return modConfig;
    },
  });
}

// Fixes for desktop-app's generated native projects that are not .NET work.
function withDesktopAppFixes(config) {
  config = withResolvedReactNativeMacosPath(config);
  return withDangerousMod(config, [
    'windows',
    async (modConfig) => {
      const { projectRoot } = modConfig.modRequest;
      fs.writeFileSync(path.join(projectRoot, 'Directory.Build.targets'), DIRECTORY_BUILD_TARGETS);
      fs.writeFileSync(path.join(projectRoot, 'NuGet.config'), NUGET_CONFIG);
      return modConfig;
    },
  ]);
}

module.exports = withDesktopAppFixes;
