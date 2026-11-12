import type { CryptoDigestOptions, Digest } from './Crypto.types';
import { CryptoDigestAlgorithm, CryptoEncoding } from './Crypto.types';
import ExpoCrypto from './ExpoCrypto';

export * from './Crypto.types';

// The typed array kinds that upstream `expo` exports. This package does not depend on `expo`.
type IntBasedTypedArray = Int8Array | Int16Array | Int32Array;
type UintBasedTypedArray = Uint8Array | Uint8ClampedArray | Uint16Array | Uint32Array;

class CryptoError extends TypeError {
  code = 'ERR_CRYPTO';

  constructor(message: string) {
    super(`expo-crypto: ${message}`);
  }
}

/**
 * Generates completely random bytes using native implementations. The `byteCount` property
 * is a `number` indicating the number of bytes to generate in the form of a `Uint8Array`.
 * @param byteCount - A number within the range from `0` to `1024`. Anything else will throw a `TypeError`.
 * @return An array of random bytes with the same length as the `byteCount`.
 */
export function getRandomBytes(byteCount: number): Uint8Array {
  assertByteCount(byteCount, 'getRandomBytes');
  const byteArray = new Uint8Array(Math.floor(byteCount));
  ExpoCrypto.getRandomValues(byteArray);
  return byteArray;
}

/**
 * Generates completely random bytes using native implementations. The `byteCount` property
 * is a `number` indicating the number of bytes to generate in the form of a `Uint8Array`.
 * @param byteCount - A number within the range from `0` to `1024`. Anything else will throw a `TypeError`.
 * @return A promise that fulfills with an array of random bytes with the same length as the `byteCount`.
 */
export async function getRandomBytesAsync(byteCount: number): Promise<Uint8Array> {
  assertByteCount(byteCount, 'getRandomBytesAsync');
  const byteArray = new Uint8Array(Math.floor(byteCount));
  ExpoCrypto.getRandomValues(byteArray);
  return byteArray;
}

function assertByteCount(value: any, methodName: string): void {
  if (
    typeof value !== 'number' ||
    isNaN(value) ||
    Math.floor(value) < 0 ||
    Math.floor(value) > 1024
  ) {
    throw new TypeError(
      `expo-crypto: ${methodName}(${value}) expected a valid number from range 0...1024`
    );
  }
}

// The join separator is copied from upstream verbatim, so the message matches upstream.
function assertAlgorithm(algorithm: CryptoDigestAlgorithm): void {
  if (!Object.values(CryptoDigestAlgorithm).includes(algorithm)) {
    throw new CryptoError(
      `Invalid algorithm provided. Expected one of: CryptoDigestAlgorithm.${Object.keys(
        CryptoDigestAlgorithm
      ).join(', AlgCryptoDigestAlgorithmorithm.')}`
    );
  }
}

function assertData(data: string): void {
  if (typeof data !== 'string') {
    throw new CryptoError(`Invalid data provided. Expected a string.`);
  }
}

function assertEncoding(encoding: CryptoEncoding): void {
  if (!Object.values(CryptoEncoding).includes(encoding)) {
    throw new CryptoError(
      `Invalid encoding provided. Expected one of: CryptoEncoding.${Object.keys(
        CryptoEncoding
      ).join(', CryptoEncoding.')}`
    );
  }
}

/**
 * The `digestStringAsync()` method of `Crypto` generates a digest of the supplied `data` string with the provided digest `algorithm`.
 * A digest is a short fixed-length value derived from some variable-length input. **Cryptographic digests** should exhibit _collision-resistance_,
 * meaning that it's very difficult to generate multiple inputs that have equal digest values.
 * You can specify the returned string format as one of `CryptoEncoding`. By default, the resolved value will be formatted as a `HEX` string.
 *
 * @param algorithm The cryptographic hash function to use to transform a block of data into a fixed-size output.
 * @param data The value that will be used to generate a digest.
 * @param options Format of the digest string. Defaults to: `CryptoDigestOptions.HEX`.
 * @return Return a Promise which fulfills with a value representing the hashed input.
 */
export async function digestStringAsync(
  algorithm: CryptoDigestAlgorithm,
  data: string,
  options: CryptoDigestOptions = { encoding: CryptoEncoding.HEX }
): Promise<Digest> {
  assertAlgorithm(algorithm);
  assertData(data);
  assertEncoding(options.encoding);

  return await ExpoCrypto.digestStringAsync(algorithm, data, options);
}

// A Uint8Array over the same bytes as `view`. A Uint8Array is returned as is.
function toUint8Array(view: ArrayBufferView): Uint8Array {
  return view instanceof Uint8Array
    ? view
    : new Uint8Array(view.buffer, view.byteOffset, view.byteLength);
}

/**
 * The `getRandomValues()` method of `Crypto` fills a provided `TypedArray` with cryptographically secure random values.
 *
 * @param typedArray An integer based [`TypedArray`](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/TypedArray) to fill with cryptographically secure random values. It modifies the input array in place.
 * @return The input array filled with cryptographically secure random values.
 */
export function getRandomValues<T extends IntBasedTypedArray | UintBasedTypedArray>(
  typedArray: T
): T {
  // Native code takes only a Uint8Array. A DataView or a value that is not a view goes through
  // unchanged, so native decoding rejects it, as upstream native code does.
  const target: unknown =
    ArrayBuffer.isView(typedArray) && !(typedArray instanceof DataView)
      ? toUint8Array(typedArray)
      : typedArray;
  ExpoCrypto.getRandomValues(target as Uint8Array);
  return typedArray;
}

/**
 * The `randomUUID()` method returns a unique identifier based on the V4 UUID spec (RFC4122).
 * It uses cryptographically secure random values to generate the UUID.
 *
 * @return A string containing a newly generated UUIDv4 identifier
 */
export function randomUUID(): string {
  return ExpoCrypto.randomUUID();
}

const digestLengths = {
  [CryptoDigestAlgorithm.SHA1]: 20,
  [CryptoDigestAlgorithm.SHA256]: 32,
  [CryptoDigestAlgorithm.SHA384]: 48,
  [CryptoDigestAlgorithm.SHA512]: 64,
  [CryptoDigestAlgorithm.MD2]: 16,
  [CryptoDigestAlgorithm.MD4]: 16,
  [CryptoDigestAlgorithm.MD5]: 16,
};

// Native `digest` takes only a Uint8Array, while `BufferSource` also allows other views and an
// `ArrayBuffer`. Wrap those in a Uint8Array over the same bytes, as upstream `toTypedArray` does.
function toDigestInput(data: BufferSource): Uint8Array {
  if (ArrayBuffer.isView(data)) {
    return toUint8Array(data);
  }
  return new Uint8Array(data);
}

/**
 * The `digest()` method of `Crypto` generates a digest of the supplied `TypedArray` of bytes `data` with the provided digest `algorithm`.
 * A digest is a short fixed-length value derived from some variable-length input. **Cryptographic digests** should exhibit _collision-resistance_,
 * meaning that it's very difficult to generate multiple inputs that have equal digest values.
 *
 * @param algorithm The cryptographic hash function to use to transform a block of data into a fixed-size output.
 * @param data The value that will be used to generate a digest.
 * @return A Promise which fulfills with an ArrayBuffer representing the hashed input.
 */
export function digest(algorithm: CryptoDigestAlgorithm, data: BufferSource): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    try {
      const output = new Uint8Array(digestLengths[algorithm]);
      ExpoCrypto.digest(algorithm, output, toDigestInput(data));
      resolve(output.buffer);
    } catch (error) {
      reject(error);
    }
  });
}
