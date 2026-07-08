# Windows Hermes Build Probe

## Hypothesis

Official `facebook/hermes` can be built on Windows with VS 2026 and staged for
the headless testhost and console app.

## Commands Run

- `Get-Content -LiteralPath scripts\hermes-ref.txt`
- `git clone https://github.com/facebook/hermes.git build\hermes\source`
- `git -C build\hermes\source fetch --tags origin 896d643e7453f507b062140f849f89ecf5448a88`
- `git -C build\hermes\source checkout --detach FETCH_HEAD`
- `cmake -S build\hermes\source -B build\hermes\windows-official\x64 -G "Visual Studio 18 2026" -A x64 -DCMAKE_BUILD_TYPE=Release -DHERMES_ENABLE_DEBUGGER=OFF -DHERMES_ENABLE_INTL=ON`
- `cmake --build build\hermes\windows-official\x64 --config Release --parallel`
- `Get-ChildItem -Path build\hermes\windows-official\x64, build\hermes\source -Recurse -File -Include hermes.lib,hermes.dll,hermes-icu.dll,hermesvm.lib,hermesvm.dll,hermes.h,jsi.h`

## Expected Result

The official build produces headers, an import library, and runtime DLLs usable
by CMake on Windows.

## Actual Result

CMake configured successfully with VS 2026 and selected Windows SDK
`10.0.26100.0`. The build then failed in Hermes Intl/ICU sources before a
usable Windows VM library/runtime layout was produced.

The concrete compile errors included:

- `LocaleResolver.cpp`: `ULOC_AVAILABLE_DEFAULT` undeclared
- `LocaleResolver.cpp`: `uloc_openAvailableByType` identifier not found
- `PlatformIntlICU.cpp`: cannot open `unicode/dtptngen.h`

Artifact discovery found only:

- `build/hermes/source/API/hermes/hermes.h`
- `build/hermes/source/API/jsi/jsi/jsi.h`

It did not find `hermes.lib`, `hermes.dll`, `hermes-icu.dll`, `hermesvm.lib`,
or `hermesvm.dll` under the official build/source roots.

## Artifacts

- `build/hermes/source`
- `build/hermes/windows-official/x64`

## Ownership/Lifetime Findings

No JSI ownership or lifetime behavior changed. This probe only evaluated
Hermes build artifact availability.

## Scheduler Findings

No scheduler behavior changed. This probe did not run a Hermes runtime.

## Stop/Go Decision

Do not use the `Microsoft.JavaScript.Hermes` NuGet package for the current
headless connector because it exposes the Hermes C API rather than the C++
`hermesvm` API used by the testhost and console proof.

Go with the official Hermes source build using a shared `hermesvm` target and
Intl disabled for the first Windows testhost slice. The Intl-enabled upstream
build remains recorded as blocked by the ICU errors above.
