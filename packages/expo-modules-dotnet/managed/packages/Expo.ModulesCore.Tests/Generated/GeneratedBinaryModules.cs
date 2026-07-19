using Expo.ModulesCore;

namespace Expo.ModulesCore.Tests.Generated;

[ExpoModule("Binary")]
public sealed class GeneratedBinaryModule
{
  private readonly byte[] returnedBytes = [4, 5, 6];

  [JS]
  public ArrayBuffer Echo(ArrayBuffer value) => value.Retain();

  [JS]
  public Task<ArrayBuffer> EchoAsync(ArrayBuffer value) => Task.FromResult(value.Retain());

  [JS]
  public byte[] EchoBytes(byte[] value) => value;

  [JS]
  public ArrayBuffer Allocate(int length) => ArrayBuffer.Allocate(length);

  [JS]
  public void Fill(Span<byte> bytes) => bytes.Fill(9);

  [JS]
  public int Sum(ReadOnlySpan<byte> bytes) => bytes.ToArray().Sum(value => value);

  [JS]
  public ArrayBuffer Transform(ReadOnlySpan<byte> bytes) => ArrayBuffer.CopyFrom(bytes);

  [JS]
  public ReadOnlySpan<byte> ReturnView() => returnedBytes;
}
