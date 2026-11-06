using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Expo.JSI;

namespace Expo.ModulesCore.Tests.Modules;

public sealed record BufferPair(ArrayBuffer First, ArrayBuffer Second);

public sealed record CountedBuffer(ArrayBuffer Buffer, int Count);

public sealed record OptionalBufferPair(ArrayBuffer? First, ArrayBuffer? Second);

public sealed record OptionalCountedBuffer(ArrayBuffer? Buffer, int Count);

public sealed record ValuePair(JavaScriptValue First, JavaScriptValue Second);

public sealed record Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer>> Rows);

public readonly record struct Slot(JavaScriptValue Value);

public sealed record Mixed(ArrayBuffer Buffer, JavaScriptValue Value);

// Leaf-free calibration twins for ReleasedValues, which also counts traversal temporaries.
public sealed record TextPair(string First, string Second);

public readonly record struct TextSlot(string Value);

public sealed record TextMixed(string Buffer, string Value);

[ExpoModule("ResourceScope", Classes = new[] { typeof(ResourceScopeEntry), typeof(ResourceScopeOptionalEntry) })]
public sealed partial class ResourceScopeModule : Module, IDisposable
{
  private BufferPair? stored;
  private ArrayBuffer? storedOptional;

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

  [JS]
  public int MeasureOptional(ArrayBuffer? value) => value?.ByteLength ?? -1;

  [JS]
  public async Task<int> MeasureOptionalAfterAwaitAsync(ArrayBuffer? value)
  {
    await Task.Yield();
    return value?.ByteLength ?? -1;
  }

  [JS]
  public int MeasureDefaulted(ArrayBuffer? value = null) => value?.ByteLength ?? -1;

  [JS]
  public ArrayBuffer? EchoOptional(ArrayBuffer? value) => value;

  [JS]
  public async Task<ArrayBuffer?> EchoOptionalAsync(ArrayBuffer? value)
  {
    await Task.Yield();
    return value;
  }

  // Stores and returns only Retain() copies; the module owns the stored copy until Dispose.
  [JS]
  public ArrayBuffer? OptionalBuffer
  {
    get => storedOptional?.Retain();
    set
    {
      var copy = value?.Retain();
      storedOptional?.Dispose();
      storedOptional = copy;
    }
  }

  [JS]
  public int MeasureOptionalPair(OptionalBufferPair pair) => (pair.First?.ByteLength ?? 0) + (pair.Second?.ByteLength ?? 0);

  [JS]
  public OptionalBufferPair EchoOptionalPair(OptionalBufferPair pair) => pair;

  [JS]
  public int MeasureOptionalCounted(OptionalCountedBuffer value) => value.Count;

  [JS]
  public int CountPresent(IReadOnlyList<ArrayBuffer?> values) => values.Count(value => value is not null);

  [JS]
  public IReadOnlyList<ArrayBuffer?> EchoOptionalList(IReadOnlyList<ArrayBuffer?> values) => values;

  [JS]
  public Dictionary<string, ArrayBuffer?> EchoOptionalMap(Dictionary<string, ArrayBuffer?> values) => values;

