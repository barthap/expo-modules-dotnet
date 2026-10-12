# Android Generated Host Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` for each behavior change and `superpowers:verification-before-completion` before claiming completion.

**Goal:** Make Android APK packaging consume the current generated
`ExpoDotnetHost` through a declared Gradle task output.

**Architecture:** The Android Gradle hook retains the loader-owned JNI staging
directory, declares it as the link task output, and makes the link task a
direct prerequisite of the JNI-folder and native-library merge tasks. This
establishes task ordering and input/output ownership for native-library
packaging without a late variant API registration.

**Tech Stack:** Gradle Groovy DSL, Android Gradle Plugin 8.12, NativeAOT,
Markdown living specs.

---

### Task 1: Establish the Android merge-task regression proof

**Files:**

- Modify: `packages/expo-modules-dotnet/android/build.gradle`

- [x] Run the Android native-library merge before changing the hook and record
  the stale staged/merged host mismatch.
- [x] Use Gradle's task graph and output validation to prove the link task is a
  direct prerequisite of both merge phases.

### Task 2: Declare and wire Android staging output

**Files:**

- Modify: `packages/expo-modules-dotnet/android/build.gradle`

- [x] Declare the existing Android `jniLibs/arm64-v8a` directory as the
  `expoDotnetLink` task output.
- [x] Make both application `merge*JniLibFolders` and `merge*NativeLibs` tasks
  directly depend on the link task.

### Task 3: Merge the contract into living documentation and verify packaging

**Files:**

- Modify: `docs/specs/dotnet-autolinking.md`

- [x] Update the Gradle-hook scenario to require declared staging output and
  direct merge-task dependencies.
- [x] Compare the staged host with the merged native-library output, then the
  stripped output with the APK's arm64-v8a entry after `:app:assembleDebug`.
- [x] Run `scripts/test-managed.sh`, `scripts/format.sh --check --all`, and
  `git diff --check`.
