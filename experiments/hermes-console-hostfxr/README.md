# Hermes Console HostFXR Proof

This experiment creates a real Hermes-backed JSI runtime in native C++, passes
an `expo_jsi_api` function table plus opaque runtime handle into managed code
through HostFXR, and verifies bridge paths without P/Invoke:

- managed code can create, inspect, read, and release JavaScript values through
  opaque JSI handles;
- JavaScript installs and calls a generated-looking module function at
  `global.expo.modules.Math.add`. Native C++ owns the JSI host function
  plumbing, while C# generated-looking code decodes borrowed arguments, calls
  `MathModule.Add`, and returns an owned JavaScript value handle.
- managed code can create and read JavaScript strings as strict UTF-8 text,
  including non-ASCII data and embedded NUL bytes;
- JavaScript installs and calls `global.expo.modules.Text.greet`, which decodes
  a borrowed string argument in C# and returns a JavaScript string value.

Build Hermes from the official repository first:

```sh
scripts/build-hermes-macos.sh
```

The script downloads `facebook/hermes` into `build/hermes/source`, builds the
macOS `hermesvm.framework`, and leaves headers/frameworks under
`build/hermes/source/destroot`.

CMake defaults to that `destroot`. To use a different local prebuilt, pass
`-DHERMES_PREBUILT_ROOT=<destroot>`.
