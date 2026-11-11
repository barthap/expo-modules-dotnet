using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore.Tests.Modules;

[ExpoModule("GeneratedMath")]
public sealed partial class GeneratedMathModule
{
  public double? LastNullable { get; private set; }
  public int? LastNullableInt { get; private set; }

  [JS("add")]
  public double Add(double a, double b) => a + b;

  [JS]
  public double AddOneWhen(double value, bool shouldAddOne) =>
      shouldAddOne ? value + 1.0 : value;

  [JS]
  public void StoreNullable(double? value)
  {
    LastNullable = value;
  }

  [JS]
  public void StoreNullableWithDefault(double? value = 42.0)
  {
    LastNullable = value;
  }

  [JS]
  public double? ReadNullable() => LastNullable;

  [JS]
  public int RoundTripInt(int value) => value;

  [JS]
  public uint RoundTripUInt(uint value) => value;

  [JS]
  public float RoundTripFloat(float value) => value;

  [JS]
  public void StoreNullableInt(int? value)
  {
    LastNullableInt = value;
  }

  [JS]
  public int? ReadNullableInt() => LastNullableInt;
}

[ExpoModule]
public sealed partial class GeneratedTextModule
{
  [JS("greet")]
  public string Greet(string name) => $"Hello, {name}";

  [JS]
  public Guid RoundTripGuid(Guid value) => value;

  [JS]
  public Uri RoundTripUri(Uri value) => value;

  [JS]
  public DateTimeOffset RoundTripDateTimeOffset(DateTimeOffset value) => value;

  [JS]
  public TimeSpan RoundTripTimeSpan(TimeSpan value) => value;
}

[ExpoModule("GeneratedValues")]
public sealed partial class GeneratedValuesModule : Module, IDisposable
{
  private JavaScriptValue? storedValue;

  public GeneratedValuesModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [JS]
  public string ReadKind(JavaScriptValue value) => value.Kind.ToString();

  [JS]
  public async Task<string> ReadKindAsync(JavaScriptValue value)
  {
    await Task.Yield();
    return await RuntimeContext.Runtime.ExecuteAsync(_ => value.Kind.ToString());
  }

  [JS]
  public JavaScriptValue CreateString()
  {
    return RuntimeContext.Runtime.CreateString("created");
  }

  [JS]
  public void StoreString()
  {
    storedValue?.Dispose();
    storedValue = RuntimeContext.Runtime.CreateString("stored");
  }

  [JS]
  public JavaScriptValue ReadStoredString()
  {
    return storedValue?.Retain() ??
        throw new InvalidOperationException("No stored value.");
  }

  public void Dispose()
  {
    storedValue?.Dispose();
    storedValue = null;
  }
}

[ExpoModule("GeneratedProperties")]
public sealed partial class GeneratedPropertiesModule : IDisposable
{
  private JavaScriptValue? storedValue;
  private ArrayBuffer? storedBuffer;
  private bool privateSetterValue = true;
  private int privateSetterCallCount;
  private int count;
  private int countSetterCallCount;

  [JS]
  public bool Ready { get; set; }

  [JS]
  public bool IsReadOnly => true;

  [JS]
  public bool PrivateSetter
  {
    get => privateSetterValue;
    private set
    {
      privateSetterCallCount++;
      privateSetterValue = value;
    }
  }

  [JS]
  public int PrivateSetterCallCount => privateSetterCallCount;

  [JS]
  internal string InternalGetter { get; private set; } = "internal";

  [JS("isReady")]
  public bool ReadyWithExplicitName => Ready;

  [JS]
  public string ThrowingGetter => throw new InvalidOperationException("getter failed");

  [JS]
  public int Count
  {
    get => count;
    set
    {
      countSetterCallCount++;
      count = value;
    }
  }

  [JS]
  public int CountSetterCallCount => countSetterCallCount;

  [JS]
  public JavaScriptValue Value
  {
    get => storedValue?.Retain() ?? throw new InvalidOperationException("No value.");
    set
    {
      var retained = value.Retain();
      storedValue?.Dispose();
      storedValue = retained;
    }
  }

