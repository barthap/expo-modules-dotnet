# 02 - Research Spikes

## Purpose

This file defines the ordered proof plan. Spikes are research instruments, not
production deliverables. Each spike should answer one architectural question,
leave evidence, and either unblock the next spike or stop for review.

Do not skip ahead to RNW, React Native macOS, views, or a source generator
before the headless bridge and generated-looking module proof exist.

## Global Spike Rules

Every spike must produce a short result note with:

- date and machine;
- repository path;
- branch or disposable repo path;
- exact commands run;
- expected result;
- actual result;
- files/artifacts created;
- whether artifacts are disposable or candidates for promotion;
- unresolved decisions;
- stop/go decision.

Suggested result note path in a clean research repo:

```text
docs/spike-results/YYYY-MM-DD-spike-N-short-name.md
```

If a spike runs in this repository before a clean research repo exists, put
notes under `docs/spike-results/` and mark proof files as disposable. Do not
modify production code.

## Spike 1: Mac HostFXR Smoke Test

Hypothesis:

Native macOS C++ can load a framework-dependent .NET assembly through HostFXR,
resolve a managed entry point, pass a small function table or context pointer,
receive a value back, and release any returned native/managed buffer explicitly.

Purpose:

Prove the first loader works on the user's Mac without involving RNW, Windows,
JSI, Hermes, NativeAOT, or app packaging. This isolates loader mechanics from
bridge semantics.

Prerequisites:

- macOS with .NET SDK installed.
- CMake or another simple native build tool.
- Access to HostFXR headers/libraries from the installed .NET SDK or runtime.
- A disposable research repo, or explicit note that files are temporary.

Implementation boundary:

Build only:

- one native executable;
- one C# class library;
- one exported managed entry point;
- one explicit returned string or buffer ownership path.

Do not build:

- JSI wrappers;
- module registry;
- source generator;
- RNW adapter;
- NativeAOT publish path.

Expected artifacts:

```text
native/hostfxr_smoke/main.cpp
managed/HostFxrSmoke/HostFxrSmoke.csproj
managed/HostFxrSmoke/EntryPoints.cs
docs/spike-results/YYYY-MM-DD-spike-1-hostfxr-smoke.md
```

Command template:

```sh
dotnet --info
dotnet build managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug
cmake -S native/hostfxr_smoke -B build/hostfxr_smoke
cmake --build build/hostfxr_smoke
./build/hostfxr_smoke/hostfxr_smoke
```

Expected output/proof:

- native executable prints the .NET runtime path it loaded;
- native executable successfully invokes a managed function;
- managed function returns a known value such as `expo-csharp-jsi-smoke`;
- any returned UTF-8 buffer has a documented release call and is released;
- result note includes the exact hostfxr discovery/loading approach.

Failure signals:

- proof depends on `LoadLibraryW` or other Windows-only loader calls;
- returned memory ownership is implicit;
- managed entry point requires reflection-heavy discovery that would not map to
  NativeAOT later;
- proof cannot explain which process owns which buffer.

Stop/go decision:

- Stop if macOS HostFXR loading requires platform assumptions that cannot be
  abstracted cleanly.
- Go if the loader path is small, explicit, and separate from bridge semantics.

## Spike 2: C ABI And Opaque Handle Skeleton

Hypothesis:

A small C ABI can represent runtime, value, object, function, string, buffer,
callback, promise, scheduler, and error concepts without exposing C++ JSI
layouts or React Native scheduler types to C#.

Purpose:

Design and compile the ABI surface before it is entangled with real module
bindings. The output is a minimal skeleton that can be used by both HostFXR and
NativeAOT later.

Prerequisites:

- Spike 1 complete or explicitly deferred with user approval.
- Agreement that handles are opaque and owned by the C++ bridge.

Implementation boundary:

Build only:

- C header with handle typedefs, enums, result structs, retain/release
  operations, schedule-on-JS callback shape, and function table shape;
- C++ fake handle table or stub implementation;
- C# P/Invoke/function pointer declarations matching the ABI;
- unit tests over fake handles if real JSI is not wired yet.

