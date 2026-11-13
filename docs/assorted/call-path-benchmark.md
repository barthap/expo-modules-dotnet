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

Stdout carries only the header, the table, and the warning lines. Progress and
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

CMake defines `NDEBUG` for Release, RelWithDebInfo, and MinSizeRel and not for
an empty build type. This holds for clang, gcc, and MSVC, so the line tells a
Release testhost from the test runners' testhost on every platform.

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
hermes ref: 896d643e7453f507b062140f849f89ecf5448a88

| case | direction | median ns/call | min ns/call | spread | bytes/call | owned linked | owned unlinked | borrowed | native ns/call | bridge ns/call |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| JS: empty driver loop | - | 16.4 | 15.3 | 1.01 | 0.0 | - | - | - | - | - |
| function: no arguments | - | 225.1 | 220.6 | 1.02 | 152.0 | 0 | 1 | 0 | 45.9 | 179.2 |
| function: two doubles, double result | round trip | 429.8 | 421.3 | 1.01 | 152.0 | 0 | 1 | 2 | 62.5 | 367.3 |
| function: async, no arguments (testhost executor) | - | 46064.4 | 44555.3 | 1.02 | 1272.1 | 2 | 1 | 0 | - | - |
| primitive: boolean | round trip | 327.4 | 325.6 | 1.01 | 152.0 | 0 | 1 | 1 | 52.8 | 274.6 |
| primitive: int | round trip | 321.2 | 317.6 | 1.01 | 152.0 | 0 | 1 | 1 | 54.7 | 266.5 |
| primitive: double | round trip | 320.2 | 317.8 | 1.01 | 152.0 | 0 | 1 | 1 | 53.4 | 266.8 |
| string: 16 characters | argument | 395.8 | 391.4 | 1.02 | 208.0 | 0 | 1 | 1 | 66.4 | 329.4 |
| string: 16 characters | return | 321.6 | 318.9 | 1.02 | 192.0 | 1 | 0 | 0 | 61.5 | 260.1 |
| record: 4 fields | argument | 1318.5 | 1309.9 | 1.03 | 504.0 | 1 | 5 | 1 | 228.2 | 1090.3 |
| record: 4 fields | return | 1201.0 | 1194.7 | 1.00 | 568.0 | 2 | 4 | 0 | 428.1 | 772.9 |
| record: 12 fields | argument | 3877.0 | 3868.8 | 1.00 | 1168.0 | 7 | 7 | 1 | 765.6 | 3111.4 |
| record: 12 fields | return | 3393.4 | 3372.9 | 1.01 | 1464.0 | 8 | 6 | 0 | 1414.9 | 1978.5 |
| record: nested, 48 values | argument | 15628.9 | 15467.8 | 1.02 | 4224.0 | 33 | 25 | 1 | 2924.6 | 12704.3 |
| record: nested, 48 values | return | 13761.9 | 13730.6 | 1.00 | 5816.0 | 34 | 24 | 0 | 5788.5 | 7973.4 |
| dictionary: 4 numbers | argument | 1973.5 | 1955.2 | 1.03 | 1136.0 | 1 | 5 | 1 | 452.6 | 1520.9 |
| dictionary: 4 numbers | return | 1574.4 | 1565.8 | 1.01 | 1032.0 | 2 | 4 | 1 | 508.9 | 1065.5 |
| dictionary: 12 numbers | argument | 4077.6 | 4063.5 | 1.05 | 2392.0 | 1 | 13 | 1 | 1218.2 | 2859.4 |
| dictionary: 12 numbers | return | 3505.8 | 3472.9 | 1.02 | 2208.0 | 2 | 12 | 1 | 1464.2 | 2041.6 |
| array: 100 numbers | argument | 13727.2 | 13638.0 | 1.00 | 3168.0 | 1 | 101 | 1 | 1610.8 | 12116.4 |
| array: 100 numbers | return | 16697.0 | 16601.9 | 1.01 | 5824.0 | 2 | 100 | 1 | 3702.1 | 12994.8 |
| array: 1,000 numbers | argument | 137180.3 | 136517.7 | 1.01 | 24776.0 | 1 | 1001 | 1 | 15456.7 | 121723.6 |
| array: 1,000 numbers | return | 161195.5 | 160682.2 | 1.01 | 56224.0 | 2 | 1000 | 1 | 36685.4 | 124510.1 |
| array: 100 strings | argument | 22801.0 | 22538.1 | 1.01 | 6368.0 | 101 | 1 | 1 | 2749.6 | 20051.4 |
| array: 100 strings | return | 27542.5 | 27390.8 | 1.00 | 12224.0 | 102 | 0 | 1 | 6713.4 | 20829.2 |
| array: 100 records, 4 fields | argument | 115760.0 | 114132.6 | 1.02 | 35176.0 | 201 | 401 | 1 | 18274.2 | 97485.8 |
| array: 100 records, 4 fields | return | 111525.7 | 110992.0 | 1.02 | 47424.0 | 202 | 400 | 1 | 41201.1 | 70324.6 |
| array: 100 mixed values | argument | 24798.4 | 24721.0 | 1.00 | 6624.0 | 34 | 68 | 1 | 2150.3 | 22648.0 |
| array: 100 mixed values | return | 21667.1 | 21565.7 | 1.01 | 10336.0 | 35 | 67 | 1 | 4838.2 | 16828.9 |
| binary: ArrayBuffer, 1,024 bytes | argument | 48733.6 | 48599.7 | 1.00 | 240.0 | 0 | 1 | 1 | 63.0 | 48670.5 |
| binary: ArrayBuffer, 1,024 bytes | return | 595.5 | 590.7 | 1.02 | 224.0 | 1 | 0 | 1 | 239.8 | 355.7 |
| binary: Uint8Array, 1,024 bytes | argument | 51777.0 | 51543.9 | 1.02 | 1248.0 | 12 | 4 | 1 | 328.9 | 51448.1 |
| binary: Uint8Array, 1,024 bytes | return | 1735.2 | 1730.4 | 1.01 | 904.0 | 6 | 4 | 1 | 371.5 | 1363.7 |

