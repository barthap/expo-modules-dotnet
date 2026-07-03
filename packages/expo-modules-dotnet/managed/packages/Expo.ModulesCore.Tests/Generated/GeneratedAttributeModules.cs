using System;
using System.Collections.Generic;
using System.Linq;

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

[ExpoModule("GeneratedArray")]
public sealed partial class GeneratedArrayModule
{
  [JS("sum")]
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  [JS("labels")]
  public IReadOnlyList<string> Labels() => ["one", "two"];
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
