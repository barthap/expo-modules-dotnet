package expo.modules.dotnet

import com.facebook.react.BaseReactPackage
import com.facebook.react.bridge.NativeModule
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.module.model.ReactModuleInfo
import com.facebook.react.module.model.ReactModuleInfoProvider

class ExpoModulesDotnetTurboPackage : BaseReactPackage() {
  override fun getModule(name: String, reactContext: ReactApplicationContext): NativeModule? {
    return when (name) {
      ExpoModulesDotnetTurboModule.NAME -> ExpoModulesDotnetTurboModule(reactContext)
      else -> null
    }
  }

  override fun getReactModuleInfoProvider() = ReactModuleInfoProvider {
    mapOf(
      ExpoModulesDotnetTurboModule.NAME to ReactModuleInfo(
        ExpoModulesDotnetTurboModule.NAME,
        ExpoModulesDotnetTurboModule::class.java.name,
        false,
        false,
        false,
        true
      )
    )
  }
}
