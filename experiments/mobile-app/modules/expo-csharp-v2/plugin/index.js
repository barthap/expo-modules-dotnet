const { createRunOncePlugin, withAppDelegate, withMainApplication } = require('@expo/config-plugins');

const pluginName = 'expo-csharp-v2';
const silgenDeclaration = `@_silgen_name("ExpoCSharpV2CreateJSRuntimeFactory")
func ExpoCSharpV2CreateJSRuntimeFactory(_ factory: JSRuntimeFactoryRef) -> JSRuntimeFactoryRef`;

function withIosRuntimeFactory(config) {
  return withAppDelegate(config, (mod) => {
    if (mod.modResults.language !== 'swift') {
      throw new Error(`${pluginName} requires a Swift AppDelegate.`);
    }

    let contents = mod.modResults.contents;
    if (!contents.includes('ExpoCSharpV2CreateJSRuntimeFactory')) {
      contents = contents.replace('@main', `${silgenDeclaration}\n\n@main`);
    }

    const override = `  override func createJSRuntimeFactory() -> JSRuntimeFactoryRef {
    ExpoCSharpV2CreateJSRuntimeFactory(super.createJSRuntimeFactory())
  }

`;
    if (!contents.includes('override func createJSRuntimeFactory()')) {
      contents = contents.replace(
        'class ReactNativeDelegate: ExpoReactNativeFactoryDelegate {\n  // Extension point for config-plugins\n\n',
        `class ReactNativeDelegate: ExpoReactNativeFactoryDelegate {\n  // Extension point for config-plugins\n\n${override}`
      );
    }

    mod.modResults.contents = contents;
    return mod;
  });
}

function withAndroidBindingsInstaller(config) {
  return withMainApplication(config, (mod) => {
    let contents = mod.modResults.contents;
    if (!contents.includes('expo.modules.csharpv2.ExpoCSharpV2BindingsInstaller')) {
      contents = contents.replace(
        'import expo.modules.ExpoReactHostFactory\n',
        'import expo.modules.ExpoReactHostFactory\nimport expo.modules.csharpv2.ExpoCSharpV2BindingsInstaller\n'
      );
    }

    if (!contents.includes('bindingsInstaller = ExpoCSharpV2BindingsInstaller()')) {
      contents = contents.replace(
        /(\n\s*packageList\s*=\s*PackageList\(this\)\.packages\.apply\s*\{[\s\S]*?\n\s*\})(\n\s*\))/,
        '$1,\n      bindingsInstaller = ExpoCSharpV2BindingsInstaller()$2'
      );
    }

    mod.modResults.contents = contents;
    return mod;
  });
}

function withExpoCSharpV2(config) {
  config = withIosRuntimeFactory(config);
  config = withAndroidBindingsInstaller(config);
  return config;
}

module.exports = createRunOncePlugin(withExpoCSharpV2, pluginName, '0.1.0');
