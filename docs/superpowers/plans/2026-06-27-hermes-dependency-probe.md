# Hermes Dependency Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decide whether packaged Hermes artifacts can support the next real-JSI bridge proof without vendoring, patching, custom Hermes builds, or reverse-engineering app build internals.

**Architecture:** This is an evidence-gate plan, not the bridge implementation. It inspects local packaged Hermes/React Native artifacts and, if possible, builds one tiny standalone CMake probe that creates a real Hermes-backed `facebook::jsi::Runtime`. If the probe path is not small and understandable, stop and switch the next design path to hosted expo-desktop/RN runtime integration.

**Tech Stack:** Shell discovery commands, CMake, C++20, packaged Hermes/JSI artifacts if locally available.

---

## Constraints

- Do not use git worktrees.
- Do not create a repo-root `CMakeLists.txt`.
- Do not vendor Hermes source.
- Do not patch Hermes.
- Do not create fake JSI.
- Do not create `Expo.JSI`, `native/packages/jsi`, or bridge ABI code in this plan.
- Do not install packages unless the user explicitly approves.
- Stop if no small standalone Hermes path is visible from existing local/package artifacts.

## File Map

Create only one of these result notes:

- `docs/spike-results/2026-06-27-hermes-dependency-probe.md`

Optional temporary files during probing:

- `$PROBE_DIR/CMakeLists.txt`
- `$PROBE_DIR/main.cpp`

Do not commit temporary probe files.

Delete stale plan:

- `docs/superpowers/plans/2026-06-26-real-jsi-abi-foundation.md`

## Task 1: Inspect Local Hermes Candidates

**Files:**
- Create: `docs/spike-results/2026-06-27-hermes-dependency-probe.md`

- [ ] **Step 1: List local candidate package roots**

Run:

```bash
find . -maxdepth 5 -type d \( -name hermes-engine -o -name react-native -o -name hermes \) -print
find <local-dev-root> -maxdepth 6 -type d \( -name hermes-engine -o -name react-native \) -print 2>/dev/null | head -100
```

Expected: command succeeds. It may print no candidates in this repo; nearby checkouts may provide candidates.

- [ ] **Step 2: Search candidates for required JSI/Hermes files**

Run:

```bash
{
  find . -maxdepth 8 -path '*jsi/jsi.h' -print
  find . -maxdepth 8 -path '*HermesRuntime.h' -print
  find . -maxdepth 8 \( -name 'libhermes*.a' -o -name 'libhermes*.dylib' -o -name 'hermes*.xcframework' \) -print
  find <local-dev-root> -maxdepth 8 -path '*jsi/jsi.h' -print 2>/dev/null | head -50
  find <local-dev-root> -maxdepth 8 -path '*HermesRuntime.h' -print 2>/dev/null | head -50
  find <local-dev-root> -maxdepth 8 \( -name 'libhermes*.a' -o -name 'libhermes*.dylib' -o -name 'hermes*.xcframework' \) -print 2>/dev/null | head -50
}
```

Expected: either discover a compact set of headers/libraries that can be consumed by standalone CMake, or gather enough evidence that packaged Hermes is not locally consumable.

- [ ] **Step 3: Stop if required files are not found**

If Step 2 does not find `jsi/jsi.h`, Hermes runtime headers, and a linkable Hermes artifact, create the result note with this content:

```markdown
# Result: Hermes Dependency Probe

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: <repo>
Branch or commit: current branch

## Question

Can packaged Hermes artifacts be consumed by a tiny standalone native CMake proof
without custom Hermes builds, source vendoring, patching, or reverse-engineering
React Native/expo app build internals?

## Commands Run

Paste the exact discovery commands and meaningful output.

## Expected Result

Find locally consumable `jsi/jsi.h`, Hermes runtime headers, and linkable Hermes
runtime libraries or frameworks.

## Actual Result

Required files were not all found in a compact packaged layout.

## Decision

Stop. Do not implement the Hermes console JSI bridge proof from fake or partial
artifacts. Switch the next design path to a hosted expo-desktop/RN proof, or ask
the user whether to install/provide a Hermes package source.

## Follow-Up Questions

- Should the next proof receive a runtime from expo-desktop, RN macOS, or RNW?
- Should a package install step be approved to inspect current React Native
  Hermes artifacts in isolation?
```

Then run:

```bash
git add docs/spike-results/2026-06-27-hermes-dependency-probe.md docs/superpowers/plans
git commit -m "Replace stale real JSI plan with Hermes probe"
```

Stop after the commit.

## Task 2: Build Tiny Hermes Probe If Required Files Exist

**Files:**
- Create temporarily: `$PROBE_DIR/CMakeLists.txt`
- Create temporarily: `$PROBE_DIR/main.cpp`
- Create: `docs/spike-results/2026-06-27-hermes-dependency-probe.md`

- [ ] **Step 1: Create temporary probe source**

Run:

