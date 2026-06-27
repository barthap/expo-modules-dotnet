# Expo Modules v2 — Macro Expansions

What each annotation actually generates. All generated code lives in `_decorateModule(object:in:appContext:)` (for modules) or `_synthesizedClassDefinition()` (for shared objects).

---

## @JS func (sync)

```swift
@JS
func greet(name: String) -> String { "Hi, \(name)" }
```

Expands to:

```swift
object.setProperty("greet") { [self] this, arguments in
    guard arguments.count == 1 else {
        throw Exceptions.ArgumentsRangeMismatch(...)
    }
    let arg0 = try arguments.unownedValue(at: 0).asString()  // zero-copy fast path
    let result = self.greet(name: arg0)
    return result.toJavaScriptValue(in: runtime)
}
```

Primitives (`Bool`, `Int`, `Double`, `String`) use `unownedValue(at:).as*()` — a raw pointer read from the argument buffer. Other types fall back to `T.getDynamicType().cast(jsValue: arguments[i], appContext:) as! T`.

## @JS func with custom name

```swift
@JS("sum")
func add(a: Double, b: Double) -> Double { a + b }
```

Same expansion but uses `"sum"` as the property key instead of `"add"`.

## @JS func (async)

```swift
@JS
func fetch(url: String) async throws -> String { ... }
```

Expands to the async `setProperty` overload — returns a JS Promise. Not stamped `@JavaScriptActor` (async dispatch handles threading).

## @JS func with optional trailing parameters

```swift
@JS
func log(message: String, level: Int = 0) { ... }
```

Expands to a `switch arguments.count` with per-arity branches:

```swift
object.setProperty("log") { [self] this, arguments in
    switch arguments.count {
    case 1:
        let arg0 = try arguments.unownedValue(at: 0).asString()
        self.log(message: arg0)          // level uses Swift default
    case 2:
        let arg0 = try arguments.unownedValue(at: 0).asString()
        let arg1 = try arguments.unownedValue(at: 1).asInt()
        self.log(message: arg0, level: arg1)
    default:
        throw Exceptions.ArgumentsRangeMismatch(...)
    }
    return JavaScriptValue.undefined(in: runtime)
}
```

## @JS var (read-only)

```swift
@JS
var status: String { "ok" }
```

Expands to `Object.defineProperty` with a getter:

```swift
let statusDescriptor = runtime.createObject()
statusDescriptor.setProperty("enumerable", value: true)
statusDescriptor.setProperty("get") { [self] this, arguments in
    return self.status.toJavaScriptValue(in: runtime)
}
object.defineProperty("status", descriptor: statusDescriptor)
```

## @JS var (read-write)

```swift
@JS
var volume: Double = 0.5
```

Same as above but adds a `set` closure to the descriptor:

```swift
statusDescriptor.setProperty("set") { [self] this, arguments in
    self.volume = try arguments.unownedValue(at: 0).asDouble()
    return JavaScriptValue.undefined(in: runtime)
}
```

## @Event

```swift
@Event
var onProgress: (ProgressEvent) -> Void
```

The accessor macro turns this into a computed getter returning a closure:

```swift
var onProgress: (ProgressEvent) -> Void {
    get {
        { [weak self] payload in
            self?.emit(event: "progress", payload: payload)
        }
    }
}
```

Name convention: `on` prefix stripped, remainder decapitalized (`onProgress` → `"progress"`).

With `@Event(sync: true)`, uses `emitSync` instead and the property is stamped `@JavaScriptActor`.

## @Record

```swift
@Record
struct Options {
    var name: String
    var count: Int = 0
    var note: String?
}
```

Synthesizes (among others):

```swift
extension Options: Record {}

init() { fatalError("required fields") }
init(name: String, count: Int = 0, note: String? = nil) { ... }

static func from(object: JavaScriptObject, appContext: AppContext) throws -> Options {
    var result = Options(name: /* decoded from object */)
    // each property: read from object, decode via its static type
    return result
}

func toDictionary(appContext: AppContext) throws -> [String: Any] {
    // each property: encode via its static type
}
```

No `@Field` wrappers, no `Mirror` reflection. Per-property reads/writes are generated at compile time from the struct's property types.

## @ExpoModule

```swift
@ExpoModule("MyModule")
final class MyModule: Module { ... }
```

Synthesizes:

- `static let _jsName = "MyModule"` — JS-side module name
- `_synthesizedDefinition()` — returns a `ModuleDefinition` (wires `classes:` if provided)
- `_decorateModule(object:in:appContext:)` — contains all `@JS` / `@Event` bindings shown above
- Stamps `@JavaScriptActor` on sync `@JS` members
- Stamps `@ModuleDefinitionBuilder` on `definition()` if present
- Adds `AnyModule` conformance if not inherited from `Module`

## @SharedObject

```swift
@SharedObject
final class Cache: SharedObject { ... }
```

Synthesizes `_synthesizedClassDefinition()` — same binding patterns as `_decorateModule` but targeting the **class prototype** instead of a module instance. Wired into the module via `@ExpoModule(classes: [Cache.self])`.

## Compile-time peer: type assertion

Every `@JS` member also generates a hidden peer:

```swift
@available(*, unavailable)
private func _assertTypesConformance_greet() {
    _ = String.self as AnyArgument.Type  // fails at compile time if not conforming
}
```

This catches non-JS-convertible types at the user's declaration site rather than in generated code.
