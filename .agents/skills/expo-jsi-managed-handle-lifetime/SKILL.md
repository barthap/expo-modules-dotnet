---
name: expo-jsi-managed-handle-lifetime
description: "Use when reviewing, designing, or editing expo-modules-csharp managed JSI handle ownership: Expo.JSI owned wrappers, JavaScriptHandleScope and scoped refs, host-function callback lifetimes, JavaScriptRuntime.Execute reentrancy, JavaScriptValueCodec and generated module JavaScriptValue ownership transfer, ABI opaque-handle ownership, and leak pitfalls such as .AsValue().AsObject() chains that create undisposed owned wrappers."
---

# Expo JSI Managed Handle Lifetime

## Overview

Use this skill to review bridge-handle lifetime. Keep it focused on ownership:
who must dispose, who may retain, what may escape, and where ownership
transfers.

## First Reads

Read only the files touched by the question. Useful ownership entry points:

- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Internal/JavaScriptHandleScope.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObject.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptFunction.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueRef.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptArguments.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptHostFunction.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`

## Ownership Model

Use these terms consistently:

- Owned wrapper: `JavaScriptValue`, `JavaScriptObject`, `JavaScriptArray`,
  `JavaScriptFunction`, promise wrappers, and error wrappers. If an API returns
  one, the caller owns a native bridge handle and must dispose it unless
  ownership is explicitly transferred or detached.
- Scoped ref: `JavaScriptValueRef`, `JavaScriptObjectRef`, `JavaScriptArrayRef`,
  and `JavaScriptArguments` values. These are temporary views valid only inside
  the current runtime access frame. Do not store, capture, dispose, or return
  them.
- Retain: the explicit transition from scoped ref to owned wrapper. After
  `Retain()`, normal dispose rules apply.
- Detach: the explicit transition from managed owned wrapper to native return
  handling. After `Detach()`, the managed wrapper must not be used again.

Do not say wrappers own JavaScript objects. They own bridge handles.

## Handle Scope

`JavaScriptHandleScope` is the lifetime fence for scoped refs and the
same-runtime reentrancy marker:

- Enter one scope for each managed runtime access frame: `Execute`,
  scheduled runtime work, or host-function callback.
- Dispose scopes in strict stack order.
- Track only traversal-created temporary handles, such as scoped property reads
  or scoped value-to-object/array conversions.
- Do not track root handles behind owned wrappers, `thisValue`, or callback
  arguments. Those are owned or borrowed by another frame.
- If `JavaScriptRuntime.Execute` is called while a scope is already current for
  the same runtime, run inline under a nested handle scope instead of calling
  the host sync scheduler. The nested scope preserves temporary-handle cleanup;
  avoiding scheduler reentry prevents React Native sync deadlocks.

## Traversal Rules

For temporary reads, prefer `Ref` traversal so intermediate values are scoped:

```csharp
var name = value.Ref.AsObject().GetProperty("name").AsString();
```

For an escaping value, retain explicitly:

```csharp
using var name = value.Ref.AsObject().GetProperty("name").Retain();
```

Every owned conversion result is independently owned:

```csharp
using var functionValue = function.AsValue();
using var functionObject = functionValue.AsObject();
```

Flag fluent chains that hide owned intermediates:

```csharp
// Risky: AsValue() returns an owned JavaScriptValue that is never disposed.
using var objectWrapper = function.AsValue().AsObject();

// Preferred when an owned object must escape.
using var value = function.AsValue();
using var objectWrapper = value.AsObject();

// Preferred for temporary inspection.
using var value = function.AsValue();
var objectRef = value.Ref.AsObject();
```

Search for chained owned conversions:

```sh
rg "\\.AsValue\\(\\)\\.(AsObject|AsArray|AsFunction)\\(" packages/expo-modules-dotnet
rg "\\.(AsObject|AsArray|AsFunction)\\(\\)\\.(AsValue|AsObject|GetProperty)\\(" packages/expo-modules-dotnet
```

Treat each match as suspicious until a named `using var` or scoped-ref path
proves all owned handles are released.

## Host Functions

`thisValue`, `JavaScriptArguments`, and argument refs are callback-scoped. Do
not capture them after the callback returns. Decode before leaving the callback;
for async generated functions, validate and decode before `await`.

Host functions return an owned `JavaScriptValue`. After return, native bridge
code takes that handle. Do not dispose it after returning it.

## Generated Module Ownership

For `JavaScriptValueCodec` and generated `[JS]` functions:

- `Decode(JavaScriptValueRef, runtime)` retains the scoped ref into an owned
  wrapper.
- `Decode(JavaScriptValue, runtime)` retains the input into a new owned wrapper.
- `Encode(JavaScriptValue, runtime)` returns the same wrapper; ownership
  transfers to generated glue.
- `JavaScriptValue` parameters are owned by generated glue for the invocation
  lifetime. Authored code must not dispose or store them.
- A returned `JavaScriptValue` transfers ownership to generated glue. Authored
  code must not dispose it after returning it.
- `Task<JavaScriptValue>` results transfer ownership when the task completes;
  generated glue must keep the wrapper alive until Promise settlement value is
  created.

Only mention `_expoDotnet`, `EventEmitter`, `NativeModule`, or class/prototype
ABI when they affect ownership of retained handles or callback/listener state.
Otherwise leave module-boundary guidance to the specs.

## Review Checklist

Before accepting a change in this area, verify:

- Every owned wrapper returned by an API is disposed, transferred, or detached.
- Handle scopes are entered for runtime access and disposed in stack order.
- Fluent chains do not hide undisposed owned wrappers.
- Scoped refs do not escape the runtime frame, host-function callback, or
  generated async pre-await decode phase.
- `JavaScriptValueCodec` transfer semantics match generated glue behavior.
- Callback/listener state has a clear owner and exactly one release path.
