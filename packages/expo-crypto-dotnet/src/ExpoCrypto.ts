import { DotnetModule, requireDotnetModule } from 'expo-modules-dotnet';

import type { CryptoDigestAlgorithm, CryptoDigestOptions, Digest } from './Crypto.types';

declare class ExpoCryptoNativeModule extends DotnetModule {
  digestString(
    algorithm: CryptoDigestAlgorithm,
    data: string,
    options: CryptoDigestOptions
  ): Digest;
  digestStringAsync(
    algorithm: CryptoDigestAlgorithm,
    data: string,
    options: CryptoDigestOptions
  ): Promise<Digest>;
  getRandomValues(typedArray: Uint8Array): void;
  digest(algorithm: CryptoDigestAlgorithm, output: Uint8Array, data: Uint8Array): void;
  randomUUID(): string;
}

export default requireDotnetModule<ExpoCryptoNativeModule>('ExpoCrypto');
