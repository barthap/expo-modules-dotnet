# Modules Core Shared Objects

## Purpose

Define authored shared objects: the identity registry, generated
constructors and members, codecs, and release.

## Requirements

### Requirement: Internal Shared-Object Identity Registry

Each `DotnetRuntimeContext` SHALL own one internal `SharedObjectRegistry` for
the runtime. The registry SHALL map a managed internal lifetime-contract
instance to one live JavaScript object entry by reference identity, and map a
private entry id back to that same instance. Each entry may retain only its
managed lifetime state, NativeState token, and opaque `JavaScriptWeakObject`;
it SHALL NOT retain an ordinary object, function, value, or prototype wrapper
after conversion returns.

#### Scenario: Internal identity round trip
- **GIVEN** an active entry for one managed lifetime-contract instance
- **WHEN** it is converted to JavaScript twice or decoded through its private
  NativeState token
- **THEN** both JavaScript conversions SHALL be strictly equal
- **AND** decoding SHALL return the original managed instance

#### Scenario: Terminal shared entry
- **GIVEN** JavaScript release, deterministic collection, NativeState cleanup,
  or runtime-context disposal reaches an entry
- **WHEN** the first terminal source removes it from both maps
- **THEN** the registry SHALL dispose the opaque weak wrapper and run the
  managed lifetime action exactly once outside both registry and weak-wrapper
  locks
- **AND** later release sources SHALL be no-ops
- **AND** the same managed instance SHALL not form a replacement pair

#### Scenario: NativeState callback re-enters registry work
- **GIVEN** NativeState cleanup occurs while the registry gate is held
- **WHEN** its callback identifies a terminal entry
- **THEN** it SHALL defer terminal work until the registry gate has been
  released
- **AND** it SHALL not use an access frame, ordinary JSI wrapper operation,
  blocking runtime scheduling, a raw managed pointer, or a JavaScript-visible
  identifier

#### Scenario: Context teardown owns shared entries first
- **GIVEN** a context is being disposed while its runtime is still valid
- **WHEN** it drains owned state
- **THEN** it SHALL drain the shared-object registry before generated host
  registrations, retained callbacks, module registry, event state, and object
  factory
- **AND** it SHALL continue later cleanup after a failure and report aggregate
  failures only after reaching the terminal context state

The registry is also the identity and lifetime mechanism for generated public
`SharedObject` bindings. Its per-context ownership, reference-identity maps,
weak counterpart, private NativeState token, no-repairing rule, exactly-once
terminal release outside locks, re-entry deferral, and teardown-first ordering
apply unchanged to the public surface. Cross-runtime pairing and a
`JavaScriptObject` codec remain separate future capabilities;
`JavaScriptValue` remains the existing advanced module convertible.

#### Scenario: Public binding reuses the proven registry

- **GIVEN** generated public shared-object glue encodes, decodes, constructs,
  releases, or tears down an instance
- **WHEN** it performs identity or terminal work
- **THEN** it SHALL use the context-owned `SharedObjectRegistry`
- **AND** it SHALL preserve every current registry identity and teardown
  scenario
- **AND** it SHALL not add an ABI entry or modify `Expo.JSI`

#### Scenario: Registry operation can trigger external work

- **GIVEN** constructor installation, class-prototype setup, author code, or a
  JavaScript callback could re-enter registry or context work
- **WHEN** generated or runtime glue performs that work
- **THEN** it SHALL do so outside the registry gate
- **AND** any failed registration SHALL roll back without weakening terminal
  or NativeState re-entry semantics

### Requirement: Public SharedObject Authoring Is Explicit

`Expo.ModulesCore` SHALL expose a public abstract `SharedObject` base class and
an `[ExpoSharedObject]` class attribute. An attributed class SHALL be top-level,
non-generic, sealed, partial, and derived directly or indirectly from
`SharedObject`. It MAY be public or internal. Indirect derivation MAY use a
generic managed carrier base such as `SharedRef<T>`, but only the concrete
sealed attributed class is generated. The attribute MAY accept one non-empty
explicit JavaScript class name; otherwise the class name SHALL be the authored
C# type name verbatim, matching current module-class naming.

