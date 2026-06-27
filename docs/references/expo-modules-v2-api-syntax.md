# Expo Modules v2 API Syntax (iOS/Swift)

The v2 API replaces the runtime-reflective DSL (`Function(…)`, `Property(…)`, `@Field`) with compile-time Swift macros that bind directly into JavaScript via JSI. No type erasure, no `Mirror`, no `[Any]` boxing.

> Based on the codebase as of June 2026. Some features (marked **OPEN**) are in open PRs and not yet merged.

---

## @ExpoModule

Marks a class as an Expo module. Synthesizes all wiring — no `definition()` needed for `@JS` members.

```swift
@ExpoModule
final class Greeter: Module {
  @JS
  func greet(name: String) -> String {
    return "Hi, \(name)"
  }
}
```

### Custom JS name

```swift
@ExpoModule("MyCustomName")
final class InternalName: Module {
  @JS
  func ping() -> String { "pong" }
}
// JS: expo.modules.MyCustomName.ping()
```

### Without Module inheritance

`Module` base class is optional — the macro synthesizes `appContext` storage and `init(appContext:)` when needed.

```swift
@ExpoModule
final class Lightweight {
  @JS
  func hello() -> String { "world" }
}
```

### Mixing with the old DSL

`@JS` members and `definition()` coexist. The macro auto-stamps `@ModuleDefinitionBuilder` on `definition()`.

```swift
@ExpoModule
final class Mixed: Module {
  @JS
  func fast() -> Int { 42 }

  func definition() -> ModuleDefinition {
    AsyncFunction("slow") { ... }
  }
}
```

---

## @JS on functions

Binds a Swift method directly onto the module's JS object as a JSI host function.

### Sync functions

```swift
@ExpoModule
final class Math: Module {
  @JS
  func add(a: Double, b: Double) -> Double {
    return a + b
  }
}
// JS: expo.modules.Math.add(2, 3) // 5
```

### Custom JS name

```swift
@JS("sum")
func add(a: Double, b: Double) -> Double { a + b }
// JS: expo.modules.Math.sum(2, 3)
```

### Async functions

```swift
@JS
func fetchData(url: String) async throws -> String {
  // ... async work
}
// JS: await expo.modules.MyModule.fetchData("https://...")
```

Async `@JS` functions return a JS Promise. They are NOT stamped `@JavaScriptActor` — async dispatch handles threading.

### Throwing functions

```swift
@JS
func parse(json: String) throws -> String {
  // throws are caught and forwarded as JS errors
}
```

### Optional / defaulted trailing parameters

Parameters with defaults or `Optional` type can be omitted by the JS caller. The macro emits a `switch arguments.count` with per-arity branches.

```swift
@JS
func log(message: String, level: Int = 0) { ... }
// JS: module.log("hello")      // level defaults to 0
// JS: module.log("hello", 2)   // level = 2
```

### Supported parameter types

Primitives (`Bool`, `Int`, `Double`, `String`) use a **zero-copy fast path** — decoded via `arguments.unownedValue(at:).asDouble()` with no heap allocation. Everything else falls back to the dynamic type path.

---

## @JS on properties

### Read-only computed property

```swift
@JS
var status: String { "ok" }
// JS: expo.modules.MyModule.status // "ok"
```

Bound via `Object.defineProperty` with a getter closure.

### Read-write property

```swift
@JS
var volume: Double = 0.5
// JS: expo.modules.MyModule.volume      // 0.5
// JS: expo.modules.MyModule.volume = 1  // sets it
```

A `var` with a setter (stored or explicit `set`) gets both `get` and `set` in the property descriptor.

---

## @SharedObject

Marks a class as a JS-bridged shared object. Synthesizes `_synthesizedClassDefinition()` that binds `@JS` members onto the class prototype.

```swift
@SharedObject
final class Cache: SharedObject {
  private var store: [String: String] = [:]

  @JS
  init(name: String) {
    // called when JS does: new Cache("myCache")
  }

  @JS
  func get(_ key: String) -> String? {
    return store[key]
  }

  @JS
  func set(key: String, value: String) {
    store[key] = value
  }

  @JS
  var size: Int { store.count }
}
```

### Wiring into a module

```swift
@ExpoModule(classes: [Cache.self])
final class CacheModule: Module { }
```

The `classes:` parameter calls each class's `_synthesizedClassDefinition()` to register the constructor and prototype bindings.

### Constraints

