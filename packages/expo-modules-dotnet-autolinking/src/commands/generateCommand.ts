import * as path from 'path';
import type { Command } from 'commander';

import { generateAggregator } from '../codegen/generateAggregator';
import { discoverDotnetManifestAsync, resolveAppRoot } from '../discovery';
import { defaultGenerateOutputDir, findAdapterPackageRoot } from '../paths';

interface GenerateCommandOptions {
  projectRoot?: string;
  output?: string;
}

export function registerGenerateCommand(program: Command): void {
  program
    .command('generate')
    .description('Generate the .NET host project for linked Expo modules')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--output <dir>', 'Generated output directory (defaults to <app>/.expo/dotnet)')
    .action(async (options: GenerateCommandOptions) => {
      const appRoot = resolveAppRoot(options.projectRoot);
      const outputDir =
        options.output !== undefined ? path.resolve(options.output) : defaultGenerateOutputDir(appRoot);
      const manifest = await discoverDotnetManifestAsync(appRoot);
      const result = generateAggregator(manifest, {
        outputDir,
        adapterPackageRoot: findAdapterPackageRoot(appRoot),
      });

      console.log(
        `Generated dotnet host: wrote ${formatCount(
          result.writtenFiles.length,
          'file'
        )}, skipped ${formatCount(result.skippedFiles.length, 'file')} in ${outputDir}`
      );
    });
}

function formatCount(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