  [JS]
  public ArrayBuffer Buffer
  {
    get => storedBuffer?.Retain() ?? throw new InvalidOperationException("No buffer.");
    set
    {
      var retained = value.Retain();
      storedBuffer?.Dispose();
      storedBuffer = retained;
    }
  }

  public void Dispose()
  {
    storedValue?.Dispose();
    storedValue = null;
    storedBuffer?.Dispose();
    storedBuffer = null;
  }
}

[ExpoModule("GeneratedArray")]
public sealed partial class GeneratedArrayModule
{
  [JS("sum")]
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  [JS("labels")]
  public IReadOnlyList<string> Labels() => ["one", "two"];
}

public record CollectionHolder(IReadOnlyList<int> Values, Dictionary<string, string> Labels);

[ExpoModule("GeneratedNestedCollections")]
public sealed partial class GeneratedNestedCollectionsModule : Module
{
  public GeneratedNestedCollectionsModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [Event]
  public partial Func<IReadOnlyList<int>, Task> Changed { get; }

  [JS]
  public IReadOnlyList<IReadOnlyList<int>> Grid(IReadOnlyList<IReadOnlyList<int>> grid) => grid;

  [JS]
  public IReadOnlyDictionary<string, IReadOnlyList<string>> Groups(
      IReadOnlyDictionary<string, IReadOnlyList<string>> groups) => groups;

  [JS]
  public CollectionHolder Holder(CollectionHolder holder) => holder;

  [JS]
  public int CountAll(JavaScriptCallback<IReadOnlyList<int>> source) => source.Invoke().Sum();

  [JS]
  public Task Raise(IReadOnlyList<int> values) => Changed(values);
}

[ExpoModule("GeneratedCallbacks")]
public sealed partial class GeneratedCallbacksModule
{
  private JavaScriptCallback<ValueTuple<string>, string>? stored;

  [JS("callNow")]
  public string CallNow(string value, JavaScriptCallback<ValueTuple<string>, string> callback) =>
      callback.Invoke(ValueTuple.Create(value));

  [JS("callExplicitTuple")]
  public string CallExplicitTuple(
      string first,
      string second,
      JavaScriptCallback<ValueTuple<string, string>, string> callback) =>
      callback.Invoke(ValueTuple.Create(first, second));

  [JS("callNoArgs")]
  public string CallNoArgs(JavaScriptCallback<string> callback) =>
      callback.Invoke();

  [JS("store")]
  public void Store(JavaScriptCallback<ValueTuple<string>, string> callback) => stored = callback;

  [JS("callStored")]
  public Task<string> CallStored(string value)
  {
    if (stored is null)
    {
      throw new InvalidOperationException("Callback has not been stored.");
    }

    return stored.InvokeAsync(ValueTuple.Create(value));
  }
}

[ExpoModule("GeneratedNullableCallbacks", Classes = new[] { typeof(NullableCallbackEntry) })]
public sealed partial class GeneratedNullableCallbacksModule
{
  // The last callback CallOptional received, so tests can reach it after the call returns.
  public static JavaScriptCallback<int>? LastOptional { get; private set; }

  [JS("callOptional")]
  public int CallOptional(JavaScriptCallback<int>? callback)
  {
    LastOptional = callback;
    return callback?.Invoke() ?? -1;
  }

  [JS("callStoredOptional")]
  public int CallStoredOptional() => LastOptional?.Invoke() ?? -1;

  [JS("disposeStoredOptional")]
  public void DisposeStoredOptional() => LastOptional?.Dispose();

  [JS("callOptionalAfterAwait")]
  public async Task<int> CallOptionalAfterAwaitAsync(JavaScriptCallback<int>? callback)
  {
    await Task.Yield();
    if (callback is null)
    {
      return -1;
    }

    return await callback.InvokeAsync();
  }

  [JS("callDefaulted")]
  public string CallDefaulted(JavaScriptCallback<ValueTuple<string>, string>? callback = null) =>
      callback?.Invoke(ValueTuple.Create("JS")) ?? "null";
}

