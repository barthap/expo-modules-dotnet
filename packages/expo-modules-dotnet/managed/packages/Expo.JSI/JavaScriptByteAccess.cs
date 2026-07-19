namespace Expo.JSI;

/// <summary>Invokes a callback with mutable bytes borrowed from a live JavaScript buffer.</summary>
public delegate void JavaScriptBytesAction(Span<byte> bytes);

/// <summary>Invokes a callback with mutable bytes and returns its result.</summary>
public delegate TResult JavaScriptBytesFunc<TResult>(Span<byte> bytes);

/// <summary>Invokes a callback with read-only bytes borrowed from a live JavaScript buffer.</summary>
public delegate void JavaScriptReadOnlyBytesAction(ReadOnlySpan<byte> bytes);

/// <summary>Invokes a callback with read-only bytes and returns its result.</summary>
public delegate TResult JavaScriptReadOnlyBytesFunc<TResult>(ReadOnlySpan<byte> bytes);
