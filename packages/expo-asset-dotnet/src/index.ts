import { DotnetModule, requireDotnetModule } from 'expo-modules-dotnet';

declare class ExpoAssetNativeModule extends DotnetModule {
  downloadAsync(url: string, md5Hash: string | null, type: string): Promise<string>;
}

const nativeModule = requireDotnetModule<ExpoAssetNativeModule>('ExpoAsset');

/**
 * Downloads an asset to the app cache and returns its local file URL.
 *
 * @param url - An absolute `file`, `http`, or `https` URL.
 * @param md5Hash - An optional 32-character hexadecimal cache identity.
 * @param type - A 1-16 character alphanumeric file extension without a dot.
 * @returns The input `file` URL or the downloaded cache file URL.
 */
export function downloadAsync(
  url: string,
  md5Hash: string | null,
  type: string
): Promise<string> {
  return nativeModule.downloadAsync(url, md5Hash, type);
}
