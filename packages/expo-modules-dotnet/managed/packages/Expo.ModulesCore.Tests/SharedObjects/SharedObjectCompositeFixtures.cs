using System.Collections.Generic;
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

  public static void Reset()
  {
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
}
