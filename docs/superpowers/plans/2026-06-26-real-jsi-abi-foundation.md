# Real JSI ABI Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the real C++ JSI -> C ABI -> C# bridge foundation after the standalone HostFXR smoke experiment is proven.

**Architecture:** This later slice must use real JSI from an approved upstream path instead of fake runtime/value handles. The C++ layer owns JSI mechanics, the C ABI exposes opaque handles and structured results, and C# owns only managed declarations/wrappers over that ABI.

**Tech Stack:** Real JSI from an approved expo-desktop, React Native macOS, React Native Windows, or narrow local JSI dependency; CMake; C++20; .NET `net10.0`; C ABI structs; C# unsafe interop declarations.

---

## Scope Boundary

This plan is intentionally separate from the HostFXR smoke plan. Do not execute
it until the HostFXR smoke proof is recorded and the real JSI upstream is chosen.

Do not build fake JSI. A fake handle table does not prove the bridge semantics
we care about.

## Required Decision Before Execution

Choose one upstream path for real JSI:

- expo-desktop-provided JSI context;
- React Native macOS host context;
- React Native Windows host context;
- narrow local JSI dependency used only for a headless proof.

Record the decision before creating ABI implementation code.

## Expected Future File Areas

Create or modify only after the upstream decision:

- `native/include/expo_csharp_jsi.h`
- `native/packages/bridge/`
- `managed/packages/Expo.CSharpJsi/`
- `managed/packages/Expo.CSharpJsi.Tests/`
- `docs/spike-results/YYYY-MM-DD-real-jsi-abi-foundation.md`

Do not create in this slice unless explicitly approved:

- `Expo.ModulesCore`
- expo-desktop example app
- autolinking package
- npm package layout
- source generator
- view APIs

## Future Task Outline

- [ ] **Step 1: Record upstream JSI decision**

Create a result/decision note that states the selected upstream, why it was
chosen, what platform it proves, and what it leaves unproven.

- [ ] **Step 2: Add the public C ABI header**

Add opaque runtime/value/object/function/buffer/promise handles, value-kind
enums, structured error structs, scheduler callback structs, and explicit
retain/release operations.

The header must not expose `jsi::Runtime`, `jsi::Value`, `jsi::Object`,
`jsi::Function`, STL types, C++ exceptions, or React Native scheduler types to
C#.

- [ ] **Step 3: Implement the smallest real JSI operation**

Use the selected upstream to prove one concrete operation against real JSI, such
as creating a number, reading its kind, and reading the number value through the
C ABI.

- [ ] **Step 4: Add managed ABI declarations**

Add C# declarations in `Expo.CSharpJsi` that match the public C ABI exactly and
use blittable structs only.

- [ ] **Step 5: Add tests that prove real bridge semantics**

Add tests or proof executables that fail if the code is disconnected from real
JSI. Do not use tests that only prove enum values in isolation.

- [ ] **Step 6: Record ownership and scheduler findings**

The result note must state which side owns every runtime/value/string/buffer
involved, and which operations are synchronous versus scheduler-backed.

## Stop Gates

Stop before implementation if:

- the upstream JSI source is not chosen;
- the proof would require fake JSI;
- C# needs raw JSI layouts;
- C# needs React Native scheduler types;
- ownership cannot be described for a returned handle;
- the slice starts pulling in modules, views, autolinking, npm packaging, or
  example-app work.

## Self-Review

- This file is a separate future plan, not part of the HostFXR smoke execution.
- It explicitly rejects fake JSI.
- It requires choosing a real upstream before implementation.
