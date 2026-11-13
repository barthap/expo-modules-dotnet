const fs = require('fs');
const path = require('path');
const semver = require('semver');

const OPTION_KEYS = ['substitute'];

function metroError(message) {
  return new Error(`expo-modules-dotnet Metro config: ${message}`);
}

function isPlainObject(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function resolveSelection(options) {
  if (options === undefined || options === null) {
    return [];
  }
  if (!isPlainObject(options)) {
    throw metroError('options must be an object, for example { substitute: ["expo-asset"] }.');
  }
  for (const key of Object.keys(options)) {
    if (!OPTION_KEYS.includes(key)) {
      throw metroError(`unknown option "${key}". The only option is "substitute".`);
    }
  }

  const substitute = options.substitute === undefined ? [] : options.substitute;
  if (!Array.isArray(substitute)) {
    throw metroError('"substitute" must be a list of upstream package names.');
  }
  const seen = new Set();
  substitute.forEach((name, index) => {
    if (typeof name !== 'string' || name.length === 0) {
      throw metroError(`"substitute[${index}]" must be a non-empty string.`);
    }
    if (seen.has(name)) {
      throw metroError(`"substitute" lists "${name}" more than once.`);
    }
    seen.add(name);
  });
  return [...substitute];
}

// Finds a package the way Node does from a directory: in each enclosing
// node_modules folder. Unlike require.resolve, it ignores NODE_PATH and the
// global folders. pnpm sets NODE_PATH to its hoisted store, which can hold a
// copy that the app does not use.
function tryResolvePackageRoot(packageName, fromDirectory) {
  let directory = path.resolve(fromDirectory);
  for (;;) {
    if (path.basename(directory) !== 'node_modules') {
      const candidate = path.join(directory, 'node_modules', ...packageName.split('/'));
      if (fs.existsSync(path.join(candidate, 'package.json'))) {
        return fs.realpathSync(candidate);
      }
    }
    const parent = path.dirname(directory);
    if (parent === directory) {
      return null;
    }
    directory = parent;
  }
}

function requireAutolinking(appRoot) {
  const autolinkingRoot = tryResolvePackageRoot('expo-modules-dotnet-autolinking', appRoot);
  if (autolinkingRoot === null) {
    throw metroError(
      `could not resolve expo-modules-dotnet-autolinking from app root "${appRoot}". ` +
        'Add expo-modules-dotnet-autolinking as a dependency of the app.'
    );
  }
  return require(autolinkingRoot);
}

async function loadManifestFromApp(appRoot) {
  return requireAutolinking(appRoot).discoverDotnetManifestAsync(appRoot);
}

// SDK packages such as expo-asset usually arrive through expo, not as direct
// app dependencies, so fall back to resolving them from the app's expo package.
function resolveUpstreamRoot(packageName, appRoot) {
  const direct = tryResolvePackageRoot(packageName, appRoot);
  if (direct !== null) {
    return direct;
  }
  const expoRoot = tryResolvePackageRoot('expo', appRoot);
  return expoRoot === null ? null : tryResolvePackageRoot(packageName, expoRoot);
}

function checkSelection(appRoot, manifest, selection) {
  const declared = new Map();
  for (const module of manifest.modules) {
    for (const substitution of module.substitutions ?? []) {
      declared.set(substitution.package, { module, substitution });
    }
  }

  return selection.map((name) => {
    const entry = declared.get(name);
    if (entry === undefined) {
      const names = [...declared.keys()].sort();
      throw metroError(
        `"${name}" is not declared by any installed dotnet module. ` +
          `Declared substitutions: ${names.length > 0 ? names.join(', ') : 'none'}.`
      );
    }

    const { module, substitution } = entry;
    const upstreamRoot = resolveUpstreamRoot(name, appRoot);
    if (upstreamRoot === null) {
      throw metroError(
        `"${name}" is not installed: it resolves neither from the app root "${appRoot}" nor from the app's expo package.`
      );
    }

    const version = JSON.parse(fs.readFileSync(path.join(upstreamRoot, 'package.json'), 'utf8')).version;
    const installed = `${name}@${version}`;
    if (!semver.satisfies(version, substitution.versionRange)) {
      throw metroError(
        `${installed} is outside the range ${substitution.versionRange} that ${module.packageName} declares for it.`
      );
    }
    if (!fs.existsSync(path.join(upstreamRoot, ...substitution.accessor.split('/')))) {
      throw metroError(
        `${installed} has no accessor file ${substitution.accessor}, which ${module.packageName} declares it replaces.`
      );
    }

    return substitution;
  });
}

// Returns the nearest enclosing package that has a name. A package.json
// without a name (such as one that only sets "type") does not end the search.
function findOwningPackage(directory, cache) {
  if (cache.has(directory)) {
    return cache.get(directory);
  }
  let owner = null;
  const packageJsonPath = path.join(directory, 'package.json');
  if (fs.existsSync(packageJsonPath)) {
    const name = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8')).name;
    if (typeof name === 'string') {
      owner = { name, root: directory };
    }
  }
  if (owner === null) {
    const parent = path.dirname(directory);
    owner = parent === directory ? null : findOwningPackage(parent, cache);
  }
  cache.set(directory, owner);
  return owner;
}

function createResolveRequest(previous, substitutions) {
  const byPackage = new Map(substitutions.map((substitution) => [substitution.package, substitution]));
  const owners = new Map();

  return function resolveRequest(context, moduleName, platform) {
    const resolution =
      typeof previous === 'function'
        ? previous(context, moduleName, platform)
        : context.resolveRequest(context, moduleName, platform);
    // @ref docs/specs/dotnet-autolinking.md "Windows Apps Opt In To Substitutions Through Metro"
    if (platform !== 'windows' || resolution.type !== 'sourceFile') {
      return resolution;
    }

    const owner = findOwningPackage(path.dirname(resolution.filePath), owners);
    const substitution = owner === null ? undefined : byPackage.get(owner.name);
    if (substitution === undefined) {
      return resolution;
    }
    // Match by package name and package-relative path, not by absolute
    // path, so every installed copy of the upstream package is covered.
    const relative = path.relative(owner.root, resolution.filePath).split(path.sep).join('/');
    if (relative !== substitution.accessor) {
      return resolution;
    }
    return { type: 'sourceFile', filePath: substitution.replacementPath };
  };
}

async function createDotnetModulesConfigAsync(configInput, selection, { loadManifest }) {
  const config = await configInput;
  // @ref docs/specs/dotnet-autolinking.md "Windows Apps Opt In To Substitutions Through Metro"
  if (selection.length === 0) {
    return config;
  }

  const appRoot = config.projectRoot;
  if (typeof appRoot !== 'string' || appRoot.length === 0) {
    throw metroError('the Metro config has no projectRoot, so the app root is unknown.');
  }

  const manifest = await loadManifest(appRoot);
  const substitutions = checkSelection(appRoot, manifest, selection);
  const resolver = config.resolver ?? {};
  return {
    ...config,
    resolver: {
      ...resolver,
      resolveRequest: createResolveRequest(resolver.resolveRequest, substitutions),
    },
  };
}

/**
 * Lets upstream Expo packages reach .NET modules on Windows.
 *
 * @param config A Metro config, or a Promise of one.
 * @param options `{ substitute: string[] }`: the upstream packages whose
 *   native-accessor files an installed dotnet module replaces.
 * @returns A Promise of the Metro config.
 */
function withDotnetModules(config, options) {
  const selection = resolveSelection(options);
  return createDotnetModulesConfigAsync(config, selection, { loadManifest: loadManifestFromApp });
}

module.exports = { withDotnetModules, createDotnetModulesConfigAsync };
