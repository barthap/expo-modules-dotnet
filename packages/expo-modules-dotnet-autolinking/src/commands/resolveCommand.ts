import type { Command } from 'commander';

import { discoverDotnetManifestAsync, resolveAppRoot } from '../discovery';

export function registerResolveCommand(program: Command): void {
  program
    .command('resolve')
    .description('Discover dotnet Expo module packages and print the linking manifest')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--json', 'Print JSON (default output format)')
    .action(async (options: { projectRoot?: string }) => {
      const manifest = await discoverDotnetManifestAsync(resolveAppRoot(options.projectRoot));
      console.log(JSON.stringify(manifest, null, 2));
    });
}
