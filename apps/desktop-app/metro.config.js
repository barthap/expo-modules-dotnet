const { getDefaultConfig } = require('expo/metro-config');

const fs = require('fs');
const path = require('path');

const config = getDefaultConfig(__dirname);
const rnwPath = fs.realpathSync(
  path.resolve(require.resolve('react-native-windows/package.json'), '..')
);

config.resolver.platforms = Array.from(
  new Set([...(config.resolver.platforms || []), 'windows', 'macos'])
);

config.resolver.blockList = [
  new RegExp(`${path.resolve(__dirname, 'windows').replace(/[/\\]/g, '/')}.*`),
  new RegExp(`${rnwPath}/build/.*`),
  new RegExp(`${rnwPath}/target/.*`),
  /.*\.ProjectImports\.zip/,
];

config.resolver.resolveRequest = (context, moduleName, platform) => {
  if (
    platform === 'macos' &&
    (moduleName === 'react-native' || moduleName.startsWith('react-native/'))
  ) {
    const newModuleName = moduleName.replace('react-native', 'react-native-macos');
    return context.resolveRequest(context, newModuleName, platform);
  }
  if (
    platform === 'windows' &&
    (moduleName === 'react-native' || moduleName.startsWith('react-native/'))
  ) {
    const newModuleName = moduleName.replace('react-native', 'react-native-windows');
    return context.resolveRequest(context, newModuleName, platform);
  }
  return context.resolveRequest(context, moduleName, platform);
};

const originalGetModulesRunBeforeMainModule =
  config.serializer.getModulesRunBeforeMainModule;
config.serializer.getModulesRunBeforeMainModule = () => {
  try {
    return [
      require.resolve('react-native-windows/Libraries/Core/InitializeCore'),
      require.resolve('react-native/Libraries/Core/InitializeCore'),
      require.resolve('react-native-macos/Libraries/Core/InitializeCore'),
    ];
  } catch {}
  return originalGetModulesRunBeforeMainModule();
};

module.exports = config;
