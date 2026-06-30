#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_dir="$(cd "${script_dir}/.." && pwd)"
repo_root="$(cd "${app_dir}/../.." && pwd)"
project="${app_dir}/dotnet/ExpoMobileV2Module/ExpoMobileV2Module.csproj"
module_dir="${app_dir}/modules/expo-csharp-v2"
android_jni_libs="${module_dir}/android/src/main/jniLibs/arm64-v8a"
ios_native_libs="${module_dir}/ios/NativeLibs"

android_home="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [[ -z "${android_home}" ]]; then
  echo "ANDROID_HOME or ANDROID_SDK_ROOT must point to an Android SDK." >&2
  exit 1
fi

ndk_root="${ANDROID_NDK_HOME:-}"
if [[ -z "${ndk_root}" ]]; then
  ndk_root="$(find "${android_home}/ndk" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort -V | tail -1)"
fi
if [[ -z "${ndk_root}" ]]; then
  echo "Android NDK not found under \$ANDROID_HOME/ndk." >&2
  exit 1
fi

ndk_bin="${ndk_root}/toolchains/llvm/prebuilt/darwin-x86_64/bin"
android_clang="$(find "${ndk_bin}" -maxdepth 1 -type f -name 'aarch64-linux-android*-clang' | sort -V | tail -1)"
if [[ -z "${android_clang}" ]]; then
  echo "Could not find aarch64-linux-android*-clang in ${ndk_bin}." >&2
  exit 1
fi

dotnet publish "${project}" \
  -c Release \
  -r android-arm64 \
  -p:PublishAot=true \
  -p:PublishAotUsingRuntimePack=true \
  -p:CppCompilerAndLinker="${android_clang}" \
  -p:StripSymbols=false \
  --self-contained true

dotnet publish "${project}" \
  -c Release \
  -r iossimulator-arm64 \
  -p:PublishAot=true \
  -p:PublishAotUsingRuntimePack=true \
  --self-contained true

mkdir -p "${android_jni_libs}" "${ios_native_libs}"
cp "${app_dir}/dotnet/ExpoMobileV2Module/bin/Release/net10.0/android-arm64/publish/ExpoMobileV2Module.so" \
  "${android_jni_libs}/libExpoMobileV2Module.so"
cp "${app_dir}/dotnet/ExpoMobileV2Module/bin/Release/net10.0/iossimulator-arm64/publish/ExpoMobileV2Module.dylib" \
  "${ios_native_libs}/libExpoMobileV2Module.dylib"
install_name_tool -id "@rpath/libExpoMobileV2Module.dylib" \
  "${ios_native_libs}/libExpoMobileV2Module.dylib"

echo "Copied NativeAOT artifacts into local Expo module:"
echo "  android/src/main/jniLibs/arm64-v8a/libExpoMobileV2Module.so"
echo "  ios/NativeLibs/libExpoMobileV2Module.dylib"
