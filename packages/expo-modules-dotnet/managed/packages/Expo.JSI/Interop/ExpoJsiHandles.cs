// These aliases document opaque C ABI handles. They are intentionally not
// distinct runtime types; C++ owns the objects behind the pointers.
global using ExpoJsiApiHandle = System.IntPtr;
global using ExpoJsiRuntimeHandle = System.IntPtr;
global using ExpoJsiValueHandle = System.IntPtr;
global using ExpoJsiPromiseHandle = System.IntPtr;
global using ExpoJsiArgumentsHandle = System.IntPtr;
global using ExpoJsiArrayBufferHandle = System.IntPtr;
global using ExpoJsiMutableBufferHandle = System.IntPtr;
global using ExpoJsiWeakObjectHandle = System.IntPtr;
