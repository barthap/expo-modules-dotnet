import { spawn } from 'child_process';
import * as path from 'path';

export interface ReactNativeWindowsAutolinkOptions {
  appRoot: string;
  solutionPath: string;
  projectPath: string;
  check: boolean;
}

export type ReactNativeCliResolver = (request: string, options: { paths: string[] }) => string;

export async function runReactNativeWindowsAutolink(
  options: ReactNativeWindowsAutolinkOptions,
  resolveCli: ReactNativeCliResolver = require.resolve
): Promise<void> {
  let cliPath: string;
  try {
    cliPath = resolveCli('react-native/cli.js', { paths: [options.appRoot] });
  } catch {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Could not resolve react-native/cli.js from app root ${options.appRoot}. Install React Native Windows and its React Native CLI integration in the app.`
    );
  }

  const args = [
    cliPath,
    'autolink-windows',
    '--sln',
    path.relative(options.appRoot, options.solutionPath),
    '--proj',
    path.relative(options.appRoot, options.projectPath),
  ];
  if (options.check) {
    args.push('--check');
  }

  await new Promise<void>((resolve, reject) => {
    const child = spawn(process.execPath, args, {
      cwd: options.appRoot,
      stdio: 'inherit',
      shell: false,
    });
    child.once('error', reject);
    child.once('close', (code) => {
      if (code === 0) {
        resolve();
        return;
      }
      reject(
        new Error(
          `[expo-modules-dotnet-autolinking] react-native autolink-windows exited with code ${code ?? 'unknown'}.`
        )
      );
    });
  });
}