[ExpoSharedObject]
public sealed partial class NullableCallbackEntry : SharedObject
{
  [JS]
  public NullableCallbackEntry(JavaScriptCallback<int>? seed)
  {
    Seed = seed?.Invoke() ?? -1;
  }

  [JS]
  public int Seed { get; }

  [JS]
  public string Call(JavaScriptCallback<ValueTuple<string>, string>? callback = null) =>
      callback?.Invoke(ValueTuple.Create("entry")) ?? "null";
}

public record CodecUser(string Name, int Age);

public record class CodecUserClass(string Name, int Age);

public readonly record struct CodecUserStruct(string Name, int Age);

public record CodecAddress(string City);

public enum CodecRecordStatus
{
  Draft,
  Published,
}

public record CodecUserWithAddress(string Name, CodecAddress Address, CodecRecordStatus Status);

public record CodecNullableUser(string Name, int? LuckyNumber);

[ExpoModule("GeneratedRecords")]
public sealed partial class GeneratedRecordsModule
{
  [JS("rename")]
  public CodecUser Rename(CodecUser user) => user with { Name = user.Name + "!" };

  [JS("renameClass")]
  public CodecUserClass RenameClass(CodecUserClass user) => user with { Name = user.Name + "!" };

  [JS("renameStruct")]
  public CodecUserStruct RenameStruct(CodecUserStruct user) =>
      user with { Name = user.Name + "!" };

  [JS("moveNested")]
  public CodecUserWithAddress MoveNested(CodecUserWithAddress user) =>
      user with
      {
        Address = user.Address with { City = user.Address.City + "!" },
        Status = CodecRecordStatus.Published,
      };

  [JS("renameNullable")]
  public CodecNullableUser RenameNullable(CodecNullableUser user) => user;
}

public record NullableLabel(string Text);

public record NullableProfile(string Name, string? Nickname);

[ExpoModule("GeneratedNullable")]
public sealed partial class GeneratedNullableModule
{
  public string? LastText { get; private set; }

  public bool StrictCallSeen { get; private set; }

  [JS]
  public void StoreText(string? value)
  {
    LastText = value;
  }

  [JS]
  public void StoreTextWithDefault(string? value = "fallback")
  {
    LastText = value;
  }

  [JS]
  public string? ReadText() => LastText;

  [JS]
  public Task<string?> ReadTextAsync() => Task.FromResult(LastText);

  // The non-nullable counterpart proves the decode boundary stayed strict: authored code must not
  // run at all when JavaScript passes null.
  [JS]
  public string RequireText(string value)
  {
    StrictCallSeen = true;
    return value;
  }

  [JS]
  public bool ReadStrictCallSeen() => StrictCallSeen;

  [JS]
  public string? Text { get; set; }

  [JS]
  public NullableLabel? EchoLabel(NullableLabel? value) => value;

  [JS]
  public NullableProfile EchoProfile(NullableProfile value) => value;

  [JS]
  public IReadOnlyList<string>? EchoList(IReadOnlyList<string>? values) => values;

  [JS]
  public Dictionary<string, string>? EchoMap(Dictionary<string, string>? values) => values;

  [JS]
  public IReadOnlyDictionary<string, string>? EchoReadOnlyMap(
      IReadOnlyDictionary<string, string>? values) => values;

  [JS]
  public IReadOnlyList<string?> EchoElements(IReadOnlyList<string?> values) => values;

  [JS]
  public Dictionary<string, string?> EchoValues(Dictionary<string, string?> values) => values;

  [JS]
  public byte[]? EchoBytes(byte[]? value) => value;
}

[ExpoModule("GeneratedEvents")]
[Events("onChange", "onReady")]
public sealed partial class GeneratedEventsModule : Module
{
  private string started = string.Empty;
  private string stopped = string.Empty;

  public GeneratedEventsModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [OnStartObserving]
  public void Start(string eventName)
  {
    started = eventName;
  }

  [OnStopObserving("onChange")]
  public void Stop()
  {
    stopped = "onChange";
  }

  [JS]
  public Task EmitChangeAsync(string value) =>
      RuntimeContext.Events.EmitAsync<StringCodec, string>(this, "onChange", value);

