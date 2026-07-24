# expo-modules-dotnet-autolinking

CLI that discovers .NET-backed Expo modules in an app, generates the
app-level aggregator project that links them together, and builds and stages
the resulting NativeAOT/HostFXR artifacts into the app.

## Commands

| Command | Purpose |
|---|---|
| `resolve` | Discover dotnet Expo module packages and print the linking manifest. |
| `generate` | Generate the .NET host project for linked Expo modules. |
| `build` | Build the generated .NET host project. |
| `stage` | Stage built .NET host artifacts into the app's managed directory. |
| `link` | Resolve, generate, build, and stage .NET-backed Expo modules in one step. |
| `sync-windows` | Run app-local RNW autolinking, then project the generated managed host, core packages, and linked C# modules into an RNW `.sln`. |

`sync-windows --check` verifies only the deterministic managed solution projection. RNW's
own `autolink-windows --check` can currently return `NeedAutolinking` immediately after
a no-op normal run, so it is deliberately not used as a stale-output signal.

Run `expo-modules-dotnet-autolinking <command> --help` for command-specific
options.

## Development

From within this package:

```sh
pnpm build
pnpm test
pnpm typecheck
```

## Normative contract

Normative contract: [`docs/specs/dotnet-autolinking.md`](../../docs/specs/dotnet-autolinking.md).
