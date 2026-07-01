import ExpoModulesDotnetInstaller from './NativeExpoModulesDotnetInstaller';

declare global {
  // eslint-disable-next-line no-var
  var _expoDotnet:
    | {
        modules?: Record<string, unknown>;
      }
    | undefined;
}

function ensureInstalled(): void {
  if (ExpoModulesDotnetInstaller == null) {
    throw new Error('expo-modules-dotnet native installer is not available.');
  }
}

export function requireDotnetModule<T>(name: string): T {
  ensureInstalled();

  const module = globalThis._expoDotnet?.modules?.[name];
  if (module == null) {
    throw new Error(`.NET module '${name}' is not installed.`);
  }

  return module as T;
}