`SharedObject` SHALL hide its registry lifetime implementation from authors.
Ordinary authored code SHALL NOT receive registry identifiers, NativeState
tokens, weak wrappers, JSI handles, or explicit runtime-scheduling duties.

#### Scenario: Valid shared-object class is discovered

- **GIVEN** a top-level non-generic sealed partial class derives from
  `SharedObject` and has `[ExpoSharedObject]`
- **WHEN** its compilation runs the ModulesCore generator
- **THEN** the generator SHALL model it as an authored shared-object class
- **AND** it SHALL emit direct generated support only when an owning module
  lists the class

#### Scenario: Implicit JavaScript class name is generated

- **GIVEN**
  `[ExpoSharedObject] public sealed partial class CacheEntry : SharedObject`
- **WHEN** the generator exports the class for an owning module
- **THEN** the JavaScript class name SHALL be `CacheEntry`
- **AND** the generator SHALL NOT transform it to a member-style lower-camel
  name

#### Scenario: Explicit JavaScript class name is preserved

- **GIVEN** a shared-object class declares `[ExpoSharedObject("NativeCache")]`
- **WHEN** the generator exports it
- **THEN** its JavaScript class name SHALL be `NativeCache` verbatim
- **AND** the generator SHALL NOT also export an implicit-name alias

#### Scenario: Shared-object declaration is invalid

- **GIVEN** `[ExpoSharedObject]` appears on a nested, generic, non-sealed,
  non-partial, or non-`SharedObject` class, or its explicit name is null, empty,
  or blank
- **WHEN** the generator analyzes the declaration
- **THEN** it SHALL report `EXPOJSI021`
- **AND** it SHALL NOT silently emit a partial or reflection-based binding

### Requirement: Modules Explicitly Own Shared-Object Classes

`ExpoModuleAttribute` SHALL expose a settable `Type[] Classes` property whose
default is empty. An authored module SHALL list each shared-object class it
owns with `[ExpoModule(Classes = new[] { typeof(CacheEntry) })]`. One authored
shared-object type SHALL have exactly one owning module in a compilation, and a
module SHALL NOT list the same type more than once. Exported class names SHALL
be unique within an owning module.

For a class with an exposed constructor, `EXPOJSI024` validation SHALL compare
its effective JavaScript name against the owning module's complete effective
JavaScript namespace. That namespace includes generated methods and
properties, all exposed class constructors, generated observing hooks, and the
inherited or reserved event-runtime members. A collision SHALL fail generation;
registration SHALL NOT overwrite an existing member or choose by source order.
Native-created-only classes do not add a module property, but their effective
class names SHALL still be unique among every class owned by the module because
the generated prototype and codec identity table uses those names.

Generated default registration SHALL preserve one-stage module laziness.
Shared-object class installation SHALL occur when the owning module object is
created, not when the package provider first registers its module metadata.

#### Scenario: Owning module is materialized

- **GIVEN** a lazy module lists one valid shared-object class
- **WHEN** JavaScript first resolves that module
- **THEN** generated registration SHALL install the class prototype for that
  runtime context
- **AND** it SHALL expose a module property containing the class constructor
  only when the class has a valid `[JS]` constructor
- **AND** later reads of the module SHALL reuse the same module and class
  installation

#### Scenario: Native-created-only class is owned

- **GIVEN** an owning module lists a valid shared-object class with no `[JS]`
  constructor
- **WHEN** the module is materialized
- **THEN** generated registration SHALL install the internal class prototype
  needed for encoded instances
- **AND** it SHALL NOT expose a constructible class property on the module
- **AND** generated methods MAY return managed instances of that class

#### Scenario: Class ownership is invalid or duplicated

- **GIVEN** a `Classes` entry is not an attributed `SharedObject`, one type is
  listed more than once, one type has multiple owning modules, an attributed
  type has no owning module, or two owned classes, including
  native-created-only classes, resolve to the same effective name in one module
- **WHEN** the generator analyzes module ownership
- **THEN** it SHALL report `EXPOJSI024`
- **AND** it SHALL NOT resolve ownership or naming by declaration order

#### Scenario: Exposed class name collides with the module namespace

- **GIVEN** a constructible shared-object class name matches a generated module
  method or property, another exposed class constructor, an observing hook, or
  an inherited or reserved event-runtime member
