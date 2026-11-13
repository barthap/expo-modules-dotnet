#pragma once

#include <jsi/jsi.h>

// Installs `__expoBenchNative` on the global object. Its functions traverse or build values in plain
// JSI C++, with no managed crossing and no value handle, so a benchmark can measure a managed call
// next to its native-only control.
void installBenchmarkControls(facebook::jsi::Runtime &runtime);
