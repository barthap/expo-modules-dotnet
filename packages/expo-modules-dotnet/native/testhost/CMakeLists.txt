cmake_minimum_required(VERSION 3.24)
project(ExpoJsiTestHost LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

get_filename_component(ADAPTER_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
get_filename_component(REPO_ROOT "${ADAPTER_ROOT}/../.." ABSOLUTE)

set(HERMES_PREBUILT_ROOT
    "$ENV{HERMES_PREBUILT_ROOT}"
    CACHE PATH
          "Local Hermes prebuilt root created by scripts/build-hermes-macos.sh")
if(NOT HERMES_PREBUILT_ROOT)
  set(HERMES_PREBUILT_ROOT "${REPO_ROOT}/build/hermes/source/destroot")
endif()

set(HERMES_ROOT "${HERMES_PREBUILT_ROOT}")
set(HERMES_FRAMEWORK_DIR "${HERMES_ROOT}/Library/Frameworks/macosx")

if(NOT EXISTS "${HERMES_ROOT}/include/hermes/hermes.h")
  message(
    FATAL_ERROR
      "Could not find Hermes headers under ${HERMES_ROOT}. "
      "Run scripts/build-hermes-macos.sh first, or pass "
      "-DHERMES_PREBUILT_ROOT=<destroot>.")
endif()
if(NOT EXISTS "${HERMES_FRAMEWORK_DIR}/hermesvm.framework/hermesvm")
  message(
    FATAL_ERROR
      "Could not find hermesvm.framework under ${HERMES_FRAMEWORK_DIR}. "
      "Run scripts/build-hermes-macos.sh first, or pass "
      "-DHERMES_PREBUILT_ROOT=<destroot>.")
endif()

add_library(
  expo_jsi_testhost SHARED
  src/ExpoJsiTestHost.cpp
  "${ADAPTER_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp"
  "${ADAPTER_ROOT}/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp")

target_include_directories(
  expo_jsi_testhost
  PRIVATE "${HERMES_ROOT}/include" "${ADAPTER_ROOT}/native/include"
          "${ADAPTER_ROOT}/native/packages/jsi/include"
          "${CMAKE_CURRENT_LIST_DIR}/include")

target_link_options(expo_jsi_testhost PRIVATE "-F${HERMES_FRAMEWORK_DIR}"
                    "-Wl,-rpath,${HERMES_FRAMEWORK_DIR}")

target_link_libraries(expo_jsi_testhost PRIVATE "-framework hermesvm")
