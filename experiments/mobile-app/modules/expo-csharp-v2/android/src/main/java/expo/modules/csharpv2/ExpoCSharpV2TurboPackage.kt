package expo.modules.csharpv2

import com.facebook.react.BaseReactPackage
import com.facebook.react.bridge.NativeModule
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.module.model.ReactModuleInfo
import com.facebook.react.module.model.ReactModuleInfoProvider

class ExpoCSharpV2TurboPackage : BaseReactPackage() {
  override fun getModule(name: String, reactContext: ReactApplicationContext): NativeModule? {
    return when (name) {
      ExpoCSharpV2TurboModule.NAME -> ExpoCSharpV2TurboModule(reactContext)
      else -> null
    }
  }

  override fun getReactModuleInfoProvider() = ReactModuleInfoProvider {
    mapOf(
      ExpoCSharpV2TurboModule.NAME to ReactModuleInfo(
        ExpoCSharpV2TurboModule.NAME,
        ExpoCSharpV2TurboModule.NAME,
        false,
        false,
        false,
        true
      )
    )
  }
}
