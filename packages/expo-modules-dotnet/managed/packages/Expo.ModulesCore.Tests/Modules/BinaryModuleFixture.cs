using Expo.ModulesCore;

namespace Expo.ModulesCore.Tests.Modules;

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

  [JS]
  public Memory<byte> MutableSlice(Memory<byte> value)
  {
    value.Span[1] = 42;
    return value.Slice(1, 2);
  }

  [JS]
  public ReadOnlyMemory<byte> ReadOnlySlice(ReadOnlyMemory<byte> value) => value.Slice(1, 2);

  [JS]
  public Memory<byte> CombineMemory(Memory<byte> mutable, ReadOnlyMemory<byte> readOnly)
  {
    var result = new byte[4];
    mutable.Span.Slice(1, 2).CopyTo(result);
    readOnly.Span.Slice(1, 2).CopyTo(result.AsSpan(2));
    return result;
  }

  [JS]
  public Task<ReadOnlyMemory<byte>> ReadOnlySliceAsync(ReadOnlyMemory<byte> value) =>
      Task.FromResult(value.Slice(1, 2));
}
