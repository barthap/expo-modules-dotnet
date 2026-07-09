module.exports = {
  dependency: {
    platforms: {
      android: null,
      ios: null,
      macos: null,
      windows: {
        sourceDir: './windows',
        solutionFile: 'ExpoModulesDotnetWindows.sln',
        projects: [
          {
            projectFile: 'ExpoModulesDotnetWindows\\ExpoModulesDotnetWindows.vcxproj',
            directDependency: true,
          },
        ],
      },
    },
  },
};