- **WHEN** the generator computes the complete effective module namespace
- **THEN** it SHALL report `EXPOJSI024` naming both conflicting surfaces
- **AND** generated registration SHALL NOT overwrite either surface

#### Scenario: Native-created-only class names collide

- **GIVEN** two native-created-only classes owned by one module resolve to the
  same effective class name
- **WHEN** the generator builds its prototype and codec identity table
- **THEN** it SHALL report `EXPOJSI024`
- **AND** it SHALL NOT choose a prototype by declaration order

### Requirement: Generated Constructors Create Registry-Paired Instances

An authored shared-object class MAY declare exactly one instance constructor
with `[JS]`. A generated JavaScript constructor SHALL be available only for a
public or internal attributed constructor whose parameters all have
compile-time decode codecs. Constructor arguments SHALL be decoded during the
JavaScript call and the C# constructor SHALL be invoked directly.

The implementation MAY use a generated host function that is valid with
JavaScript `new`, assign its generated prototype explicitly, and return a
registry-paired object created with that prototype. It SHALL NOT depend on a
particular helper being callback-capable. Construction SHALL finish with one
managed instance paired to the returned JavaScript object, carrying the
registry's private NativeState token.

The generated factory SHALL release leaf-bearing constructor arguments in a
`finally`, after the factory returns the instance or throws. Registry pairing
is unchanged.

Registry entry creation, prototype selection, and rollback SHALL NOT execute
user-controlled JavaScript or other re-entrant work while the registry gate is
held. Argument-decoding failure or an authored constructor that throws before
returning an instance SHALL release temporary wrappers but has no managed
instance to release. Once an attributed `[JS]` constructor returns a managed
instance, the generated construction path SHALL own that instance until it is
successfully paired and returned to JavaScript. If prototype setup, NativeState
attachment, weak-object creation, map insertion, or later pairing work fails,
generated/runtime glue SHALL dispose every partial registration and owned
wrapper, mark the instance terminal under the no-repairing rule, and invoke its
`OnRelease` exactly once outside registry and weak-wrapper locks. NativeState or
other rollback re-entry SHALL converge on that same terminal action.

This constructor-originated transfer is distinct from encoding an existing
module-owned instance. Ordinary encoding SHALL NOT transfer ownership of that
instance merely by attempting a first pairing.

#### Scenario: JavaScript constructs a shared object

- **GIVEN** an owned class has one valid `[JS]` constructor
- **WHEN** JavaScript evaluates `new module.CacheEntry(arguments)`
- **THEN** generated glue SHALL decode the arguments and call that C#
  constructor directly
- **AND** the returned object SHALL inherit from
  `module.CacheEntry.prototype`
- **AND** it SHALL carry an active private registry token for the new managed
  instance
- **AND** encoding that managed instance later SHALL return the same
  JavaScript object by strict equality

#### Scenario: Shared-object constructor is invalid

- **GIVEN** a shared-object class declares multiple `[JS]` constructors, an
  inaccessible attributed constructor, an explicit `[JS(name)]` constructor
  name, or an otherwise unsupported constructor shape
- **WHEN** the generator analyzes the class
- **THEN** it SHALL report `EXPOJSI022`
- **AND** it SHALL NOT expose a JavaScript constructor for that class

#### Scenario: Constructor parameter has no codec

- **GIVEN** a valid-shaped `[JS]` constructor has a parameter without a
  compile-time decode codec
- **WHEN** the generator analyzes the constructor
- **THEN** it SHALL report `EXPOJSI023` naming the constructor, parameter, and
  unsupported type
- **AND** it SHALL NOT emit dynamic conversion or runtime reflection

#### Scenario: Constructor registration fails partway

- **GIVEN** an attributed `[JS]` constructor has returned a managed instance
  and generated construction has created some but not all pairing state
- **WHEN** a later construction or registration operation throws
- **THEN** all temporary wrappers and partial registry state SHALL be released
- **AND** no entry SHALL resolve for the failed construction
- **AND** the managed instance SHALL be marked terminal and `OnRelease` SHALL
  run exactly once outside registry and weak-wrapper locks
- **AND** any leaked reference to that instance SHALL fail later encoding under
  the no-repairing rule
- **AND** a later valid construction SHALL not encounter stale ownership or
  duplicate-registration state

