#pragma once

#include "targetver.h"

#define NOMINMAX 1
#define WIN32_LEAN_AND_MEAN 1
#define WINRT_LEAN_AND_MEAN 1

#include <windows.h>
#undef GetCurrentTime
#include <unknwn.h>

#include <CppWinRTIncludes.h>
#include <winrt/Microsoft.ReactNative.Composition.h>
#include <winrt/Microsoft.ReactNative.h>
#include <winrt/Microsoft.UI.Composition.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/base.h>

#include <malloc.h>
#include <memory.h>
#include <stdlib.h>
#include <tchar.h>

#include <cstdint>
#include <future>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
