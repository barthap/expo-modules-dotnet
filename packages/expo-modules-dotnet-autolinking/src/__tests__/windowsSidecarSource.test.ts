import * as fs from 'fs';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

const repoRoot = path.resolve(__dirname, '../../../..');

function readRepoFile(relativePath: string): string {
  return fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');
}

describe('Windows native view sidecar source invariants', () => {
  it('routes managed view calls through the owning ReactContext instead of a process-global runtime context', () => {
    const installer = readRepoFile(
      'packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp'
    );
    const installerHeader = readRepoFile(
      'packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.h'
    );
    const installerDef = readRepoFile(
      'packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.def'
    );
    const viewHost = readRepoFile(
      'packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ManagedViewHost.cpp'
    );

    expect(installer).not.toContain('g_currentManagedRuntimeContext');
    expect(installer).not.toContain('CurrentManagedRuntimeContext');
    expect(installerHeader).not.toContain('CurrentManagedRuntimeContext');
    expect(installerDef).not.toContain('expo_modules_dotnet_current_runtime_context');
    expect(viewHost).not.toContain('GetProcAddress(module, "expo_modules_dotnet_current_runtime_context")');
    expect(viewHost).toContain('RuntimeContextFromReactContext');
    expect(viewHost).toContain('ReactContext const &reactContext');
    expect(viewHost).toContain('readLastViewError');
    expect(viewHost).toContain('Managed view metadata is unavailable: ');
  });
});
