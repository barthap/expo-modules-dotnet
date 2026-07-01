#pragma once

#include <stddef.h>

using char_t = char;
using hostfxr_handle = void *;

struct get_hostfxr_parameters {
  size_t size;
  const char_t *assembly_path;
  const char_t *dotnet_root;
};

enum hostfxr_delegate_type {
  hdt_load_assembly_and_get_function_pointer = 5,
};

using get_hostfxr_path_fn =
  int (*)(char_t *buffer, size_t *buffer_size, const get_hostfxr_parameters *parameters);

using hostfxr_initialize_for_runtime_config_fn =
  int (*)(const char_t *runtime_config_path, const void *parameters, hostfxr_handle *host_context);

using hostfxr_get_runtime_delegate_fn =
  int (*)(hostfxr_handle host_context, hostfxr_delegate_type type, void **delegate);

using hostfxr_close_fn = int (*)(hostfxr_handle host_context);

using load_assembly_and_get_function_pointer_fn =
  int (*)(const char_t *assembly_path,
          const char_t *type_name,
          const char_t *method_name,
          const char_t *delegate_type_name,
          void *reserved,
          void **delegate);
