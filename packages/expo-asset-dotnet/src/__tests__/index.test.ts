import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockDotnet = vi.hoisted(() => ({
  downloadAsync: vi.fn(),
  requireDotnetModule: vi.fn(),
}));

vi.mock('expo-modules-dotnet', () => ({
  DotnetModule: class {},
  requireDotnetModule: mockDotnet.requireDotnetModule,
}));

describe('downloadAsync', () => {
  beforeEach(() => {
    vi.resetModules();
    mockDotnet.downloadAsync.mockReset();
    mockDotnet.requireDotnetModule.mockReset();
    mockDotnet.requireDotnetModule.mockReturnValue({
      downloadAsync: mockDotnet.downloadAsync,
    });
  });

  it('resolves ExpoAsset and forwards the upstream argument order', async () => {
    const fileUri = 'file:///cache/ExponentAsset-abc.png';
    mockDotnet.downloadAsync.mockResolvedValue(fileUri);
    const facade = await import('../index');

    await expect(facade.downloadAsync('https://example.com/a.png', null, 'png')).resolves.toBe(
      fileUri
    );
    expect(mockDotnet.requireDotnetModule).toHaveBeenCalledWith('ExpoAsset');
    expect(mockDotnet.downloadAsync).toHaveBeenCalledWith(
      'https://example.com/a.png',
      null,
      'png'
    );
  });
});
