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

public sealed record DefaultedBlob(string Name, ArrayBuffer? Data = null);

public sealed record ValuePair(JavaScriptValue First, JavaScriptValue Second);

public sealed record Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer>> Rows);

public readonly record struct Slot(JavaScriptValue Value);

public sealed record Mixed(ArrayBuffer Buffer, JavaScriptValue Value);

// Leaf-free calibration twins for ReleasedValues, which also counts traversal temporaries.
public sealed record TextPair(string First, string Second);

public readonly record struct TextSlot(string Value);

public sealed record TextMixed(string Buffer, string Value);

[ExpoModule("LeafRelease", Classes = new[] { typeof(LeafReleaseEntry), typeof(LeafReleaseOptionalEntry) })]
public sealed partial class LeafReleaseModule : Module, IDisposable
{
  private BufferPair? stored;
  private ArrayBuffer? storedOptional;

  public LeafReleaseModule(DotnetRuntimeContext context)
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
  public JavaScriptValue RetainedValue(JavaScriptValue value) => value.Retain();

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
  public Task<int> ThrowBeforeTaskAsync(ArrayBuffer value) => throw new InvalidOperationException("before task");

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

  // Retain() is a runtime call, so it runs before the await, on the runtime thread.
  [JS]
  public async Task<JavaScriptValue> HoldRetainedValueAsync(JavaScriptValue value)
  {
    var copy = value.Retain();
    await Gate.Task;
    return copy;
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
  public ArrayBuffer PickRetained(ReadOnlySpan<byte> bytes, ArrayBuffer other) => other.Retain();

  [JS]
  public int MeasurePair(BufferPair pair) => pair.First.ByteLength + pair.Second.ByteLength;

  // The echo fixtures return Retain() copies, so only the result release drops the returned leases.
  [JS]
  public BufferPair EchoPair(BufferPair pair) => new(pair.First.Retain(), pair.Second.Retain());

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
  public async Task<BufferPair> BrokenPairAsync(ArrayBuffer value)
  {
    await Task.Yield();
    return new BufferPair(value.Retain(), null!);
  }

  [JS]
  public IReadOnlyList<ArrayBuffer> EchoList(IReadOnlyList<ArrayBuffer> values) =>
      values.Select(value => value.Retain()).ToArray();

  [JS]
  public Dictionary<string, ArrayBuffer> EchoMap(Dictionary<string, ArrayBuffer> values) => RetainEntries(values);

  [JS]
  public Shelf EchoShelf(Shelf shelf) => new(shelf.Rows.Select(RetainEntries).ToArray());

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
    return new BufferPair(pair.First.Retain(), pair.Second.Retain());
  }

  [JS]
  public string? SlotKind(Slot? slot) => slot?.Value.Kind.ToString();

  [JS]
  public Slot? EchoSlot(Slot? slot) => slot is { } value ? new Slot(value.Value.Retain()) : null;

  [JS]
  public int CountOptional(IReadOnlyList<ArrayBuffer>? buffers = null) => buffers?.Count ?? -1;

  [JS]
  public TextPair TextTwin(string value) => new(value, value);

  [JS]
  public TextSlot? EchoTextSlot(TextSlot? slot) => slot;

  [JS]
  public Mixed EchoMixed(Mixed value) => new(value.Buffer.Retain(), value.Value.Retain());

  [JS]
  public TextMixed EchoTextMixed(TextMixed value) => value;

  [JS]
  public IReadOnlyList<JavaScriptValue> EchoValueList(IReadOnlyList<JavaScriptValue> values) =>
      values.Select(value => value.Retain()).ToArray();

  [JS]
  public IReadOnlyList<string> EchoTextList(IReadOnlyList<string> values) => values;

  [JS]
  public int MeasurePairs(IReadOnlyList<BufferPair> pairs) => pairs.Count;

  // Release must skip the null element and still release the retained copies after it. A Release
  // that stopped at the null element would leak them and keep both entries pinned.
  [JS]
  public IReadOnlyList<BufferPair> PairsWithNull(BufferPair pair) =>
      new[] { null!, new BufferPair(pair.First.Retain(), pair.Second.Retain()) };

