import * as path from 'path';

import { describe, expect, it, vi } from 'vitest';

const spawn = vi.fn();

vi.mock('child_process', () => ({ spawn }));

describe('runReactNativeWindowsAutolink', () => {
  it('runs the app-local React Native CLI in check mode', async () => {
    const { runReactNativeWindowsAutolink } = await import('../windows/reactNativeCli');
    const appRoot = 'app-root';
    const solutionPath = 'app-root/windows/App.sln';
    const projectPath = 'app-root/windows/App/App.vcxproj';
    const resolvedCli = 'app-root/node_modules/react-native/cli.js';
    const resolveCli = vi.fn(() => resolvedCli);
    const child = Object.assign(new (await import('events')).EventEmitter(), { unref: vi.fn() });
    spawn.mockReturnValueOnce(child);

    const operation = runReactNativeWindowsAutolink(
      { appRoot, solutionPath, projectPath, check: true },
      resolveCli
    );
    child.emit('close', 0);
    await operation;

    expect(resolveCli).toHaveBeenCalledWith('react-native/cli.js', { paths: [appRoot] });
    expect(spawn).toHaveBeenCalledWith(
      process.execPath,
      [resolvedCli, 'autolink-windows', '--sln', path.join('windows', 'App.sln'), '--proj', path.join('windows', 'App', 'App.vcxproj'), '--check'],
      { cwd: appRoot, stdio: 'inherit', shell: false }
    );
  });

  it('reports an upstream autolink failure', async () => {
    const { runReactNativeWindowsAutolink } = await import('../windows/reactNativeCli');
    const child = new (await import('events')).EventEmitter();
    spawn.mockReturnValueOnce(child);

    const operation = runReactNativeWindowsAutolink(
      { appRoot: 'app-root', solutionPath: 'app.sln', projectPath: 'app.vcxproj', check: false },
      () => 'react-native/cli.js'
    );
    child.emit('close', 1);

    await expect(operation).rejects.toThrow('autolink-windows exited with code 1');
  });
});
