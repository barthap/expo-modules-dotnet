package expo.modules.csharpv2

import com.facebook.jni.HybridData
import com.facebook.react.runtime.BindingsInstaller
import com.facebook.soloader.SoLoader

class ExpoCSharpV2BindingsInstaller private constructor(hybridData: HybridData) : BindingsInstaller(hybridData) {
  constructor() : this(initHybrid())

  private companion object {
    init {
      SoLoader.loadLibrary("ExpoMobileV2Module")
      SoLoader.loadLibrary("expo-csharp-v2")
    }

    @JvmStatic
    private external fun initHybrid(): HybridData
  }
}
