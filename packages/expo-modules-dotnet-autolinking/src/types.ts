export interface DotnetProjectRef {
  /** Absolute path to the module's csproj file. */
  csprojPath: string;
  assemblyName: string;
}

/** An upstream native-accessor file that this module can replace on Windows. */
export interface DotnetSubstitution {
  /** The upstream npm package name. */
  package: string;
  /** The semver range of upstream versions whose native module shape the module implements. */
  versionRange: string;
  /** The accessor file path, relative to the upstream package root, with forward slashes. */
  accessor: string;
  /** Absolute path of the replacing entry point in the declaring package. */
  replacementPath: string;
}

export interface DotnetModule {
  packageName: string;
  packageRoot: string;
  projects: DotnetProjectRef[];
  substitutions: DotnetSubstitution[];
  /** True when the module reads the app config that staging writes as app.config. */
  appConfig: boolean;
}

export interface DotnetLinkingManifest {
  modules: DotnetModule[];
}

export interface RawDotnetSubstitution {
  package: string;
  versionRange: string;
  accessor: string;
  replacement: string;
}

export interface RawDotnetConfig {
  projects?: { path: string; assemblyName?: string }[];
  substitutions?: RawDotnetSubstitution[];
  appConfig?: boolean;
}
