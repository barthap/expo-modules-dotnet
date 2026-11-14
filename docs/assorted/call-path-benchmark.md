# Call-Path Benchmark

This page describes the benchmark for calls between JavaScript and C#. It lists
what each column means, what the numbers do and do not represent, and one
results snapshot.

The benchmark prints, for each call shape, the time per call, the managed bytes
per call, and the native value handles per call. A native-only control row for
each shape shows how much of the time the bridge adds. A change to the call path
pastes the table before and after the change.

The requirements are in `### Requirement: Canonical Benchmark Runners`,
`### Requirement: Value Handle Creation Counters`, and
`### Requirement: Native Benchmark Controls` (`hermes-testhost.md`).

## How To Run

| Platform | Command |
| --- | --- |
| macOS, Linux | `scripts/bench-managed.sh` |
| Windows | `scripts\bench-managed.ps1` |

Each script builds the managed projects in Release and the native testhost with
an optimized build type. It then runs `Expo.ModulesCore.Benchmarks` against that
testhost. The Hermes prebuilt must exist first, as for the test runners.

The testhost build directory is `build/jsi-testhost-bench` (Windows:
`build\jsi-testhost-bench-windows`). It is not the directory the test runners
use, and no script deletes it, so later runs build incrementally.

Stdout carries only the header, the table, the footer line
`async rows use the testhost executor`, and the warning lines. Progress and
build output go to stderr in the sh script and to the host in the PowerShell
script. Arguments after `--` go to the benchmark:

```sh
scripts/bench-managed.sh -- --filter "record"
```

`--filter <regex>` runs only the cases whose text matches. The text is
`<name> (<direction>)` for a case with a direction, and `<name>` for a case
without one. The match ignores case. Run `scripts/bench-managed.sh --help` for
the full usage.

| Exit code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | At least one set of samples is noisy. The table is still printed. The scripts also use 1 when the Hermes prebuilt is missing |
| 2 | `--filter` matches no case |
| 3 | A case failed verification or threw |
| 4 | Tiered compilation is not off, or the native testhost is not optimized. The table is still printed |
| 64 | Unknown argument or invalid regex. The scripts also use 64 for an unknown argument before the benchmark arguments |
| 70 | A script build step failed |

Code 4 wins over code 1, because a table that is not steady state says nothing
about noise. A direct run of the benchmark project can hit code 4: the test
runners build the native testhost without a build type, so a run against that
library prints `native testhost: not optimized`. The PowerShell script cannot
tell `-- --filter x` from `--filter x`, because PowerShell consumes the first
`--` itself. It accepts both and rejects a leading argument that does not start
with `--`.

Even with tiered compilation off, a run can show one random slow sample in a row
with no link to the JIT or to garbage collection. The runner then marks the row
as noisy and exits with 1. Run the benchmark again on a quiet machine.

## The Header

| Line | Meaning |
| --- | --- |
| configuration | The build configuration of the benchmark project |
| runtime, jit | The .NET runtime, and whether it compiles code at run time |
| tiered compilation, tiered PGO, quick JIT for loops | The setting that is in effect, and where it came from |
| native testhost | `optimized` when the library was built with `NDEBUG`, otherwise `not optimized` |
| platform, logical processors, hermes ref | The machine class and the Hermes revision |

An environment variable (`DOTNET_`, then `COMPlus_`) wins over the runtime
config, as it does in the runtime since .NET 9. The line then ends in
`(environment)`. A value from the runtime config ends in `(runtime config)`.
With neither, the line says `runtime default` and does not say what that default
is.

The signal is `NDEBUG`, which the default Release flags of CMake define. It was
observed on macOS with clang: the Release testhost of the scripts prints
`optimized`.

## How To Read The Columns

| Column | Meaning |
| --- | --- |
| case | The call shape |
| direction | `argument`, `return`, or `round trip` |
| median ns/call | Median time per call over 10 samples |
| min ns/call | Fastest sample |
| spread | Slowest sample divided by the median. A row also counts as noisy when its median is above 1.20 times its fastest sample |
| bytes/call | Managed bytes allocated per call, including the allocations of the module method itself |
| owned linked | Owned handles created per call and linked into the runtime's owned-value list |
| owned unlinked | Owned handles created per call and not linked |
| borrowed | Borrowed handles created per call |
| native ns/call | Median time of the native-only control |
| bridge ns/call | Median time minus native ns/call |

Details for each column:

- Each case compiles one JavaScript driver function, `function (n) { ...; for
  (let i = 0; i < n; i++) { <call>; } }`. The runner raises `n` until one sample
  takes about 50 ms. It then runs one warm-up and 10 timed samples.
