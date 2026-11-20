using Expo.ModulesCore;

namespace Expo.ModulesCore.Benchmarks;

public sealed record BenchRecord4(double A, double B, double C, double D);

public sealed record BenchRecord12(
    double N1,
    double N2,
    double N3,
    double N4,
    double N5,
    double N6,
    string S1,
    string S2,
    string S3,
    string S4,
    string S5,
    string S6);

public sealed record BenchNested48(BenchRecord12 R1, BenchRecord12 R2, BenchRecord12 R3, BenchRecord12 R4);

[Union]
public abstract record BenchMixed
{
  public sealed record Number(double Value) : BenchMixed;
  public sealed record Text(string Value) : BenchMixed;
  public sealed record Flag(bool Value) : BenchMixed;
}

// Each make method builds the value that the matching native control builds. Each take method reads
// its argument through the generated decoder and keeps a count and a checksum of the ends, so the
// result stays observable without a traversal that the native control does not do.
[ExpoModule("Benchmark")]
public sealed partial class BenchmarkModule
{
  private const string String16 = "abcdefghijklmnop";

  private double receivedCount = -1;
  private double receivedChecksum = -1;

  [JS]
  public void ResetReceived()
  {
    receivedCount = -1;
    receivedChecksum = -1;
  }

  [JS]
  public double ReceivedCount() => receivedCount;

  [JS]
  public double ReceivedChecksum() => receivedChecksum;

  [JS]
  public void Noop()
  {
  }

  [JS]
  public Task NoopAsync() => Task.CompletedTask;

  [JS]
  public double AddDoubles(double a, double b) => a + b;

  [JS]
  public bool EchoBoolean(bool value) => value;

  [JS]
  public int EchoInt(int value) => value;

  [JS]
  public double EchoDouble(double value) => value;

  [JS]
  public void TakeString(string value)
  {
    receivedCount = value.Length;
    receivedChecksum = value[0] + value[^1];
  }

  [JS]
  public string MakeString() => String16;

  [JS]
  public void TakeRecord4(BenchRecord4 value)
  {
    receivedCount = 4;
    receivedChecksum = value.A + value.D;
  }

  [JS]
  public BenchRecord4 MakeRecord4() => new(1, 2, 3, 4);

  [JS]
  public void TakeRecord12(BenchRecord12 value)
  {
    receivedCount = 12;
    receivedChecksum = value.N1 + value.N6 + value.S1.Length + value.S6.Length;
  }

  [JS]
  public BenchRecord12 MakeRecord12() => NewRecord12();

  [JS]
  public void TakeNested48(BenchNested48 value)
  {
    receivedCount = 48;
    receivedChecksum = value.R1.N1 + value.R4.N6 + value.R4.S6.Length;
  }

  [JS]
  public BenchNested48 MakeNested48() => new(NewRecord12(), NewRecord12(), NewRecord12(), NewRecord12());

  [JS]
  public void TakeDictionary(IReadOnlyDictionary<string, double> value)
  {
    receivedCount = value.Count;
    receivedChecksum = value["k3"];
  }

  [JS]
  public IReadOnlyDictionary<string, double> MakeDictionary(int count)
  {
    var result = new Dictionary<string, double>(count);
    for (var i = 0; i < count; i++)
    {
      result["k" + i] = i;
    }
    return result;
  }

  [JS]
  public void TakeNumbers(IReadOnlyList<double> value)
  {
    receivedCount = value.Count;
    receivedChecksum = value[0] + value[^1];
  }

  [JS]
  public IReadOnlyList<double> MakeNumbers(int count)
  {
    var result = new double[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = i;
    }
    return result;
  }

  [JS]
  public void TakeStrings(IReadOnlyList<string> value)
  {
    receivedCount = value.Count;
    receivedChecksum = value[0].Length + value[^1].Length;
  }

  [JS]
  public IReadOnlyList<string> MakeStrings(int count)
  {
    var result = new string[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = "s" + i;
    }
    return result;
  }

  [JS]
  public void TakeRecords4(IReadOnlyList<BenchRecord4> value)
  {
    receivedCount = value.Count;
    receivedChecksum = value[0].A + value[^1].D;
  }

  [JS]
  public IReadOnlyList<BenchRecord4> MakeRecords4(int count)
  {
    var result = new BenchRecord4[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = new BenchRecord4(1, 2, 3, 4);
    }
    return result;
  }

  [JS]
  public void TakeMixed(IReadOnlyList<BenchMixed> value)
  {
    receivedCount = value.Count;
    receivedChecksum = (value[0] as BenchMixed.Number)?.Value ?? -1;
    receivedChecksum += (value[^1] as BenchMixed.Number)?.Value ?? -1;
  }

  [JS]
  public IReadOnlyList<BenchMixed> MakeMixed(int count)
  {
    var result = new BenchMixed[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = (i % 3) switch
      {
        0 => new BenchMixed.Number(i),
        1 => new BenchMixed.Text("s" + i),
        _ => new BenchMixed.Flag(true),
      };
    }
    return result;
  }

  [JS]
  public void TakeArrayBuffer(ArrayBuffer value)
  {
    receivedCount = value.ByteLength;
    receivedChecksum = 0;
  }

  [JS]
  public ArrayBuffer MakeArrayBuffer(int length) => ArrayBuffer.Allocate(length);

  [JS]
  public void TakeUint8Array(Uint8Array value)
  {
    receivedCount = value.Length;
    receivedChecksum = value.ByteOffset;
  }

  [JS]
  public Uint8Array MakeUint8Array(int length) => Uint8Array.Allocate(length);

  private static BenchRecord12 NewRecord12() => new(1, 2, 3, 4, 5, 6, "s1", "s2", "s3", "s4", "s5", "s6");
}
