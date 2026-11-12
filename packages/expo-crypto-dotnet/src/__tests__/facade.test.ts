import { beforeEach, describe, expect, it, vi } from 'vitest';

// A stand-in for `_expoDotnet.modules.ExpoCrypto`. beforeEach gives `getRandomValues` and
// `digest` fixed outputs, so a test can see exactly which bytes native code received.
const native = vi.hoisted(() => ({
  module: {
    digestString: vi.fn(),
    digestStringAsync: vi.fn(),
    getRandomValues: vi.fn(),
    digest: vi.fn(),
    randomUUID: vi.fn(),
  },
  requireDotnetModule: vi.fn(),
}));

vi.mock('expo-modules-dotnet', () => ({
  DotnetModule: class {},
  requireDotnetModule: native.requireDotnetModule,
}));

const AES_NAMES = [
  'AESEncryptionKey',
  'AESSealedData',
  'aesEncryptAsync',
  'aesDecryptAsync',
  'AESKeySize',
];

describe('expo-crypto-dotnet facade', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.restoreAllMocks();
    for (const fn of Object.values(native.module)) {
      fn.mockReset();
    }
    native.module.getRandomValues.mockImplementation((view: Uint8Array) => {
      view.fill(0x5a);
    });
    // Writes the input length everywhere and the first input byte first, so a test can tell
    // inputs apart.
    native.module.digest.mockImplementation(
      (_algorithm: string, output: Uint8Array, data: Uint8Array) => {
        output.fill(data.length);
        if (data.length > 0) {
          output[0] = data[0];
        }
      }
    );
    native.module.randomUUID.mockReturnValue('native-uuid');
    native.requireDotnetModule.mockReset();
    native.requireDotnetModule.mockReturnValue(native.module);
  });

  it('resolves ExpoCrypto and exports exactly the upstream non-AES API', async () => {
    const crypto = await import('../index');

    expect(native.requireDotnetModule).toHaveBeenCalledWith('ExpoCrypto');
    expect(Object.keys(crypto).sort()).toEqual([
      'CryptoDigestAlgorithm',
      'CryptoEncoding',
      'digest',
      'digestStringAsync',
      'getRandomBytes',
      'getRandomBytesAsync',
      'getRandomValues',
      'randomUUID',
    ]);
    for (const name of AES_NAMES) {
      expect(name in crypto).toBe(false);
    }
  });

  it('keeps the upstream enum values', async () => {
    const { CryptoDigestAlgorithm, CryptoEncoding } = await import('../index');

    expect({ ...CryptoDigestAlgorithm }).toEqual({
      SHA1: 'SHA-1',
      SHA256: 'SHA-256',
      SHA384: 'SHA-384',
      SHA512: 'SHA-512',
      MD2: 'MD2',
      MD4: 'MD4',
      MD5: 'MD5',
    });
    expect({ ...CryptoEncoding }).toEqual({ HEX: 'hex', BASE64: 'base64' });
  });

  it('fills a typed array of any integer kind in place and returns the same object', async () => {
    const { getRandomValues } = await import('../index');
    const a = new Uint32Array(8);
    const sub = new Uint16Array(a.buffer, 4, 4);
    const bytes = new Uint8Array(a.buffer);

    expect(getRandomValues(a)).toBe(a);
    let received = native.module.getRandomValues.mock.calls[0][0];
    expect(received).toBeInstanceOf(Uint8Array);
    expect([received.buffer, received.byteOffset, received.byteLength]).toEqual([a.buffer, 0, 32]);

    bytes.fill(0);
    expect(getRandomValues(sub)).toBe(sub);
    received = native.module.getRandomValues.mock.calls[1][0];
    expect([received.buffer, received.byteOffset, received.byteLength]).toEqual([a.buffer, 4, 8]);
    expect(Array.from(bytes)).toEqual([
      ...new Array(4).fill(0),
      ...new Array(8).fill(0x5a),
      ...new Array(20).fill(0),
    ]);
  });

  it('passes a Uint8Array to native code unchanged', async () => {
    const { getRandomValues } = await import('../index');
    const view = new Uint8Array(new ArrayBuffer(16), 2, 4);

    expect(getRandomValues(view)).toBe(view);
    expect(native.module.getRandomValues.mock.calls[0][0]).toBe(view);
  });

  it('passes a DataView or a non-view to native code unchanged', async () => {
    const { getRandomValues } = await import('../index');
    const dataView = new DataView(new ArrayBuffer(4));
    const buffer = new ArrayBuffer(4);
    // The real native module throws for both; this stand-in only records what it received.
    native.module.getRandomValues.mockImplementation(() => {});

    getRandomValues(dataView as never);
    getRandomValues(buffer as never);
    expect(native.module.getRandomValues.mock.calls[0][0]).toBe(dataView);
    expect(native.module.getRandomValues.mock.calls[1][0]).toBe(buffer);
  });

  it('validates and floors byteCount and never uses Math.random', async () => {
    const { getRandomBytes, getRandomBytesAsync } = await import('../index');
    const random = vi.spyOn(Math, 'random');

    for (const value of [-1, 1025, NaN, '4']) {
      const message = `expo-crypto: getRandomBytes(${value}) expected a valid number from range 0...1024`;
      expect(() => getRandomBytes(value as number)).toThrow(TypeError);
      expect(() => getRandomBytes(value as number)).toThrow(message);
      const rejection = await getRandomBytesAsync(value as number).then(
        () => undefined,
        (reason: unknown) => reason
      );
      expect(rejection).toBeInstanceOf(TypeError);
      expect(rejection).toMatchObject({
        message: message.replace('getRandomBytes(', 'getRandomBytesAsync('),
      });
    }
    expect(getRandomBytes(0)).toEqual(new Uint8Array(0));
    const bytes = getRandomBytes(1024.9);
    expect(bytes).toBeInstanceOf(Uint8Array);
    expect(bytes.length).toBe(1024);
    expect(bytes.every((byte) => byte === 0x5a)).toBe(true);
    const asyncBytes = await getRandomBytesAsync(3.7);
    expect(Array.from(asyncBytes)).toEqual([0x5a, 0x5a, 0x5a]);
    expect(random).not.toHaveBeenCalled();
  });

  it('forwards digestStringAsync with the upstream default options', async () => {
    const { CryptoDigestAlgorithm, CryptoEncoding, digestStringAsync } = await import('../index');
    native.module.digestStringAsync.mockResolvedValue('digest');

    await expect(digestStringAsync(CryptoDigestAlgorithm.SHA256, 'Expo')).resolves.toBe('digest');
    await expect(
      digestStringAsync(CryptoDigestAlgorithm.MD5, 'Expo', { encoding: CryptoEncoding.BASE64 })
    ).resolves.toBe('digest');
    expect(native.module.digestStringAsync.mock.calls).toEqual([
      ['SHA-256', 'Expo', { encoding: 'hex' }],
      ['MD5', 'Expo', { encoding: 'base64' }],
    ]);
  });

  it('rejects invalid digestStringAsync input with CryptoError before native code runs', async () => {
    const { digestStringAsync } = await import('../index');
    const cases: [unknown, unknown, unknown, string][] = [
      [
        'SHA-224',
        'Expo',
        { encoding: 'hex' },
        'expo-crypto: Invalid algorithm provided. Expected one of: CryptoDigestAlgorithm.SHA1, AlgCryptoDigestAlgorithmorithm.SHA256, AlgCryptoDigestAlgorithmorithm.SHA384, AlgCryptoDigestAlgorithmorithm.SHA512, AlgCryptoDigestAlgorithmorithm.MD2, AlgCryptoDigestAlgorithmorithm.MD4, AlgCryptoDigestAlgorithmorithm.MD5',
      ],
      ['SHA-256', 42, { encoding: 'hex' }, 'expo-crypto: Invalid data provided. Expected a string.'],
      [
        'SHA-256',
        'Expo',
        { encoding: 'utf8' },
        'expo-crypto: Invalid encoding provided. Expected one of: CryptoEncoding.HEX, CryptoEncoding.BASE64',
      ],
    ];

    for (const [algorithm, data, options, message] of cases) {
      const error = await digestStringAsync(algorithm as never, data as never, options as never).then(
        () => undefined,
        (reason: unknown) => reason
      );
      expect(error).toBeInstanceOf(TypeError);
      expect(error).toMatchObject({ code: 'ERR_CRYPTO', message });
    }
    expect(native.module.digestStringAsync).not.toHaveBeenCalled();
  });

  it('digests every buffer source into a new ArrayBuffer of the upstream length', async () => {
    const { CryptoDigestAlgorithm, digest } = await import('../index');
    // The same 8 bytes, 1 to 8, each in its own buffer; the subview and the DataView sit at
    // offset 2 of a 12-byte buffer.
    const eight = () => new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
    const padded = () => new Uint8Array([9, 9, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9]).buffer;
    const sources: BufferSource[] = [
      eight().buffer,
      new Uint8Array(padded(), 2, 8),
      new Int16Array(eight().buffer),
      new DataView(padded(), 2, 8),
    ];

    for (const source of sources) {
      const result = await digest(CryptoDigestAlgorithm.SHA256, source);
      const sourceBuffer = source instanceof ArrayBuffer ? source : source.buffer;
      const [, output, data] = native.module.digest.mock.calls.at(-1)!;
      expect(data).toBeInstanceOf(Uint8Array);
      expect(Array.from(data)).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
      expect(data.buffer).toBe(sourceBuffer);
      expect(result).toBeInstanceOf(ArrayBuffer);
      expect(result.byteLength).toBe(32);
      expect(result).toBe(output.buffer);
      expect(result).not.toBe(sourceBuffer);
      new Uint8Array(sourceBuffer).fill(0);
      expect(new Uint8Array(result)[0]).toBe(1);
    }

    const lengths: [string, number][] = [
      ['SHA-1', 20],
      ['SHA-256', 32],
      ['SHA-384', 48],
      ['SHA-512', 64],
      ['MD2', 16],
      ['MD4', 16],
      ['MD5', 16],
    ];
    for (const [algorithm, length] of lengths) {
      const result = await digest(algorithm as never, new Uint8Array(1));
      expect(result.byteLength).toBe(length);
    }
  });

  it('rejects instead of throwing when native digest throws', async () => {
    const { digest } = await import('../index');
    const failure = new Error("'MD2' is not a valid ExpoCryptoDotnet.DigestAlgorithm value.");
    native.module.digest.mockImplementationOnce(() => {
      throw failure;
    });

    let result: Promise<ArrayBuffer> | undefined;
    expect(() => {
      result = digest('MD2' as never, new Uint8Array(1));
    }).not.toThrow();
    await expect(result).rejects.toBe(failure);
  });

  it('returns the native UUID', async () => {
    const { randomUUID } = await import('../index');

    expect(randomUUID()).toBe('native-uuid');
    expect(native.module.randomUUID).toHaveBeenCalledTimes(1);
  });
});