  public void Dispose()
  {
    Release(stored);
    stored = null;
    storedOptional?.Dispose();
    storedOptional = null;
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

[ExpoSharedObject("ResourceScopeOptionalEntry")]
public sealed partial class ResourceScopeOptionalEntry : SharedObject
{
  private readonly int seedLength;
  private ArrayBuffer? stored;

  [JS]
  public ResourceScopeOptionalEntry(ArrayBuffer? seed)
  {
    seedLength = seed?.ByteLength ?? -1;
  }

  [JS]
  public int ReadSeed() => seedLength;

  [JS]
  public int Measure(ArrayBuffer? value) => value?.ByteLength ?? -1;

  // Stores and returns only Retain() copies; the entry owns the stored copy until OnRelease.
  [JS]
  public ArrayBuffer? Buffer
  {
    get => stored?.Retain();
    set
    {
      var copy = value?.Retain();
      stored?.Dispose();
      stored = copy;
    }
  }

  protected override void OnRelease()
  {
    stored?.Dispose();
    stored = null;
  }
}

public sealed record ViewHolder(Uint8Array Bytes, string Label);

[ExpoSharedObject]
public sealed partial class TypedArrayEntry : SharedObject
{
  private readonly string seed;

  [JS]
  public TypedArrayEntry(Uint8Array seed)
  {
    this.seed = TypedArrayThingsModule.Describe(seed);
  }

  [JS]
  public string ReadSeed() => seed;

  [JS]
  public string Describe(Uint8Array bytes) => TypedArrayThingsModule.Describe(bytes);
}

// Owns TypedArrayEntry, so it does not report EXPOJSI024. No other module may list it.
[ExpoModule("TypedArrayThings", Classes = new[] { typeof(TypedArrayEntry) })]
public sealed partial class TypedArrayThingsModule : Module, IDisposable
{
  private Uint8Array? held;
  private Uint8Array? stored;

  public TypedArrayThingsModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  // Counts every authored read of a view, so a test proves a failed decode never reached authored code.
  public static int Calls;

  // "{ByteOffset}:{Length}:{uppercase hex}". Synchronous, so runtime thread only.
  public static string Describe(Uint8Array bytes)
  {
    Calls++;
    return $"{bytes.ByteOffset}:{bytes.Length}:{Convert.ToHexString(bytes.ToArray())}";
  }

  [JS]
  public void Fill(Uint8Array bytes)
  {
    Calls++;
    bytes.WithBytes(span => span.Fill(0x42));
  }

  [JS]
  public string FillOptional(Uint8Array? bytes) => bytes is null ? "null" : Describe(bytes);

  [JS]
  public int FailAfterRead(Uint8Array bytes)
  {
    Calls++;
    throw new InvalidOperationException("view failed");
  }

  [JS]
  public string Hex(Uint8Array bytes) => Describe(bytes);

  [JS]
  public async Task<string> HexLater(Uint8Array bytes)
  {
    await Task.Yield();
    var copy = await bytes.ToArrayAsync();
    Calls++;
    return $"{bytes.ByteOffset}:{bytes.Length}:{Convert.ToHexString(copy)}";
  }

  [JS]
  public Uint8Array Make() => Uint8Array.CopyFrom(new byte[] { 1, 2, 3 });

  [JS]
  public Uint8Array Echo(Uint8Array bytes) => bytes.Retain();

  [JS]
  public async Task<Uint8Array> EchoLater(Uint8Array bytes)
  {
    await Task.Yield();
    return bytes.Retain();
  }

  // Stores and returns only Retain() copies; the module owns the stored copy until Dispose.
  [JS]
  public Uint8Array? Stored
  {
    get => stored?.Retain();
    set
    {
      var copy = value?.Retain();
      stored?.Dispose();
      stored = copy;
    }
  }

  [JS]
  public string HexAll(IReadOnlyList<Uint8Array> parts) => string.Join("|", parts.Select(Describe));

  [JS]
  public IReadOnlyList<Uint8Array> EchoList(IReadOnlyList<Uint8Array> views) => views;

  [JS]
  public Dictionary<string, Uint8Array> EchoMap(Dictionary<string, Uint8Array> views) => views;

  [JS]
  public ViewHolder EchoHolder(ViewHolder holder) => holder;

  [JS]
  public void Hold(Uint8Array bytes)
  {
    held?.Dispose();
    held = bytes.Retain();
  }

  [JS]
  public string ReadHeld() => held is null ? "none" : Describe(held);

  [JS]
  public void ReleaseHeld()
  {
    held?.Dispose();
    held = null;
  }

  [JS]
  public int TakeBuffer(ArrayBuffer buffer) => buffer.ByteLength;

  [JS]
  public int TakeBytes(byte[] bytes) => bytes.Length;

  public void Dispose()
  {
    held?.Dispose();
    held = null;
    stored?.Dispose();
    stored = null;
  }
}
