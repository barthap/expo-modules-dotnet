# Expo Modules JSI Swift Wrapper Model

This note summarizes the Apple/Swift wrapper model in Expo's
`expo-modules-jsi` package as a reference for the C# opaque-handle bridge.

Research scope:

- Primary source: `<expo_repo>/packages/expo-modules-jsi`
- Narrow nearby source: `<expo_repo>/packages/expo-modules-core/ios`
- Focus: wrapper types, conversions, host functions, scheduler access, and the
  parts that do or do not translate to a C ABI.

## High-Level Shape

`expo-modules-jsi` is a Swift-first wrapper around React Native JSI. It is not a
C ABI. The package uses Swift/C++ interop, API notes, and small C++ helper
functions so Swift can hold `facebook::jsi` values directly.

The package describes three layers:

```text
Swift API wrappers
  JavaScriptRuntime, JavaScriptValue, JavaScriptObject, JavaScriptArray,
  JavaScriptFunction, JavaScriptArrayBuffer, JavaScriptPromise, ...

C++ helper layer
  ExpoModulesJSI-Cxx wrappers around unsupported C++ templates, host functions,
  array buffers, errors, native state, and scheduler helpers

JSI / Hermes
  facebook::jsi::Runtime, Value, Object, Function, Array, ArrayBuffer, ...
```

That is useful as an API model, but the C# bridge should keep the repo's
existing rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

In practice, Swift wrappers that store `facebook.jsi.Value`, `Object`,
`Function`, or `Array` should become C# wrappers over opaque native handles,
not C# wrappers over C++ object layouts.

## Runtime Wrapper

Swift type: `JavaScriptRuntime`

Source:

- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/JavaScriptRuntime.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/APINotes/jsi.apinotes`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI-Cxx/include/RuntimeScheduler.h`

Important details:

- `JavaScriptRuntime` is a Swift class.
- It stores both `facebook.jsi.Runtime` and `facebook.jsi.IRuntime`.
- API notes import `Runtime`, `IRuntime`, and `ICast` as immortal Swift
  reference types. Swift therefore does not retain or release the real JSI
  runtime.
- `runtime.id` is the address of the underlying runtime. It is stable only
  while that runtime is alive.
- `global()` returns the runtime global object as `JavaScriptObject`.
- `createObject()`, `createArray()`, `createArrayBuffer()`, `createFunction()`,
  and `createAsyncFunction()` create JS values in that runtime.
- `withUnsafePointee` exposes the raw `facebook::jsi::Runtime` pointer for
  scoped native interop only. The pointer must not escape.

C# mapping:

- Use `JavaScriptRuntime` as a managed wrapper around
  `expo_js_runtime_handle`.
- Treat the runtime handle as borrowed from the host unless the headless proof
  creates and owns it.
- Add an identity API only if needed, and document that it is runtime-lifetime
  identity, not a permanent id.
- Do not expose raw `jsi::Runtime *` to C#.
- Provide runtime methods like `Global()`, `CreateObject()`, `CreateArray()`,
  `CreateString()`, `CreateFunction()`, and `CreateArrayBuffer()` through the
  ABI.

## Value Wrappers

Swift types:

- `JavaScriptValue`
- `JavaScriptUnownedValue`
- `JavaScriptRef<T>`
- `JavaScriptValuesBuffer`

Sources:

- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/Values/JavaScriptValue.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/Values/JavaScriptUnownedValue.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/JavaScriptRef.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/JavaScriptValuesBuffer.swift`

`JavaScriptValue` is the general owned value wrapper. It is a `final class`
that stores a `facebook.jsi.Value` plus a weak `JavaScriptRuntime`. It can be
stored, captured, copied, and passed across isolation contexts. It has type
checks like `isString()`, `isObject()`, and `isFunction()`, and typed accessors
like `getString()`, `getObject()`, `getArray()`, and `getFunction()`.

`JavaScriptUnownedValue` is the borrowed fast path. It is a non-copyable Swift
struct that points at a `facebook.jsi.Value` owned by someone else, typically an
argument still inside a `JavaScriptValuesBuffer` for the duration of one host
function call. Its contract is call-scoped: read it synchronously, do not store
or capture it, and call `copied(in:)` if the value must escape.

`JavaScriptValuesBuffer` wraps a contiguous buffer of JSI values. It can either
borrow an argument buffer from C++ or own an allocated buffer. When it owns
memory, its deinitializer runs the `jsi::Value` destructors and deallocates the
buffer. It exposes both:

- `subscript(index)` -> owning `JavaScriptValue`
- `unownedValue(at:)` -> borrowed `JavaScriptUnownedValue`

`JavaScriptRef<T>` is a reference box for Swift non-copyable values. It lets a
non-copyable wrapper be captured in escaping closures or stored in containers,
then transferred to a new owner with `take()`. This is mostly a Swift language
workaround, not a C# design target.

C# mapping:

- Use an owning `JavaScriptValue : IDisposable` over
  `expo_js_value_handle`.
- Use a separate scoped/borrowed value type for host-call decode, for example
  `JavaScriptUnownedValue` or `JavaScriptBorrowedValue`, over an argument
  handle that is valid only during the callback.
- Use `JavaScriptArguments` as the C# analog of `JavaScriptValuesBuffer`.
- Make borrowed values impossible or awkward to store. A `ref struct` is a good
  candidate for the C# borrowed wrapper.
- Provide an explicit copy/promote operation from borrowed to owned, requiring
  the matching runtime.
- Do not copy Swift's `JavaScriptRef<T>` unless C# later has an equivalent
  non-copyable-wrapper problem.

## Object, Array, Function, And String Model

Swift types:

- `JavaScriptObject`
- `JavaScriptArray`
- `JavaScriptFunction`
- no standalone public `JavaScriptString` wrapper

Sources:

- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/Values/JavaScriptObject.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/Values/JavaScriptArray.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/Values/JavaScriptFunction.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Protocols/JSIRepresentable.swift`

`JavaScriptObject` is a non-copyable Swift struct that stores
`facebook.jsi.Object` plus a weak runtime. It supports property checks,
property get/set, property names, prototype access, `instanceOf`, function
lookup, native state, and conversion to `JavaScriptValue`.

`JavaScriptArray` is a non-copyable Swift struct over `facebook.jsi.Array`. It
supports `length`, indexed get/set, iteration helpers, and conversion to
`JavaScriptValue`.

`JavaScriptFunction` is a non-copyable Swift struct over
`facebook.jsi.Function`. It supports:

- `call(arguments:)`
- `call(this:arguments:)`
- `callAsConstructor(...)`
- conversion to `JavaScriptValue`
- conversion to `JavaScriptObject`

Strings are handled as Swift `String` conversions. `String` conforms to
`JSIRepresentable` by using `facebook.jsi.String.createFromUtf8(...)` for
Swift -> JS and `value.getString(runtime).utf8(runtime)` for JS -> Swift. There
is no separate public `JavaScriptString` wrapper in the package.

C# mapping:

- Keep distinct object, array, and function handle types even if native stores
  all of them as JSI pointer values internally.
- Add explicit conversions:
  - object -> value
  - array -> object/value
  - function -> object/value
  - value -> object/array/function with typed errors
- Model strings as UTF-8 copies crossing the ABI, with explicit release for
  native-allocated string results.
- Do not expose an unsafe raw pointer accessor in the public C# API. If native
  internals need one, keep it inside C++.

## Conversion APIs

Swift conversion protocols:

- `JavaScriptRepresentable`
- `JSIRepresentable`
- `JavaScriptCodable`

Nearby Expo Modules Core conversion:

- `<expo_repo>/packages/expo-modules-core/ios/Core/Conversions.swift`

`JavaScriptRepresentable` is the public protocol:

```swift
static func fromJavaScriptValue(_ value: JavaScriptValue) -> Self
func toJavaScriptValue(in runtime: JavaScriptRuntime) -> JavaScriptValue
```