- Median and minimum are nanoseconds per call. Time rows include the driver
  loop. The first row of the table, the empty driver loop, shows that cost. No
  row subtracts it.
- A set of samples is noisy when its slowest sample is above 1.20 times its
  median, or when its median is above 1.20 times its fastest sample. The second
  check sees a run where most samples sit on a slow level, which the first check
  misses. The table marks noisy managed samples with `(noisy)` after the spread
  and noisy control samples with `(noisy)` after the native ns/call value. The
  benchmark prints one warning per noisy set, says whether it is the managed or
  the control samples, and exits with code 1.
- Before each timed sample the runner collects garbage in .NET, waits until the
  testhost executor is idle, collects garbage in Hermes, and waits again. Work
  that one sample defers does not land in the next sample or in a control.
- bytes/call is the change of `GC.GetTotalAllocatedBytes(precise: true)` over
  one sample, divided by `n`. The counter is process-wide because host
  functions run on the testhost executor thread. The runner also measures a pass
  with `n = 0` and subtracts it, which removes the allocations of the harness
  call itself.
- The three handle columns come from a separate pass of exactly 1,000 calls.
  The runner resets the testhost counters before the pass and divides the
  counter values by 1,000. It subtracts the same counters of an `n = 0` pass,
  which removes the handle that the harness call creates. These numbers are
  exact, and the separate pass keeps the 32-bit counters far from overflow.
- Owned handles are linked when they hold a value that is not undefined, null, a
  boolean, or a number. The split matters because the cost of linking is
  separate from the cost of allocating a handle.
- native ns/call is the same shape done by a plain JSI host function in C++. An
  argument control reads every leaf of its argument. A return control builds the
  same kind of value that the managed case returns, for example an `ArrayBuffer`
  over native memory and not one that the JavaScript constructor allocates. A
  control uses constant property names where the generated code does, and builds
  names at run time where the module method does. A control creates no value
  handle and does not call managed code. bridge ns/call is the median minus this
  value, and both include the driver loop.
- bytes/call and the time of a return row include the work of the module method
  itself, for example the array that the method allocates. Not all of that is
  bridge cost.
- The `Uint8Array` argument row includes the managed check that the value is a
  `Uint8Array` (`Uint8ArrayCodec`), which the control does not run. That check
  counts as bridge cost.
- The empty driver loop row has no control, and its handle columns show `-`. The
  async row has no control.

## What The Numbers Represent

- A sync case runs fully on the runtime thread. The driver loop calls the host
  function, the bridge calls managed code, and the result returns. The testhost
  executor runs only the call that starts each sample. No sync call goes through
  it. The bridge code under test is the same code that a
  React Native app links.
- An async case goes through the runtime scheduler and the testhost executor,
  not through a React Native `CallInvoker`. The table marks it `testhost
  executor`. The row compares one bridge revision with another. It does not
  predict latency in an app. It also includes the driver's own promise
  bookkeeping, because the driver awaits every promise of a sample.
- The benchmark runs on the .NET JIT, not on NativeAOT. Tiered compilation is
  off for the benchmark project, so the repo's own assemblies are compiled one
  time at full optimization. With tiering on, the first samples after the
  warm-up run unoptimized code until a background compile batch completes. The
  median then depends on which sample that step lands in, and one measured run
  reported a median 55% above the steady value with no noisy mark. Fully
  optimized code with no dynamic profile data is also nearer to NativeAOT than
  the tiered steady state is. The setting is in the project file, so it holds
  for both scripts and for a direct run.
- One caveat about tiering. Whether the runtime uses precompiled (ReadyToRun)
  framework code as it is when tiering is off was not measured, and the .NET
  documentation does not state it directly. If it does, framework code keeps its
  precompiled quality, while the repo's own assemblies are compiled at full
  optimization.
- The CPU is a desktop CPU, not a phone CPU.
- The driver is JavaScript evaluated from source, not precompiled bytecode. The
  empty driver loop row shows its cost.

## The Uncounted Handles

Two borrowed handles that the bridge creates inside its own trampolines appear
in no handle column. The testhost counting table does not see them:

- The `this` handle of a host-function call. Every function row creates one.
- The value handle that the host-object property setter passes to managed code.
  No benchmark row writes a host-object property.

Counting them needs a change in the production bridge, and the testhost counters
exist so that the bridge stays free of diagnostics. Add 1 to the `borrowed`
column of every function row to get the number of handles the bridge makes for
the call. The host-object getter and the class and constructor trampolines
create no handle of their own: a getter returns a handle that managed code made
through the counted table.

## Results Snapshot

