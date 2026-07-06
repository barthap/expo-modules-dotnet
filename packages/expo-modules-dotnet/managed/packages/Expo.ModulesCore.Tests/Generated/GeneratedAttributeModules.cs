using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore.Tests.Generated;

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

[ExpoModule("GeneratedArray")]
public sealed partial class GeneratedArrayModule
{
  [JS("sum")]
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  [JS("labels")]
  public IReadOnlyList<string> Labels() => ["one", "two"];
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
