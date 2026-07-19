namespace Expo.ModulesCore;

public delegate void ArrayBufferBytesAction(Span<byte> bytes);
public delegate TResult ArrayBufferBytesFunc<TResult>(Span<byte> bytes);
public delegate void ArrayBufferReadOnlyBytesAction(ReadOnlySpan<byte> bytes);
public delegate TResult ArrayBufferReadOnlyBytesFunc<TResult>(ReadOnlySpan<byte> bytes);
