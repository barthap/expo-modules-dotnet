const { makeMetroConfig } = require('expo-desktop-metro-config');
const { withDotnetModules } = require('expo-modules-dotnet/metro');

// On Windows, upstream expo-asset and expo-constants reach the .NET modules
// instead of the expo-desktop stubs. Other platforms resolve unchanged.
module.exports = withDotnetModules(makeMetroConfig(__dirname), {
  substitute: ['expo-asset', 'expo-constants'],
});
