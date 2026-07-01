#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
package_dir="$(cd "${script_dir}/.." && pwd)"
repo_root="$(cd "${package_dir}/../.." && pwd)"
project="${package_dir}/dotnet/ExampleModule/ExampleModule.csproj"
adapter_dir="${repo_root}/packages/expo-modules-dotnet"
android_jni_libs="${adapter_dir}/android/src/main/jniLibs/arm64-v8a"
ios_native_libs="${adapter_dir}/ios/NativeLibs"

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
cp "${package_dir}/dotnet/ExampleModule/bin/Release/net10.0/android-arm64/publish/ExampleModule.so" \
  "${android_jni_libs}/libExampleModule.so"
cp "${package_dir}/dotnet/ExampleModule/bin/Release/net10.0/iossimulator-arm64/publish/ExampleModule.dylib" \
  "${ios_native_libs}/libExampleModule.dylib"
install_name_tool -id "@rpath/libExampleModule.dylib" \
  "${ios_native_libs}/libExampleModule.dylib"

echo "Copied ExampleModule NativeAOT artifacts into expo-modules-dotnet:"
echo "  android/src/main/jniLibs/arm64-v8a/libExampleModule.so"
echo "  ios/NativeLibs/libExampleModule.dylib"
