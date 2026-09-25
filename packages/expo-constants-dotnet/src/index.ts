import { DotnetModule, requireDotnetModule } from 'expo-modules-dotnet';

/**
 * App constants supplied by the Windows or macOS Expo .NET host.
 * Native version values remain null when their named host source is unavailable.
 */
export interface ExpoConstants {
  /** The operating system hosting this module. */
  readonly platform: 'windows' | 'macos';
  /** The app-owned execution environment. This package does not run in Expo Go. */
  readonly executionEnvironment: 'bare';
  /** A new ID for each JavaScript runtime context. It is not persisted. */
  readonly sessionId: string;
  /** The macOS main bundle's app version, or null on Windows or when unavailable. */
  readonly nativeAppVersion: string | null;
  /** The macOS bundle build version or Windows package version, if available. */
  readonly nativeBuildVersion: string | null;
  /** Always null because this host is not Expo Go. */
  readonly expoVersion: null;
}

declare class ExpoConstantsNativeModule extends DotnetModule implements ExpoConstants {
  readonly platform: 'windows' | 'macos';
  readonly executionEnvironment: 'bare';
  readonly sessionId: string;
  readonly nativeAppVersion: string | null;
  readonly nativeBuildVersion: string | null;
  readonly expoVersion: null;
}

const constants: ExpoConstants = requireDotnetModule<ExpoConstantsNativeModule>('ExponentConstants');

export default constants;
