package expo.modules.csharpv2

import com.facebook.proguard.annotations.DoNotStrip
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.module.annotations.ReactModule
import com.facebook.react.turbomodule.core.interfaces.BindingsInstallerHolder
import com.facebook.react.turbomodule.core.interfaces.TurboModule
import com.facebook.react.turbomodule.core.interfaces.TurboModuleWithJSIBindings
import com.facebook.soloader.SoLoader

@DoNotStrip
@ReactModule(name = ExpoCSharpV2TurboModule.NAME)
class ExpoCSharpV2TurboModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext),
  TurboModule,
  TurboModuleWithJSIBindings {
  override fun getName() = NAME

  @DoNotStrip
  external override fun getBindingsInstaller(): BindingsInstallerHolder

  companion object {
    const val NAME = "ExpoCSharpV2Installer"

    init {
      SoLoader.loadLibrary("ExpoMobileV2Module")
      SoLoader.loadLibrary("expo-csharp-v2")
    }
  }
}
