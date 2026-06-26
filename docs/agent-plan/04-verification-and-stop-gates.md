# 04 - Verification And Stop Gates

## Purpose

This file defines how future agents prove progress. The previous failure mode
was treating file existence and keyword search as completion evidence. Do not
repeat that. Completion evidence must match the scope of the claim.

For this research track, proof means:

- the requested artifact exists;
- the artifact contains the required operational detail;
- command output or written spike evidence demonstrates the intended behavior;
- ownership, platform, and reflection constraints are explicitly checked;
- unresolved decisions are named instead of hidden.

## General Verification Rules

Before claiming completion of any spike or phase:

1. Re-read the relevant objective or phase section.
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

- Do not modify production code during planning.
- Do not edit `docs_old/` unless explicitly asked.
- Do not publish to GitHub, open PRs, or post comments without approval.
- Do not create a new repository without approval.
- Prefer `bunx` over `npx`.
- Any `xcodebuild` command must pipe output to `xcsift -f toon`.
- Use `.sync/` only for Mac/Windows handoff notes; it is not source of truth
  for committed planning docs.

## Mac-Local Responsibilities

Use the Mac for work that does not require Windows tooling:

- writing and maintaining these docs;
- clean research repo setup after approval;
- HostFXR macOS loader proof;
- C ABI and C# wrapper design;
- headless JSI proof if dependencies build locally;
- generated-looking v2 binding proof;
- Roslyn source-generator prototype;
- macOS NativeAOT proof;
- ordinary `dotnet test`, `dotnet build`, and CMake checks;
- source review before pushing to a shared branch, if pushing is approved.

Common Mac command templates:

```sh
git status --short --branch
dotnet --info
dotnet build
dotnet test
cmake -S . -B build
cmake --build build
dotnet publish -c Release -r osx-arm64 /p:PublishAot=true
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|JsonSerializer" .
```

Mac-local proof is insufficient for:

- RNW app packaging;
- Visual Studio/MSBuild behavior;
- WinUI composition;
- Windows NativeAOT artifacts;
- Windows app screenshots.

## Windows-Remote Responsibilities

Use <windows-test-machine> for Windows-specific proof:

- RNW app integration;
- Visual Studio and MSBuild failures;
- Windows packaging;
- WinUI and XAML view work;
- Windows NativeAOT publish proof;
- app screenshots or interactive validation.

Known remote context:

```text
Remote machine: <windows-test-machine>
Remote repo: <windows-repo>
Mac repo: <windows-prototype-repo>
Shared notes: <repo>/.sync
```

When coordinating remote work:

- steer the existing remote Codex session directly when asked;
- use pushed branches as shared source of truth when the user approves pushing;
- use `.sync/` for handoff Markdown only;
- compare remote dirty state against `origin/<branch>` before cleaning it;
- do not let Windows packaging issues block headless Mac research.

Current historical Windows proof caution:

The prior RNW proof had moved past an early `pwsh.exe`/Visual Studio discovery
gate and reached a DesktopBridge/AppX packaging blocker around
`GetFrameworkSdkPackages`. Do not treat that as part of the headless bridge
research unless the goal explicitly switches back to Windows app integration.

## Required Spike Result Evidence

Every spike result note must include:

```markdown
# Spike N Result: <name>

Date:
Machine:
Repo/path:
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

- creating a new repository;
- modifying production code;
- editing `docs_old/`;
- adding a real RNW host app;
- adding a real React Native macOS host app;
- publishing to GitHub;
- opening a PR;
- posting GitHub comments;
- changing the C ABI after more than one spike depends on it;
- accepting runtime hot-path reflection for v2;
- moving view creation into the universal headless core;
- deciding that Windows packaging work should take priority over headless core
  research.

Stop and write an explicit decision point if:

- ownership cannot be described for a handle;
- borrowed and owned wrappers become hard to distinguish;
- async work touches JSI without going through an adapter-owned scheduler;
- a scheduler design requires exposing `react::CallInvoker`,
  `RuntimeExecutor`, or `RuntimeScheduler` to C# instead of wrapping it behind a
  portable service;
- NativeAOT compatibility conflicts with HostFXR convenience;
- source-generator output would need `MethodInfo.Invoke`;
- a platform adapter starts owning module invocation logic;
- the proof depends on JSON for ordinary JSI values.

## Completion Audit Template

Use this template before marking the planning or implementation goal complete.
Fill it with actual file sections and command outputs.

```markdown
## Completion Audit

- Requirement:
  Evidence:
  Status: proven / incomplete / weak / contradicted

- Requirement:
  Evidence:
  Status:
```

For the planning goal, audit every ending criterion from
`<goal-objective.md>`.
Do not replace this audit with a keyword search.

## Planning Goal Audit Map

Use these sections as evidence for the current planning goal:

- Self-contained docs: `README.md`, all agent-plan docs, all learning-guide
  docs.
- Old note incorporated without dependency: `README.md`,
  `01-architecture.md`, `05-repo-strategy.md`.
- Architecture rule: `README.md`, `01-architecture.md`.
- Loader/runtime distinction: `README.md`, `01-architecture.md`,
  `learning-guide/01-dotnet-interop-basics.md`.
- Major layers: `01-architecture.md`.
- Universal vs platform-gated: `README.md`, `01-architecture.md`,
  `learning-guide/04-platform-adapters-and-views.md`.
- React Native macOS future proof: `02-research-spikes.md`,
  `03-implementation-phases.md`,
  `learning-guide/04-platform-adapters-and-views.md`.
- Repo strategy: `05-repo-strategy.md`.
- Ordered research spikes: `02-research-spikes.md`.
- Runtime reflection ban: `README.md`, `01-architecture.md`,
  `02-research-spikes.md`,
  `learning-guide/03-source-generators-and-v2-api.md`.
- Ownership rules: `01-architecture.md`,
  `learning-guide/02-jsi-wrapper-model.md`.
- JS scheduling / CallInvoker-like adapter boundary: `01-architecture.md`,
  `02-research-spikes.md`,
  `learning-guide/02-jsi-wrapper-model.md`,
  `learning-guide/03-source-generators-and-v2-api.md`,
  `learning-guide/04-platform-adapters-and-views.md`.
- Generated-looking proof: `02-research-spikes.md`,
  `learning-guide/03-source-generators-and-v2-api.md`.
- Mac-local vs Windows-remote: this file.
- Phase 1 execution detail: `02-research-spikes.md`,
  `03-implementation-phases.md`, `05-repo-strategy.md`.

If any mapped section is too thin to satisfy the criterion, edit the section.
Do not mark completion based on the map alone.
