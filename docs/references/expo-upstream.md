# Expo Upstream References

Use these notes when comparing this C# bridge to upstream Expo APIs:

- `expo-modules-jsi-swift-wrapper-model.md` maps Apple/Swift JSI wrappers,
  ownership, conversion, host-function, and scheduler patterns to the C# opaque
  handle design.
- `expo-modules-v2-api-syntax.md` summarizes the Swift v2 module authoring API.
- `expo-modules-v2-macro-expansions.md` sketches the generated-looking binding
  shape that should inform the C# source-generator path.

Primary upstream packages:

- [`expo-modules-jsi`](https://github.com/expo/expo/tree/main/packages/expo-modules-jsi)
- [`expo-modules-core`](https://github.com/expo/expo/tree/main/packages/expo-modules-core)
