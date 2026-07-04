import { Command } from 'commander';

export function createProgram(): Command {
  const program = new Command('expo-modules-dotnet-autolinking');
  program.description('Autolinking tool for .NET-backed Expo modules');
  return program;
}

export function main(argv: string[]): void {
  createProgram().parseAsync(argv, { from: 'user' }).catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  });
}
