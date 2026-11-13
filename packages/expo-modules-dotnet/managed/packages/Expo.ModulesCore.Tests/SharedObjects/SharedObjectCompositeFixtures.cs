using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;

namespace Expo.ModulesCore.Tests.SharedObjects;

[ExpoSharedObject]
public sealed partial class Entry : SharedObject
{
  public static List<Entry> Constructed { get; } = new();

  public static int ReleaseCount;

  [JS]
  public Entry(string label)
  {
    Label = label;
    Constructed.Add(this);
  }

  [JS]
  public string Label { get; }

  [Event]
  public partial Func<EntryPair, Task> OnShared { get; }

  // The other instance and this one reach the listener as paired objects.
  [JS]
  public Task EmitSharedAsync(Entry other) => OnShared(new EntryPair(this, other));

  protected override void OnRelease() => System.Threading.Interlocked.Increment(ref ReleaseCount);
}

[ExpoSharedObject]
public sealed partial class OtherEntry : SharedObject
{
  [JS]
  public OtherEntry()
  {
  }
}

public sealed record EntryPair(Entry First, Entry? Second);

public sealed record HeldEntry(ArrayBuffer Data, Entry Entry);

[Union]
public abstract record EntryChoice
{
  public sealed record Held(EntryPair Value) : EntryChoice;
  public sealed record Listed(IReadOnlyList<Entry> Value) : EntryChoice;
  public sealed record Text(string Value) : EntryChoice;
}

[ExpoModule("CompositeEntries", Classes = new[] { typeof(Entry), typeof(OtherEntry) })]
public sealed partial class CompositeEntriesModule
{
  public static int CallCount;

  public static IReadOnlyList<Entry?>? LastList;

  public static Dictionary<string, Entry>? LastDictionary;

  public static EntryPair? LastPair;

  private static IReadOnlyList<Entry>? created;

  private static Entry? nativeFirst;

  private static IReadOnlyList<Entry>? stashed;

  private static JavaScriptCallback<ValueTuple<IReadOnlyList<Entry>>, Entry?>? storedCallback;

  // Holds the async methods below until a test opens it.
  public static TaskCompletionSource Gate { get; private set; } = NewGate();

  public static Entry? LastHeld;

  public static Entry? LastCallbackResult;

  public static EntryChoice? LastChoice;

  private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

  public static void Reset()
  {
    Gate = NewGate();
    LastHeld = null;
    LastCallbackResult = null;
    LastChoice = null;
    storedCallback = null;
    CallCount = 0;
    LastList = null;
    LastDictionary = null;
    LastPair = null;
    created = null;
    nativeFirst = null;
    stashed = null;
    Entry.Constructed.Clear();
    Entry.ReleaseCount = 0;
  }

  [JS]
  public IReadOnlyList<Entry?> EchoList(IReadOnlyList<Entry?> entries)
  {
    CallCount++;
    LastList = entries;
    return entries;
  }

  [JS]
  public IReadOnlyList<Entry> EchoStrict(IReadOnlyList<Entry> entries)
  {
    CallCount++;
    return entries;
  }

  [JS]
  public Entry TakeOne(Entry entry)
  {
    CallCount++;
    return entry;
  }

  [JS]
  public Dictionary<string, Entry> EchoDictionary(Dictionary<string, Entry> entries)
  {
    CallCount++;
    LastDictionary = entries;
    return entries;
  }

  [JS]
  public EntryPair EchoPair(EntryPair pair)
  {
    CallCount++;
    LastPair = pair;
    return pair;
  }

  // The same three native-created instances on every call, never paired before the first return.
  [JS]
  public IReadOnlyList<Entry> MakeEntries() =>
      created ??= new[] { new Entry("0"), new Entry("1"), new Entry("2") };

  // Keeps a native-created instance and a JavaScript-created one for a later encode, so the test
  // can release the second one before the list is returned.
  [JS]
  public void StashForEncode(Entry second)
  {
    nativeFirst = new Entry("native");
    stashed = new[] { nativeFirst, second };
  }

  [JS]
  public IReadOnlyList<Entry> ReturnStashed() => stashed!;

  [JS]
  public Entry ReturnNativeFirst() => nativeFirst!;

  [JS]
  public async Task<IReadOnlyList<Entry>> HoldEntriesAsync(Entry first)
  {
    await Gate.Task;
    return new[] { first, new Entry("late") };
  }

  // The result holds a Retain() copy of the argument leaf and a native-created instance.
  [JS]
  public async Task<HeldEntry> HoldLeafAndEntryAsync(ArrayBuffer data)
  {
    await Gate.Task;
    return new HeldEntry(data.Retain(), LastHeld = new Entry("held"));
  }

  [JS]
  public Entry? CallWithEntries(IReadOnlyList<Entry> entries, JavaScriptCallback<ValueTuple<IReadOnlyList<Entry>>, Entry?> callback) =>
      LastCallbackResult = callback.Invoke(ValueTuple.Create(entries));

  // The callback argument holds instances that were never paired before the encode.
  [JS]
  public Entry? CallWithNativeEntries(JavaScriptCallback<ValueTuple<IReadOnlyList<Entry>>, Entry?> callback) =>
      LastCallbackResult = callback.Invoke(ValueTuple.Create<IReadOnlyList<Entry>>(new[] { new Entry("n0"), new Entry("n1") }));

  [JS]
  public void StoreCallback(JavaScriptCallback<ValueTuple<IReadOnlyList<Entry>>, Entry?> callback) => storedCallback = callback;

  [JS]
  public async Task<Entry?> CallStoredAsync(IReadOnlyList<Entry> entries)
  {
    await Task.Yield();
    return LastCallbackResult = await storedCallback!.InvokeAsync(ValueTuple.Create(entries));
  }

  [Event]
  public partial Func<EntryPair, Task> OnNote { get; }

  // The second instance is native-created, so the event encode pairs it for the first time.
  [JS]
  public Task EmitNoteAsync(Entry first) => OnNote(new EntryPair(first, new Entry("native")));

  [Event]
  public partial Func<EntryChoice, Task> OnChoice { get; }

  [JS]
  public Task EmitChoiceAsync(Entry first) => OnChoice(new EntryChoice.Held(new EntryPair(first, null)));

  [JS]
  public EntryChoice EchoChoice(EntryChoice value)
  {
    LastChoice = value;
    return value;
  }
}
