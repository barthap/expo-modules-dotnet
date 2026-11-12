import type * as Crypto from './index';
import type { CryptoDigestAlgorithm, CryptoDigestOptions, Digest } from './index';

type Api = typeof Crypto;
type Assert<T extends true> = T;
type Absent<K extends PropertyKey> = K extends keyof Api ? false : true;
type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;

type NoAesKey = Assert<Absent<'AESEncryptionKey'>>;
type NoAesSealedData = Assert<Absent<'AESSealedData'>>;
type NoAesEncrypt = Assert<Absent<'aesEncryptAsync'>>;
type NoAesDecrypt = Assert<Absent<'aesDecryptAsync'>>;
type NoAesKeySize = Assert<Absent<'AESKeySize'>>;

// Upstream `expo-crypto@58.0.7` signatures.
type GetRandomBytes = Assert<Same<Api['getRandomBytes'], (byteCount: number) => Uint8Array>>;
type GetRandomBytesAsync = Assert<
  Same<Api['getRandomBytesAsync'], (byteCount: number) => Promise<Uint8Array>>
>;
type DigestStringAsync = Assert<
  Same<
    Api['digestStringAsync'],
    (
      algorithm: CryptoDigestAlgorithm,
      data: string,
      options?: CryptoDigestOptions
    ) => Promise<Digest>
  >
>;
type RandomUuid = Assert<Same<Api['randomUUID'], () => string>>;
type DigestFn = Assert<
  Same<Api['digest'], (algorithm: CryptoDigestAlgorithm, data: BufferSource) => Promise<ArrayBuffer>>
>;
type DigestIsString = Assert<Same<Digest, string>>;
type GetRandomValuesKeepsKind = Assert<
  Same<ReturnType<typeof Crypto.getRandomValues<Int16Array>>, Int16Array>
>;

export type PublicApiChecks = [
  NoAesKey,
  NoAesSealedData,
  NoAesEncrypt,
  NoAesDecrypt,
  NoAesKeySize,
  GetRandomBytes,
  GetRandomBytesAsync,
  DigestStringAsync,
  RandomUuid,
  DigestFn,
  DigestIsString,
  GetRandomValuesKeepsKind,
];
