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

The table goes to stdout. Progress and build output go to stderr. Arguments
after `--` go to the benchmark:

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
| 1 | At least one row is noisy. The table is still printed. The scripts also use 1 when the Hermes prebuilt is missing |
| 2 | `--filter` matches no case |
| 3 | A case failed verification or threw |
| 64 | Unknown argument or invalid regex |
| 70 | A script build step failed |

Even with tiered compilation off, a run can show one random slow sample in a row
with no link to the JIT or to garbage collection. The runner then marks the row
as noisy and exits with 1. Run the benchmark again on a quiet machine.

## How To Read The Columns

| Column | Meaning |
| --- | --- |
| case | The call shape |
| direction | `argument`, `return`, or `round trip` |
| median ns/call | Median time per call over 10 samples |
| min ns/call | Fastest sample |
| spread | Slowest sample divided by the median |
| bytes/call | Managed bytes allocated per call |
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
- A row whose spread is above 1.20 is marked `(noisy)`. The benchmark prints a
  warning that lists the noisy rows and exits with code 1.
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
  same value that the managed case returns. A control creates no value handle
  and does not call managed code. bridge ns/call is the median minus this value,
  and both include the driver loop.
- The empty driver loop row has no control and no handle columns. The async row
  has no control.

## What The Numbers Represent

- A sync case runs fully on the runtime thread. The driver loop calls the host
  function, the bridge calls managed code, and the result returns. The runtime
  executor runs once per sample, to start the loop. So sync rows do not depend
  on the testhost executor. The bridge code under test is the same code that a
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
- One caveat about tiering. The .NET documentation read for this benchmark does
  not say in one sentence what happens to precompiled (ReadyToRun) framework
  code when tiering is off. The pages imply that the runtime uses it as it is.
  This was not measured. So framework code keeps its precompiled quality, while
  the repo's own assemblies are compiled at full optimization.
- The CPU is a desktop CPU, not a phone CPU.
- The driver is JavaScript evaluated from source, not precompiled bytecode. The
  empty driver loop row shows its cost.

## The Uncounted `this` Handle

Each host-function call creates one more handle that no handle column shows: a
borrowed `this` handle. The bridge creates it inside its own trampoline, where
the testhost counting table does not see it. Counting it needs a change in the
production bridge, and the testhost counters exist so that the bridge stays free
of diagnostics. Add 1 to the `borrowed` column of every function row to get the
number of handles the bridge makes for the call.

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
platform: osx-arm64
logical processors: 12
hermes ref: 896d643e7453f507b062140f849f89ecf5448a88

| case | direction | median ns/call | min ns/call | spread | bytes/call | owned linked | owned unlinked | borrowed | native ns/call | bridge ns/call |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| JS: empty driver loop | - | 16.7 | 16.4 | 1.03 | 0.0 | 0 | 0 | 0 | - | - |
| function: no arguments | - | 222.4 | 219.2 | 1.02 | 152.0 | 0 | 1 | 0 | 46.1 | 176.3 |
| function: two doubles, double result | round trip | 423.3 | 418.4 | 1.02 | 152.0 | 0 | 1 | 2 | 62.1 | 361.2 |
| function: async, no arguments (testhost executor) | - | 45369.6 | 44720.6 | 1.03 | 1272.1 | 2 | 1 | 0 | - | - |
| primitive: boolean | round trip | 328.2 | 327.2 | 1.01 | 152.0 | 0 | 1 | 1 | 52.8 | 275.3 |
| primitive: int | round trip | 323.0 | 319.7 | 1.05 | 152.0 | 0 | 1 | 1 | 54.1 | 268.9 |
| primitive: double | round trip | 321.0 | 318.0 | 1.03 | 152.0 | 0 | 1 | 1 | 54.0 | 267.0 |
| string: 16 characters | argument | 399.9 | 393.5 | 1.01 | 208.0 | 0 | 1 | 1 | 67.1 | 332.8 |
| string: 16 characters | return | 326.9 | 321.8 | 1.04 | 192.0 | 1 | 0 | 0 | 61.4 | 265.5 |
| record: 4 fields | argument | 1332.1 | 1314.5 | 1.02 | 504.0 | 1 | 5 | 1 | 228.6 | 1103.5 |
| record: 4 fields | return | 1202.6 | 1192.6 | 1.02 | 568.0 | 2 | 4 | 0 | 436.7 | 766.0 |
| record: 12 fields | argument | 3927.3 | 3847.4 | 1.05 | 1168.0 | 7 | 7 | 1 | 948.8 | 2978.5 |
| record: 12 fields | return | 3396.2 | 3387.5 | 1.01 | 1464.0 | 8 | 6 | 0 | 1633.7 | 1762.4 |
| record: nested, 48 values | argument | 15589.2 | 15498.7 | 1.01 | 4224.0 | 33 | 25 | 1 | 3618.1 | 11971.1 |
| record: nested, 48 values | return | 13871.2 | 13767.1 | 1.02 | 5816.0 | 34 | 24 | 0 | 6631.9 | 7239.2 |
| dictionary: 4 numbers | argument | 1963.3 | 1945.5 | 1.02 | 1136.0 | 1 | 5 | 1 | 461.7 | 1501.6 |
| dictionary: 4 numbers | return | 1560.6 | 1543.1 | 1.04 | 1032.0 | 2 | 4 | 1 | 510.4 | 1050.2 |
| dictionary: 12 numbers | argument | 4059.6 | 4028.2 | 1.04 | 2392.0 | 1 | 13 | 1 | 1216.5 | 2843.1 |
| dictionary: 12 numbers | return | 3456.1 | 3449.7 | 1.01 | 2208.0 | 2 | 12 | 1 | 1468.6 | 1987.5 |
| array: 100 numbers | argument | 13704.7 | 13558.9 | 1.01 | 3168.0 | 1 | 101 | 1 | 1612.3 | 12092.4 |
| array: 100 numbers | return | 17254.2 | 17112.0 | 1.02 | 5824.0 | 2 | 100 | 1 | 3717.1 | 13537.1 |
| array: 1,000 numbers | argument | 135668.4 | 135473.2 | 1.00 | 24776.0 | 1 | 1001 | 1 | 15465.6 | 120202.8 |
| array: 1,000 numbers | return | 165899.9 | 164119.9 | 1.04 | 56224.0 | 2 | 1000 | 1 | 36612.0 | 129287.9 |
| array: 100 strings | argument | 22463.9 | 22340.5 | 1.01 | 6368.0 | 101 | 1 | 1 | 2746.1 | 19717.8 |
| array: 100 strings | return | 27935.1 | 27683.9 | 1.03 | 12224.0 | 102 | 0 | 1 | 6878.2 | 21057.0 |
| array: 100 records, 4 fields | argument | 115231.9 | 112799.6 | 1.03 | 35176.0 | 201 | 401 | 1 | 18212.5 | 97019.5 |
| array: 100 records, 4 fields | return | 111597.3 | 111140.3 | 1.01 | 47424.0 | 202 | 400 | 1 | 42710.8 | 68886.5 |
| array: 100 mixed values | argument | 24822.7 | 24753.8 | 1.01 | 6624.0 | 34 | 68 | 1 | 2167.4 | 22655.2 |
| array: 100 mixed values | return | 22158.1 | 22025.1 | 1.01 | 10336.0 | 35 | 67 | 1 | 4935.2 | 17222.9 |
| binary: ArrayBuffer, 1,024 bytes | argument | 49072.4 | 48939.3 | 1.00 | 240.0 | 0 | 1 | 1 | 63.1 | 49009.4 |
| binary: ArrayBuffer, 1,024 bytes | return | 585.6 | 580.9 | 1.04 | 224.0 | 1 | 0 | 1 | 206.9 | 378.7 |
| binary: Uint8Array, 1,024 bytes | argument | 52220.8 | 52036.8 | 1.02 | 1248.0 | 12 | 4 | 1 | 324.5 | 51896.3 |
| binary: Uint8Array, 1,024 bytes | return | 1719.2 | 1703.4 | 1.01 | 904.0 | 6 | 4 | 1 | 217.3 | 1501.9 |

