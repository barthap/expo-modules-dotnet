using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Expo.JSI;

namespace Expo.ModulesCore.Tests.Modules;

public sealed record BufferPair(ArrayBuffer First, ArrayBuffer Second);

public sealed record CountedBuffer(ArrayBuffer Buffer, int Count);

public sealed record ValuePair(JavaScriptValue First, JavaScriptValue Second);

public sealed record Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer>> Rows);

public readonly record struct Slot(JavaScriptValue Value);

public sealed record Mixed(ArrayBuffer Buffer, JavaScriptValue Value);

// Leaf-free calibration twins for ReleasedValues, which also counts traversal temporaries.
public sealed record TextPair(string First, string Second);

public readonly record struct TextSlot(string Value);

public sealed record TextMixed(string Buffer, string Value);

[ExpoModule("ResourceScope", Classes = new[] { typeof(ResourceScopeEntry) })]
public sealed partial class ResourceScopeModule : Module, IDisposable
{
  private BufferPair? stored;

  public ResourceScopeModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

  [JS]
  public int Measure(ArrayBuffer value) => value.ByteLength;

  [JS]
  public int MeasureOrThrow(ArrayBuffer value) => throw new InvalidOperationException("measure failed");

  [JS]
  public int MeasureTwo(ArrayBuffer first, ArrayBuffer second) => first.ByteLength + second.ByteLength;

  [JS]
  public int MeasureWithCount(ArrayBuffer value, int count) => value.ByteLength + count;

  [JS]
  public async Task<int> MeasureWithCountAsync(ArrayBuffer value, int count)
  {
    await Task.Yield();
    return value.ByteLength + count;
  }

  [JS]
  public ArrayBuffer Same(ArrayBuffer value) => value;

  [JS]
  public ArrayBuffer Retained(ArrayBuffer value) => value.Retain();

  // Leaf-free calibration twin for ReleasedValues, which also counts traversal temporaries.
  [JS]
  public string Text(string value) => value;

  [JS]
  public JavaScriptValue SameValue(JavaScriptValue value) => value;

  [JS]
  public JavaScriptValue DisposedValue()
  {
    var value = RuntimeContext.Runtime.CreateString("gone");
    value.Dispose();
    return value;
  }

  [JS]
  public async Task<JavaScriptValue> SameValueAsync(JavaScriptValue value)
  {
    await Task.Yield();
    return value;
  }

  [JS]
  public async Task<int> LengthAfterAwaitAsync(ArrayBuffer value)
  {
    await Task.Yield();
    return value.ByteLength;
  }

  [JS]
  public async Task<int> FailAfterAwaitAsync(ArrayBuffer value)
  {
    await Task.Yield();
    throw new InvalidOperationException("async failed");
  }

  [JS]
  public async Task<ArrayBuffer> HoldAsync(ArrayBuffer value)
  {
    await Gate.Task;
    return value;
  }

  [JS]
  public async Task<JavaScriptValue> HoldValueAsync(JavaScriptValue value)
  {
    await Gate.Task;
    return value;
  }

  // Leaf-free twin of HoldValueAsync for ReleasedValues calibration.
  [JS]
  public async Task<string> HoldTextAsync(string value)
  {
    await Gate.Task;
    return value;
  }

  [JS]
  public async Task<ArrayBuffer> HoldRetainedAsync(ArrayBuffer value)
  {
    await Gate.Task;
    return value.Retain();
  }

  [JS]
  public ArrayBuffer Pick(ReadOnlySpan<byte> bytes, ArrayBuffer other) => other;

  [JS]
  public int MeasurePair(BufferPair pair) => pair.First.ByteLength + pair.Second.ByteLength;

  [JS]
  public BufferPair EchoPair(BufferPair pair) => pair;

  [JS]
  public int MeasureCounted(CountedBuffer value) => value.Count;

  [JS]
  public ValuePair Twin(JavaScriptValue value)
  {
    var copy = value.Retain();
    return new ValuePair(copy, copy);
  }

  [JS]
  public ValuePair EchoValues(ValuePair pair) => pair;

  [JS]
  public BufferPair BrokenPair(ArrayBuffer value) => new(value.Retain(), null!);

  [JS]
  public IReadOnlyList<ArrayBuffer> EchoList(IReadOnlyList<ArrayBuffer> values) => values;

  [JS]
  public Dictionary<string, ArrayBuffer> EchoMap(Dictionary<string, ArrayBuffer> values) => values;

  [JS]
  public Shelf EchoShelf(Shelf shelf) => shelf;

  [JS]
  public async Task<BufferPair> EchoPairAsync(BufferPair pair)
  {
    await Task.Yield();
    return pair;
  }

  [JS]
  public async Task<BufferPair> HoldPairAsync(BufferPair pair)
  {
    await Gate.Task;
    return pair;
  }

  [JS]
  public string? SlotKind(Slot? slot) => slot?.Value.Kind.ToString();

  [JS]
  public Slot? EchoSlot(Slot? slot) => slot;

  [JS]
  public int CountOptional(IReadOnlyList<ArrayBuffer>? buffers = null) => buffers?.Count ?? -1;

  [JS]
  public TextPair TextTwin(string value) => new(value, value);

  [JS]
  public TextSlot? EchoTextSlot(TextSlot? slot) => slot;

  [JS]
  public Mixed EchoMixed(Mixed value) => value;

  [JS]
  public TextMixed EchoTextMixed(TextMixed value) => value;

  [JS]
  public IReadOnlyList<JavaScriptValue> EchoValueList(IReadOnlyList<JavaScriptValue> values) => values;

  [JS]
  public IReadOnlyList<string> EchoTextList(IReadOnlyList<string> values) => values;

  [JS]
  public int MeasurePairs(IReadOnlyList<BufferPair> pairs) => pairs.Count;

  // Retained copies register only through Transfer, so a Transfer that stopped at the null element
  // would leak them and keep both entries pinned.
  [JS]
  public IReadOnlyList<BufferPair> PairsWithNull(BufferPair pair) =>
      new[] { null!, new BufferPair(pair.First.Retain(), pair.Second.Retain()) };

  // Stores and returns only Retain() copies; the module owns the stored copies until Dispose.
  [JS]
  public BufferPair StoredPair
  {
    get => new(stored!.First.Retain(), stored.Second.Retain());
    set
    {
      var copy = new BufferPair(value.First.Retain(), value.Second.Retain());
      Release(stored);
      stored = copy;
    }
  }

  public void Dispose()
  {
    Release(stored);
    stored = null;
  }

  private static void Release(BufferPair? pair)
  {
    pair?.First.Dispose();
    pair?.Second.Dispose();
  }
}

[ExpoSharedObject("ResourceScopeEntry")]
public sealed partial class ResourceScopeEntry : SharedObject
{
  [JS]
  public ResourceScopeEntry(ArrayBuffer seed, bool fail)
  {
    if (fail)
    {
      throw new InvalidOperationException("entry failed");
    }
    SeedLength = seed.ByteLength;
  }

  public int SeedLength { get; }

  [JS]
  public int Measure(ArrayBuffer value) => value.ByteLength;
}
