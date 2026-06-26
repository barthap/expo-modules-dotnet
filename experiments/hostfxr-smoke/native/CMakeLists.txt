cmake_minimum_required(VERSION 3.24)
project(HostFxrSmoke LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

set(DOTNET_ROOT_DEFAULT "<dotnet-root>")
set(DOTNET_ROOT "$ENV{DOTNET_ROOT}")
if(NOT DOTNET_ROOT)
  set(DOTNET_ROOT "${DOTNET_ROOT_DEFAULT}")
endif()

if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
  set(DOTNET_HOST_RID "osx-arm64")
else()
  set(DOTNET_HOST_RID "osx-x64")
endif()

set(DOTNET_HOST_PACK_DIR
  "${DOTNET_ROOT}/packs/Microsoft.NETCore.App.Host.${DOTNET_HOST_RID}"
)

file(GLOB DOTNET_HOST_NATIVE_DIRS
  "${DOTNET_HOST_PACK_DIR}/*/runtimes/${DOTNET_HOST_RID}/native"
)
list(SORT DOTNET_HOST_NATIVE_DIRS COMPARE NATURAL)
list(POP_BACK DOTNET_HOST_NATIVE_DIRS DOTNET_HOST_NATIVE_DIR)

if(NOT DOTNET_HOST_NATIVE_DIR)
  message(
    FATAL_ERROR
    "Could not find Microsoft.NETCore.App.Host native assets for "
    "${DOTNET_HOST_RID}. DOTNET_ROOT=${DOTNET_ROOT}"
  )
endif()

add_executable(hostfxr_smoke main.cpp)
target_include_directories(hostfxr_smoke PRIVATE "${DOTNET_HOST_NATIVE_DIR}")
target_link_libraries(hostfxr_smoke
  PRIVATE "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib"
)

add_custom_command(TARGET hostfxr_smoke POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib"
    "$<TARGET_FILE_DIR:hostfxr_smoke>/libnethost.dylib"
  COMMENT "Copying libnethost.dylib next to hostfxr_smoke"
)
