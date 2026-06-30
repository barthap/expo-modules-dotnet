module.exports = {
  dependency: {
    platforms: {
      android: {
        sourceDir: './android',
        packageImportPath: 'import expo.modules.csharpv2.ExpoCSharpV2TurboPackage;',
        packageInstance: 'new ExpoCSharpV2TurboPackage()',
      },
      ios: {},
    },
  },
};
