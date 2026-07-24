namespace Expo.ModulesCore;

/// <summary>Receives writable buffer bytes for a synchronous callback.</summary>
/// <remarks>The span is valid only until the callback returns.</remarks>
public delegate void ArrayBufferBytesAction(Span<byte> bytes);

/// <summary>Receives writable buffer bytes for a synchronous callback and returns a result.</summary>
/// <remarks>The span is valid only until the callback returns.</remarks>
public delegate TResult ArrayBufferBytesFunc<TResult>(Span<byte> bytes);

/// <summary>Receives readable buffer bytes for a synchronous callback.</summary>
/// <remarks>The span is valid only until the callback returns.</remarks>
public delegate void ArrayBufferReadOnlyBytesAction(ReadOnlySpan<byte> bytes);

/// <summary>Receives readable buffer bytes for a synchronous callback and returns a result.</summary>
/// <remarks>The span is valid only until the callback returns.</remarks>
public delegate TResult ArrayBufferReadOnlyBytesFunc<TResult>(ReadOnlySpan<byte> bytes);