#### Scenario: Authored constructor fails before returning

- **GIVEN** argument decoding or the attributed C# constructor throws before it
  returns a managed instance
- **WHEN** generated construction reports the failure to JavaScript
- **THEN** it SHALL dispose every temporary wrapper
- **AND** it SHALL NOT fabricate a terminal managed instance or registry entry

### Requirement: Shared-Object Members Are Generated On The Prototype

`[JS]` SHALL support instance methods and instance accessor properties on an
authored shared-object class. Generated methods SHALL support the existing
synchronous and `Task`/`Task<T>` asynchronous forms. Generated properties
SHALL follow the existing readable-getter and optional public/internal ordinary
setter rules. Static, generic, indexed, setter-only, init-only, or inaccessible
members SHALL be rejected.

Implicit method and property names SHALL lowercase only the first authored C#
character invariantly. Explicit `[JS(name)]` member names SHALL be exported
verbatim. Generated members SHALL be installed on the class prototype, not as
per-instance own functions or accessors. `release`, `constructor`, and
`__proto__` SHALL be reserved on generated shared-object prototypes. Authored
members SHALL NOT replace or shadow those lifetime and prototype surfaces.

Each invocation SHALL resolve its JavaScript receiver through the owning
context's registry, validate the expected managed class, call the authored
member directly, and encode its result with compile-time codecs. A foreign,
wrong-class, released, or torn-down receiver SHALL fail with a catchable
JavaScript error before authored code runs.

#### Scenario: Method and property names use current rules

- **GIVEN** a shared object declares `[JS] GetSize`, `[JS("ResetNow")] Reset`,
  and `[JS] IsReady`
- **WHEN** generated registration installs its prototype
- **THEN** JavaScript SHALL receive `getSize`, `ResetNow`, and `isReady`
- **AND** it SHALL NOT receive PascalCase aliases for implicit names

#### Scenario: Shared-object method is invoked

- **GIVEN** JavaScript calls a generated prototype method with an active
  shared-object receiver
- **WHEN** receiver and parameter decoding succeeds
- **THEN** generated glue SHALL call the original managed instance directly
- **AND** it SHALL encode the result through its compile-time codec
- **AND** it SHALL NOT use reflection, dynamic invocation, JSON, or
  `object?[]` as the normal argument path

#### Scenario: Shared-object property is accessed

- **GIVEN** a shared object declares a valid `[JS]` accessor property
- **WHEN** JavaScript reads or writes the generated prototype accessor
- **THEN** the getter or setter SHALL operate directly on the registry-resolved
  managed instance
- **AND** a missing or inaccessible setter SHALL produce a read-only
  descriptor
- **AND** codec failure SHALL occur before the authored setter runs

#### Scenario: Shared-object member is invalid

- **GIVEN** a `[JS]` member has an unsupported shape or parameter, return, or
  property type, or `[Event]` is declared on a shared object in this change
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report `EXPOJSI023` with the member and reason
- **AND** it SHALL NOT silently skip the member or emit a dynamic fallback

#### Scenario: Shared-object JavaScript member name conflicts

- **GIVEN** generated members resolve to the same JavaScript name or a member
  resolves to the reserved name `release`, `constructor`, or `__proto__`
- **WHEN** the generator analyzes the class
- **THEN** it SHALL report `EXPOJSI025`
- **AND** it SHALL NOT resolve the conflict by declaration order

#### Scenario: Prototype infrastructure name is reserved

- **GIVEN** a `[JS]` method or property explicitly or implicitly resolves to
  `release`, `constructor`, or `__proto__`
- **WHEN** the generator builds the shared-object prototype model
- **THEN** it SHALL report `EXPOJSI025` naming the reserved surface
- **AND** generated prototype installation SHALL retain its lifetime and
  prototype infrastructure unchanged

### Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity

The generator SHALL provide compile-time codecs for each valid, owned, sealed
authored shared-object type used directly as a method parameter, method return,
constructor parameter, or property type, with or without a nullable annotation.
The codec SHALL be bound to the current `DotnetRuntimeContext` and exact managed
class. `SharedObject`, `SharedRef<T>`, and other polymorphic base types SHALL
NOT be generated-boundary codec types; an authored abstract base is the one
decode-only exception (see "Authored Abstract Bases Decode As Parameters") and
SHALL NOT be an encode-side codec type.

