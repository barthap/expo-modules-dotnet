# Mobile App NativeAOT Connector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local Expo app proof that calls a NativeAOT C# `[ExpoModule]` from JavaScript through the real React Native Hermes runtime and the existing `expo_jsi.h` ABI.

**Architecture:** The managed proof is a NativeAOT library with one exported registration entry point. A nested local npm package exposes a tiny TurboModule whose JSI binding installer receives the active React Native Hermes runtime and `CallInvoker`, wraps them in a `native/packages/jsi` React Native connector, then passes the existing ABI table and opaque runtime handle to managed code. JavaScript reads the generated `globalThis.expo.modules.ExpoCSharpV2.add` result and renders it on screen.

**Tech Stack:** Expo app, React Native Hermes, C++20 JSI, `native/include/expo_jsi.h`, `native/packages/jsi`, .NET 10 NativeAOT, `Expo.JSI`, `Expo.ModulesCore`, `Expo.ModulesCore.Generator`.

---

## File Structure

- `native/packages/jsi/include/ReactNativeRuntimeConnector.h`: React Native runtime connector public API.
- `native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`: connector implementation over an existing `facebook::jsi::Runtime` and React Native `CallInvoker`.
- `experiments/mobile-app/`: generated Expo app plus local native module glue.
- `experiments/mobile-app/modules/expo-csharp-v2`: nested local npm package that combines minimal Expo module packaging with a TurboModule JSI bindings installer.
- `experiments/mobile-app/dotnet/ExpoMobileV2Module`: managed NativeAOT module library.
- `experiments/mobile-app/scripts/build-dotnet-module.sh`: builds NativeAOT artifacts for the selected platform.
- `docs/specs/runtime-and-abi.md` and `docs/specs/modules-core-boundary.md`: merge the accepted mobile proof requirements after implementation.

## Task 1: Bootstrap And Baseline The Expo App

**Files:**
- Create: `experiments/mobile-app/`

- [ ] **Step 1: Create the app**

Run:

```bash
bunx create expo experiments/mobile-app --template blank-typescript
```

Expected: Expo app files are created under `experiments/mobile-app`.

- [ ] **Step 2: Install dependencies**

Run:

```bash
cd experiments/mobile-app && bun install
```

Expected: dependencies install without using `npx`.

- [ ] **Step 3: Prebuild native projects**

Run:

```bash
cd experiments/mobile-app && bunx expo prebuild --platform all
```

Expected: `ios/` and `android/` exist and Hermes remains enabled.

- [ ] **Step 4: Replace the app screen**

Edit `experiments/mobile-app/App.tsx` so it renders a result from
`globalThis.expo.modules.ExpoCSharpV2.add(20, 22)` with fallback error text and logs
the result:

```tsx
import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";

declare global {
  // eslint-disable-next-line no-var
  var expo:
    | {
        modules?: {
          ExpoCSharpV2?: {
            add(a: number, b: number): number;
          };
        };
      }
    | undefined;
}

export default function App() {
  const [message, setMessage] = useState("Loading C# module...");

  useEffect(() => {
    try {
      const result = globalThis.expo?.modules?.ExpoCSharpV2?.add?.(20, 22);
      if (result !== 42) {
        throw new Error(`Unexpected C# module result: ${String(result)}`);
      }
      console.log("[ExpoCSharpV2] C# add(20, 22) returned", result);
      setMessage(`C# add result: ${result}`);
    } catch (error) {
      console.error("[ExpoCSharpV2] module call failed", error);
      setMessage(error instanceof Error ? error.message : String(error));
    }
  }, []);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>Expo.ModulesCore NativeAOT</Text>
      <Text style={styles.result}>{message}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
    backgroundColor: "#f7f7f2",
  },
  label: {
    color: "#24515c",
    fontSize: 18,
    fontWeight: "600",
    marginBottom: 12,
  },
  result: {
    color: "#111",
    fontSize: 28,
    fontWeight: "700",
    textAlign: "center",
  },
});
```

## Task 2: Add Managed NativeAOT Module

**Files:**
- Create: `experiments/mobile-app/dotnet/ExpoMobileV2Module/ExpoMobileV2Module.csproj`
- Create: `experiments/mobile-app/dotnet/ExpoMobileV2Module/MobileV2MathModule.cs`
- Create: `experiments/mobile-app/dotnet/ExpoMobileV2Module/EntryPoints.cs`

- [ ] **Step 1: Add the NativeAOT project**

Create `ExpoMobileV2Module.csproj` with repo-local project references and the
generator analyzer reference.

- [ ] **Step 2: Add the module**

Create `MobileV2MathModule.cs` with `[ExpoModule("ExpoCSharpV2")]`, `[JS("add")]`,
and a direct `double Add(double a, double b)` implementation. The module name
targets the existing native Expo module object because real Expo runtimes mark
`expo.modules` as read-only for brand-new keys after startup.

- [ ] **Step 3: Add the registration export**

Create `EntryPoints.cs` with:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore.Generated;

namespace ExpoMobileV2Module;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "expo_mobile_v2_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
      ExpoModulesProvider_ExpoMobileV2Module.Register(runtime);
      Console.WriteLine("ExpoMobileV2Module registered ExpoCSharpV2.add.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }
}
```

- [ ] **Step 4: Build the managed project**

Run:

```bash
dotnet build experiments/mobile-app/dotnet/ExpoMobileV2Module/ExpoMobileV2Module.csproj -c Debug
```