The header and table below come from one clean run of `scripts/bench-managed.sh`.
Another machine gives other numbers.

```
call-path benchmark
configuration: Release
runtime: .NET 10.0.5
jit: RyuJIT (dynamic code compiled)
tiered compilation: off (runtime config)
tiered PGO: runtime default
quick JIT for loops: runtime default
native testhost: optimized
platform: osx-arm64
logical processors: 12
hermes ref: 3477757eb2475555cf8d8df24bfb1deb0613880d

| case | direction | median ns/call | min ns/call | spread | bytes/call | owned linked | owned unlinked | borrowed | native ns/call | bridge ns/call |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| JS: empty driver loop | - | 7.0 | 6.7 | 1.01 | 0.0 | - | - | - | - | - |
| function: no arguments | - | 200.2 | 197.9 | 1.02 | 152.0 | 0 | 1 | 0 | 22.0 | 178.1 |
| function: two doubles, double result | round trip | 398.1 | 394.1 | 1.03 | 152.0 | 0 | 1 | 2 | 32.3 | 365.9 |
| function: async, no arguments (testhost executor) | - | 49916.0 | 49392.9 | 1.05 | 1272.0 | 2 | 1 | 0 | - | - |
| primitive: boolean | round trip | 302.8 | 300.1 | 1.02 | 152.0 | 0 | 1 | 1 | 25.8 | 277.1 |
| primitive: int | round trip | 296.0 | 294.2 | 1.01 | 152.0 | 0 | 1 | 1 | 26.7 | 269.3 |
| primitive: double | round trip | 294.3 | 292.4 | 1.00 | 152.0 | 0 | 1 | 1 | 26.6 | 267.7 |
| string: 16 characters | argument | 371.9 | 367.2 | 1.03 | 208.0 | 0 | 1 | 1 | 38.9 | 333.0 |
| string: 16 characters | return | 294.3 | 290.9 | 1.19 | 192.0 | 1 | 0 | 0 | 33.4 | 260.9 |
| record: 4 fields | argument | 1240.9 | 1231.5 | 1.01 | 504.0 | 1 | 5 | 1 | 140.1 | 1100.9 |
| record: 4 fields | return | 1095.6 | 1084.3 | 1.03 | 568.0 | 2 | 4 | 0 | 270.5 | 825.1 |
| record: 12 fields | argument | 3691.4 | 3661.3 | 1.01 | 1168.0 | 7 | 7 | 1 | 550.1 | 3141.3 |
| record: 12 fields | return | 3242.9 | 3226.3 | 1.01 | 1464.0 | 8 | 6 | 0 | 1031.6 | 2211.3 |
| record: nested, 48 values | argument | 14896.9 | 14785.7 | 1.07 | 4224.0 | 33 | 25 | 1 | 2100.8 | 12796.1 |
| record: nested, 48 values | return | 13173.6 | 13125.0 | 1.01 | 5816.0 | 34 | 24 | 0 | 4141.6 | 9032.0 |
| dictionary: 4 numbers | argument | 1809.5 | 1788.4 | 1.01 | 1136.0 | 1 | 5 | 1 | 302.1 | 1507.4 |
| dictionary: 4 numbers | return | 1495.5 | 1484.8 | 1.01 | 1032.0 | 2 | 4 | 1 | 366.5 | 1128.9 |
| dictionary: 12 numbers | argument | 3781.8 | 3662.2 | 1.02 | 2392.0 | 1 | 13 | 1 | 793.3 | 2988.4 |
| dictionary: 12 numbers | return | 3330.1 | 3313.2 | 1.02 | 2208.0 | 2 | 12 | 1 | 1152.5 | 2177.6 |
| array: 100 numbers | argument | 12985.9 | 12852.7 | 1.03 | 3168.0 | 1 | 101 | 1 | 712.0 | 12273.8 |
| array: 100 numbers | return | 15595.3 | 15525.7 | 1.01 | 5824.0 | 2 | 100 | 1 | 2634.3 | 12961.0 |
| array: 1,000 numbers | argument | 128018.0 | 126784.7 | 1.03 | 24776.0 | 1 | 1001 | 1 | 6769.8 | 121248.2 |
| array: 1,000 numbers | return | 151647.8 | 148842.0 | 1.02 | 56224.0 | 2 | 1000 | 1 | 25955.6 | 125692.1 |
| array: 100 strings | argument | 21523.7 | 21358.1 | 1.01 | 6368.0 | 101 | 1 | 1 | 1656.2 | 19867.5 |
| array: 100 strings | return | 26074.4 | 25860.7 | 1.01 | 12224.0 | 102 | 0 | 1 | 5279.2 | 20795.2 |
| array: 100 records, 4 fields | argument | 106450.1 | 105570.6 | 1.01 | 35176.0 | 201 | 401 | 1 | 11694.4 | 94755.7 |
| array: 100 records, 4 fields | return | 102296.2 | 101483.6 | 1.01 | 47424.0 | 202 | 400 | 1 | 26341.7 | 75954.5 |
| array: 100 mixed values | argument | 22845.2 | 22745.3 | 1.04 | 6624.0 | 34 | 68 | 1 | 1141.6 | 21703.6 |
| array: 100 mixed values | return | 20381.2 | 20348.2 | 1.01 | 10336.0 | 35 | 67 | 1 | 3610.9 | 16770.3 |
| binary: ArrayBuffer, 1,024 bytes | argument | 485.8 | 481.3 | 1.03 | 728.0 | 0 | 1 | 1 | 36.5 | 449.3 |
| binary: ArrayBuffer, 1,024 bytes | return | 492.4 | 490.1 | 1.02 | 224.0 | 1 | 0 | 1 | 143.1 | 349.4 |
| binary: Uint8Array, 1,024 bytes | argument | 3060.0 | 3013.5 | 1.01 | 1736.0 | 12 | 4 | 1 | 194.5 | 2865.5 |
| binary: Uint8Array, 1,024 bytes | return | 1588.9 | 1575.9 | 1.02 | 904.0 | 6 | 4 | 1 | 241.6 | 1347.3 |

async rows use the testhost executor
```

