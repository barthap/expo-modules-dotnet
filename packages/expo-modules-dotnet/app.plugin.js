const fs = require('fs');
const path = require('path');
const { withDangerousMod } = require('expo/config-plugins');

const DOTNET_REQUIRE_LINE =
  'require File.join(File.dirname(`node --print "require.resolve(\'expo-modules-dotnet/package.json\')"`), "scripts/autolinking.rb")';
const DOTNET_CALL_LINE =
  "  use_expo_modules_dotnet!(platform: :ios, project_root: File.expand_path('..', __dir__))";

function addExpoModulesDotnetPodfileHook(contents) {
  if (contents.includes('use_expo_modules_dotnet!')) {
    return contents;
  }

  const lines = contents.split('\n');
  const expoAutolinkingRequireIndex = lines.findIndex(
    (line) =>
      line.includes("require.resolve('expo/package.json')") &&
      line.includes('"scripts/autolinking"')
  );

  if (expoAutolinkingRequireIndex === -1) {
    throw new Error(
      'expo-modules-dotnet config plugin could not find the Expo autolinking require in ios/Podfile.'
    );
  }

  if (!contents.includes("require.resolve('expo-modules-dotnet/package.json')")) {
    lines.splice(expoAutolinkingRequireIndex + 1, 0, DOTNET_REQUIRE_LINE);
  }

  const useExpoModulesIndex = lines.findIndex((line) => line.trim() === 'use_expo_modules!');
  if (useExpoModulesIndex === -1) {
    throw new Error(
      'expo-modules-dotnet config plugin could not find use_expo_modules! in ios/Podfile.'
    );
  }

  lines.splice(useExpoModulesIndex + 1, 0, DOTNET_CALL_LINE);
  return lines.join('\n');
}

module.exports = function withExpoModulesDotnet(config) {
  return withDangerousMod(config, [
    'ios',
    async (modConfig) => {
      const podfilePath = path.join(modConfig.modRequest.platformProjectRoot, 'Podfile');
      const contents = fs.readFileSync(podfilePath, 'utf8');
      const updatedContents = addExpoModulesDotnetPodfileHook(contents);

      if (updatedContents !== contents) {
        fs.writeFileSync(podfilePath, updatedContents);
      }

      return modConfig;
    },
  ]);
};
