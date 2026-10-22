const fs = require('fs');
const path = require('path');
const { withDangerousMod } = require('expo/config-plugins');

const WINDOWS_PROPS_RELATIVE_PATH = path.join(
  '.expo',
  'dotnet',
  'windows',
  'ExpoDotnetReactNativeDir.props'
);
const WINDOWS_PROPS_IMPORT_MARKER = 'expo-modules-dotnet-react-native-dir';
const APP_IMPORT_MARKER = 'expo-modules-dotnet-app';
const PACKAGE_IMPORT_MARKER = 'expo-modules-dotnet-package';
const PACKAGE_WINDOWS_DIR = path.resolve(__dirname, '..', 'windows');
const MANAGED_GITIGNORE = '*\n!.gitignore\n!.gitkeep\n';

function escapeXml(value) {
  return value.replace(/[&<>"']/g, (character) => {
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

// Each property is set only when MSBuild has no value yet, so a value from the
// command line or the host still wins.
function writeExpoDotnetWindowsProps(appRoot, { reactNativeDir, loader }) {
  const propsPath = path.join(appRoot, WINDOWS_PROPS_RELATIVE_PATH);
  const temporaryPropsPath = `${propsPath}.tmp`;
  const props =
    '<Project>\n' +
    '  <PropertyGroup>\n' +
    `    <ExpoDotnetReactNativeDir>${escapeXml(reactNativeDir)}</ExpoDotnetReactNativeDir>\n` +
    `    <ReactNativeDir Condition="'$(ReactNativeDir)' == ''">$(ExpoDotnetReactNativeDir)</ReactNativeDir>\n` +
    `    <ExpoDotnetLoader Condition="'$(ExpoDotnetLoader)' == ''">${loader}</ExpoDotnetLoader>\n` +
    `    <ExpoDotnetManagedDir Condition="'$(ExpoDotnetManagedDir)' == ''">$(MSBuildThisFileDirectory)..\\..\\..\\windows\\Managed\\</ExpoDotnetManagedDir>\n` +
    '  </PropertyGroup>\n' +
    '</Project>\n';

  fs.mkdirSync(path.dirname(propsPath), { recursive: true });
  fs.writeFileSync(temporaryPropsPath, props);
  fs.renameSync(temporaryPropsPath, propsPath);
  return propsPath;
}

function configureWindowsDirectoryProps(appRoot, { loader }, resolvePackage = require.resolve) {
  const reactNativeDir = resolveReactNativeDir(appRoot, resolvePackage);

  const directoryBuildPropsPath = path.join(appRoot, 'Directory.Build.props');
  const existingDirectoryBuildProps = fs.existsSync(directoryBuildPropsPath)
    ? fs.readFileSync(directoryBuildPropsPath, 'utf8')
    : '<Project>\n</Project>\n';
  const updatedDirectoryBuildProps = mergeExpoDotnetReactNativePropsImport(
    existingDirectoryBuildProps
  );

  writeExpoDotnetWindowsProps(appRoot, { reactNativeDir, loader });

  if (updatedDirectoryBuildProps !== existingDirectoryBuildProps) {
    fs.writeFileSync(directoryBuildPropsPath, updatedDirectoryBuildProps);
  }
}

function addProjectImport(contents, relativeImportPath, marker) {
  if (contents.includes(`<!-- ${marker} -->`)) {
    return contents;
  }
  const closingProjectIndex = contents.lastIndexOf('</Project>');
  if (closingProjectIndex === -1) {
    throw new Error(`expo-modules-dotnet config plugin: project has no closing </Project> element.`);
  }
  const importLines =
    `  <!-- ${marker} -->\n` +
    `  <Import Project="$(MSBuildThisFileDirectory)${relativeImportPath}" />\n`;
  return `${contents.slice(0, closingProjectIndex)}${importLines}${contents.slice(closingProjectIndex)}`;
}

function findSolutionPath(windowsRoot) {
  const solutions = fs.readdirSync(windowsRoot).filter((file) => file.endsWith('.sln'));
  if (solutions.length !== 1) {
    throw new Error(
      `expo-modules-dotnet config plugin expected exactly one .sln in ${windowsRoot}, found ${solutions.length}.`
    );
  }
  return path.join(windowsRoot, solutions[0]);
}

// The app projects live under windows/. Projects that RNW autolinking adds
// (react-native-windows, native modules) are referenced from node_modules
// with a leading "..".
function findAppProjectPath(solution, solutionPath, extension) {
  const matches = Array.from(
    solution.matchAll(/^Project\("[^"]+"\) = "[^"]+", "([^"]+)", "\{[^}]+\}"/gim),
    (match) => match[1]
  ).filter((projectPath) => projectPath.endsWith(extension) && !projectPath.startsWith('..'));
  if (matches.length !== 1) {
    throw new Error(
      `expo-modules-dotnet config plugin expected exactly one app ${extension} project in ${solutionPath}, found ${matches.length}.`
    );
  }
  return path.resolve(path.dirname(solutionPath), matches[0].split('\\').join(path.sep));
}

function importPathFrom(projectPath, targetsFileName) {
  return path
    .relative(path.dirname(projectPath), path.join(PACKAGE_WINDOWS_DIR, targetsFileName))
    .split(path.sep)
    .join('\\');
}

function addImportToProjectFile(projectPath, targetsFileName, marker) {
  const contents = fs.readFileSync(projectPath, 'utf8');
  const updated = addProjectImport(contents, importPathFrom(projectPath, targetsFileName), marker);
  if (updated !== contents) {
    fs.writeFileSync(projectPath, updated);
  }
}

function ensureManagedDirectory(windowsRoot) {
  const managedDirectory = path.join(windowsRoot, 'Managed');
  const gitignorePath = path.join(managedDirectory, '.gitignore');
  fs.mkdirSync(managedDirectory, { recursive: true });
  if (!fs.existsSync(gitignorePath)) {
    fs.writeFileSync(gitignorePath, MANAGED_GITIGNORE);
  }
}

function requireAutolinking(appRoot) {
  let autolinkingPath;
  try {
    autolinkingPath = require.resolve('expo-modules-dotnet-autolinking', { paths: [appRoot] });
  } catch (error) {
    throw new Error(
      `expo-modules-dotnet config plugin could not resolve expo-modules-dotnet-autolinking from app root "${appRoot}". ` +
        'Add expo-modules-dotnet-autolinking as a dependency of the app before Windows prebuild.'
    );
  }
  return require(autolinkingPath);
}

async function configureWindowsProjectAsync(appRoot, { loader }) {
  const { prepareWindowsSolutionAsync } = requireAutolinking(appRoot);
  const windowsRoot = path.join(appRoot, 'windows');
  const solutionPath = findSolutionPath(windowsRoot);
  const solution = fs.readFileSync(solutionPath, 'utf8');
  const appProjectPath = findAppProjectPath(solution, solutionPath, '.vcxproj');
  const packageProjectPath = findAppProjectPath(solution, solutionPath, '.wapproj');

  configureWindowsDirectoryProps(appRoot, { loader });
  addImportToProjectFile(appProjectPath, 'ExpoDotnetApp.targets', APP_IMPORT_MARKER);
  addImportToProjectFile(packageProjectPath, 'ExpoDotnetPackage.targets', PACKAGE_IMPORT_MARKER);
  ensureManagedDirectory(windowsRoot);
  await prepareWindowsSolutionAsync({ appRoot, solutionPath });
}

function withExpoModulesDotnetWindows(config, { loader }) {
  return withDangerousMod(config, [
    'windows',
    async (modConfig) => {
      await configureWindowsProjectAsync(modConfig.modRequest.projectRoot, { loader });
      return modConfig;
    },
  ]);
}

module.exports = {
  addProjectImport,
  configureWindowsDirectoryProps,
  configureWindowsProjectAsync,
  mergeExpoDotnetReactNativePropsImport,
  resolveReactNativeDir,
  withExpoModulesDotnetWindows,
  writeExpoDotnetWindowsProps,
};