It has default conformances for `Optional`, `Array`, and
`Dictionary<String, Value>`.

`JSIRepresentable` is internal and lower-level:

```swift
static func fromJSIValue(_ value: borrowing facebook.jsi.Value, in runtime: facebook.jsi.IRuntime) -> Self
func toJSIValue(in runtime: facebook.jsi.IRuntime) -> facebook.jsi.Value
```

It is implemented for primitives, numeric types, `String`, `Optional`,
`Array`, and `Dictionary`. This layer is possible only because Swift can name
and hold C++ JSI value types.

Expo Modules Core also has type-erased conversion paths:

- `Conversions.anyToJavaScriptValue(...)`
- `Conversions.unknownToJavaScriptValue(...)`
- dynamic-type conversion via `AnyArgument` / `AnyDynamicType`
- result conversion for records, enumerable values, shared objects, data, arrays,
  dictionaries, and optionals

C# mapping:

- Public C# conversion should resemble `JavaScriptRepresentable` in spirit:
  typed converters convert between C# values and wrapper values.
- Internal C# code cannot have a true `JSIRepresentable` equivalent because it
  must not produce raw `jsi::Value`. Instead, generated code should call ABI
  functions such as:
  - create undefined/null/bool/number/string
  - read bool/number/string
  - create object/array
  - get/set object property
  - get/set array index
  - promote borrowed argument to owned value
- Keep a slower type-erased fallback for compatibility if needed, but the v2
  generated fast path should use typed converter calls, not reflection or JSON.

## Host Functions And Swift Calls From JS

Swift sources:

- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Runtime/JavaScriptRuntime.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI/Contexts/HostFunctionContext.swift`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI-Cxx/include/HostFunctionClosure.h`
- `<expo_repo>/packages/expo-modules-jsi/apple/Sources/ExpoModulesJSI-Cxx/include/JSIUtils.h`

The normal sync host function path is:

1. Swift calls `runtime.createFunction(name)`.
2. Swift retains a `HostFunctionContext` with the runtime and Swift closure.
3. Swift creates an `expo.HostFunctionClosure` that stores the retained Swift
   context pointer plus C-callable closure and deallocator callbacks.
4. C++ creates `jsi::Function::createFromHostFunction`.
5. When JS calls the function, C++ passes `this`, `args`, and `count` to the
   Swift trampoline.
6. Swift builds a `JavaScriptValuesBuffer` over the call-scoped argument
   pointer and calls the Swift closure under `JavaScriptActor.assumeIsolated`.
7. Swift returns a `JavaScriptValue`, then converts it back to a JSI value.
8. Errors are forwarded into JS through the C++ error bridge.

There are two sync closure shapes:

- owning `this`: `JavaScriptValue`
- borrowed `this`: `JavaScriptUnownedValue`

The borrowed-`this` overload exists so generated `@JS` bindings that ignore
`this` avoid an owning value allocation and weak-runtime traffic on every host
call.

The async host function path wraps a sync host function that creates a
`JavaScriptPromise`, copies the argument buffer for safe async access, schedules
the async Swift body, and resolves or rejects the promise later.

C# mapping:

- Native C++ should own `jsi::Function::createFromHostFunction`.
- The C ABI should accept a callback function pointer plus callback context.
- The callback should receive:
  - runtime handle
  - borrowed `this` handle
  - borrowed argument-buffer handle
  - result/error out parameter
- The C++ host-function trampoline should catch native exceptions and convert
  managed callback errors into JS exceptions.
- Managed generated bindings should decode borrowed arguments synchronously.
- If an async managed method needs arguments after returning to JS, it must
  copy/promote them before the callback frame ends.

## Global Object And Runtime Access

`JavaScriptRuntime.global()` calls `pointee.global()` and wraps the result as
`JavaScriptObject`.

Several higher-level operations use the global object:

- `createObject(prototype:)` calls global `Object.create`.
- `defineProperty` calls global `Object.defineProperty`.
- promise construction uses global `Promise`.
- data conversion can use global `Uint8Array`.
- `value.is("Promise")` looks up a named global constructor.

