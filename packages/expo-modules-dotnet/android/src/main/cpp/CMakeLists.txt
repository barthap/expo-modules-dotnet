cmake_minimum_required(VERSION 3.22.1)
project(expo-modules-dotnet)

find_package(fbjni REQUIRED CONFIG)
find_package(ReactAndroid REQUIRED CONFIG)
find_library(log-lib log)

set(REACT_NATIVE_DIR
    ${ADAPTER_ROOT}/../../apps/mobile-app/node_modules/react-native)

add_library(
  expo-modules-dotnet SHARED
  ExpoModulesDotnetBindingsInstaller.cpp
  ${ADAPTER_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp
  ${ADAPTER_ROOT}/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp)

target_include_directories(
  expo-modules-dotnet
  PRIVATE ${ADAPTER_ROOT}/native/include
          ${ADAPTER_ROOT}/native/packages/jsi/include
          ${REACT_NATIVE_DIR}/ReactCommon
          ${REACT_NATIVE_DIR}/ReactCommon/callinvoker
          ${REACT_NATIVE_DIR}/ReactAndroid/src/main/jni)

target_compile_definitions(expo-modules-dotnet PRIVATE FOLLY_NO_CONFIG=1)

target_link_libraries(expo-modules-dotnet fbjni::fbjni ReactAndroid::jsi
                      ReactAndroid::reactnative ${log-lib})