async rows use the testhost executor
```

The async row includes the promise bookkeeping of the driver and has no control
row. It creates 2 linked handles, the promise as a value and the retained return
value, and 1 unlinked handle, the `undefined` that resolves the promise.

## What The Snapshot Shows

- The empty driver loop costs 16.7 ns per iteration. A call with no arguments
  and no result takes 222.4 ns, of which 176.3 ns is above the native control.
- The function rows with no arguments, two doubles, and a boolean, int, or
  double each allocate 152.0 bytes per call and create 1 owned unlinked handle.
  They create 0, 2, and 1 borrowed handles.
- The handle count of an array of numbers follows its length. An argument of 100
  numbers creates 101 owned unlinked handles, and an argument of 1,000 numbers
  creates 1,001.
- An array of 100 strings creates 101 owned linked handles where an array of 100
  numbers creates 101 unlinked ones. The argument takes 22,463.9 ns instead of
  13,704.7 ns, and the return takes 27,935.1 ns instead of 17,254.2 ns.
- A record argument with 12 fields creates 7 owned linked, 7 owned unlinked, and
  1 borrowed handle, 15 in all, plus the uncounted `this` handle. A record
  argument with 4 fields creates 1, 5, and 1.
- `docs/assorted/architecture-review.md` Finding 1 estimates "3 argument
  borrows + 3 argument value reads + 1 return value encode ≈ 7 allocations" for
  a generated sync function with three parameters and a return value, and
  counts two heap allocations for each value handle. The table has no row with
  that shape. The nearest row, two doubles and a double result, creates 3
  counted handles: 2 borrowed and 1 owned unlinked. A 12-field record argument
  creates 15 counted handles in one call. The estimate counts allocations and
  the table counts handles, so the two numbers do not compare one to one.
- In every row with a control, the bridge time is larger than the native control
  time. For a 12-field record return, the two are close: 1,762.4 ns and
  1,633.7 ns.
- An `ArrayBuffer` argument of 1,024 bytes takes 49,072.4 ns and a `Uint8Array`
  argument of the same size takes 52,220.8 ns. The native control for the
  `ArrayBuffer` takes 63.1 ns. The same `ArrayBuffer` as a return value takes
  585.6 ns. The cost of an `ArrayBuffer` or `Uint8Array` argument is about 50
  microseconds per call regardless of size.

### The Binary Argument Cost

This is a measured observation with limits.

- The decode of a binary argument calls the detached check twice, in
  `retainArrayBuffer` and in `tryGetMutableBuffer` in `ExpoJsiBridge.cpp`,
  through `isArrayBufferDetached` in `ArrayBufferCapabilities.h`.
- On the pinned Hermes, that check throws an error that says the operation is
  "not supported". The helper catches it and treats the buffer as not detached.
  So each decode throws and catches two C++ exceptions.
- A temporary experiment that skipped the check reduced the `Uint8Array`
  argument to about 3.8 microseconds. The experiment was reverted and is not
  in the repository.
- After that experiment, the remaining cost of the `ArrayBuffer` argument was not
  explained.

The experiment does not show that two exceptions are the whole cost of an
`ArrayBuffer` argument. It shows that they are most of the cost of a `Uint8Array`
argument on this Hermes.