async rows use the testhost executor
```

The async row includes the promise bookkeeping of the driver and has no control
row. It creates 2 linked handles, the promise as a value and the retained return
value, and 1 unlinked handle, the `undefined` that resolves the promise.

## What The Snapshot Shows

- The empty driver loop costs 16.4 ns per iteration. A call with no arguments
  and no result takes 225.1 ns, of which 179.2 ns is above the native control.
- The function rows with no arguments, two doubles, and a boolean, int, or
  double each allocate 152.0 bytes per call and create 1 owned unlinked handle.
  They create 0, 2, and 1 borrowed handles.
- The handle count of an array of numbers follows its length. An argument of 100
  numbers creates 101 owned unlinked handles, and an argument of 1,000 numbers
  creates 1,001.
- An array of 100 strings creates 101 owned linked handles where an array of 100
  numbers creates 101 unlinked ones. The argument takes 22,801.0 ns instead of
  13,727.2 ns, and the return takes 27,542.5 ns instead of 16,697.0 ns.
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
  time. For a 12-field record return the control takes 1,414.9 ns and the bridge
  time is 1,978.5 ns. For the nested record return the numbers are 5,788.5 ns
  and 7,973.4 ns.
- An `ArrayBuffer` argument of 1,024 bytes takes 48,733.6 ns and a `Uint8Array`
  argument of the same size takes 51,777.0 ns. The native control for the
  `ArrayBuffer` takes 63.0 ns. The same `ArrayBuffer` as a return value takes
  595.5 ns. At 1,024 bytes, a binary argument costs about 49 to 52 microseconds
  per call.

### The Binary Argument Cost

This is a measured observation with limits.

- A separate measurement of `ArrayBuffer` arguments at 0, 16, 1,024, and 16,384
  bytes gave about the same cost for each size. The table has only the 1,024
  byte row.
- The decode of a binary argument calls the detached check twice, in
  `retainArrayBuffer` and in `tryGetMutableBuffer` in `ExpoJsiBridge.cpp`,
  through `isArrayBufferDetached` in `ArrayBufferCapabilities.h`.
- On the pinned Hermes, that check throws an error that says the operation is
  "not supported". The helper catches it and treats the buffer as not detached.
  So each decode throws and catches two C++ exceptions.
- The share of the argument cost that these exceptions take was not measured.
