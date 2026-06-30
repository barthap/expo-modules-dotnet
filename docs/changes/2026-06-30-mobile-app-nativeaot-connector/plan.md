# Mobile App NativeAOT Connector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local Expo app proof that calls a NativeAOT C# `[ExpoModule]` from JavaScript through the real React Native Hermes runtime and the existing `expo_jsi.h` ABI.

**Architecture:** The managed proof is a NativeAOT library with one exported registration entry point. Native iOS and Android app glue borrow the active React Native Hermes runtime, wrap it in a `native/packages/jsi` React Native connector, then pass the existing ABI table and opaque runtime handle to managed code. JavaScript invokes a tiny deferred installer after Expo owns its module namespace, then reads the generated `globalThis.expo.modules.ExpoCSharpV2.add` result and renders it on screen.

**Tech Stack:** Expo app, React Native Hermes, C++20 JSI, `native/include/expo_jsi.h`, `native/packages/jsi`, .NET 10 NativeAOT, `Expo.JSI`, `Expo.ModulesCore`, `Expo.ModulesCore.Generator`.

---

## File Structure

- `native/packages/jsi/include/ReactNativeRuntimeConnector.h`: React Native runtime connector public API.
- `native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`: connector implementation over an existing `facebook::jsi::Runtime` and scheduler callbacks.
- `experiments/mobile-app/`: generated Expo app plus local native module glue.
- `experiments/mobile-app/modules/expo-csharp-v2`: local Expo module that installs the C# provider on app startup.
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

- [ ] **Step 1: Add a focused connector test or compile check**

Add the connector files and include them in a native compile target used by the
mobile app or a lightweight syntax check.

- [ ] **Step 2: Implement scheduler callback types**

Represent async and optional sync scheduling as injected C++ callables so iOS
and Android can adapt their platform-specific New Architecture primitives
without pulling React Native headers into `ExpoJsiBridge.cpp`.

- [ ] **Step 3: Implement `JsiRuntimeConnector`**

The connector stores a borrowed `facebook::jsi::Runtime *`, reports validity
without touching invalid runtime state, and delegates execution through the
injected scheduler.

- [ ] **Step 4: Verify ABI use**

Run:

```bash
rg "expo_jsi.h|ReactNativeRuntimeConnector|CallInvoker|RuntimeExecutor" native/packages/jsi
```

Expected: the connector exists in `native/packages/jsi`, includes
`expo_jsi.h` through the bridge path, and names the React Native scheduling
primitive boundary.

## Task 4: Wire iOS Local Module

**Files:**
- Create/modify files under `experiments/mobile-app/modules/expo-csharp-v2/ios/`
- Modify: `experiments/mobile-app/ios/Podfile` only if the local module scaffold does not autolink the required C++ sources

- [ ] **Step 1: Scaffold a local Expo module**

Run:

```bash
cd experiments/mobile-app && CI=1 bunx create-expo-module --local --name ExpoCSharpV2 --package expo.modules.csharpv2
```

Expected: local module scaffold exists under `modules/`.

- [ ] **Step 2: Remove unused view boilerplate**

Keep only the native module needed to install the C# provider.

- [ ] **Step 3: Add C++ installer**

The installer links `native/packages/jsi`, resolves
`expo_mobile_v2_register_modules`, creates a runtime handle with
`createRuntimeHandle(...)`, calls the export, and releases the handle after
installation.

- [ ] **Step 4: Hook the installer into React Native startup**

Use the Expo module/app delegate lifecycle point that has access to the bridge
runtime installation path. If the generated scaffold cannot expose that point,
patch the app delegate narrowly and document the stop/go finding in this
change directory.

## Task 5: Wire Android Local Module

**Files:**
- Create/modify files under `experiments/mobile-app/modules/expo-csharp-v2/android/`
- Modify: `experiments/mobile-app/android/settings.gradle` and app Gradle files only if local module autolinking does not include C++ sources

- [ ] **Step 1: Add CMake/JNI glue**

Build the same installer shape as iOS, using the Android React Native runtime
access point and call-invoker/runtime-executor primitive available in the
prebuilt app.

- [ ] **Step 2: Link NativeAOT output**

Copy or reference the NativeAOT shared library from the managed publish output
for the Android ABI being tested.

- [ ] **Step 3: Install module on runtime creation**

Call the managed registration export once the Hermes runtime is available and
before app JavaScript calls `V2Math.add`.

## Task 6: Run Platform Verification

**Files:**
- Create: screenshot artifacts under `experiments/mobile-app/artifacts/`

- [ ] **Step 1: Run managed verification**

Run:

```bash
scripts/test-managed.sh
```

Expected: all managed Hermes-backed tests pass.

- [ ] **Step 2: Run formatting verification**

Run:

```bash
scripts/format.sh --check --all
```

Expected: formatting check passes.

- [ ] **Step 3: Run iOS app**

Run the app on the preferred iPhone 17 Pro simulator and save a screenshot
showing `C# V2Math.add result: 42`.

- [ ] **Step 4: Run Android app**

Run the app on an Android emulator and save a screenshot showing
`C# V2Math.add result: 42`.

- [ ] **Step 5: Capture Metro logs**

Save or quote the Metro log line:

```text
[ExpoCSharpV2] V2Math.add(20, 22) returned 42
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
