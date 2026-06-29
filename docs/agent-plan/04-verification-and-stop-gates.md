# 04 - Verification And Stop Gates

Last refreshed: 2026-06-29.

## Purpose

This file defines how future agents prove progress in the current repository.
The recurring failure mode is treating file existence, keyword search, or an
old spike result as current evidence. Completion evidence must match the scope
of the claim.

For this repo, proof means:

- the requested artifact exists in the expected package or doc area;
- the artifact contains the required operational detail;
- command output demonstrates the intended behavior;
- ownership, platform, and reflection constraints are explicitly checked;
- unresolved decisions are named instead of hidden.

## General Verification Rules

Before claiming completion of any slice:

1. Re-read the relevant objective, spec, or phase section.
2. Make a checklist from every explicit requirement.
3. Identify the evidence that would prove each item.
4. Run the commands that produce that evidence.
5. Read the full output, including failures and skipped checks.
6. If evidence is weak or indirect, keep working or state the gap.

Do not use these as proof:

- "the file exists";
- "the keyword appears";
- "the code looks plausible";
- "an earlier run passed";
- "a subagent said it was done";
- "the old note already explained it."

## Project-Wide Constraints

- Do not publish to GitHub, open PRs, or post comments without approval.
- Do not introduce RNW, WinUI, AppKit, or packaging dependencies into the
  portable core.
- Do not commit local absolute paths, usernames, machine names, private
  hostnames, or machine-specific install paths.
- Prefer `bunx` over `npx`.
- Any `xcodebuild` command must pipe output to `xcsift -f toon`.
- Do not use worktrees unless the user explicitly asks for them.
- Keep generated-looking module proof code temporary until `Expo.ModulesCore`
  exists.

## Canonical Checks

For code changes to `Expo.JSI`, native ABI, native testhost, or tests:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

If formatting needs to be applied:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

For docs-only changes:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/agent-plan
```

Any match should be intentional.

For module-layer work after `Expo.ModulesCore` exists:

```sh
scripts/test-jsi.sh
dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
scripts/format.sh --check --all
git diff --check
```

For NativeAOT audit work:

```sh
dotnet publish -c Release -r osx-arm64 /p:PublishAot=true
nm -gU <publish-output>
rg "RequiresUnreferencedCode|RequiresDynamicCode|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" managed
```

## Mac-Local Responsibilities

Use Mac for work that does not require Windows tooling:

- maintaining docs;
- HostFXR and NativeAOT local loader proofs;
- C ABI and C# wrapper design;
- Hermes-backed headless tests;
- generated-looking v2 binding proof;
- source-generator prototype;
- ordinary `dotnet test`, `dotnet build`, and CMake checks;
- source review before pushing to a shared branch, if pushing is approved.

Mac-local proof is insufficient for:

- RNW app packaging;
- Visual Studio/MSBuild behavior;
- WinUI composition;
- Windows NativeAOT artifacts;
- Windows app screenshots.

## Windows-Remote Responsibilities

Use a Windows test machine for Windows-specific proof:

- RNW app integration;
- Visual Studio and MSBuild failures;
- Windows packaging;
- WinUI and XAML view work;
- Windows NativeAOT publish proof;
- app screenshots or interactive validation.

When coordinating remote work:

- steer the existing remote Codex session directly when asked;
- use pushed branches as shared source of truth only when the user approves
  pushing;
- use handoff Markdown only as notes, not as source of truth for committed
  architecture docs;
- compare remote dirty state against the target branch before cleaning it;
- do not let Windows packaging issues block headless Mac research.

Historical Windows proof caution:

The prior RNW proof had moved past an early PowerShell/Visual Studio discovery
gate and reached a DesktopBridge/AppX packaging blocker around
`GetFrameworkSdkPackages`. Do not treat that as part of the headless bridge
research unless the goal explicitly switches back to Windows app integration.

## Required Spike Result Evidence

Every spike result note must include:

```markdown
# Result: <name>

Date:
Machine:
Repo:
Branch or commit:

## Question

## Commands Run

## Expected Result

## Actual Result

## Artifacts

## Ownership And Lifetime Findings

## Platform Findings

## Scheduler Findings

## Reflection/AOT Findings

## Decision

## Follow-Up Questions
```

If any command fails, include the first meaningful error and explain whether it
blocks the spike or changes the next step.

## Stop Gates

Stop and ask the user before:

- adding a real RNW host app;
- adding a real React Native macOS host app;
- publishing to GitHub;
- opening a PR;
- posting GitHub comments;
- changing the C ABI after generated code or multiple tests depend on it;
- accepting runtime hot-path reflection for v2;
- moving view creation into the universal headless core;
- deciding that Windows packaging work should take priority over headless core
  work.

Stop and write an explicit decision point if:

- ownership cannot be described for a handle;
- scoped refs and owned wrappers become hard to distinguish;
- async work touches JSI without going through the runtime scheduler;
- a scheduler design requires exposing `react::CallInvoker`,
  `RuntimeExecutor`, or `RuntimeScheduler` to C# instead of wrapping it behind a
  portable service;
- NativeAOT compatibility conflicts with HostFXR convenience;
- source-generator output would need `MethodInfo.Invoke`;
- a platform adapter starts owning module invocation logic;
- the proof depends on JSON for ordinary JSI values.

## Completion Audit Template

Use this template before marking a multi-step goal complete:

```markdown
## Completion Audit

- Requirement:
  Evidence:
  Status: proven / incomplete / weak / contradicted

- Requirement:
  Evidence:
  Status:
```

Do not replace this audit with a keyword search.
