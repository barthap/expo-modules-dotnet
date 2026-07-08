cmake_minimum_required(VERSION 3.24)
project(ExpoJsiTestHost LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

get_filename_component(ADAPTER_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
get_filename_component(REPO_ROOT "${ADAPTER_ROOT}/../.." ABSOLUTE)

include("${REPO_ROOT}/cmake/ExpoHermesPrebuilt.cmake")

add_library(
  expo_jsi_testhost SHARED
  src/ExpoJsiTestHost.cpp
  "${ADAPTER_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp"
  "${ADAPTER_ROOT}/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp")

target_include_directories(
  expo_jsi_testhost
  PRIVATE "${ADAPTER_ROOT}/native/include"
          "${ADAPTER_ROOT}/native/packages/jsi/include"
          "${CMAKE_CURRENT_LIST_DIR}/include")

expo_configure_hermes_target(expo_jsi_testhost ROOT "${HERMES_PREBUILT_ROOT}")
