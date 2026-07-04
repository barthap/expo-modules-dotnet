export interface DotnetProjectRef {
  /** Absolute path to the module's csproj file. */
  csprojPath: string;
  assemblyName: string;
}

export interface DotnetModule {
  packageName: string;
  packageRoot: string;
  projects: DotnetProjectRef[];
}

export interface DotnetLinkingManifest {
  modules: DotnetModule[];
}

export interface RawDotnetConfig {
  projects?: { path: string; assemblyName?: string }[];
}
