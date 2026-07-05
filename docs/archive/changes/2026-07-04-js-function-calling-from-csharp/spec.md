# JS Function Calling From C# Delta Spec

## Goal

Add the low-level and module-layer contracts needed for C# code to call
JavaScript functions, including retained JavaScript callbacks that can be
invoked later from module logic.

## Scope

This change covers:

- `Expo.JSI` ABI and managed wrapper support for calling JavaScript functions.
- `Expo.ModulesCore` retained callback wrappers built on generated codecs.
- Source generator support for callback parameters.
- Hermes-backed tests for function calls, callback retention, lifecycle guards,
  and generated module behavior.

This change does not cover EventEmitter, HostObject, NativeState, ArrayBuffer,
or replacing the explicit callback type with authored `Func<>` syntax.

## Accepted Design

`Expo.JSI` remains the low-level package. It SHALL expose call operations on
`JavaScriptFunction` and SHALL accept arguments through
`IJavaScriptValueRepresentable` so callers can pass typed wrappers without
manually converting every argument to `JavaScriptValue`.

`JavaScriptArguments` remains an inbound host-function callback type. It SHALL
NOT become the outbound call argument container because its values are scoped to
an incoming callback frame.

`Expo.ModulesCore` SHALL expose retained `JavaScriptCallback` wrappers. These
wrappers SHALL retain the JavaScript function, bind it to the owning runtime
context, encode arguments through existing codecs, decode return values through
existing codecs, and release the retained function when disposed or when the
owning runtime context is torn down.

Retained callbacks SHALL support both immediate runtime-thread invocation and
scheduled later invocation:

- `Invoke(...)` is valid only while managed code is already executing on the
  owning JavaScript runtime.
- `InvokeAsync(...)` schedules callback invocation onto the owning JavaScript
  runtime and is the path intended for later event-style use.

The source generator MAY support `Func<>` syntax in a later ergonomics pass,
but this slice SHALL use explicit `JavaScriptCallback<TArgs, TResult>` types as
the runtime contract. `TArgs` SHALL be `System.ValueTuple` for zero arguments or
tuple syntax / `System.ValueTuple<...>` for one or more arguments. For eight
arguments, explicit `System.ValueTuple` spelling uses C#'s nested rest shape:
`System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8>>`.

## Delta Requirements

### ADDED: Runtime ABI Function Calls

The native ABI SHALL expose function-call entries for:

- calling a JavaScript function with JavaScript `undefined` as `this`;
- calling a JavaScript function with an explicit JavaScript object as `this`;
- calling a JavaScript function as a constructor.

#### Scenario: Function is called

- **GIVEN** managed code owns a `JavaScriptFunction`
- **AND** managed code supplies zero or more JavaScript value arguments
- **WHEN** managed code calls the function
- **THEN** native SHALL call the underlying JSI function
- **AND** return the JavaScript result as an owned `JavaScriptValue`

#### Scenario: Function is called with explicit this

- **GIVEN** managed code owns a `JavaScriptFunction`
- **AND** managed code owns a `JavaScriptObject` to use as `this`
- **WHEN** managed code calls the function with that `this` object
- **THEN** native SHALL call the underlying JSI function with that object as
  `this`
- **AND** return the JavaScript result as an owned `JavaScriptValue`

#### Scenario: Function is called as constructor

- **GIVEN** managed code owns a `JavaScriptFunction`
- **WHEN** managed code calls the function as a constructor
- **THEN** native SHALL call the underlying JSI constructor path
- **AND** return the constructed JavaScript value as an owned `JavaScriptValue`

### ADDED: Managed Function Wrapper Calls

`JavaScriptFunction` SHALL expose `Call`, `CallWithThis`, and
`CallAsConstructor` methods. These methods SHALL accept
`IJavaScriptValueRepresentable` arguments, materialize temporary owned value
handles only for the duration of the call, and dispose those temporary handles
after native returns.

#### Scenario: Call arguments use representable wrappers

- **GIVEN** managed code has a `JavaScriptFunction`
- **AND** managed code has arguments represented by values, objects, arrays, or
  functions
- **WHEN** managed code calls `Call`, `CallWithThis`, or `CallAsConstructor`
- **THEN** each argument SHALL be converted through `AsValue()`
- **AND** those temporary value wrappers SHALL be disposed after the call
- **AND** the source wrappers SHALL remain owned by the caller

#### Scenario: JavaScript call throws

- **GIVEN** a JavaScript function throws during a managed call
- **WHEN** native reports the failure through the ABI
- **THEN** managed code SHALL throw a managed exception with the native error
  message