Expected: generator output compiles and no unsupported signature diagnostic is
reported.

## Task 3: Add React Native Runtime Connector

**Files:**
- Create: `native/packages/jsi/include/ReactNativeRuntimeConnector.h`
- Create: `native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`
- Modify: any local CMake or app build files that list `native/packages/jsi` sources

- [x] **Step 1: Add a focused connector test or compile check**

Add the connector files and include them in a native compile target used by the
mobile app or a lightweight syntax check.

- [x] **Step 2: Implement CallInvoker-backed execution**

Store a `std::shared_ptr<facebook::react::CallInvoker>` in
`ReactNativeRuntimeExecutor`. Route async work through `invokeAsync` and sync
work through `invokeSync`. Do not add a JS-thread predicate or override React
Native runtime classes.

- [x] **Step 3: Implement `JsiRuntimeConnector`**

The connector stores a borrowed `facebook::jsi::Runtime *`, reports validity
without touching invalid runtime state, and delegates execution through the
executor-owned call invoker.

- [x] **Step 4: Verify ABI use**

Run:

```bash
rg "expo_jsi.h|ReactNativeRuntimeConnector|CallInvoker|RuntimeExecutor" native/packages/jsi
```

Expected: the connector exists in `native/packages/jsi`, includes
`expo_jsi.h` through the bridge path, and names the React Native scheduling
primitive boundary.

## Task 4: Wire Local TurboModule Package

**Files:**
- Create/modify: `experiments/mobile-app/modules/expo-csharp-v2/package.json`
- Create/modify: `experiments/mobile-app/modules/expo-csharp-v2/src/NativeExpoCSharpV2.ts`
- Create/modify: `experiments/mobile-app/modules/expo-csharp-v2/android/`
- Create/modify: `experiments/mobile-app/modules/expo-csharp-v2/ios/`
- Modify: `experiments/mobile-app/package.json`

- [x] **Step 1: Add nested package metadata**

Add `package.json` with React Native `codegenConfig` and reference it from the
app using a local `file:` dependency. Keep `expo-module.config.json` for Expo
module autolinking only if it remains useful for packaging.

- [x] **Step 2: Add TurboModule TypeScript spec**

Create a minimal `NativeExpoCSharpV2.ts` spec so React Native codegen can
discover the module. The proof does not need JS-callable TurboModule methods;
the TurboModule exists to own the JSI binding installation hook.

- [x] **Step 3: Add Android TurboModule package**

Implement an Android `BaseReactPackage` and a module that implements
`TurboModuleWithJSIBindings`. Its bindings installer SHALL receive
`jsi::Runtime&` and `CallInvoker`, create the React Native connector, call the
NativeAOT registration export, and keep native state alive as long as installed
host functions can run.

- [x] **Step 4: Add iOS TurboModule**

Implement an Objective-C++ TurboModule that conforms to
`RCTTurboModuleWithJSIBindings`. Its
`installJSIBindingsWithRuntime:callInvoker:` implementation SHALL create the
React Native connector, call the NativeAOT registration export, and retain the
native state for the module lifetime.

- [x] **Step 5: Remove app-hook hacks**

Remove the config plugin, iOS `JSRuntimeFactory` wrapper, Android
`MainApplication`/`ReactInstance` bindings installer, and JavaScript deferred
installer. The app should call the generated C# function directly once
TurboModule installation has run.

## Task 5: Refresh Native Projects

- [x] **Step 1: Reinstall JS dependencies**

Run `bun install` from the app so the nested local package is linked into
`node_modules`.

- [x] **Step 2: Regenerate native projects**

Run Expo prebuild or the smallest equivalent native project refresh needed for
React Native autolinking/codegen to discover the local TurboModule package.

- [x] **Step 3: Link NativeAOT output**

Copy or reference the NativeAOT shared library from the managed publish output
for the platform being tested.

## Task 6: Run Platform Verification

**Files:**
- Create: screenshot artifacts under `experiments/mobile-app/artifacts/`

- [x] **Step 1: Run managed verification**

Run:

```bash
scripts/test-managed.sh
```

Expected: all managed Hermes-backed tests pass.

- [x] **Step 2: Run formatting verification**

Run:

```bash
scripts/format.sh --check --all
```

Expected: formatting check passes.

- [x] **Step 3: Run iOS app**

Run the app on the preferred iPhone 17 Pro simulator and save a screenshot
showing `C# add result: 42`.

- [x] **Step 4: Run Android app**

Run the app on an Android emulator and save a screenshot showing
`C# add result: 42`.

- [x] **Step 5: Capture Metro logs**

Save or quote the Metro log line:

```text
[ExpoCSharpV2] C# add(20, 22) returned 42
```

## Task 7: Merge Living Spec Delta

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Move or remove: `docs/changes/2026-06-30-mobile-app-nativeaot-connector/`

- [ ] **Step 1: Merge accepted requirements**

Add concise current-state requirements for the mobile NativeAOT proof and React
Native connector.

- [ ] **Step 2: Archive transient artifacts**

Move this change directory under `docs/archive/changes/` after the proof is
implemented and verified.

- [ ] **Step 3: Final scan**

Run:

```bash
git diff --check
git diff --cached --name-only -- docs experiments native managed \
  | xargs perl -ne 'print "$ARGV:$.:$_" if /\Q$ENV{HOME}\E/ || /\Q$ENV{USER}\E/'
```

Expected: `git diff --check` passes and the scan finds no committed local
absolute paths or usernames.
