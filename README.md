# Expo Modules C\#

Portable C# / JSI bridge research for Expo Modules.

This repo explores a cross-platform architecture where C++ owns JSI mechanics,
C# owns module logic, and a small C ABI connects them. The first goal is a
headless bridge that can be developed on macOS, with later adapters for React
Native Windows and React Native macOS.

Start with:

- `docs/README.md` for the research plan
- `docs/agent-plan/` for implementation phases
- `docs/learning-guide/` for .NET interop background

Previous Windows-first prototype: [expo-modules-windows](<previous-windows-prototype-repo>).

