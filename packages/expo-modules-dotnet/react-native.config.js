module.exports = {
  dependency: {
    platforms: {
      android: {
        sourceDir: './android',
        packageImportPath: 'import expo.modules.dotnet.ExpoModulesDotnetTurboPackage;',
        packageInstance: 'new ExpoModulesDotnetTurboPackage()',
      },
      ios: {},
    },
  },
};
