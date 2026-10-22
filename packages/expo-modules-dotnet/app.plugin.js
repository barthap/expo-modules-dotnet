const fs = require('fs');
const path = require('path');
const { withDangerousMod } = require('expo/config-plugins');

const { addExpoModulesDotnetPodfileHook } = require('./plugin/ios');
const { withExpoModulesDotnetMacos } = require('./plugin/macos');
const { resolvePluginOptions } = require('./plugin/options');

const WINDOWS_PROPS_RELATIVE_PATH = path.join(
  '.expo',
  'dotnet',
  'windows',
  'ExpoDotnetReactNativeDir.props'
);
const WINDOWS_PROPS_IMPORT_MARKER = 'expo-modules-dotnet-react-native-dir';

function escapeXml(value) {
  return value.replace(/[&<>"']/g, character => {
    switch (character) {
      case '&':
        return '&amp;';
      case '<':
        return '&lt;';
      case '>':
        return '&gt;';
      case '"':
        return '&quot;';
      case "'":
        return '&apos;';
    }
  });
}

function resolveReactNativeDir(appRoot, resolvePackage = require.resolve) {
  try {
    return path.dirname(resolvePackage('react-native/package.json', { paths: [appRoot] }));
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(
      `expo-modules-dotnet could not resolve react-native/package.json from app root "${appRoot}". ` +
        'Add react-native as a dependency of the consuming app before Windows prebuild. ' +
        `Original error: ${message}`
    );
  }
}

function mergeExpoDotnetReactNativePropsImport(contents) {
  if (contents.includes(WINDOWS_PROPS_IMPORT_MARKER)) {
    return contents;
  }

  const importLine =
    `  <!-- ${WINDOWS_PROPS_IMPORT_MARKER} -->\n` +
    `  <Import Project="$(MSBuildThisFileDirectory).expo\\dotnet\\windows\\ExpoDotnetReactNativeDir.props" ` +
    `Condition="Exists('$(MSBuildThisFileDirectory).expo\\dotnet\\windows\\ExpoDotnetReactNativeDir.props')" />`;
  const closingProjectIndex = contents.lastIndexOf('</Project>');

  if (closingProjectIndex === -1) {
    throw new Error('Directory.Build.props must contain a closing </Project> element.');
  }

  return `${contents.slice(0, closingProjectIndex)}${importLine}\n${contents.slice(closingProjectIndex)}`;
}

function writeExpoDotnetReactNativeProps(appRoot, reactNativeDir) {
  const propsPath = path.join(appRoot, WINDOWS_PROPS_RELATIVE_PATH);
  const propsDirectory = path.dirname(propsPath);
  const temporaryPropsPath = `${propsPath}.tmp`;
  const escapedReactNativeDir = escapeXml(reactNativeDir);
  const props =
    '<Project>\n' +
    '  <PropertyGroup>\n' +
    `    <ExpoDotnetReactNativeDir>${escapedReactNativeDir}</ExpoDotnetReactNativeDir>\n` +
    `    <ReactNativeDir Condition="'$(ReactNativeDir)' == ''">$(ExpoDotnetReactNativeDir)</ReactNativeDir>\n` +
    '  </PropertyGroup>\n' +
    '</Project>\n';

  fs.mkdirSync(propsDirectory, { recursive: true });
  fs.writeFileSync(temporaryPropsPath, props);
  fs.renameSync(temporaryPropsPath, propsPath);
  return propsPath;
}

function configureWindowsReactNativeDir(appRoot, resolvePackage = require.resolve) {
  const reactNativeDir = resolveReactNativeDir(appRoot, resolvePackage);

  const directoryBuildPropsPath = path.join(appRoot, 'Directory.Build.props');
  const existingDirectoryBuildProps = fs.existsSync(directoryBuildPropsPath)
    ? fs.readFileSync(directoryBuildPropsPath, 'utf8')
    : '<Project>\n</Project>\n';
  const updatedDirectoryBuildProps = mergeExpoDotnetReactNativePropsImport(
    existingDirectoryBuildProps
  );

  writeExpoDotnetReactNativeProps(appRoot, reactNativeDir);

  if (updatedDirectoryBuildProps !== existingDirectoryBuildProps) {
    fs.writeFileSync(directoryBuildPropsPath, updatedDirectoryBuildProps);
  }
}

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

  return withDangerousMod(macosConfigured, [
    'windows',
    async (modConfig) => {
      configureWindowsReactNativeDir(modConfig.modRequest.projectRoot);
      return modConfig;
    },
  ]);
}

withExpoModulesDotnet.resolveReactNativeDir = resolveReactNativeDir;
withExpoModulesDotnet.mergeExpoDotnetReactNativePropsImport = mergeExpoDotnetReactNativePropsImport;
withExpoModulesDotnet.writeExpoDotnetReactNativeProps = writeExpoDotnetReactNativeProps;
withExpoModulesDotnet.configureWindowsReactNativeDir = configureWindowsReactNativeDir;

module.exports = withExpoModulesDotnet;
