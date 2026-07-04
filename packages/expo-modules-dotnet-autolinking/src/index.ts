import { Command } from 'commander';

import { registerBuildCommand } from './commands/buildCommand';
import { registerGenerateCommand } from './commands/generateCommand';
import { registerResolveCommand } from './commands/resolveCommand';
import { registerStageCommand } from './commands/stageCommand';

export function createProgram(): Command {
  const program = new Command('expo-modules-dotnet-autolinking');
  program.description('Autolinking tool for .NET-backed Expo modules');
  registerBuildCommand(program);
  registerGenerateCommand(program);
  registerResolveCommand(program);
  registerStageCommand(program);
  return program;
}

export function main(argv: string[]): void {
  createProgram().parseAsync(argv, { from: 'user' }).catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  });
}
