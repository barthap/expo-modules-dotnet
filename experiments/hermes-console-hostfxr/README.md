# Hermes Console HostFXR Proof

This experiment creates a real Hermes-backed JSI runtime in native C++, passes
an `expo_jsi_api` function table plus opaque runtime handle into managed code
through HostFXR, and verifies that `Expo.JSI` can create, inspect, read, and
release a number value without P/Invoke.

Build Hermes from the official repository first:

```sh
scripts/build-hermes-macos.sh
```

The script downloads `facebook/hermes` into `build/hermes/source`, builds the
macOS `hermesvm.framework`, and leaves headers/frameworks under
`build/hermes/source/destroot`.

CMake defaults to that `destroot`. To use a different local prebuilt, pass
`-DHERMES_PREBUILT_ROOT=<destroot>`.