```bash
PROBE_DIR="$(mktemp -d -t expo-csharp-hermes-probe)"
mkdir -p "$PROBE_DIR"
```

Create `$PROBE_DIR/main.cpp` with:

```cpp
#include <hermes/hermes.h>
#include <jsi/jsi.h>

#include <iostream>
#include <memory>

int main()
{
  std::unique_ptr<facebook::jsi::Runtime> runtime =
    facebook::hermes::makeHermesRuntime();
  if (!runtime) {
    std::cerr << "Failed to create Hermes runtime." << std::endl;
    return 1;
  }

  facebook::jsi::Value value(42.5);
  if (!value.isNumber() || value.asNumber() != 42.5) {
    std::cerr << "Hermes JSI number check failed." << std::endl;
    return 1;
  }

  std::cout << "hermes dependency probe: ok" << std::endl;
  return 0;
}
```

- [ ] **Step 2: Create temporary CMake file with discovered paths**

Create `$PROBE_DIR/CMakeLists.txt` using the exact include and library paths found in Task 1. The file must be understandable enough to paste into the result note. Do not use broad recursive include paths if a specific include/library path exists.

The shape should be:

```cmake
cmake_minimum_required(VERSION 3.24)
project(ExpoCSharpHermesProbe LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_executable(hermes_probe main.cpp)

target_include_directories(hermes_probe PRIVATE
  "/absolute/path/to/jsi/include/root"
  "/absolute/path/to/hermes/include/root")

target_link_libraries(hermes_probe PRIVATE
  "/absolute/path/to/linkable/hermes/artifact")
```

If the exact CMake file cannot be written without broad app-build internals, stop and write the result note with `Decision: Stop`.

- [ ] **Step 3: Configure, build, and run probe**

Run:

```bash
cmake -S "$PROBE_DIR" -B "$PROBE_DIR/build"
cmake --build "$PROBE_DIR/build" --target hermes_probe
"$PROBE_DIR/build/hermes_probe"
```

Expected output:

```text
hermes dependency probe: ok
```

- [ ] **Step 4: Record probe result**

Create `docs/spike-results/2026-06-27-hermes-dependency-probe.md` with:

```markdown
# Result: Hermes Dependency Probe

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: <repo>
Branch or commit: current branch

## Question

Can packaged Hermes artifacts be consumed by a tiny standalone native CMake proof
without custom Hermes builds, source vendoring, patching, or reverse-engineering
React Native/expo app build internals?

## Commands Run

Paste the exact discovery, configure, build, and run commands with meaningful output.

## Expected Result

The temporary native probe includes `jsi/jsi.h`, includes Hermes runtime headers,
creates a Hermes-backed `facebook::jsi::Runtime`, checks a primitive JSI number,
prints `hermes dependency probe: ok`, and exits 0.

## Actual Result

Paste the observed output.

## Artifacts

- Temporary probe source: `$PROBE_DIR/main.cpp`
- Temporary probe CMake file: `$PROBE_DIR/CMakeLists.txt`

## Ownership And Lifetime Findings

The probe owns the Hermes runtime in a `std::unique_ptr` and lets it clean up at
process exit. No bridge handles are created in this probe.

## Platform Findings

The probe is macOS-local and standalone. It does not use expo-desktop, RNW, RN
macOS, or app packaging.

## Scheduler Findings

No scheduler is involved in this dependency probe.

## Reflection/AOT Findings

No managed code runs in this probe.

## Decision

Go if the probe built and printed `hermes dependency probe: ok`. Write the full
Hermes console JSI implementation plan using the exact include/library paths
from this note.

## Follow-Up Questions

- Should the next implementation plan commit the exact CMake glue under
  `native/packages/jsi` and `experiments/hermes-console-hostfxr`?
```

- [ ] **Step 5: Commit probe result and replacement plan**

```bash
git add docs/spike-results/2026-06-27-hermes-dependency-probe.md docs/superpowers/plans
git commit -m "Replace stale real JSI plan with Hermes probe"
```

## Final Completion Check

- [ ] `docs/superpowers/plans/2026-06-26-real-jsi-abi-foundation.md` no longer exists.
- [ ] `docs/superpowers/plans/2026-06-27-hermes-dependency-probe.md` exists.
- [ ] No `native/`, `managed/packages/Expo.JSI`, or `experiments/hermes-console-hostfxr` files were created by this plan.
- [ ] The result note clearly says `Go` or `Stop`.
- [ ] `git status --short --branch` is clean except unrelated pre-existing untracked reference docs.

## Self-Review

- Spec coverage: covers the approved evidence-based Hermes dependency gate.
- Scope check: intentionally does not implement bridge code before Hermes evidence exists.
- Placeholder scan: no committed source files contain placeholders. Temporary CMake paths must be filled from discovery evidence before running Task 2.
- Type consistency: the probe uses `facebook::hermes::makeHermesRuntime` and `facebook::jsi::Runtime` only as a dependency check, not as bridge implementation.