Do not build:

- a full JSI runtime;
- module registry;
- real RN host integration;
- generator.

Expected artifacts:

```text
native/include/expo_csharp_jsi.h
native/bridge/HandleTable.cpp
managed/Expo.CSharpJsi/Interop/NativeAbi.cs
managed/Expo.CSharpJsi.Tests/AbiLayoutTests.cs
docs/spike-results/YYYY-MM-DD-spike-2-abi-handles.md
```

Command template:

```sh
cmake --build build
dotnet test managed/Expo.CSharpJsi.Tests/Expo.CSharpJsi.Tests.csproj
```

Expected output/proof:

- C++ compiles with the ABI header;
- C# declarations compile;
- tests assert expected enum values and struct sizes where layout matters;
- the result note explains which handles are borrowed vs owned;
- the result note explains which operations require a scheduler and which
  operations are valid only inside the current JS callback frame;
- no C++ class type appears in C# declarations.

Failure signals:

- C# needs a `jsi::Value*` type or C++ template type;
- ABI exposes STL types, C++ exceptions, or C++ object layouts;
- ABI exposes `react::CallInvoker`, `RuntimeExecutor`, `RuntimeScheduler`, or
  another host scheduler type directly to C#;
- structs contain non-blittable fields;
- ownership cannot be described for a handle returned by the ABI.

Stop/go decision:

- Stop if the ABI cannot be expressed in C-compatible types.
- Go if wrappers can be built over the ABI without knowing C++ layouts.

## Spike 3: Headless JSI Runtime Through C# Wrappers

Hypothesis:

C++ can own a headless JSI runtime while C# reads arguments and creates return
values only through wrapper calls backed by the C ABI.

Purpose:

Prove the Swift-like wrapper model in C# without Swift/C++ interop. This is the
first real bridge proof, but it should stay headless and independent of RNW.

Prerequisites:

- Spike 2 ABI skeleton.
- A headless JSI runtime strategy, such as Hermes/JSI if practical locally, or
  a clearly marked fake runtime only for wrapper semantics.
- Decision note if fake runtime is used: what remains unproven.

Implementation boundary:

Build:

- `JavaScriptRuntime`;
- `JavaScriptUnownedValue`;
- `JavaScriptValue`;
- `JavaScriptObject`;
- `JavaScriptFunction`;
- `JavaScriptArguments`;
- string and buffer conversion examples;
- structured error propagation.
- a minimal scheduler abstraction, even if the headless implementation runs
  scheduled work immediately on the same thread.

Do not build:

- source generator;
- RNW adapter;
- view adapter;
- broad type conversion library.

Expected artifacts:

```text
managed/Expo.CSharpJsi/JavaScriptRuntime.cs
managed/Expo.CSharpJsi/JavaScriptUnownedValue.cs
managed/Expo.CSharpJsi/JavaScriptValue.cs
managed/Expo.CSharpJsi/JavaScriptObject.cs
managed/Expo.CSharpJsi/JavaScriptFunction.cs
managed/Expo.CSharpJsi/JavaScriptArguments.cs
managed/Expo.CSharpJsi.Tests/WrapperLifetimeTests.cs
native/bridge/JsiBridge.cpp
docs/spike-results/YYYY-MM-DD-spike-3-headless-wrappers.md
```

Command template:

```sh
dotnet test managed/Expo.CSharpJsi.Tests/Expo.CSharpJsi.Tests.csproj
cmake --build build
./build/headless-jsi-wrapper-proof
```

Expected output/proof:

- C# reads a borrowed number/string argument;
- C# gets and sets an object property;
- C# creates a JS object result;
- C# returns the object to native/JS through an owned handle;
- a scheduled callback can run through the same abstraction that a real RNW or
  React Native macOS adapter would implement;
- string/buffer ownership is documented and tested;
- errors become structured results instead of crossing ABI frames as
  exceptions;
- no JSON is used for ordinary value conversion.

Failure signals:

