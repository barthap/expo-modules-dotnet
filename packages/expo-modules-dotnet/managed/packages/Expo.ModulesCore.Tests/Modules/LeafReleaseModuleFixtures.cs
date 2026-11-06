using System;
using System.Collections.Generic;
using System.Linq;
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

[ExpoModule("LeafRelease", Classes = new[] { typeof(LeafReleaseEntry) })]
public sealed partial class LeafReleaseModule : Module, IDisposable
{
  private BufferPair? stored;

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
  public Slot? EchoSlot(Slot? slot) => slot;

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
  public IReadOnlyList<JavaScriptValue> EchoValueList(IReadOnlyList<JavaScriptValue> values) => values;

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