  [JS]
  public Task EmitReadyAsync() =>
      RuntimeContext.Events.EmitAsync(this, "onReady");

  [JS]
  public Task EmitUndeclaredAsync() =>
      RuntimeContext.Events.EmitAsync(this, "missing");

  [JS]
  public string ReadStarted() => started;

  [JS]
  public string ReadStopped() => stopped;
}

public readonly record struct TypedProgress(int Percent);

public readonly record struct SharedEventPayload(int Value);

[ExpoModule("GeneratedTypedEvents")]
[Events("onLegacy")]
public sealed partial class GeneratedTypedEventsModule : Module
{
  public GeneratedTypedEventsModule(DotnetRuntimeContext context)
      : base(context)
  {
    try
    {
      _ = OnReady;
    }
    catch (InvalidOperationException exception)
    {
      ConstructorEventError = exception.Message;
    }
  }

  [Event]
  public partial Func<Task> OnReady { get; }

  [Event]
  public partial Func<string, Task> OnChange { get; }

  [Event]
  public partial Func<TypedProgress, Task> OnProgress { get; }

  [Event]
  public partial Func<JavaScriptValue, Task> OnValue { get; }

  [Event]
  public partial Func<ArrayBuffer, Task> OnBuffer { get; }

  [Event]
  public partial Func<ArrayBuffer?, Task> OnOptionalBuffer { get; }

  [Event]
  public partial Func<string?, Task> OnText { get; }

  public string? ConstructorEventError { get; }

  public Delegate? ReadySeenOnCreate { get; private set; }

  public string Started { get; private set; } = string.Empty;

  public string Stopped { get; private set; } = string.Empty;

  [OnCreate]
  public void Create() => ReadySeenOnCreate = OnReady;

  [OnStartObserving("onChange")]
  public void Start() => Started = "onChange";

  [OnStopObserving("onChange")]
  public void Stop() => Stopped = "onChange";

  public Task EmitLegacyAsync(string value) =>
      SendEventAsync<StringCodec, string>("onLegacy", value);
}

[ExpoModule("SharedThings", Classes = new[]
{
  typeof(CounterEntry),
  typeof(SnapshotEntry),
  typeof(TrackedRef),
  typeof(SiblingEntry),
})]
public sealed partial class SharedThingsModule
{
  public static CounterEntry? LastSeenCounter;

  public static TaskCompletionSource<CounterEntry>? PendingCounter;

  [JS]
  public CounterEntry MakeCounter(double start) => new(start);

  [JS]
  public CounterEntry EchoCounter(CounterEntry entry)
  {
    LastSeenCounter = entry;
    return entry;
  }

  [JS]
  public double ReadCounter(CounterEntry entry) => entry.Current;

  [JS]
  public async Task<CounterEntry> MakeCounterLater(double start)
  {
    await Task.Yield();
    return new CounterEntry(start);
  }

  [JS]
  public Task<CounterEntry> AwaitPendingCounter() => PendingCounter!.Task;

  [JS]
  public SnapshotEntry MakeSnapshot() => new();

  [JS]
  public TrackedRef WrapTracked() => new(new TrackedResource());
}

[ExpoSharedObject]
public sealed partial class CounterEntry : SharedObject
{
  public static int ReleaseCount;

  [JS]
  public CounterEntry(double start)
  {
    Current = start;
  }

  [JS]
  public double Current { get; set; }

  [Event]
  public partial Func<double, Task> OnChange { get; }

  [Event]
  public partial Func<Task> OnReady { get; }

  [Event]
  public partial Func<SharedEventPayload, Task> OnPayload { get; }

  [Event]
  public partial Func<JavaScriptValue, Task> OnEventValue { get; }

  [Event]
  public partial Func<ArrayBuffer, Task> OnBuffer { get; }

  [Event]
  public partial Func<ArrayBuffer?, Task> OnOptionalBuffer { get; }

  [JS("increment")]
  public double Increment(double by)
  {
    Current += by;
    return Current;
  }