- wrapper stores a borrowed value beyond the callback lifetime;
- wrapper leaks owned values or double-releases them;
- C# uses raw native pointers after release;
- proof falls back to JSON for ordinary values;
- errors are thrown across unmanaged boundaries.
- scheduled work bypasses the scheduler abstraction and calls JSI directly from
  arbitrary managed code.

Stop/go decision:

- Stop if borrowed/owned value rules are unclear or violated.
- Go if wrapper semantics are testable and the result note explains remaining
  real-JSI vs fake-runtime gaps.

## Spike 4: Generated-Binding-Shaped Module Without Generator

Hypothesis:

Hand-written generated-looking C# code can register a module and invoke methods
through typed wrappers without runtime hot-path reflection.

Purpose:

Prove the shape of source generator output before building the generator. This
keeps the generator honest: it should emit code we already know works.

Prerequisites:

- Spike 3 wrapper proof.
- A minimal `ModuleRegistry`.
- Agreement that this spike hand-writes generated-looking code.

Implementation boundary:

Build:

- one authored module class;
- one generated-looking provider class;
- direct argument decoding;
- direct method call;
- direct return conversion;
- tests that fail if the generated-looking path is replaced by reflection.

Do not build:

- Roslyn source generator;
- dynamic discovery;
- broad type conversion.

Expected artifacts:

```text
managed/Expo.CSharpJsi/Modules/ModuleRegistry.cs
managed/Expo.CSharpJsi.Samples/MathModule.cs
managed/Expo.CSharpJsi.Samples/GeneratedExpoModulesProvider.cs
managed/Expo.CSharpJsi.Tests/GeneratedShapeTests.cs
docs/spike-results/YYYY-MM-DD-spike-4-generated-shape.md
```

Generated-looking code shape:

```csharp
[ExpoModule]
public sealed partial class MathModule
{
  [JS]
  public double Add(double a, double b) => a + b;
}

public static partial class GeneratedExpoModulesProvider
{
  public static void Register(JavaScriptRuntime runtime, ModuleRegistry registry)
  {
    var module = new MathModule();
    using var exports = runtime.CreateObject();

    using var addFunction = JavaScriptFunction.Create(
      runtime,
      name: "add",
      callback: static (thisValue, args, context) =>
      {
        var module = (MathModule)context;
        var a = args.UnownedValueAt(0).AsDouble();
        var b = args.UnownedValueAt(1).AsDouble();
        return args.Runtime.CreateNumber(module.Add(a, b));
      },
      context: module);
    using var addValue = addFunction.AsValue();
    exports.SetProperty("add", addValue);

    using var exportsValue = exports.AsValue();
    registry.RegisterModule("MathModule", exportsValue.RetainForRegistry());
  }
}
```

Command template:

```sh
dotnet test managed/Expo.CSharpJsi.Tests/Expo.CSharpJsi.Tests.csproj
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/Expo.CSharpJsi managed/Expo.CSharpJsi.Samples
```

Expected output/proof:

- JS/headless proof invokes `MathModule.Add(2, 3)` and receives `5`;
- generated-looking code decodes arguments through `JavaScriptArguments`;
- method body is called directly;
- return value goes through `args.Runtime.CreateNumber`, which calls the C ABI
  to ask the native bridge to create a JSI number and returns an owned
  `JavaScriptValue` handle;
- the owned return handle is transferred to the native bridge by the
  host-function return path, so generated code does not invent a handleless
  primitive representation;
- search output shows no forbidden reflection/dynamic invocation in the v2
  generated-looking path.

Failure signals:

- proof uses `Assembly.GetTypes()` to find the module;
- proof uses `MethodInfo.Invoke()` to call the method;
- proof uses `Delegate.DynamicInvoke()`;
- proof boxes ordinary arguments into `object?[]` as the normal path;
- proof serializes ordinary JSI arguments through JSON.

Stop/go decision:

- Stop if a direct generated-looking path is not ergonomic or cannot handle the
  basic module.
- Go if the hand-written shape is clear enough to become generator output.

## Spike 5: NativeAOT Compatibility Audit

Hypothesis:

