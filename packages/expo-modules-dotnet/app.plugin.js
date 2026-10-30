const fs = require('fs');
const path = require('path');
const { withDangerousMod } = require('expo/config-plugins');

const { addExpoModulesDotnetPodfileHook } = require('./plugin/ios');
const { withExpoModulesDotnetMacos } = require('./plugin/macos');
const { resolvePluginOptions } = require('./plugin/options');
const windows = require('./plugin/windows');

function withExpoModulesDotnet(config, props) {
  const options = resolvePluginOptions(props);
  const iosConfigured = withDangerousMod(config, [
    'ios',
    async (modConfig) => {
      const podfilePath = path.join(modConfig.modRequest.platformProjectRoot, 'Podfile');
      const contents = fs.readFileSync(podfilePath, 'utf8');
      const updatedContents = addExpoModulesDotnetPodfileHook(contents, 'ios');

      if (updatedContents !== contents) {
        fs.writeFileSync(podfilePath, updatedContents);
      }

      return modConfig;
    },
  ]);
  const macosConfigured = withExpoModulesDotnetMacos(iosConfigured, options.macos);
  return windows.withExpoModulesDotnetWindows(macosConfigured, options.windows);
}

withExpoModulesDotnet.resolveReactNativeDir = windows.resolveReactNativeDir;
withExpoModulesDotnet.mergeExpoDotnetReactNativePropsImport = windows.mergeExpoDotnetReactNativePropsImport;
withExpoModulesDotnet.writeExpoDotnetWindowsProps = windows.writeExpoDotnetWindowsProps;
withExpoModulesDotnet.configureWindowsDirectoryProps = windows.configureWindowsDirectoryProps;

module.exports = withExpoModulesDotnet;