  [JS]
  public async Task<double> IncrementLater(double by)
  {
    await Task.Yield();
    Current += by;
    return Current;
  }

  [JS]
  public async Task<double> IncrementAndEmitAsync(double by)
  {
    Current += by;
    await OnChange(Current);
    return Current;
  }

  public Task EmitReadyAsync() => OnReady();

  public Task EmitPayloadAsync(int value) => OnPayload(new SharedEventPayload(value));

  public Task EmitValueAsync(JavaScriptValue value) => OnEventValue(value);

  public Task EmitBufferAsync(ArrayBuffer value) => OnBuffer(value);

  public Task EmitOptionalBufferAsync(ArrayBuffer? value) => OnOptionalBuffer(value);

  protected override void OnRelease() => ReleaseCount++;
}

[ExpoSharedObject("NativeSnapshot")]
public sealed partial class SnapshotEntry : SharedObject
{
  [JS]
  public double Stamp => 7;
}

public sealed class TrackedResource : IDisposable
{
  public bool Disposed { get; private set; }

  public void Dispose() => Disposed = true;
}

[ExpoSharedObject]
public sealed partial class TrackedRef : SharedRef<TrackedResource>
{
  public static TrackedRef? LastCreated;

  public TrackedRef(TrackedResource reference)
      : base(reference)
  {
    LastCreated = this;
  }

  [JS]
  public bool IsDisposed => Ref.Disposed;
}

[ExpoSharedObject("Sibling")]
public sealed partial class SiblingEntry : SharedObject
{
  [JS]
  public SiblingEntry()
  {
  }

  [JS]
  public double Kind => 1;

  [JS]
  public string? Label { get; set; }

  [JS]
  public string? EchoLabel(string? value) => value;
}

[ExpoModule("NullableSharedThings", Classes = new[]
{
  typeof(OptionalEntry),
  typeof(OptionalTrackedRef),
})]
public sealed partial class NullableSharedThingsModule
{
  // The last value Echo or Describe received, so tests can check identity and null from C#.
  public static OptionalEntry? LastEntry;

  [JS]
  public OptionalEntry MakeEntry(string label) => new(label);

  [JS]
  public OptionalEntry? Echo(OptionalEntry? entry)
  {
    LastEntry = entry;
    return entry;
  }

  [JS]
  public OptionalEntry EchoExact(OptionalEntry entry) => entry;

  [JS]
  public string Describe(OptionalEntry? entry = null)
  {
    LastEntry = entry;
    return entry?.Label ?? "null";
  }

  [JS]
  public async Task<OptionalEntry?> EchoLater(OptionalEntry? entry)
  {
    await Task.Yield();
    return entry;
  }

  [JS]
  public OptionalEntry? Stored { get; set; }

  [JS]
  public OptionalTrackedRef MakeRef() => new(new TrackedResource());

  [JS]
  public OptionalTrackedRef? EchoRef(OptionalTrackedRef? value) => value;

  [JS]
  public async Task<OptionalTrackedRef?> EchoRefLater(OptionalTrackedRef? value)
  {
    await Task.Yield();
    return value;
  }

  [JS]
  public OptionalTrackedRef? StoredRef { get; set; }
}

[ExpoSharedObject]
public sealed partial class OptionalEntry : SharedObject
{
  [JS]
  public OptionalEntry(string label, OptionalEntry? parent = null)
  {
    Label = label;
    Parent = parent;
  }

  [JS]
  public string Label { get; }

  [JS]
  public OptionalEntry? Parent { get; set; }

  [JS]
  public OptionalEntry? EchoParent(OptionalEntry? value) => value;

  [JS]
  public OptionalTrackedRef? Tracked { get; set; }
}

[ExpoSharedObject]
public sealed partial class OptionalTrackedRef : SharedRef<TrackedResource>
{
  public static OptionalTrackedRef? LastCreated;

  public OptionalTrackedRef(TrackedResource reference)
      : base(reference)
  {
    LastCreated = this;
  }

  [JS]
  public bool IsDisposed => Ref.Disposed;
}

