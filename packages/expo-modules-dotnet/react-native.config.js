module.exports = {
  dependency: {
    platforms: {
      android: {
        sourceDir: './android',
        packageImportPath: 'import expo.modules.dotnet.ExpoModulesDotnetTurboPackage;',
        packageInstance: 'new ExpoModulesDotnetTurboPackage()',
      },
      ios: {},
      windows: {
        sourceDir: './windows',
        solutionFile: 'ExpoModulesDotnet.sln',
        projects: [
          {
            projectFile: 'ExpoModulesDotnet\\ExpoModulesDotnet.vcxproj',
            directDependency: true,
          },
        ],
      },
    },
  },
};