  // The callback payload is encoded without being consumed, so the author's buffer stays usable.
  [JS]
  public int ForwardBuffer(ArrayBuffer buffer, JavaScriptCallback<ValueTuple<BufferPair>, int> callback)
      => callback.Invoke(ValueTuple.Create(new BufferPair(buffer, buffer))) + buffer.ByteLength;

  [JS]
  public string ForwardValue(JavaScriptValue value, JavaScriptCallback<ValueTuple<ValuePair>, bool> callback)
      => $"{(callback.Invoke(ValueTuple.Create(new ValuePair(value, value))) ? "true" : "false")}:{value.Kind}";

  // Leaf-free twin of ForwardValue for ReleasedValues calibration.
  [JS]
  public string ForwardText(string value, JavaScriptCallback<ValueTuple<TextPair>, bool> callback)
      => $"{(callback.Invoke(ValueTuple.Create(new TextPair(value, value))) ? "true" : "false")}:{value}";

  // The decoded callback result belongs to the author, who disposes every returned buffer.
  [JS]
  public int TakeBuffers(JavaScriptCallback<IReadOnlyList<ArrayBuffer>> callback)
  {
    var buffers = callback.Invoke();
    try
    {
      var total = 0;
      foreach (var buffer in buffers)
      {
        total += buffer.ByteLength;
      }
      return total;
    }
    finally
    {
      foreach (var buffer in buffers)
      {
        buffer.Dispose();
      }
    }
  }

  // The callback result decodes through the owned Decode overload of the record codec.
  [JS]
  public int TakeCounted(JavaScriptCallback<CountedBuffer> callback)
  {
    var counted = callback.Invoke();
    counted.Buffer.Dispose();
    return counted.Count;
  }

  [Event]
  public partial Func<BufferPair, Task> OnPair { get; }

  [JS]
  public int EmitPair(ArrayBuffer buffer)
  {
    OnPair(new BufferPair(buffer, buffer)).GetAwaiter().GetResult();
    return buffer.ByteLength;
  }

  // Retain() is a runtime call, so it runs before the await, on the runtime thread.
  [JS]
  public async Task EmitPairLater(ArrayBuffer buffer)
  {
    using var copy = buffer.Retain();
    await Task.Yield();
    await OnPair(new BufferPair(copy, copy));
  }

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
  public int MeasureBlob(DefaultedBlob blob) => blob.Data?.ByteLength ?? -1;

  [JS]
  public int CountPresent(IReadOnlyList<ArrayBuffer?> values) => values.Count(value => value is not null);

  [JS]
  public IReadOnlyList<ArrayBuffer?> EchoOptionalList(IReadOnlyList<ArrayBuffer?> values) => values;

  [JS]
  public Dictionary<string, ArrayBuffer?> EchoOptionalMap(Dictionary<string, ArrayBuffer?> values) => values;

  // The callback argument stays with the author, so the buffer is still readable afterwards.
  [JS]
  public int ForwardOptionalBuffer(ArrayBuffer? buffer, JavaScriptCallback<ValueTuple<ArrayBuffer?>, int> callback)
      => callback.Invoke(ValueTuple.Create(buffer)) + (buffer?.ByteLength ?? 0);

  // The decoded callback result belongs to the author, who disposes a returned buffer.
  [JS]
  public int TakeOptionalBuffer(JavaScriptCallback<ArrayBuffer?> callback)
  {
    using var buffer = callback.Invoke();
    return buffer?.ByteLength ?? -1;
  }

  // A nullable callback is context-retained, never a leaf holder. The buffer passed to it stays
  // with the author, and the buffer it returns belongs to the author, who disposes it.
  [JS]
  public int ExchangeThroughOptionalCallback(
      ArrayBuffer buffer,
      JavaScriptCallback<ValueTuple<ArrayBuffer>, ArrayBuffer>? callback)
  {
    if (callback is null)
    {
      return -1;
    }

    using var result = callback.Invoke(ValueTuple.Create(buffer));
    return result.ByteLength + buffer.ByteLength;
  }

  [Event]
  public partial Func<IReadOnlyList<ArrayBuffer?>, Task> OnOptionalBuffers { get; }