// The authored abstract base of FileEntry and DirEntry. It has no [ExpoSharedObject] attribute.
public abstract class PathBase : SharedObject
{
  protected PathBase(string name)
  {
    Name = name;
  }

  public string Name { get; }

  // The concrete class name plus the name, so a test sees which instance authored code received.
  public string Describe() => $"{GetType().Name}:{Name}";
}

[ExpoSharedObject]
public sealed partial class FileEntry : PathBase
{
  [JS]
  public FileEntry(string name)
      : base(name)
  {
  }
}

[ExpoSharedObject]
public sealed partial class DirEntry : PathBase
{
  [JS]
  public DirEntry(string name, PathBase parent)
      : base(name)
  {
    Parent = parent;
    BaseThingsModule.LastPath = parent;
  }

  public PathBase Parent { get; }

  [JS]
  public string Compare(PathBase other)
  {
    BaseThingsModule.LastPath = other;
    return ReferenceEquals(other, Parent) ? "parent" : other.Describe();
  }
}

// Owns FileEntry and DirEntry, so neither reports EXPOJSI024. No other module may list them.
[ExpoModule("BaseThings", Classes = new[] { typeof(FileEntry), typeof(DirEntry) })]
public sealed partial class BaseThingsModule
{
  // The last PathBase that authored code received through any member, so tests can check
  // identity from C# and prove that a failed call never reached authored code.
  public static PathBase? LastPath;

  [JS]
  public string Describe(PathBase path)
  {
    LastPath = path;
    return path.Describe();
  }

  [JS]
  public async Task<string> DescribeLater(PathBase path)
  {
    await Task.Yield();
    LastPath = path;
    return path.Describe();
  }

  // The exact twin of Describe, the oracle for the failure messages.
  [JS]
  public string DescribeFile(FileEntry file)
  {
    LastPath = file;
    return file.Describe();
  }
}

[ExpoSharedObject]
public sealed partial class UnionWriter : SharedObject
{
  private readonly string seed;

  [JS]
  public UnionWriter(Either<string, ArrayBuffer> seed)
  {
    this.seed = UnionThingsModule.Describe(seed);
  }

  [JS]
  public string ReadSeed() => seed;

  [JS]
  public string Write(Either<string, ArrayBuffer> content) => UnionThingsModule.Describe(content);

  [JS]
  public int Pick(Either<double, bool> value) => UnionThingsModule.Score(value);
}

// Owns UnionWriter, so it does not report EXPOJSI024. No other module may list it.
[ExpoModule("UnionThings", Classes = new[] { typeof(UnionWriter) })]
public sealed partial class UnionThingsModule
{
  // Counts every authored read of a union, so a test proves a failed decode never reached authored code.
  public static int Calls;

  // Reads the active member through IsFirst, First, and Second, as authored code is expected to.
  public static string Describe(Either<string, ArrayBuffer> content)
  {
    Calls++;
    return content.IsFirst
        ? $"first:{content.First}"
        : $"second:{string.Join(",", content.Second.ToArray())}";
  }

  public static int Score(Either<double, bool> value) =>
      value.Match(number => 100 + (int)(number * 10), flag => flag ? 1 : 0);

  [JS]
  public string Write(Either<string, ArrayBuffer> content) => Describe(content);

  [JS]
  public async Task<string> WriteLater(Either<string, ArrayBuffer> content)
  {
    await Task.Yield();

    // After the await this method runs off the runtime thread, where a JavaScript-backed buffer
    // allows only asynchronous byte access.
    if (content.IsFirst)
    {
      return Describe(content);
    }

    var bytes = await content.Second.ToArrayAsync();
    Calls++;
    return $"second:{string.Join(",", bytes)}";
  }

  [JS]
  public string WriteOptional(Either<string, ArrayBuffer>? content) =>
      content is { } value ? Describe(value) : "null";

  [JS]
  public string WriteWithCount(Either<string, ArrayBuffer> content, int count) =>
      $"{Describe(content)}:{count}";

  [JS]
  public string WriteAll(IReadOnlyList<Either<string, ArrayBuffer>> parts) =>
      string.Join("|", parts.Select(Describe));

