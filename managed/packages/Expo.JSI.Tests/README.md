# Expo.JSI.Tests

Run the Hermes-backed test suite with:

```sh
scripts/test-jsi.sh
```

The script builds the native Hermes testhost and passes
`EXPO_JSI_TESTHOST_LIBRARY` to `dotnet test`.

Add low-level runtime, value, string, ownership, and host-function tests here.
Module behavior tests are temporary here until `Expo.ModulesCore` exists; move
them to `Expo.ModulesCore.Tests` when that package is added.