The HostFXR-based proof can keep exported entry points, function tables, and
generated binding code compatible with NativeAOT.

Purpose:

Prevent HostFXR convenience from smuggling in runtime-only assumptions. This
spike does not need to make NativeAOT production-ready, but it must identify
whether the ABI and generated-looking code are viable.

Prerequisites:

- Spike 4 complete.
- Mac-local .NET SDK with NativeAOT workload/support for `osx-arm64` or
  relevant RID.

Implementation boundary:

Build:

- minimal NativeAOT publish of the managed entry point library;
- exported symbol check;
- note comparing HostFXR entry point path to NativeAOT entry point path.

Do not build:

- Windows NativeAOT proof on macOS;
- RNW adapter;
- production package.

Expected artifacts:

```text
managed/Expo.CSharpJsi.NativeAotProof/Expo.CSharpJsi.NativeAotProof.csproj
managed/Expo.CSharpJsi.NativeAotProof/EntryPoints.cs
docs/spike-results/YYYY-MM-DD-spike-5-nativeaot-audit.md
```

Command template:

```sh
dotnet publish managed/Expo.CSharpJsi.NativeAotProof/Expo.CSharpJsi.NativeAotProof.csproj -c Release -r osx-arm64 /p:PublishAot=true
nm -gU managed/Expo.CSharpJsi.NativeAotProof/bin/Release/net*/osx-arm64/publish/*
rg "RequiresUnreferencedCode|RequiresDynamicCode|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" managed
```

Expected output/proof:

- publish either succeeds or fails with documented AOT blocker;
- exported entry points are visible if publish succeeds;
- generated-looking module path remains free of forbidden hot-path reflection;
- result note distinguishes HostFXR loader work from runtime binding work.

Failure signals:

- generated-looking binding relies on APIs incompatible with trimming/AOT;
- exported entry point parameters are not blittable;
- initialization depends on runtime assembly scanning;
- NativeAOT blockers are hand-waved instead of written as decision points.

Stop/go decision:

- Stop if NativeAOT requires an ABI redesign.
- Go if blockers are isolated and the HostFXR proof can continue without
  invalidating NativeAOT compatibility.

## Spike 6: Platform Adapter Boundary Plan

Hypothesis:

The headless core can be mounted into RNW first, and later React Native macOS,
through thin adapters that provide host services without owning the C# module
system.

Purpose:

Define the adapter seam after the headless bridge is proven. This is not the
Windows app proof itself; it is the plan for connecting the proven core.

Prerequisites:

- Spike 4 complete.
- Spike 5 complete or explicitly accepted as later risk.
- Current Windows proof state understood on <windows-test-machine> if touching RNW.

Implementation boundary:

Build only a design sketch or interface proof:

- adapter service table;
- scheduler callback shape, explicitly mapping React Native call-invoker-like
  facilities to the portable `schedule_on_js` service;
- lifecycle install/uninstall shape;
- view adapter optional interface;
- notes on RNW and React Native macOS responsibilities.

Do not create or modify a real host app without user review.

Expected artifacts:

```text
docs/spike-results/YYYY-MM-DD-spike-6-platform-adapter-boundary.md
native/include/expo_csharp_jsi_platform.h
```

Command template:

```sh
dotnet test
cmake --build build
```

Expected output/proof:

- headless tests still run without adapter code;
- adapter interface names the services the host must supply;
- adapter interface states that RNW may implement scheduling with
  `react::CallInvoker`, `RuntimeExecutor`, or `RuntimeScheduler`, while the
  portable core sees only `schedule_on_js`;
- RNW-specific tasks are assigned to Windows-remote work;
- React Native macOS is described as a future host proof, not assumed to exist.

Failure signals:

- portable core starts depending on RNW, WinUI, AppKit, or packaging;
- view creation leaks into headless module wrappers;
- adapter owns module invocation logic instead of installing the bridge.
- adapter asks C# to hold or call a raw React Native scheduler object.

Stop/go decision:

- Stop for user review before implementing any real RNW or React Native macOS
  adapter.
- Go only when the user chooses the first host integration target.