  [JS]
  public int EmitOptionalBuffers(ArrayBuffer? buffer)
  {
    OnOptionalBuffers(new[] { null, buffer }).GetAwaiter().GetResult();
    return buffer?.ByteLength ?? -1;
  }

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

  private static Dictionary<string, ArrayBuffer> RetainEntries(Dictionary<string, ArrayBuffer> values) =>
      values.ToDictionary(entry => entry.Key, entry => entry.Value.Retain());
}

[ExpoSharedObject("LeafReleaseEntry")]
public sealed partial class LeafReleaseEntry : SharedObject
{
  [JS]
  public LeafReleaseEntry(ArrayBuffer seed, bool fail)
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

  [Event]
  public partial Func<IReadOnlyList<ArrayBuffer>, Task> OnBuffers { get; }

  [JS]
  public int EmitBuffers(ArrayBuffer buffer)
  {
    OnBuffers(new[] { buffer }).GetAwaiter().GetResult();
    return buffer.ByteLength;
  }
}

[ExpoSharedObject("LeafReleaseOptionalEntry")]
public sealed partial class LeafReleaseOptionalEntry : SharedObject
{
  private readonly int seedLength;
  private ArrayBuffer? stored;

  [JS]
  public LeafReleaseOptionalEntry(ArrayBuffer? seed)
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

  [Event]
  public partial Func<OptionalBufferPair, Task> OnOptionalPair { get; }

  // Reads the buffer after the event, so the test proves the payload leaf stays with the author.
  [JS]
  public int EmitOptionalPair(ArrayBuffer? buffer)
  {
    OnOptionalPair(new OptionalBufferPair(null, buffer)).GetAwaiter().GetResult();
    return buffer?.ByteLength ?? -1;
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

[Union]
public abstract record StringOrView
{
  public sealed record Text(string Value) : StringOrView;
  public sealed record View(Uint8Array Value) : StringOrView;
}

[Union]
public abstract record BufferOrView
{
  public sealed record Buffer(ArrayBuffer Value) : BufferOrView;
  public sealed record View(Uint8Array Value) : BufferOrView;
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
  public IReadOnlyList<Uint8Array> EchoList(IReadOnlyList<Uint8Array> views) =>
      views.Select(view => view.Retain()).ToArray();

  [JS]
  public Dictionary<string, Uint8Array> EchoMap(Dictionary<string, Uint8Array> views) =>
      views.ToDictionary(entry => entry.Key, entry => entry.Value.Retain());

  [JS]
  public ViewHolder EchoHolder(ViewHolder holder) => holder with { Bytes = holder.Bytes.Retain() };

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

  [JS]
  public string Write(StringOrView content) => content switch
  {
    StringOrView.Text(var text) => $"text:{text}",
    StringOrView.View(var view) => $"bytes:{Describe(view)}",
    _ => throw new ArgumentOutOfRangeException(nameof(content)),
  };

  [JS]
  public string DescribeBufferOrView(BufferOrView value) => value switch
  {
    BufferOrView.Buffer(var buffer) => $"buffer:{buffer.ByteLength}",
    BufferOrView.View(var view) => $"view:{view.Length}",
    _ => throw new ArgumentOutOfRangeException(nameof(value)),
  };

  [Event]
  public partial Func<Uint8Array?, Task> OnBytes { get; }

  // The view, or null, goes to the event and to the callback, and stays with the author, so the
  // author still reads it afterwards.
  [JS]
  public string Forward(Uint8Array? bytes, JavaScriptCallback<ValueTuple<Uint8Array?>, bool> callback)
  {
    OnBytes(bytes).GetAwaiter().GetResult();
    var seen = callback.Invoke(ValueTuple.Create(bytes));
    return $"{(seen ? "true" : "false")}:{(bytes is null ? "null" : Describe(bytes))}";
  }

  // The decoded callback result belongs to the author, who disposes a returned view.
  [JS]
  public string Take(JavaScriptCallback<Uint8Array?> callback)
  {
    using var bytes = callback.Invoke();
    return bytes is null ? "null" : Describe(bytes);
  }

  public void Dispose()
  {
    held?.Dispose();
    held = null;
    stored?.Dispose();
    stored = null;
  }
}
