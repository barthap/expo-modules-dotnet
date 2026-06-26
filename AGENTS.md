# AGENTS.md

## Project Rule

This repo is the portable C# / JSI bridge successor to the previous
`expo-modules-windows` prototype.

Core architecture rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## Before Working

Read:

1. `docs/README.md`
2. `docs/agent-plan/01-architecture.md`
3. The specific spike or phase doc relevant to the task

Use `docs/references/previous-windows-prototype.md` only for historical context.

If `AGENTS.local.md` exists, read it after this file. It is gitignored and may
contain machine-specific paths or local workflow notes. Do not commit it.

## Constraints

- Keep the portable core headless unless the task explicitly asks for a platform adapter.
- Do not introduce RNW, WinUI, AppKit, or packaging dependencies into the portable core.
- Do not expose raw `jsi::Runtime`, `jsi::Value`, or `jsi::Object` layouts to C#.
- Do not use runtime hot-path reflection for v2 generated bindings.
- Prefer HostFXR for early development, but keep ABI and generated bindings NativeAOT-compatible.
- Do not create GitHub PRs, publish packages, or post comments without explicit user approval.

## Verification

Each spike must record:

- hypothesis
- commands run
- expected result
- actual result
- artifacts
- ownership/lifetime findings
- scheduler findings
- stop/go decision
