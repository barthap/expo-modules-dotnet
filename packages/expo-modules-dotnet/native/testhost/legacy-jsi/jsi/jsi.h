#pragma once

// Compile-only pre-extension JSI fixture. These declarations deliberately omit
// ArrayBuffer members so ArrayBufferCapabilities.h proves its newer-member calls
// are discarded when a target selects an older JSI header.
namespace facebook::jsi {

class Runtime {};

class MutableBuffer {};

class ArrayBuffer {};

} // namespace facebook::jsi
