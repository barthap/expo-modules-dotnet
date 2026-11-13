# Roadmap

This page shows where the project is going. Current behavior lives in
`docs/specs/`, and the [module authoring guide](module-authoring-guide.md)
shows what modules can do today.

## Direction

The repo is the portable C# / JSI bridge successor to `expo-modules-windows`.
C++ owns JSI mechanics, C# owns module logic, and a C ABI with opaque handles
connects them.

Milestone order:

1. **Bridge and authoring core** (shipped): ABI, managed JSI wrappers,
   lifecycle, scheduling, generated bindings, codecs, events, ArrayBuffer,
   SharedObject, and autolinking across Android, iOS, macOS, and Windows.
2. **Authored modules** (in progress): real `expo-*-dotnet` packages for
   Windows and macOS, in this order: asset, constants, local file system,
   crypto. They exist to replace the expo-desktop native stubs on Windows.
   They register only into `_expoDotnet.modules`.
3. **expo-desktop integration** (in progress): expo-desktop 1.0 prebuild
   now generates the desktop app. Next, let a Windows app opt in to
   `*-dotnet` modules in place of the expo-desktop stubs. Windows only:
   on macOS, upstream `expo-modules-core` and the upstream Expo packages
   already work, which is why our packages are named `expo-*-dotnet`.
4. **Production hardening**: mobile NativeAOT trimming and export audits,
   and desktop packaging.

Milestones 2 and 3 must show real value: four authored modules and one demo
app that uses upstream Expo, expo-desktop, and .NET modules on all four
platforms.

Standing quality rule: authored modules use no workarounds for missing core
features. Fix the core first.

The C# authoring API follows upstream Expo Modules v2 (the Swift macros and
the Kotlin annotations). Where both upstream platforms ship a feature, C#
matches it, even if existing C# modules must change.

## Shipped

- The bridge and authoring core on Android, iOS, macOS, and Windows
- Build-time generated bindings with NativeAOT support
- Promises, events (also on shared objects), and JavaScript callbacks
- Shared objects (`SharedObject`, `SharedRef<T>`), also as nullable and
  abstract-base parameters
- Codecs for records, lists, dictionaries, nullable types, two-member unions,
  `ArrayBuffer`, and `Uint8Array`
- Autolinking of .NET modules on all four platforms
- expo-desktop 1.0 prebuild for the desktop example app, on macOS and Windows
- `expo-asset-dotnet`, `expo-constants-dotnet`, `expo-crypto-dotnet` (digests,
  random values, and UUIDs), and `expo-file-system-dotnet` (local files: read,
  write, bytes, copy, and move)

## Planned

- `expo-crypto-dotnet`: AES-GCM encryption
- Windows apps can opt in to `*-dotnet` modules in place of the expo-desktop
  stubs
- A demo app that uses upstream Expo, expo-desktop, and .NET modules on all
  four platforms
- Coded JavaScript errors, as upstream Expo modules have
- Authoring API parity with upstream Expo Modules v2: unions, record field
  defaults, constants, more typed arrays, tuples, callbacks as delegates,
  and upstream naming for modules and events
- A module authoring guide that maps C# to the upstream Swift and Kotlin
  syntax, with a feature support table
- TypeScript types generated with the upstream `typegen` tool
- Desktop packaging for macOS (HostFXR and NativeAOT)
- Mobile NativeAOT hardening
