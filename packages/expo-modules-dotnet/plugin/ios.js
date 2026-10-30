const DOTNET_REQUIRE_LINE =
  'require File.join(File.dirname(`node --print "require.resolve(\'expo-modules-dotnet/package.json\')"`), "scripts/autolinking.rb")';

function dotnetCallLine(platform) {
  return `  use_expo_modules_dotnet!(platform: :${platform}, project_root: File.expand_path('..', __dir__))`;
}

function addExpoModulesDotnetPodfileHook(contents, platform) {
  if (contents.includes('use_expo_modules_dotnet!')) {
    return contents;
  }

  const podfileName = `${platform}/Podfile`;
  const lines = contents.split('\n');
  const expoAutolinkingRequireIndex = lines.findIndex(
    (line) =>
      line.includes("require.resolve('expo/package.json')") &&
      line.includes('"scripts/autolinking"')
  );

  if (expoAutolinkingRequireIndex === -1) {
    throw new Error(
      `expo-modules-dotnet config plugin could not find the Expo autolinking require in ${podfileName}.`
    );
  }

  if (!contents.includes("require.resolve('expo-modules-dotnet/package.json')")) {
    lines.splice(expoAutolinkingRequireIndex + 1, 0, DOTNET_REQUIRE_LINE);
  }

  const useExpoModulesIndex = lines.findIndex((line) => line.trim() === 'use_expo_modules!');
  if (useExpoModulesIndex === -1) {
    throw new Error(
      `expo-modules-dotnet config plugin could not find use_expo_modules! in ${podfileName}.`
    );
  }

  lines.splice(useExpoModulesIndex + 1, 0, dotnetCallLine(platform));
  return lines.join('\n');
}

module.exports = { addExpoModulesDotnetPodfileHook };