The async row includes the promise bookkeeping of the driver and has no control
row. It creates 2 linked handles, the promise as a value and the retained return
value, and 1 unlinked handle, the `undefined` that resolves the promise.

## What The Snapshot Shows

- The empty driver loop costs 7.0 ns per iteration. A call with no arguments
  and no result takes 200.2 ns, of which 178.1 ns is above the native control.
- The function rows with no arguments, two doubles, and a boolean, int, or
  double each allocate 152.0 bytes per call and create 1 owned unlinked handle.
  They create 0, 2, and 1 borrowed handles.
- The handle count of an array of numbers follows its length. An argument of 100
  numbers creates 101 owned unlinked handles, and an argument of 1,000 numbers
  creates 1,001.
- An array of 100 strings creates 101 owned linked handles where an array of 100
  numbers creates 101 unlinked ones. The argument takes 21,523.7 ns instead of
  12,985.9 ns, and the return takes 26,074.4 ns instead of 15,595.3 ns.
- A record argument with 12 fields creates 7 owned linked, 7 owned unlinked, and
  1 borrowed handle, 15 in all, plus the uncounted `this` handle. A record
  argument with 4 fields creates 1, 5, and 1.
- `docs/assorted/architecture-review.md` Finding 1 estimates "3 argument
  borrows + 3 argument value reads + 1 return value encode ≈ 7 allocations" for
  a generated sync function with three parameters and a return value. The 7 is a
  count of operations. The same page says each owned value is two heap
  allocations, so do not divide the 7 by 2. The table has no row with
  that shape. The nearest row, two doubles and a double result, creates 3
  counted handles: 2 borrowed and 1 owned unlinked. A 12-field record argument
  creates 15 counted handles in one call. The estimate counts allocations and
  the table counts handles, so the two numbers do not compare one to one.
- In every row with a control, the bridge time is larger than the native control
  time. For a 12-field record return the control takes 1,031.6 ns and the bridge
  time is 2,211.3 ns. For the nested record return the numbers are 4,141.6 ns
  and 9,032.0 ns.
- An `ArrayBuffer` argument of 1,024 bytes takes 485.8 ns, and the same buffer
  as a return value takes 492.4 ns. A `Uint8Array` argument of the same size
  takes 3,060.0 ns and creates 16 owned handles, where the `ArrayBuffer`
  argument creates 1.

### Binary Arguments On A Runtime Without The Detached Check

The decode of a binary argument calls the detached check twice, in
`retainArrayBuffer` and in `tryGetMutableBuffer` in `ExpoJsiBridge.cpp`, through
`isArrayBufferDetached` in `ArrayBufferCapabilities.h`. The pinned Hermes
implements `jsi::Runtime::detached`, so the check returns a value. A runtime
that does not implement it throws an error that says the operation is "not
supported". The helper catches it and treats the buffer as not detached, so each
decode on such a runtime throws and catches two C++ exceptions. A measurement
on a Hermes without the method gave 48,733.6 ns for the `ArrayBuffer` argument
row, about the same at 0, 16, 1,024, and 16,384 bytes.
