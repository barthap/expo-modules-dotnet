import { Command } from 'commander';

import { registerResolveCommand } from './commands/resolveCommand';

export function createProgram(): Command {
  const program = new Command('expo-modules-dotnet-autolinking');
  program.description('Autolinking tool for .NET-backed Expo modules');
  registerResolveCommand(program);
  return program;
}

export function main(argv: string[]): void {
  createProgram().parseAsync(argv, { from: 'user' }).catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  });
}