- Must inherit from `SharedObject`.
- At most one `@JS init` (JS classes have a single constructor).
- **OPEN** (PR #47107) — not yet merged.

---

## @Record

Replaces the old `Record` + `@Field` pattern. Every stored property is a record field — no wrappers needed.

### Basic usage

```swift
@Record
struct Options {
  var name: String          // required — JS must provide it
  var count: Int = 0        // optional — defaults to 0 if omitted
  var note: String?         // nullable — nil if omitted or null
}
```

### As function parameters and return values

```swift
@ExpoModule
final class MyModule: Module {
  @JS
  func configure(options: Options) -> Options {
    return Options(name: options.name, count: options.count + 1)
  }
}
// JS: expo.modules.MyModule.configure({ name: "test", count: 5 })
//  => { name: "test", count: 6, note: null }
```

### What the macro synthesizes

- `init()` — memberwise initializer
- `from(object:appContext:)` — decode from `JavaScriptObject`
- `from(dictionary:appContext:)` — decode from `[String: Any]`
- `toDictionary(appContext:)` / `toObject(appContext:)` — encode
- `_assertTypesConformance()` — compile-time check that all property types are JS-convertible
- `Record` protocol conformance (which includes `JavaScriptCodable`)

### Excluded properties

`static`, `private`, `fileprivate`, `lazy`, and computed properties are not treated as record fields.

### Compared to the old way

```swift
// Old — runtime reflection, Mirror-based
struct Options: Record {
  @Field var name: String = ""
  @Field var count: Int = 0
}

// New — compile-time, ~5x faster
@Record
struct Options {
  var name: String
  var count: Int = 0
}
```

---

## @Event

Declares a typed event emitter on a module or shared object.

### Basic usage

```swift
@Record
struct ProgressEvent {
  var percent: Int = 0
}

@ExpoModule
final class Downloader: Module {
  @Event
  var onProgress: (ProgressEvent) -> Void

  @Event
  var onDone: () -> Void

  @JS
  func start() {
    // Emit events — callable from any thread (async by default)
    onProgress(ProgressEvent(percent: 50))
    onDone()
  }
}
```

```js
// JS:
const module = expo.modules.Downloader;
module.addListener('progress', (e) => console.log(e.percent));
module.addListener('done', () => console.log('finished'));
module.start();
```

### JS event name convention

The `on` prefix is stripped and the remainder decapitalized:

| Swift declaration | JS event name |
|---|---|
| `onProgress` | `"progress"` |
| `onStatusChange` | `"statusChange"` |
| `onURLChange` | `"urlChange"` |

Override with `@Event("customName")`.

### Sync events

```swift
@Event(sync: true)
var onSync: (String) -> Void
```

Sync events use `emitSync` and require the call site to be on the JS thread. The macro auto-stamps `@JavaScriptActor` on sync event properties.

### Constraints

- Must be instance `var` with a function type returning `Void`.
- At most one payload parameter.
- Cannot be combined with `@JS`.
- No custom accessors or initializers.

---

## JavaScriptCodable — supported types

Types usable as `@JS func` parameters and return values:

| Swift type | JS type | Path |
|---|---|---|
| `Bool` | `boolean` | Zero-copy fast |
| `Int` | `number` | Zero-copy fast (throws above 2^53) |
| `Double`, `Float`, `CGFloat` | `number` | Zero-copy fast |
| `String` | `string` | Zero-copy fast |
| `Int8`...`Int32`, `UInt8`...`UInt32` | `number` | Rounded, range-checked |
| `Int64`, `UInt64` | `bigint` | Lossless 64-bit |
| `Data` | `Uint8Array` | Copies bytes |
| `ArrayBuffer` | `ArrayBuffer` / `TypedArray` | Zero-copy when possible |
| `[T]` | `Array` | Recursive where `T: JavaScriptCodable` |
| `T?` | `T \| null \| undefined` | Zero-copy forwarding |
| `[String: T]` | `object` | String-keyed, recursive |
| `@Record` structs | `object` | Via synthesized `from(object:)` |
| `Enumerable` enums | raw value type | Via `RawValue` conversion |
| `JavaScriptValue` | any | Identity passthrough (no conversion) |

### Fast path vs dynamic path

The macro generates different decode code depending on the type:

- **Fast path** (primitives): `arguments.unownedValue(at: i).asDouble()` — reads directly from the JSI argument buffer through a raw pointer. No heap allocation, no ARC.
- **Dynamic path** (everything else): `T.getDynamicType().cast(jsValue: arguments[i], appContext:) as! T` — goes through the type-erased `AnyDynamicType` system. Still correct, just slower.

---

## Compile-time safety

### Type conformance assertions

Every `@JS` member generates a hidden `_assertTypesConformance_<member>()` peer that statically checks all parameter and return types conform to `AnyArgument`. Non-conforming types fail at compile time on the user's declaration site — not deep in generated code.

### Arity checking

Generated bindings guard `arguments.count` and throw `ArgumentsRangeMismatch` with the expected range when the JS caller passes the wrong number of arguments.

---

## Actor isolation

| Member | Isolation |
|---|---|
| Sync `@JS func` | Auto-stamped `@JavaScriptActor` |
| Async `@JS func` | No auto-stamp (async dispatch handles it) |
| `@JS var` getter/setter | Auto-stamped `@JavaScriptActor` |
| `@Event` (default) | No stamp — callable from any thread |
| `@Event(sync: true)` | Auto-stamped `@JavaScriptActor` |
| Members with `nonisolated` | Skipped |
| Members with explicit `@MainActor` | Skipped (user's choice respected) |

---

## Quick comparison: v1 DSL vs v2 macros

```swift
// ═══ v1: DSL-based ═══
class OldModule: Module {
  func definition() -> ModuleDefinition {
    Name("OldModule")

    Function("greet") { (name: String) -> String in
      return "Hi, \(name)"
    }

    Property("status") {
      return "ok"
    }

    Events("onProgress")
  }
}

// ═══ v2: Macro-based ═══
@ExpoModule
final class NewModule: Module {
  @JS
  func greet(name: String) -> String {
    return "Hi, \(name)"
  }

  @JS
  var status: String { "ok" }

  @Event
  var onProgress: (ProgressEvent) -> Void
}
```

The v2 version is plain Swift — methods are real methods (callable from Swift too), properties are real properties, and the macros handle all JSI wiring at compile time.