C# mapping:

- `JavaScriptRuntime.Global()` should be one of the first object APIs.
- Constructors such as `Object`, `Promise`, and `Uint8Array` should be ordinary
  property lookups and function calls, not special C# runtime features.
- Cache property names or constructor handles only with clear runtime-lifetime
  ownership.

## Ownership And Lifetime Findings

Visible ownership rules:

- The JSI runtime is externally owned. Swift imports it as immortal and stores
  weak runtime references in many wrappers.
- JS values belong to exactly one runtime. Copying reference values requires
  the same runtime.
- `JavaScriptValue` owns a copied/moved `jsi::Value` and can escape.
- `JavaScriptObject`, `JavaScriptArray`, and `JavaScriptFunction` own their JSI
  wrapper values but are Swift non-copyable structs.
- `JavaScriptUnownedValue` borrows a value owned by another call-scoped buffer.
- `JavaScriptValuesBuffer` may borrow the host-call argument pointer or own an
  allocated copied buffer.
- Host-function contexts are retained when the function is created and released
  by the C++ `HostFunctionClosure` deallocator.
- Native array buffers can wrap external memory with an explicit cleanup
  closure that runs when the JS ArrayBuffer is collected.

C# handle rules to preserve:

- Separate borrowed and owned handles.
- Every owned value/object/array/function handle needs an explicit release path.
- Borrowed argument handles are valid only during a host-function callback.
- A borrowed handle can be promoted to an owned handle by copying in the same
  runtime.
- Runtime handles must be checked or carried with every value handle; cross-
  runtime copying must fail loudly.
- Any native-owned UTF-8 result crossing the ABI must have a paired release.
- Async callbacks and promise settlement must never retain borrowed arguments
  without copying them first.

## Scheduler And Threading

Swift uses `@JavaScriptActor` for compile-time isolation, but that actor is
synchronous. It does not hop to the JS thread by itself.

`JavaScriptRuntime` supplies:

- `schedule(priority:_:)`
- sync and async `execute(...)`
- `supportsAsyncScheduling`
- `isOnJavaScriptThread()`

Standalone runtimes schedule synchronously. React-backed runtimes are
constructed with a host scheduler pointer and dispatch function.

C# mapping:

- Keep scheduler as an adapter-provided runtime capability.
- The portable core should expose a `schedule_on_js` service table or equivalent
  function pointer, not React Native scheduler types.
- Sync host functions are already executing on the JS callback frame and should
  decode/return directly.
- Promise settlement, retained cleanup that touches JSI, event emission, and
  callbacks from non-JS threads must go through the scheduler.

## Expo Modules Core Generated Dispatch

Relevant nearby files:

- `<expo_repo>/packages/expo-modules-core/ios/Core/ExpoModulesMacros.swift`
- `<expo_repo>/packages/expo-modules-core/ios/Core/Functions/SyncFunctionDefinition.swift`
- `<expo_repo>/packages/expo-modules-core/ios/Core/Functions/ConcurrentFunctionDefinition.swift`
- `<expo_repo>/packages/expo-modules-core/ios/Core/Functions/OptimizedSyncFunctionDefinition.swift`
- `<expo_repo>/packages/expo-modules-core/ios/JS/EXOptimizedFunctionUtils.h`
- `<expo_repo>/packages/expo-modules-core/ios/JS/EXOptimizedFunctionUtils.mm`

The older/general Expo Modules path builds `runtime.createFunction(...)`
closures that convert `this` and `JavaScriptValuesBuffer` into native arguments
through dynamic converter objects.

The newer optimized path has macros produce descriptors, then installs optimized
host functions by passing scoped raw runtime/object pointers into an
Objective-C++ helper. That helper creates JSI host functions and invokes
`@convention(block)` closures through type encodings, bypassing
`JavaScriptValue` boxing for supported primitive/string cases.

C# mapping:

- For v2, prefer generated C# binding code that decodes directly from
  `JavaScriptArguments`, calls the target method directly, and encodes the
  return value directly.
