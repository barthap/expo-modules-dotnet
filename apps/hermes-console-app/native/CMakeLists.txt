cmake_minimum_required(VERSION 3.24)
project(HermesConsoleApp LANGUAGES C CXX)

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

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../.." ABSOLUTE)
include("${REPO_ROOT}/cmake/ExpoHermesPrebuilt.cmake")

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

  if(WIN32)
    expo_detect_windows_arch(DOTNET_WINDOWS_ARCH)
    if(DOTNET_WINDOWS_ARCH STREQUAL "x86")
      set(DOTNET_HOST_RID "win-x86")
    elseif(DOTNET_WINDOWS_ARCH STREQUAL "arm64")
      set(DOTNET_HOST_RID "win-arm64")
    else()
      set(DOTNET_HOST_RID "win-x64")
    endif()
  elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
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

set(HERMES_PREBUILT_ROOT
    "$ENV{HERMES_PREBUILT_ROOT}"
    CACHE PATH
          "Local Hermes prebuilt root created by scripts/build-hermes-macos.sh or scripts/build-hermes-windows.ps1")
if(NOT HERMES_PREBUILT_ROOT)
  set(HERMES_PREBUILT_ROOT "${REPO_ROOT}/build/hermes/source/destroot")
endif()

add_executable(
  hermes_console_app
  main.cpp
  ManagedProofLoader.cpp
  "${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp"
  "${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp"
)

target_include_directories(
  hermes_console_app
  PRIVATE
    "${REPO_ROOT}/packages/expo-modules-dotnet/native/include"
    "${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/include")

if(EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr")
  target_compile_definitions(
    hermes_console_app
    PRIVATE EXPO_JSI_USE_HOSTFXR=1
            EXPO_JSI_MANAGED_CONFIGURATION="${EXPO_JSI_MANAGED_CONFIGURATION}")
  target_include_directories(hermes_console_app
                             PRIVATE "${DOTNET_HOST_NATIVE_DIR}")
else()
  target_compile_definitions(
    hermes_console_app
    PRIVATE EXPO_JSI_USE_NATIVEAOT=1
            EXPO_JSI_MANAGED_CONFIGURATION="${EXPO_JSI_MANAGED_CONFIGURATION}")
endif()

expo_configure_hermes_target(hermes_console_app ROOT "${HERMES_PREBUILT_ROOT}")

if(EXPO_JSI_DOTNET_LOADER STREQUAL "hostfxr")
  if(WIN32)
    target_link_libraries(hermes_console_app
                          PRIVATE "${DOTNET_HOST_NATIVE_DIR}/nethost.lib")

    add_custom_command(
      TARGET hermes_console_app
      POST_BUILD
      COMMAND
        ${CMAKE_COMMAND} -E copy_if_different
        "${DOTNET_HOST_NATIVE_DIR}/nethost.dll"
        "$<TARGET_FILE_DIR:hermes_console_app>/nethost.dll"
      COMMENT "Copying nethost.dll next to hermes_console_app")
  else()
    target_link_libraries(hermes_console_app
                          PRIVATE "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib")

    add_custom_command(
      TARGET hermes_console_app
      POST_BUILD
      COMMAND
        ${CMAKE_COMMAND} -E copy_if_different
        "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib"
        "$<TARGET_FILE_DIR:hermes_console_app>/libnethost.dylib"
      COMMENT "Copying libnethost.dylib next to hermes_console_app")
  endif()
endif()
