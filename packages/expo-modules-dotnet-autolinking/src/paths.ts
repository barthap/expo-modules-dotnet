import * as path from 'path';

export function defaultGenerateOutputDir(appRoot: string): string {
  return path.join(appRoot, '.expo', 'dotnet');
}

export function findAdapterPackageRoot(appRoot: string): string {
  try {
    return path.dirname(require.resolve('expo-modules-dotnet/package.json', { paths: [appRoot] }));
  } catch (error) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Could not resolve expo-modules-dotnet from app root ${appRoot}. expo-modules-dotnet must be a dependency.`
    );
  }
}