  [JS]
  public int Pick(Either<double, bool> value) => Score(value);

  // Encode positions. A returned union belongs to generated glue. Each method returns a Retain()
  // copy that shares the parameter's native entry, so the entry releases only when glue releases
  // both the parameter and the result.
  [JS]
  public Either<string, ArrayBuffer> Echo(Either<string, ArrayBuffer> content) => Copy(content);

  // Retain() is a runtime call, so it runs before the await, on the runtime thread.
  [JS]
  public async Task<Either<string, ArrayBuffer>> EchoLater(Either<string, ArrayBuffer> content)
  {
    var copy = Copy(content);
    await Task.Yield();
    return copy;
  }

  [JS]
  public UnionHolder Wrap(UnionHolder holder) => holder with { Content = Copy(holder.Content) };

  [JS]
  public IReadOnlyList<Either<string, ArrayBuffer>> EchoAll(IReadOnlyList<Either<string, ArrayBuffer>> parts) =>
      parts.Select(Copy).ToArray();

  // Stores and returns only Retain() copies; the module owns the stored copy until it is replaced.
  [JS]
  public Either<string, ArrayBuffer> Stored
  {
    get => Copy(stored);
    set
    {
      var copy = Copy(value);
      if (!stored.IsFirst)
      {
        stored.Second.Dispose();
      }
      stored = copy;
    }
  }

  [Event]
  public partial Func<Either<string, ArrayBuffer>, Task> OnContent { get; }

  // The event payload and the callback argument stay with the author, who reads the union after both.
  [JS]
  public string Forward(
      Either<string, ArrayBuffer> content,
      JavaScriptCallback<ValueTuple<Either<string, ArrayBuffer>>, bool> callback)
  {
    OnContent(content).GetAwaiter().GetResult();
    var seen = callback.Invoke(ValueTuple.Create(content));
    return $"{(seen ? "true" : "false")}:{Describe(content)}";
  }

  // The decoded callback result belongs to the author, who disposes a buffer member.
  [JS]
  public string Take(JavaScriptCallback<Either<string, ArrayBuffer>> callback)
  {
    var content = callback.Invoke();
    try
    {
      return Describe(content);
    }
    finally
    {
      if (!content.IsFirst)
      {
        content.Second.Dispose();
      }
    }
  }

  private Either<string, ArrayBuffer> stored = Either<string, ArrayBuffer>.FromFirst(string.Empty);

  private static Either<string, ArrayBuffer> Copy(Either<string, ArrayBuffer> value) =>
      value.IsFirst ? value : Either<string, ArrayBuffer>.FromSecond(value.Second.Retain());
}

public sealed record UnionHolder(Either<string, ArrayBuffer> Content, int Count);

public enum ParameterDefaultMode
{
  Slow,
  Fast,
}

[ExpoSharedObject]
public sealed partial class ParameterDefaultPicker : SharedObject
{
  [JS]
  public ParameterDefaultPicker(ParameterDefaultMode mode = ParameterDefaultMode.Fast)
  {
    Picked = mode.ToString();
  }

  [JS]
  public string Picked { get; }
}

[ExpoModule("GeneratedParameterDefaults", Classes = new[] { typeof(ParameterDefaultPicker) })]
public sealed partial class GeneratedParameterDefaultsModule
{
  [JS]
  public string Pick(ParameterDefaultMode mode = ParameterDefaultMode.Fast) => mode.ToString();

  [JS]
  public string PickNullable(ParameterDefaultMode? mode = ParameterDefaultMode.Slow) =>
      mode?.ToString() ?? "null";

  [JS]
  public double Wait(TimeSpan delay = default) => delay.Ticks;

  [JS]
  public double Edge(
      int which,
      double nan = double.NaN,
      double low = double.NegativeInfinity,
      double zero = -0.0) =>
      which switch
      {
        0 => nan,
        1 => low,
        _ => zero,
      };

  [JS]
  public int Small(byte value = 5) => value;

  [JS]
  public string Escaped(string value = "q\"b\\n\n\t\u2028\u00e9\U0001F600") => value;
}
