# Expo.JSI.Tests

Run the Hermes-backed test suite with:

```sh
scripts/test-managed.sh
```

The script builds the native Hermes testhost and passes
`EXPO_JSI_TESTHOST_LIBRARY` to `dotnet test`.

Add low-level runtime, value, string, ownership, and host-function tests here.
Module behavior tests belong in `Expo.ModulesCore.Tests`.
