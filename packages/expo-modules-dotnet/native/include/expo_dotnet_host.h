#pragma once

// Single source of truth for the runtime-context ABI shared between the native
// hosts and the generated managed entry points. Every host crosses these types
// through a function pointer, so they must never be redeclared per platform.

#include <stdint.h>

#include <cstddef>
#include <type_traits>

#include "expo_jsi.h"

// Version of the host-supplied structs below. The loader and the generated
// managed host are built together for one app, so the managed side validates
// strict equality rather than parsing tolerantly.
#define EXPO_DOTNET_HOST_ABI_VERSION 1

namespace expo::modules::dotnet {

struct RuntimeContextError {
  const char *message = nullptr;
  int32_t messageLength = 0;
  void *releaseContext = nullptr;
  void (*release)(void *) = nullptr;
};

struct RuntimeContextResult {
  int32_t ok = 0;
  void *runtimeContext = nullptr;
  RuntimeContextError error;
};

// App-scoped directories the platform host resolves and hands to managed
// context creation. Only the host knows them: every managed path API is
// user-wide or process-wide, so a portable module resolving its own directory
// would collide with every other app on the machine.
typedef struct expo_dotnet_app_directories {
  uint32_t size;    // sizeof(expo_dotnet_app_directories)
  uint32_t version; // EXPO_DOTNET_HOST_ABI_VERSION

  // All strings: UTF-8, not NUL-terminated. Borrowed — valid only for the
  // duration of the create call, which copies them into managed strings before
  // returning, so no release callback is needed. A null pointer paired with
  // zero length means "not configured", and each directory is independent of
  // the other. A non-null pointer with zero length is a supplied empty string
  // and fails managed path validation; a null pointer with a nonzero length is
  // an invalid pair and is rejected. Any other pointer/length mismatch is
  // invalid.

  // Temporary files the operating system may remove at any time.
  const uint8_t *cache_directory;
  int32_t cache_directory_length;

  // App files that must survive OS cache eviction.
  const uint8_t *persistent_files_directory;
  int32_t persistent_files_directory_length;
} expo_dotnet_app_directories;

// The generated managed host mirrors this layout in a second language, so lock
// it down here. A silent field-order or padding change would corrupt memory
// instead of failing to compile.
static_assert(sizeof(void *) == 4 || sizeof(void *) == 8);
static_assert(std::is_standard_layout_v<expo_dotnet_app_directories>);
static_assert(offsetof(expo_dotnet_app_directories, size) == 0);
static_assert(offsetof(expo_dotnet_app_directories, version) == 4);
static_assert(offsetof(expo_dotnet_app_directories, cache_directory) == 8);
static_assert(offsetof(expo_dotnet_app_directories, cache_directory_length) == 8 + sizeof(void *));
static_assert(offsetof(expo_dotnet_app_directories, persistent_files_directory) ==
              (sizeof(void *) == 8 ? 24 : 16));
static_assert(offsetof(expo_dotnet_app_directories, persistent_files_directory_length) ==
              (sizeof(void *) == 8 ? 32 : 20));
static_assert(sizeof(expo_dotnet_app_directories) == (sizeof(void *) == 8 ? 40 : 24));

// The app-directories pointer may be null, which means both directories are
// unconfigured. The v2 suffix is load-bearing: a stale adapter and host pair
// must fail symbol or method resolution instead of calling through the wrong
// function-pointer signature, which the version field inside the struct cannot
// protect against.
using CreateRuntimeContextV2Fn = void (*)(const expo_jsi_api *,
                                          expo_jsi_runtime_handle,
                                          const expo_dotnet_app_directories *,
                                          RuntimeContextResult *);
using TeardownRuntimeContextFn = void (*)(void *);

} // namespace expo::modules::dotnet