Decoding SHALL read the private NativeState token through the existing
registry, require an active entry owned by the current context, validate the
exact expected managed runtime type, and return the original managed instance.
Assignable base/derived compatibility SHALL NOT select an exact codec or a
prototype. An authored abstract base is decoded only through
`SharedObjectBaseCodec<TBase>` (see "Authored Abstract Bases Decode As
Parameters"). Decoding SHALL NOT construct, clone, or substitute a managed
object.

Encoding SHALL ask the existing registry for the live JavaScript counterpart.
For an unpaired, unreleased managed instance, it SHALL create one JavaScript
object with the generated class prototype, attach one private registry token,
and add one entry only after verifying that the instance's runtime type exactly
matches the codec's sealed authored type. For an active paired instance, it
SHALL return a newly owned wrapper for the same JavaScript object. A released
instance SHALL never be paired again. Cross-context, cross-runtime, or
base/derived pairing SHALL fail loudly.

If first pairing fails while encoding a pre-existing module-owned instance,
generated/runtime glue SHALL dispose every partial registration and owned
wrapper but SHALL NOT mark that instance terminal or invoke `OnRelease`.
Ownership remains with the module author, and a later pairing attempt MAY be
made. This rollback SHALL not leave an entry, NativeState association, or
released-instance marker.

The registry entry SHALL continue to retain only managed lifetime state, its
NativeState state, and an opaque `JavaScriptWeakObject`. Generated conversion
SHALL dispose or transfer every ordinary object, function, value, prototype,
and scoped wrapper before returning.

#### Scenario: Managed instance is encoded twice

- **GIVEN** an unreleased managed shared-object instance is encoded twice in
  one runtime context while its JavaScript counterpart remains live
- **WHEN** JavaScript compares both results
- **THEN** they SHALL be strictly equal
- **AND** the registry SHALL contain one entry for that managed instance

#### Scenario: JavaScript object is decoded

- **GIVEN** JavaScript passes an active paired object to a generated parameter
  expecting its authored shared-object type
- **WHEN** the generated codec decodes it
- **THEN** it SHALL return the exact original managed instance
- **AND** it SHALL NOT allocate another instance or registry entry

#### Scenario: Shared-object type does not match

- **GIVEN** JavaScript passes a foreign object or a paired object of another
  authored shared-object class, including a base/derived mismatch
- **WHEN** a generated shared-object codec decodes it
- **THEN** decoding SHALL fail with a catchable JavaScript error
- **AND** authored method or property code SHALL NOT run

#### Scenario: Encoded runtime type does not match exactly

- **GIVEN** generated code attempts to encode a value whose runtime type is not
  exactly the sealed attributed type selected by the codec
- **WHEN** the codec validates the value
- **THEN** encoding SHALL fail before creating or looking up a registry pair
- **AND** it SHALL NOT select a base or derived prototype
- **AND** it SHALL NOT terminally release the caller-owned value

#### Scenario: First pairing of a pre-existing instance fails

- **GIVEN** a module-owned, unreleased shared-object instance existed before an
  ordinary generated return or property encoding began
- **WHEN** its first pairing fails after creating partial state
- **THEN** generated/runtime glue SHALL remove and dispose all partial pairing
  state and surface the conversion failure
- **AND** ownership SHALL remain with the module author
- **AND** the instance SHALL remain unreleased, `OnRelease` SHALL not run, and a
  later pairing attempt MAY proceed

#### Scenario: Shared-object conversion follows terminal release

- **GIVEN** a shared-object entry has reached terminal release
- **WHEN** generated code encodes its managed instance or decodes its stale
  JavaScript object
- **THEN** conversion SHALL fail loudly
- **AND** it SHALL NOT create a replacement pair or invoke `OnRelease` again

#### Scenario: Span function returns a shared object
- **GIVEN** a synchronous `[JS]` method has one `Span<byte>` or
  `ReadOnlySpan<byte>` parameter and returns a valid `[ExpoSharedObject]` class
- **WHEN** the method is generated
- **THEN** the return encode SHALL receive the current `DotnetRuntimeContext`
- **AND** the generated source SHALL compile

### Requirement: Authored Abstract Bases Decode As Parameters

A top-level, non-generic, abstract class whose base chain reaches
`SharedObject`, that is not `SharedObject` or `SharedRef<T>` itself, and that
carries no `[ExpoSharedObject]` attribute, SHALL be accepted as a parameter type
of generated module methods and of supported shared-object constructors and
methods. It SHALL select `SharedObjectBaseCodec<TBase>` with the descriptor
`StaticHelper`, `DecodeOnly`, requiring `DotnetRuntimeContext`, and `Borrowed`.
Decoding SHALL resolve the registry entry exactly as the exact codec does and
SHALL accept the managed instance when it is an instance of `TBase`; it SHALL
NOT construct, clone, or substitute an instance. The codec has no `Release`: a
shared object is registry-owned, never a leaf holder. The same type as a
return, async result, or property, with a nullable annotation, nested,
generic, or inside a composed codec SHALL report `EXPOJSI023` with a reason
that names the rule. A concrete non-sealed class SHALL keep its existing
diagnostic.

#### Scenario: Base parameter receives any generated subclass
- **GIVEN** an abstract `PathBase : SharedObject` with generated sealed
  subclasses `FileEntry` and `DirEntry`, a module method
  `string Describe(PathBase path)`, an async method
  `Task<string> DescribeLater(PathBase path)`, and a shared-object method
  `string Compare(PathBase other)` and constructor
  `DirEntry(string name, PathBase parent)`
- **WHEN** JavaScript passes a `FileEntry`, a `DirEntry`, and a JavaScript
  subclass instance of `FileEntry`
- **THEN** authored code SHALL receive the original managed instance each
  time, typed as `PathBase`
- **AND** no pairing step SHALL be observed during decode

#### Scenario: Existing failures stay
- **GIVEN** the `PathBase` parameter
- **WHEN** JavaScript passes a plain object, a primitive, `null`,
  `undefined`, a released instance, an instance from another context, or an
  instance of a shared class that does not derive from `PathBase`
- **THEN** the call SHALL fail before authored code runs
- **AND** the message for the unrelated class SHALL be the base codec's
  `is not a '...PathBase' instance` text

#### Scenario: Non-parameter positions and the nullable form stay rejected
- **GIVEN** `PathBase` as a return type, a `Task<PathBase>` result, a
  property type, a `PathBase?` parameter, a nested `PathBase` inside a
  list, dictionary, record, callback, or typed event payload, a generic or
  nested abstract base, and `SharedObject` and `SharedRef<X>` parameters
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the reason
  the design names, and SHALL NOT emit the member

#### Scenario: Concrete non-sealed classes keep their diagnostic
- **GIVEN** a non-abstract, non-sealed class deriving from `SharedObject`
  used as a parameter, with and without `[ExpoSharedObject]`
- **WHEN** the generator runs
- **THEN** it SHALL report the existing `EXPOJSI023` reasons

### Requirement: Public SharedObject Release Is Exactly Once

`SharedObject` SHALL expose a protected virtual `OnRelease()` hook. All
terminal sources, including JavaScript `release()`, deterministic JavaScript
collection, and `DotnetRuntimeContext` teardown, SHALL converge on the existing
registry terminal path. The first source SHALL make the instance terminal and
invoke `OnRelease` exactly once outside registry and weak-wrapper locks. Later
terminal sources and repeated JavaScript `release()` calls SHALL be no-ops.

`OnRelease` SHALL run synchronously on whichever thread wins terminal release.
Authors SHALL NOT assume JavaScript, UI, or scheduler thread affinity. The hook
MAY release thread-safe managed or native resources. It SHALL NOT access JSI,
use bridge wrappers, enter or schedule JavaScript runtime work, block waiting
for runtime work, or resurrect/re-pair the instance.

An `OnRelease` failure SHALL NOT undo terminal state or prevent later context
owners from being cleaned up. Explicit JavaScript release SHALL surface a
catchable JavaScript error; context teardown SHALL include the failure in its
aggregate-and-continue result. Collection-triggered cleanup SHALL not allow an
exception to escape through a native release callback.

#### Scenario: JavaScript explicitly releases an object

- **GIVEN** JavaScript holds an active generated shared-object instance
- **WHEN** it calls `release()` one or more times
- **THEN** the first call SHALL terminally detach the managed pairing and call
  `OnRelease` once
- **AND** later calls SHALL be no-ops
- **AND** later generated method or property access SHALL fail before authored
  member code runs

#### Scenario: JavaScript collection releases an object

- **GIVEN** an active generated shared object has no strong JavaScript owner
- **WHEN** deterministic collection releases its private NativeState
- **THEN** the registry SHALL reach the same terminal path and call
  `OnRelease` once
- **AND** cleanup SHALL use no JSI wrapper, access frame, blocking runtime
  operation, or raw managed pointer

#### Scenario: Runtime teardown releases live shared objects

- **GIVEN** a `DotnetRuntimeContext` still owns active public shared-object
  entries
- **WHEN** context teardown drains its shared-object registry first
- **THEN** every entry SHALL become terminal and attempt `OnRelease` once
- **AND** cleanup SHALL continue after any hook failure
- **AND** later use SHALL fail without touching invalid runtime state

### Requirement: SharedRef Is A Non-Owning SharedObject

`Expo.ModulesCore` SHALL expose a public derivable `SharedRef<T>` that extends
`SharedObject`, receives a `T` in its constructor, and exposes that same value
through a read-only `Ref` property. It SHALL hold a strong managed
reference to `T` for the lifetime of the `SharedRef<T>` instance.

`SharedRef<T>` SHALL NOT infer ownership from `T`, test whether `T` implements
`IDisposable` or `IAsyncDisposable`, or dispose `T` automatically. Its default
release behavior SHALL be a no-op for `T`. A subclass that owns the resource
MAY override `OnRelease` and perform allowed cleanup explicitly.

`SharedRef<T>` itself SHALL be a managed carrier base, not a generated codec
surface. A generated parameter, return, constructor parameter, or property
SHALL use a concrete, sealed, non-generic `[ExpoSharedObject]` subclass of
`SharedRef<T>`. Direct generated-boundary use of `SharedRef<T>`, whether open or
constructed, SHALL report `EXPOJSI023` instead of selecting a polymorphic
prototype.

#### Scenario: Non-owning SharedRef is released

- **GIVEN** a `SharedRef<T>` carries a disposable value supplied by another
  owner
- **WHEN** JavaScript release, collection, or runtime teardown releases the
  shared ref
- **THEN** its inherited lifetime SHALL become terminal exactly once
- **AND** `SharedRef<T>` SHALL NOT call `Dispose` or `DisposeAsync` on `T`

#### Scenario: Owning subclass releases its resource

- **GIVEN** a concrete sealed attributed subclass of `SharedRef<T>` explicitly
  owns its carried resource
- **WHEN** terminal release invokes the subclass's `OnRelease`
- **THEN** the subclass MAY release that resource once without JSI or runtime
  work
- **AND** repeated JavaScript release and later teardown SHALL not repeat the
  hook

#### Scenario: Concrete SharedRef subclass crosses the boundary

- **GIVEN** a sealed non-generic `[ExpoSharedObject]` class derives from
  `SharedRef<NativeImage>`
- **WHEN** a generated member uses that concrete class as a parameter, return,
  constructor parameter, or property type
- **THEN** the generator SHALL use that class's exact generated codec and
  prototype
- **AND** `SharedRef<NativeImage>` SHALL remain only its managed carrier base

#### Scenario: SharedRef base crosses the boundary directly

- **GIVEN** a generated member directly declares `SharedRef<T>` or a constructed
  `SharedRef<NativeImage>` as a parameter, return, constructor parameter, or
  property type
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report `EXPOJSI023`
- **AND** it SHALL NOT emit a base-type codec, reflection fallback, or
  assignability-based prototype selection

### Requirement: TypeScript Facades Expose Explicit Release

`expo-modules-dotnet` SHALL export a real JavaScript class value named
`DotnetSharedObject` for TypeScript facade heritage clauses. Its public type
surface SHALL contain `release(): void`. Direct construction of
`DotnetSharedObject` SHALL throw and explain that usable instances come from a
generated module class or a generated module return value. Native generated
instances SHALL NOT be guaranteed to satisfy `instanceof DotnetSharedObject`.

An authored TypeScript facade for a constructible class SHALL declare a class
extending `DotnetSharedObject` and type the owning module's class property as
that class constructor. A native-created-only class SHALL be represented as an
instance type returned by module methods, without a constructible module
property.

The package and module authoring guide SHALL document `release()` as the only
deterministic JavaScript cleanup API in this change. Examples SHALL use
`try/finally` when deterministic cleanup is required. They SHALL state that
release is idempotent and that any later native member access fails.

#### Scenario: TypeScript facade declares a constructible class

- **GIVEN** an example module exposes a valid generated shared-object
  constructor
- **WHEN** its TypeScript facade models that module
- **THEN** the facade SHALL provide a constructible class type extending
  `DotnetSharedObject`
- **AND** instances SHALL expose the generated members and `release()`

#### Scenario: Deterministic JavaScript cleanup is documented

- **GIVEN** an example acquires a resource-owning shared object
- **WHEN** documentation demonstrates bounded lifetime
- **THEN** it SHALL call `release()` from `finally`
- **AND** it SHALL NOT require `Symbol.dispose` or JavaScript `using` syntax

#### Scenario: Symbol disposal is considered later

- **GIVEN** a future change proposes `Symbol.dispose` support
- **WHEN** that change is designed
- **THEN** it SHALL separately review the package's minimum TypeScript version,
  emitted library types, and supported JavaScript runtimes
- **AND** this change SHALL remain compatible with explicit `release()`

### Requirement: SharedObject Bindings Are Fully Generated And Portable

Shared-object discovery, validation, construction, member dispatch, and codec
selection SHALL be build-time generated and NativeAOT-compatible. The runtime
path SHALL use direct calls, typed codecs, context-owned generated host-function
registrations, and the existing managed registry and `Expo.JSI` primitives.
Private generated record-codec type names SHALL derive from fully qualified
record identity so distinct payload records with the same simple name do not
share or suppress one another's codec.
It SHALL NOT use runtime reflection, dynamic invocation, JSON conversion, raw
JSI layouts, a new C ABI entry, or a platform-specific dependency.

#### Scenario: Same simple-name event records keep distinct codecs

- **GIVEN** a module event and a shared-object event use distinct payload
  records with the same simple type name in different namespaces
- **WHEN** the generator emits and runs both dispatch paths
- **THEN** each path SHALL encode with the codec for its fully qualified payload
  type
- **AND** both generated codec declarations SHALL compile in one provider

#### Scenario: Generated provider registers shared objects

- **GIVEN** a library compilation contains owned authored shared-object classes
- **WHEN** its generated provider registers with a `DotnetRuntimeContext`
- **THEN** generated code SHALL use context-owned host-function registrations
  and direct typed glue
- **AND** the same generated source SHALL remain valid for HostFXR and
  NativeAOT loading

#### Scenario: Generator diagnostics are verified

- **GIVEN** invalid declarations exercise `EXPOJSI021` through `EXPOJSI025`
- **WHEN** generator tests compile each invalid source shape
- **THEN** each diagnostic SHALL be asserted independently with its relevant
  source location and message arguments
- **AND** rejected declarations SHALL not produce secondary generated-C#
  errors

#### Scenario: Public identity and lifetime behavior is verified

- **GIVEN** the public shared-object surface is implemented
- **WHEN** the Hermes-backed ModulesCore suite runs
- **THEN** it SHALL prove JavaScript construction and prototype identity,
  managed-to-JavaScript strict identity, JavaScript-to-managed original
  instance lookup, explicit release, deterministic collection, context
  teardown, use after release, exact-type encode/decode rejection, and
  `SharedRef<T>` non-ownership
- **AND** it SHALL prove that post-constructor pairing failure terminally
  releases the returned instance exactly once while failed pairing of a
  pre-existing module-owned instance does not release it
- **AND** generator tests SHALL cover a non-sealed attributed class, direct
  `SharedRef<T>` boundary use, a valid sealed concrete `SharedRef<T>` subclass,
  collisions with each effective module namespace category, duplicate
  native-created-only class names, and each of the `release`, `constructor`,
  and `__proto__` prototype reservations
- **AND** existing internal `SharedObjectRegistryTests` SHALL remain unchanged
  and pass
