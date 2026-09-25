#pragma once

// Single source of truth for the runtime-context ABI shared between the native
// hosts and the generated managed entry points. Every host crosses these types
// through a function pointer, so they must never be redeclared per platform.

#include <stdint.h>

#include <cstddef>
#include <type_traits>

#include "expo_jsi.h"

// Version of the host-context layout and field meanings. Appending complete
// fields does not change this version; an incompatible change does.
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

// Host-owned inputs for one managed runtime context. The size covers the
// complete fields supplied by this caller; future fields may be appended
// without changing the create function's shape or this struct version.
typedef struct expo_dotnet_host_context {
  uint32_t size;    // byte size through the last complete supplied field
  uint32_t version; // EXPO_DOTNET_HOST_ABI_VERSION

  // All strings: UTF-8, not NUL-terminated. Borrowed — valid only for the
  // duration of the create call, which copies them into managed strings before
  // returning, so no release callback is needed. A null pointer paired with
  // zero length means "not configured", and each directory is independent of
  // the other. A non-null pointer with zero length is a supplied empty string
  // and fails managed path validation; a null pointer with a nonzero length is
  // an invalid pair and is rejected. Any other pointer/length mismatch is
  // invalid.

  // App-scoped temporary files the operating system may remove at any time.
  const uint8_t *cache_directory;
  int32_t cache_directory_length;

  // App-scoped files that must survive OS cache eviction.
  const uint8_t *persistent_files_directory;
  int32_t persistent_files_directory_length;

  // Native versions owned by the embedding app. Both are optional and
  // independent; their buffers follow the same borrowed UTF-8 rules above.
  const uint8_t *native_app_version;
  int32_t native_app_version_length;
  const uint8_t *native_build_version;
  int32_t native_build_version_length;
} expo_dotnet_host_context;

// The generated managed host mirrors this layout in a second language, so lock
// it down here. A silent field-order or padding change would corrupt memory
// instead of failing to compile.
static_assert(sizeof(void *) == 4 || sizeof(void *) == 8);
static_assert(std::is_standard_layout_v<expo_dotnet_host_context>);
static_assert(offsetof(expo_dotnet_host_context, size) == 0);
static_assert(offsetof(expo_dotnet_host_context, version) == 4);
static_assert(offsetof(expo_dotnet_host_context, cache_directory) == 8);
static_assert(offsetof(expo_dotnet_host_context, cache_directory_length) == 8 + sizeof(void *));
static_assert(offsetof(expo_dotnet_host_context, persistent_files_directory) ==
              (sizeof(void *) == 8 ? 24 : 16));
static_assert(offsetof(expo_dotnet_host_context, persistent_files_directory_length) ==
              (sizeof(void *) == 8 ? 32 : 20));
static_assert(offsetof(expo_dotnet_host_context, native_app_version) ==
              (sizeof(void *) == 8 ? 40 : 24));
static_assert(offsetof(expo_dotnet_host_context, native_app_version_length) ==
              (sizeof(void *) == 8 ? 48 : 28));
static_assert(offsetof(expo_dotnet_host_context, native_build_version) ==
              (sizeof(void *) == 8 ? 56 : 32));
static_assert(offsetof(expo_dotnet_host_context, native_build_version_length) ==
              (sizeof(void *) == 8 ? 64 : 36));
static_assert(sizeof(expo_dotnet_host_context) == (sizeof(void *) == 8 ? 72 : 40));

// The host-context pointer may be null, which means every field is
// unconfigured. The v3 suffix is load-bearing: a stale adapter and host pair
// must fail symbol or method resolution instead of calling through the wrong
// contract, which the version field inside the struct cannot protect against.
using CreateRuntimeContextV3Fn = void (*)(const expo_jsi_api *,
                                          expo_jsi_runtime_handle,
                                          const expo_dotnet_host_context *,
                                          RuntimeContextResult *);
using TeardownRuntimeContextFn = void (*)(void *);

} // namespace expo::modules::dotnet
