---
name: rnw-windows-setup
description: Diagnose and fix React Native Windows setup, build, autolinking, codegen, Visual Studio, MSBuild, Appx deployment, and PowerShell issues in the expo-modules-dotnet desktop app. Use when working on apps/desktop-app Windows/RNW support, VS 2026/RNW 0.81 tooling, v143/v145 toolset failures, autolink-windows drift, generated AutolinkedNativeModules.g.* files, or Windows 10 Appx/PowerShell deployment failures.
---

# RNW Windows Setup

## Goal

Keep the desktop Windows proof on the normal React Native Windows paths:

- CLI: `pnpm --filter desktop-app exec react-native run-windows`
- package script: `pnpm --filter desktop-app windows`
- VS: open `apps/desktop-app/windows/DesktopApp.sln`, select `Debug` / `x64`, set `DesktopApp.Package` as startup, start Metro separately, press Run/Debug

Do not replace these with custom launcher scripts unless the user explicitly accepts a workaround. If the normal RNW path fails, diagnose the exact layer first.

## Setup Checks

Start with deterministic environment facts:

```powershell
where.exe node
node -v
where.exe pnpm
pnpm --version
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber
```

For this repo, expected local lane assumptions are:

- `apps/desktop-app` uses React Native / React Native Windows 0.81-era tooling.
- VS 2026 uses MSVC toolset `v145`.
- Some Expo Desktop dependency projects may still declare `v143`.
- Node 24 from VS 2026 is acceptable.
- `apps/mobile-app` is separate; do not introduce desktop/RNW dependencies there.

## Autolinking

Treat RNW autolinking as a first-class invariant.

Use this check from repo root:

```powershell
pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

If it fails with `NeedAutolinking`, run:

```powershell
pnpm --filter desktop-app exec react-native autolink-windows --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

RNW templates track `AutolinkedNativeModules.g.*`. Keep these files checked in when the app template expects them. Do not hand-format them. The `--check` command compares generated output byte-for-byte, so generic whitespace format checks are not authoritative for these files.

Important nuance: `react-native run-windows` runs autolink before build and may pass `/p:RunAutolinkCheck=false` into MSBuild. VS/MSBuild builds still rely on the MSBuild autolink check. Keep both flows working.

## Codegen

Do not disable `RunCodegenWindows` as a default fix. If codegen fails, capture the command and failing project first. Expected MSBuild behavior for this desktop proof is:

- RNW dependency projects can run their own `codegen-windows`.
- The app project can skip codegen when no `codegenConfig` exists.
- Managed proof artifacts should still build and stage through `apps/desktop-app/scripts/build-managed.ps1`.

## Toolset Issues

If the build fails with `MSB8020` for missing `v143`, identify which project requests it.

Useful inspection:

```powershell
Select-String -Path "apps/desktop-app/node_modules/*/windows/**/*.vcxproj" -Pattern "PlatformToolset" -Context 1,1
```

Prefer an app-scoped MSBuild override over manually editing `node_modules`. In this repo, `apps/desktop-app/Directory.Build.targets` may map dependency projects that request `v143` onto `v145` when VS 2026 has the toolset installed.

Visual Studio's setup assistant may still show raw dependency metadata from `node_modules` before full MSBuild evaluation. Distinguish between:

- actual CLI/MSBuild build failure
- VS UI retarget prompt based on dependency project XML

Do not ask contributors to install old v143 tools unless the build genuinely requires them after the app-level override is evaluated.

## PowerShell and Appx

When `react-native run-windows` builds but fails deploy with `Get-AppxPackage` / `Appx` loading errors, isolate the PowerShell host:

```powershell
powershell -NoProfile -Command "Import-Module Appx; (Get-AppxPackage -Name 'DesktopApp').PackageFamilyName"
pwsh -NoProfile -Command "Import-Module Appx; (Get-AppxPackage -Name 'DesktopApp').PackageFamilyName"
pwsh -NoProfile -Command "Import-Module Appx -UseWindowsPowerShell; (Get-AppxPackage -Name 'DesktopApp').PackageFamilyName"
```

On Windows 10, PowerShell 7 may discover the inbox Windows `Appx` module but fail to load it directly. Windows PowerShell can load it natively, and PowerShell 7 may load it through the Windows PowerShell compatibility bridge with `Import-Module Appx -UseWindowsPowerShell`. RNW 0.81.6 can also assume `$PSHOME\pwsh.exe` when relaunching elevated, which is invalid under Windows PowerShell. If this is the root cause, a pnpm patch to RNW tooling is preferable to bypassing RNW deploy with a custom script.

Keep patches narrowly scoped and document them in `apps/desktop-app/README.md`.

## Verification

Use the smallest command that proves the layer under discussion:

```powershell
pnpm install --frozen-lockfile
pnpm --filter desktop-app typecheck
pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
pnpm --filter desktop-app exec react-native run-windows --no-packager --no-launch
MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /m:1 /v:minimal
```

Report what each command proves. Do not claim VS flow works solely from CLI deploy, and do not claim CLI deploy works solely from MSBuild success.

## Documentation and Commit Hygiene

Update `apps/desktop-app/README.md` when setup or run instructions change.

Before committing, scan staged content for local paths, usernames, machine names, private hostnames, and concrete install paths. Use repo-relative paths or placeholders in committed docs. Generic system paths such as `C:\Windows` may be valid in code when they are actual Windows API/runtime behavior, not local-machine metadata.

Do not commit this skill unless the user explicitly asks; it is repo-local working knowledge by default.
