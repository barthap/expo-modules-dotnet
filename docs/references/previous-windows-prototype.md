# Previous Windows Prototype

This repository starts from research done in the earlier Windows-first prototype:

- Repository: [expo-modules-windows](<previous-windows-prototype-repo>).
- Original focus: prove C# Expo Modules on React Native Windows through `expo-desktop`, HostFXR, Windows autolinking, and app-local C# modules.

## Why This New Repo Exists

`expo-modules-windows` was useful for proving that a Windows app can load C# modules through a native host, but its architecture mixed several concerns:

- React Native Windows integration
- `expo-desktop` runtime ownership
- HostFXR loading
- C# module definitions
- JSI host object wiring
- Windows view and packaging concerns

This repo, `expo-modules-csharp`, is the clean portable successor. Its focus is the cross-platform C# / JSI bridge:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## What To Reuse From The Prototype

The old repo remains useful as historical reference for:

- Windows app integration constraints
- HostFXR proof work
- expo-desktop runtime composition
- Windows autolinking ideas
- RNW packaging and deployment lessons
- early C# module DSL experiments

## What Not To Carry Forward Blindly

Do not treat the old repo as the architecture source of truth for this repo.

In particular, do not copy forward:

- Windows-specific assumptions into the portable core
- direct RNW dependencies into the C# / JSI bridge
- reflection-heavy runtime discovery as the v2 path
- JSON as the ordinary JSI value bridge
- WinUI or AppX packaging concerns into headless proofs

## Current Source Of Truth

For new work, read this repo’s docs instead:

- `docs/README.md`
- `docs/specs/`
- `docs/roadmap.md`

The old Windows prototype should answer “how did we get here?”  
This repo should answer “what are we building now?”
