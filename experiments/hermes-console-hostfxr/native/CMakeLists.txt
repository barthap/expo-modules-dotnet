cmake_minimum_required(VERSION 3.24)
project(HermesConsoleHostFxrProof LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

set(EXPO_JSI_DOTNET_LOADER
    "hostfxr"
    CACHE STRING
          "Managed loader for the Hermes console proof: hostfxr or nativeaot")
set_property(CACHE EXPO_JSI_DOTNET_LOADER PROPERTY STRINGS hostfxr nativeaot)
if(NOT EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr" AND NOT EXPO_JSI_DOTNET_LOADER
                                                     STREQUAL "nativeaot")
  message(FATAL_ERROR "EXPO_JSI_DOTNET_LOADER must be hostfxr or nativeaot")
endif()
set(EXPO_JSI_MANAGED_CONFIGURATION
    "Debug"
    CACHE STRING "Managed build configuration used by the Hermes console proof")

if(EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr")
  set(DOTNET_ROOT "$ENV{DOTNET_ROOT}")
  if(NOT DOTNET_ROOT)
    find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
    execute_process(
      COMMAND "${DOTNET_EXECUTABLE}" --info
      OUTPUT_VARIABLE DOTNET_INFO
      OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    string(REGEX MATCH "Base Path:[ \t]*([^\n\r]+)" _dotnet_base_path
                 "${DOTNET_INFO}")
    if(NOT CMAKE_MATCH_1)
      message(
        FATAL_ERROR
          "Could not discover DOTNET_ROOT. Set DOTNET_ROOT explicitly.")
    endif()
    get_filename_component(DOTNET_SDK_DIR "${CMAKE_MATCH_1}" ABSOLUTE)
    get_filename_component(DOTNET_ROOT "${DOTNET_SDK_DIR}/../.." ABSOLUTE)
  endif()

  if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
    set(DOTNET_HOST_RID "osx-arm64")
  else()
    set(DOTNET_HOST_RID "osx-x64")
  endif()

  set(DOTNET_HOST_PACK_DIR
      "${DOTNET_ROOT}/packs/Microsoft.NETCore.App.Host.${DOTNET_HOST_RID}")

  file(GLOB DOTNET_HOST_NATIVE_DIRS
       "${DOTNET_HOST_PACK_DIR}/*/runtimes/${DOTNET_HOST_RID}/native")
  list(SORT DOTNET_HOST_NATIVE_DIRS COMPARE NATURAL)
  list(POP_BACK DOTNET_HOST_NATIVE_DIRS DOTNET_HOST_NATIVE_DIR)

  if(NOT DOTNET_HOST_NATIVE_DIR)
    message(
      FATAL_ERROR "Could not find Microsoft.NETCore.App.Host native assets for "
                  "${DOTNET_HOST_RID}. DOTNET_ROOT=${DOTNET_ROOT}")
  endif()
endif()

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../.." ABSOLUTE)

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

add_executable(
  hermes_console_hostfxr
  main.cpp "${REPO_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp"
  "${REPO_ROOT}/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp")

target_include_directories(
  hermes_console_hostfxr
  PRIVATE "${HERMES_ROOT}/include" "${REPO_ROOT}/native/include"
          "${REPO_ROOT}/native/packages/jsi/include")

if(EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr")
  target_compile_definitions(
    hermes_console_hostfxr
    PRIVATE EXPO_JSI_USE_HOSTFXR=1
            EXPO_JSI_MANAGED_CONFIGURATION="${EXPO_JSI_MANAGED_CONFIGURATION}")
  target_include_directories(hermes_console_hostfxr
                             PRIVATE "${DOTNET_HOST_NATIVE_DIR}")
else()
  target_compile_definitions(
    hermes_console_hostfxr
    PRIVATE EXPO_JSI_USE_NATIVEAOT=1
            EXPO_JSI_MANAGED_CONFIGURATION="${EXPO_JSI_MANAGED_CONFIGURATION}")
endif()

target_link_options(hermes_console_hostfxr PRIVATE "-F${HERMES_FRAMEWORK_DIR}"
                    "-Wl,-rpath,${HERMES_FRAMEWORK_DIR}")

target_link_libraries(hermes_console_hostfxr PRIVATE "-framework hermesvm")

if(EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr")
  target_link_libraries(hermes_console_hostfxr
                        PRIVATE "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib")

  add_custom_command(
    TARGET hermes_console_hostfxr
    POST_BUILD
    COMMAND
      ${CMAKE_COMMAND} -E copy_if_different
      "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib"
      "$<TARGET_FILE_DIR:hermes_console_hostfxr>/libnethost.dylib"
    COMMENT "Copying libnethost.dylib next to hermes_console_hostfxr")
endif()
