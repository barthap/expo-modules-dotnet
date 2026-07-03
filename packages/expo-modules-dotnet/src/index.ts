import ExpoModulesDotnetInstaller from './NativeExpoModulesDotnetInstaller';

declare global {
  // eslint-disable-next-line no-var
  var _expoDotnet:
    | {
        modules?: Record<string, unknown>;
      }
    | undefined;
}

type InstallerDiagnostics = {
  getLastError?: () => string;
};

function ensureInstalled(): void {
  if (globalThis._expoDotnet?.modules != null) {
    return;
  }

  if (ExpoModulesDotnetInstaller == null) {
    throw new Error('expo-modules-dotnet native installer is not available.');
  }

  const installed = ExpoModulesDotnetInstaller.installModules();
  if (!installed || globalThis._expoDotnet?.modules == null) {
    const nativeError =
      (ExpoModulesDotnetInstaller as InstallerDiagnostics).getLastError?.() ?? '';
    throw new Error(
      nativeError.length > 0
        ? `expo-modules-dotnet native installer did not install _expoDotnet: ${nativeError}`
        : 'expo-modules-dotnet native installer did not install _expoDotnet.'
    );
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
