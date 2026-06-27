# Result: Hermes Dependency Probe

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: `<repo>`
Branch or commit: current `main`

## Question

Can packaged Hermes artifacts be consumed by a tiny standalone native CMake
proof without custom Hermes builds, source vendoring, patching, or
reverse-engineering React Native/expo app build internals?

## Commands Run

Dependency build:

```sh
scripts/build-hermes-macos.sh
```

Expected meaningful output:

```text
Hermes source: <repo>/build/hermes/source
Hermes ref: 896d643e7453
Hermes macOS prebuilt ready:
  HERMES_PREBUILT_ROOT=<repo>/build/hermes/source/destroot
```

Temporary probe:

```sh
PROBE_DIR="$(mktemp -d -t expo-csharp-hermes-probe)"
cat > "$PROBE_DIR/main.cpp"
cat > "$PROBE_DIR/CMakeLists.txt"
<cmake> -S "$PROBE_DIR" -B "$PROBE_DIR/build" -DHERMES_PREBUILT_ROOT="<repo>/build/hermes/source/destroot"
<cmake> --build "$PROBE_DIR/build" --target hermes_probe
"$PROBE_DIR/build/hermes_probe"
```

Meaningful output:

```text
-- Build files have been written to: <temp-probe>/build
[100%] Built target hermes_probe
hermes dependency probe: ok
```

## Expected Result

The temporary native probe includes `jsi/jsi.h`, includes Hermes runtime
headers, creates a Hermes-backed `facebook::jsi::Runtime`, checks a primitive
JSI number, prints `hermes dependency probe: ok`, and exits 0.

## Actual Result

The dependency build succeeded from the official `facebook/hermes` repository
at `896d643e7453f507b062140f849f89ecf5448a88`.

The proof uses packaged Hermes artifacts built locally from that source:

- headers from `<repo>/build/hermes/source/destroot/include`;
- framework from
  `<repo>/build/hermes/source/destroot/Library/Frameworks/macosx/hermesvm.framework`.

## Artifacts

- Temporary probe source: `<temp-probe>/main.cpp`
- Temporary probe CMake file: `<temp-probe>/CMakeLists.txt`

Temporary probe files were not committed.

## Ownership And Lifetime Findings

The probe owns the Hermes runtime in a `std::unique_ptr` and lets it clean up at
process exit. No bridge handles are created in this probe.

## Platform Findings

The probe is macOS-local and standalone. It does not use expo-desktop, RNW, RN
macOS, or app packaging.

## Decision

Go after `scripts/build-hermes-macos.sh` succeeds. The Hermes console JSI
HostFXR proof uses the generated `destroot` through `HERMES_PREBUILT_ROOT`.