- **AND** no temporary argument handles SHALL leak

### ADDED: Function Value Conversion

`JavaScriptValue` and scoped value refs SHALL support explicit conversion to a
`JavaScriptFunction` wrapper when the underlying value is a JavaScript function.

#### Scenario: Value converts to function

- **GIVEN** a `JavaScriptValue` containing a JavaScript function
- **WHEN** managed code calls `AsFunction`
- **THEN** the returned `JavaScriptFunction` SHALL own a retained handle and
  must be disposed independently

#### Scenario: Scoped value ref converts to function

- **GIVEN** a scoped JavaScript value ref containing a JavaScript function
- **WHEN** managed code calls `AsFunction`
- **THEN** the returned `JavaScriptFunction` SHALL own a retained handle and
  must be disposed independently

### ADDED: Retained Callback Wrappers

`Expo.ModulesCore` SHALL expose one explicit retained callback wrapper type for
module parameters: `JavaScriptCallback<TArgs, TResult>`. `TArgs` SHALL encode
callback arguments as a value tuple. `ValueTuple` represents zero arguments.
Tuple element types SHALL remain statically known and codec-backed. The
implementation SHALL support up to eight callback arguments in this slice
without runtime reflection or dynamic invocation on the generated hot path.

#### Scenario: Callback parameter is decoded

- **GIVEN** a generated module method has a `JavaScriptCallback` parameter
- **WHEN** JavaScript passes a function for that parameter
- **THEN** the generated binding SHALL decode it into a retained callback
  wrapper
- **AND** the wrapper SHALL retain the JavaScript function beyond the host
  function callback frame

#### Scenario: Callback is invoked immediately

- **GIVEN** a retained callback wrapper
- **AND** managed code is already executing on the owning JavaScript runtime
- **WHEN** managed code calls `Invoke` with a `TArgs` tuple
- **THEN** the wrapper SHALL encode each tuple element with its configured
  tuple argument codec
- **AND** call the retained JavaScript function
- **AND** decode the returned JavaScript value with the configured return codec

#### Scenario: Callback is invoked later

- **GIVEN** a retained callback wrapper
- **WHEN** managed code calls `InvokeAsync` with a `TArgs` tuple
- **THEN** invocation SHALL be scheduled onto the owning JavaScript runtime
- **AND** the returned managed task SHALL complete with the decoded return value
  or fail with the callback invocation error

#### Scenario: Runtime is torn down

- **GIVEN** a retained callback wrapper belongs to a runtime context
- **WHEN** that runtime context is torn down
- **THEN** later callback invocation SHALL fail loudly
- **AND** retained JavaScript function handles SHALL be released according to
  existing runtime-context teardown ownership rules

### MODIFIED: Source Generator Supported Types

The source generator SHALL treat explicit `JavaScriptCallback<TArgs, TResult>`
parameter types as supported module function parameters when `TArgs` is a
supported value tuple shape and all tuple element and return types have codecs.

#### Scenario: Callback type has unsupported codec

- **GIVEN** a generated module method uses a `JavaScriptCallback` parameter
- **AND** `TArgs` is not a supported tuple shape or a tuple element or return
  type does not have a supported codec
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported type

### ADDED: Value Tuple Argument Codecs

`Expo.ModulesCore` SHALL provide value-tuple argument codecs for retained
callbacks. These codecs SHALL encode tuple elements into JavaScript argument
values positionally.

#### Scenario: Zero callback arguments are encoded

- **GIVEN** a callback uses `JavaScriptCallback<ValueTuple, TResult>`
- **WHEN** managed code invokes the callback
- **THEN** the tuple argument codec SHALL produce zero JavaScript arguments

#### Scenario: Tuple callback arguments are encoded

- **GIVEN** a callback uses `JavaScriptCallback<(string name, int count), TResult>`
- **WHEN** managed code invokes the callback with `("expo", 3)`
- **THEN** the tuple argument codec SHALL encode `"expo"` as the first argument
- **AND** encode `3` as the second argument

## Verification Requirements

Implementation SHALL verify:

- low-level `JavaScriptFunction.Call`, `CallWithThis`, and `CallAsConstructor`
  behavior in `Expo.JSI.Tests`;
- temporary argument disposal and source-wrapper ownership;
- JavaScript exception propagation through managed calls;
- callback decoding, immediate invocation, scheduled invocation, and teardown
  failure in `Expo.ModulesCore.Tests`;
- generator diagnostics for unsupported callback codec types;
- `scripts/test-managed.sh`;
- `scripts/format.sh --check --all`;
- `git diff --check`.
