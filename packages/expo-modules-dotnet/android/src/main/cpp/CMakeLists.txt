cmake_minimum_required(VERSION 3.22.1)
project(expo-csharp-v2)

find_package(fbjni REQUIRED CONFIG)
find_package(ReactAndroid REQUIRED CONFIG)
find_library(log-lib log)

set(REACT_NATIVE_DIR
    ${REPO_ROOT}/experiments/mobile-app/node_modules/react-native)

add_library(
  expo-csharp-v2 SHARED
  ExpoCSharpV2BindingsInstaller.cpp
  ${REPO_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp
  ${REPO_ROOT}/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp)

target_include_directories(
  expo-csharp-v2
  PRIVATE ${REPO_ROOT}/native/include
          ${REPO_ROOT}/native/packages/jsi/include
          ${REACT_NATIVE_DIR}/ReactCommon
          ${REACT_NATIVE_DIR}/ReactCommon/callinvoker
          ${REACT_NATIVE_DIR}/ReactAndroid/src/main/jni)

target_compile_definitions(expo-csharp-v2 PRIVATE FOLLY_NO_CONFIG=1)

target_link_libraries(expo-csharp-v2 fbjni::fbjni ReactAndroid::jsi
                      ReactAndroid::reactnative ${log-lib})
