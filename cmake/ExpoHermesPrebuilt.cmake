function(expo_detect_windows_arch out_var)
  if(CMAKE_VS_PLATFORM_NAME)
    string(TOLOWER "${CMAKE_VS_PLATFORM_NAME}" _platform)
  elseif(CMAKE_SYSTEM_PROCESSOR)
    string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" _platform)
  elseif(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(_platform "x64")
  else()
    set(_platform "x86")
  endif()

  if(_platform MATCHES "arm64ec")
    set(${out_var}
        "arm64ec"
        PARENT_SCOPE)
  elseif(_platform MATCHES "arm64|aarch64")
    set(${out_var}
        "arm64"
        PARENT_SCOPE)
  elseif(_platform MATCHES "x86|win32")
    set(${out_var}
        "x86"
        PARENT_SCOPE)
  else()
    set(${out_var}
        "x64"
        PARENT_SCOPE)
  endif()
endfunction()

function(expo_configure_hermes_target target_name)
  set(options)
  set(one_value_args ROOT)
  set(multi_value_args)
  cmake_parse_arguments(EXPO_HERMES "${options}" "${one_value_args}"
                        "${multi_value_args}" ${ARGN})

  if(EXPO_HERMES_ROOT)
    set(_hermes_root "${EXPO_HERMES_ROOT}")
  elseif(HERMES_PREBUILT_ROOT)
    set(_hermes_root "${HERMES_PREBUILT_ROOT}")
  elseif(DEFINED ENV{HERMES_PREBUILT_ROOT})
    set(_hermes_root "$ENV{HERMES_PREBUILT_ROOT}")
  else()
    get_filename_component(_repo_root "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
    set(_hermes_root "${_repo_root}/build/hermes/source/destroot")
  endif()

  set(HERMES_PREBUILT_ROOT
      "${_hermes_root}"
      CACHE PATH "Local Hermes prebuilt root")

  if(APPLE)
    set(_framework_dir "${_hermes_root}/Library/Frameworks/macosx")
    if(NOT EXISTS "${_hermes_root}/include/hermes/hermes.h")
      message(
        FATAL_ERROR
          "Could not find Hermes headers under ${_hermes_root}. "
          "Run scripts/build-hermes-macos.sh first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()
    if(NOT EXISTS "${_framework_dir}/hermesvm.framework/hermesvm")
      message(
        FATAL_ERROR
          "Could not find hermesvm.framework under ${_framework_dir}. "
          "Run scripts/build-hermes-macos.sh first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()

    target_include_directories(${target_name} PRIVATE "${_hermes_root}/include")
    target_link_options(${target_name} PRIVATE "-F${_framework_dir}"
                        "-Wl,-rpath,${_framework_dir}")
    target_link_libraries(${target_name} PRIVATE "-framework hermesvm")
  elseif(WIN32)
    expo_detect_windows_arch(_hermes_arch)
    set(_include_dir "${_hermes_root}/include")
    set(_lib_dir "${_hermes_root}/lib/win32/${_hermes_arch}")
    set(_bin_dir "${_hermes_root}/bin/win32/${_hermes_arch}")
    set(_jsi_lib "${_lib_dir}/jsi.lib")
    set(_hermes_lib "${_lib_dir}/hermesvm.lib")
    set(_hermes_dll "${_bin_dir}/hermesvm.dll")
    set(_hermes_runtime_name "hermesvm.dll")

    if(NOT EXISTS "${_hermes_lib}" OR NOT EXISTS "${_hermes_dll}")
      set(_hermes_lib "${_lib_dir}/hermes.lib")
      set(_hermes_dll "${_bin_dir}/hermes.dll")
      set(_hermes_runtime_name "hermes.dll")
    endif()

    if(NOT EXISTS "${_include_dir}/hermes/hermes.h")
      message(
        FATAL_ERROR
          "Could not find Hermes headers under ${_include_dir}. "
          "Run scripts/build-hermes-windows.ps1 first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()
    if(NOT EXISTS "${_include_dir}/jsi/jsi.h")
      message(
        FATAL_ERROR
          "Could not find JSI headers under ${_include_dir}. "
          "Run scripts/build-hermes-windows.ps1 first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()
    if(NOT EXISTS "${_hermes_lib}")
      message(
        FATAL_ERROR
          "Could not find Hermes import library at ${_hermes_lib}. "
          "Run scripts/build-hermes-windows.ps1 first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()
    if(NOT EXISTS "${_hermes_dll}")
      message(
        FATAL_ERROR
          "Could not find Hermes runtime DLL at ${_hermes_dll}. "
          "Run scripts/build-hermes-windows.ps1 first, or pass "
          "-DHERMES_PREBUILT_ROOT=<destroot>.")
    endif()

    target_include_directories(${target_name} PRIVATE "${_include_dir}")
    target_link_libraries(${target_name} PRIVATE "${_hermes_lib}")
    if(EXISTS "${_jsi_lib}")
      target_link_libraries(${target_name} PRIVATE "${_jsi_lib}")
    endif()

    add_custom_command(
      TARGET ${target_name}
      POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_hermes_dll}"
              "$<TARGET_FILE_DIR:${target_name}>/${_hermes_runtime_name}"
      COMMENT "Copying ${_hermes_runtime_name} next to ${target_name}")

    if(EXISTS "${_bin_dir}/hermes-icu.dll")
      add_custom_command(
        TARGET ${target_name}
        POST_BUILD
        COMMAND
          ${CMAKE_COMMAND} -E copy_if_different "${_bin_dir}/hermes-icu.dll"
          "$<TARGET_FILE_DIR:${target_name}>/hermes-icu.dll"
        COMMENT "Copying hermes-icu.dll next to ${target_name}")
    endif()
  else()
    message(
      FATAL_ERROR "Unsupported Hermes prebuilt platform: ${CMAKE_SYSTEM_NAME}")
  endif()
endfunction()
