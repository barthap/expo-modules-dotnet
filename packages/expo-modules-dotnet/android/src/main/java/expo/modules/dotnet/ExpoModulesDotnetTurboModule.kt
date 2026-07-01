package expo.modules.dotnet

import com.facebook.proguard.annotations.DoNotStrip
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.module.annotations.ReactModule
import com.facebook.react.turbomodule.core.interfaces.BindingsInstallerHolder
import com.facebook.react.turbomodule.core.interfaces.TurboModule
import com.facebook.react.turbomodule.core.interfaces.TurboModuleWithJSIBindings
import com.facebook.soloader.SoLoader

@DoNotStrip
@ReactModule(name = ExpoModulesDotnetTurboModule.NAME)
class ExpoModulesDotnetTurboModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext),
  TurboModule,
  TurboModuleWithJSIBindings {
  override fun getName() = NAME

  @DoNotStrip
  external override fun getBindingsInstaller(): BindingsInstallerHolder

  companion object {
    const val NAME = "ExpoModulesDotnetInstaller"

    init {
      SoLoader.loadLibrary("ExampleModule")
      SoLoader.loadLibrary("expo-modules-dotnet")
    }
  }
}
