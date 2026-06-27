// These aliases document opaque C ABI handles. They are intentionally not
// distinct runtime types; C++ owns the objects behind the pointers.
global using ExpoJsiApiHandle = System.IntPtr;
global using ExpoJsiRuntimeHandle = System.IntPtr;
global using ExpoJsiValueHandle = System.IntPtr;
global using ExpoJsiObjectHandle = System.IntPtr;
global using ExpoJsiFunctionHandle = System.IntPtr;
global using ExpoJsiArgumentsHandle = System.IntPtr;