- Do not build ordinary dispatch around runtime reflection, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]`, or JSON.
- Keep any raw-pointer optimized helper equivalent inside native C++ and expose
  only stable opaque handles and function tables to C#.

## Patterns That Map Cleanly To C# Opaque Handles

- `JavaScriptRuntime` as the root wrapper for creation, global object, function
  creation, scheduling, and script evaluation.
- Distinct wrappers for value, object, array, function, array buffer, promise,
  error, and arguments.
- Borrowed argument values for synchronous generated decode.
- Explicit promotion from borrowed argument to owned value.
- `JavaScriptRepresentable`-style typed conversion, implemented by generated
  wrappers and converter structs/classes.
- Host function creation from callback context plus callback/deallocator.
- Runtime-owned property-name caches, if scoped to runtime lifetime.
- Native buffer cleanup callbacks.
- Structured error conversion at every native/managed boundary.

## Patterns That Do Not Map Directly

- Swift/C++ interop over `facebook.jsi.Value`, `Object`, or `Function`.
- API notes that make C++ runtime references appear immortal to Swift.
- Swift `~Copyable`, `borrowing`, and `consuming` as compile-time ownership
  enforcement.
- `JavaScriptRef<T>` as a workaround for Swift non-copyable values.
- Scoped `withUnsafePointee` in public API.
- Objective-C `NSInvocation` and type encoding for optimized function calls.
- Raw `RuntimeScheduler`, `CallInvoker`, or `RuntimeSchedulerBinding` types in
  portable API.

These can inspire C# API shape, but they should be re-expressed as ABI handles,
explicit release functions, generated typed conversion, and adapter-provided
scheduling.

## Recommended Next C# API Candidates

Start with the smallest set that supports a generated-looking module dispatch
proof:

1. `JavaScriptRuntime`
   - `Global()`
   - `CreateUndefined()`, `CreateNull()`, `CreateBool()`, `CreateNumber()`
   - `CreateString(ReadOnlySpan<byte> utf8)` or `CreateString(string)`
   - `CreateObject()`
   - `CreateArray(int length = 0)`
   - `CreateFunction(string name, JavaScriptHostFunction callback)`
2. `JavaScriptValue`
   - `Kind`
   - `AsBool()`, `AsDouble()`, `AsString()`
   - `AsObject()`, `AsArray()`, `AsFunction()`
   - `Copy()` / owned retain if needed
   - `Dispose()`
3. `JavaScriptUnownedValue` or `JavaScriptBorrowedValue`
   - type checks and primitive/string reads
   - `Copy(JavaScriptRuntime runtime)` to promote
   - object promotion only with matching runtime
4. `JavaScriptObject`
   - `GetProperty(string name)`
   - `SetProperty(string name, JavaScriptValue value)`
   - `GetPropertyNames()`
   - `GetPropertyAsFunction(string name)`
   - `AsValue()`
5. `JavaScriptArray`
   - `Length`
   - `GetValue(int index)`
   - `SetValue(int index, JavaScriptValue value)`
   - `AsObject()`, `AsValue()`
6. `JavaScriptFunction`
   - `Call(JavaScriptArguments args)`
   - `Call(JavaScriptObject thisObject, JavaScriptArguments args)`
   - `CallAsConstructor(JavaScriptArguments args)`
   - `AsObject()`, `AsValue()`
7. `JavaScriptArguments`
   - `Count`
   - `GetValue(int index)` returning owned value
   - `GetBorrowedValue(int index)` returning borrowed value
   - `Copy()` for async use
8. Converter layer
   - primitive and string converters
   - array and dictionary converters
   - explicit nullable/undefined handling
   - generated method-specific decode/encode code
9. Generic module dispatch proof
   - a generated-looking registration table
   - each exported function installs one host function
   - the host function decodes borrowed arguments, calls a direct managed
     delegate, encodes the return value, and returns a structured result

Stop before adding broad records, shared objects, promises, event emitters, or
optimized ObjC-style dispatch unless a proof needs them.
