const LOADERS = ['hostfxr', 'nativeaot'];
const PLATFORMS = ['macos', 'windows'];
const PLATFORM_KEYS = ['loader'];

function isPlainObject(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function optionsError(message) {
  return new Error(`expo-modules-dotnet config plugin: ${message}`);
}

// Only desktop platforms take options: iOS and Android always load the
// NativeAOT library, so a shared `loader` key would invite a wrong value.
function resolvePluginOptions(props) {
  const options = props === undefined || props === null ? {} : props;
  if (!isPlainObject(options)) {
    throw optionsError('options must be an object.');
  }

  for (const key of Object.keys(options)) {
    if (!PLATFORMS.includes(key)) {
      throw optionsError(
        `unknown option "${key}". Allowed options are "macos" and "windows", for example { "windows": { "loader": "hostfxr" } }.`
      );
    }
  }

  const resolved = {};
  for (const platform of PLATFORMS) {
    const platformOptions = options[platform] === undefined ? {} : options[platform];
    if (!isPlainObject(platformOptions)) {
      throw optionsError(`"${platform}" must be an object.`);
    }
    for (const key of Object.keys(platformOptions)) {
      if (!PLATFORM_KEYS.includes(key)) {
        throw optionsError(`unknown option "${platform}.${key}". Allowed: "${platform}.loader".`);
      }
    }
    const loader = platformOptions.loader === undefined ? 'hostfxr' : platformOptions.loader;
    if (!LOADERS.includes(loader)) {
      throw optionsError(
        `"${platform}.loader" must be "hostfxr" or "nativeaot", got ${JSON.stringify(loader)}.`
      );
    }
    resolved[platform] = { loader };
  }
  return resolved;
}

module.exports = { resolvePluginOptions };
