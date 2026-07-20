# camelCase JavaScript Names And `[JS]` Properties

## Goal

Make the generated C# module contract idiomatic and predictable on the
JavaScript side. Unnamed `[JS]` methods and generated record fields use the
repository's lower-camel mapping, and an annotated instance property becomes a
JavaScript accessor property with the same readable/writable shape as its C#
declaration.

This follows the Expo Modules 2.0 authoring direction: ordinary annotated
methods and properties are the module contract. It deliberately keeps the
generated C# path direct, compile-time typed, and NativeAOT-compatible.

## Scope

This change updates `[JS]` member discovery and generated record codecs in
`Expo.ModulesCore.Generator`, the generated-function runtime helper needed to
install JavaScript accessors, the example module and typed facade, generator
and Hermes-backed tests, the module-authoring guide, and the modules-core
living spec.

It does not change module-name derivation, add a native ABI entry, change
event declaration syntax, add custom record-field naming, or implement a
`JavaScriptObject` codec. `JavaScriptValue` remains the existing advanced
module convertible. A `JavaScriptObject` codec is a separate future optional
module-convertibles slice.

## Accepted Design

`[JS]` continues to be the explicit opt-in. A parameterless `[JS]` maps an
authored C# member name with the generator's `LowerCamel` convention: it
lowercases the first character invariantly (`Add` becomes `add`,
`GetMessageAsync` becomes `getMessageAsync`). `[JS("ExactName")]` is an
escape hatch and exports `ExactName` verbatim.

The generator treats a record's C# property name and its JavaScript field name
as distinct model fields. Generated encode and decode use only the lower-camel
JavaScript field name. Decode has no PascalCase compatibility fallback: a
contract mismatch is handled only through the lower-camel field's normal codec
semantics instead of silently reading the stale field.

`[JS]` applies to instance properties as well as methods. The generated module
object receives an own accessor descriptor installed through
`Object.defineProperty`; the descriptor is enumerable and configurable. Its
getter is a zero-argument direct host function. A writable property has a
one-argument direct host-function setter. A get-only property has no `set`
descriptor member, so strict-mode assignment uses ordinary JavaScript
read-only semantics and throws `TypeError`.

The module object's existing native/event prototype is unchanged. The
accessor's `get` and `set` functions are direct descriptor values, not
prototype methods, scheduled work, or a new native ABI feature.

## Delta Requirements

### MODIFIED Requirement: Generated Sync Function Naming Is JavaScript-Native

Generated synchronous and asynchronous `[JS]` method names SHALL default to
the lower-camel form of the authored C# method name. An explicit `[JS(name)]`
name SHALL remain verbatim.

#### Scenario: Implicit method name is lower camel case

- **GIVEN** a module declares `[JS] public double Add(double a, double b)`
- **WHEN** generated registration installs the method
- **THEN** JavaScript SHALL receive an `add` function
- **AND** it SHALL NOT receive an `Add` compatibility alias

#### Scenario: Explicit method name is preserved

- **GIVEN** a module declares `[JS("ExactName")] public void Add()`
- **WHEN** generated registration installs the method
- **THEN** JavaScript SHALL receive `ExactName` verbatim
- **AND** it SHALL NOT transform the explicit name

### MODIFIED Requirement: Generated Record Codecs Use JavaScript Field Names

Generated record codecs SHALL model the authored C# property name separately
from its JavaScript field name. They SHALL lower-camel every supported record
property name for JavaScript encoding and decoding, while direct C# member
access continues to use the original C# property name. Generated decode SHALL
never read a PascalCase compatibility field. When the lower-camel field is
missing, the field's existing codec SHALL decide whether `undefined` is
rejected or decoded to a value such as `null`.

#### Scenario: Record is encoded for JavaScript

- **GIVEN** a supported record has C# properties `Name`, `Age`, and `Summary`
- **WHEN** generated glue encodes the record
- **THEN** the JavaScript object SHALL have `name`, `age`, and `summary` own
  properties
- **AND** generated C# code SHALL read `value.Name`, `value.Age`, and
  `value.Summary`

#### Scenario: Record is decoded from JavaScript

- **GIVEN** a supported record expects a `Name` property in C#
- **WHEN** generated glue decodes its JavaScript input
- **THEN** it SHALL read only the `name` JavaScript property
- **AND** it SHALL not probe, read, or fall back to `Name`

#### Scenario: Stale PascalCase record input is passed to a required field

- **GIVEN** a caller passes `{ Name: "Ada" }` for a required string field
  expecting `name`
- **WHEN** generated glue decodes that argument
- **THEN** decoding SHALL fail through the normal required-field codec path
- **AND** the failure SHALL surface as the normal catchable JavaScript error

#### Scenario: Missing lower-camel nullable field follows its codec

- **GIVEN** a caller supplies only a stale PascalCase field for a nullable
  record property
- **WHEN** generated glue reads the absent lower-camel field as `undefined`
- **THEN** it SHALL NOT read the PascalCase field
- **AND** the existing nullable codec MAY decode `undefined` as `null`

### ADDED Requirement: `[JS]` Instance Properties Are Accessor Properties

`[JS]` SHALL support valid authored instance properties. A valid property MUST
have a public or internal getter, MUST NOT be static or an indexer, MUST NOT
have an `init` accessor, and MUST have a compile-time supported codec for its
property type. A public or internal ordinary setter makes the JavaScript
property writable; an absent or inaccessible setter makes it read-only.

The JavaScript property name SHALL follow the same name rule as methods:
lower-camel by default and explicit `[JS(name)]` verbatim.

#### Scenario: Read-write property is installed

- **GIVEN** a module declares `[JS] public bool Ready { get; set; }`
- **WHEN** generated registration installs its members
- **THEN** the module object SHALL have an own `ready` accessor property
- **AND** the descriptor SHALL be enumerable and configurable
- **AND** its getter SHALL have arity zero
- **AND** its setter SHALL have arity one
- **AND** reading and assigning `module.ready` SHALL directly read and update
  the authored property

#### Scenario: Getter-only property is read-only

- **GIVEN** a module declares `[JS] public bool Ready => isReady`
- **WHEN** generated registration installs its members
- **THEN** the module object SHALL have an own enumerable, configurable `ready`
  accessor descriptor with `get` and without `set`
- **AND** strict-mode assignment to `module.ready` SHALL throw `TypeError`
- **AND** the assignment SHALL not invoke authored module code

#### Scenario: Inaccessible setter is read-only

- **GIVEN** a module declares a `[JS]` property with a public or internal
  getter and a private setter
- **WHEN** generated registration installs its members
- **THEN** JavaScript SHALL see a getter-only accessor
- **AND** the private setter SHALL not become callable from JavaScript

#### Scenario: Explicit property name is preserved

- **GIVEN** a module declares `[JS("isReady")] public bool Ready { get; }`
- **WHEN** generated registration installs its members
- **THEN** JavaScript SHALL expose `isReady` verbatim
- **AND** it SHALL not also expose `ready`

#### Scenario: Property getter throws

- **GIVEN** a generated JavaScript getter invokes an authored property getter
  that throws a managed exception
- **WHEN** JavaScript reads the property
- **THEN** the host-function boundary SHALL expose a catchable JavaScript
  `Error`

#### Scenario: Property setter receives an invalid value

- **GIVEN** a generated JavaScript setter has a compile-time codec
- **WHEN** JavaScript assigns a value the codec cannot decode
- **THEN** assignment SHALL fail through the host-function boundary as a
  catchable JavaScript `Error`
- **AND** the authored setter SHALL not run

### ADDED Requirement: Property Access Uses Generated Direct Calls

Generated accessor glue SHALL use compile-time codecs and direct C# property
access. It SHALL not use runtime reflection, dynamic invocation, thread
scheduling, or an ABI extension. Generated consumer assemblies SHALL call a
public generated-glue-only entry point in `Expo.ModulesCore`, such as a public
method on `GeneratedFunction` or a public `GeneratedProperty` helper. Any
implementation-only helper behind that cross-assembly entry point MAY remain
internal.

#### Scenario: Getter returns a module-convertible value

- **GIVEN** a generated property getter returns a type with a supported codec
- **WHEN** JavaScript reads the accessor
- **THEN** generated glue SHALL encode the result through that codec and
  transfer its owned return wrapper to the host-function bridge using the same
  rule as a synchronous `[JS]` method return

#### Scenario: Setter receives an owned module-convertible value

- **GIVEN** a generated property setter accepts a type whose codec retains an
  owned JavaScript wrapper, such as `JavaScriptValue`
- **WHEN** JavaScript assigns that property
- **THEN** generated glue SHALL decode and own the value for the synchronous
  setter invocation
- **AND** it SHALL dispose the decoded wrapper after the authored setter
  returns or throws
- **AND** authored code SHALL not dispose or store the invocation-owned wrapper
- **AND** authored code that needs the value after the setter returns SHALL
  store an explicit retained copy

### ADDED Requirement: Accessor Installations Have Explicit Lifetime Ownership

The runtime context SHALL own every generated property getter/setter callback
through `GeneratedHostFunctionRegistration`. Accessor installation may use
temporary owned JavaScript object, function, and value wrappers only while
constructing and synchronously passing the descriptor to
`Object.defineProperty`; it SHALL dispose those temporary wrappers immediately
after that call returns. The JavaScript descriptor/property retains its host
function values independently.

Callback `this` values and arguments are scoped references and SHALL not
escape the host-function callback. The configurable descriptor SHALL let later
registration replace the accessor used by ordinary property lookup. A
previously captured accessor function MAY remain callable until its owning
runtime context is disposed, matching existing generated method callback
semantics; its registration SHALL remain bounded by that context rather than
becoming an unowned leak. Runtime-context teardown SHALL invalidate every old
and current registration exactly once, so later calls through captured
accessor functions fail safely instead of calling disposed managed state.

#### Scenario: Descriptor installation completes

- **GIVEN** generated registration creates a property descriptor and its host
  functions
- **WHEN** `Object.defineProperty` returns synchronously
- **THEN** generated glue SHALL dispose every temporary owned wrapper used for
  the global lookup, descriptor, functions, and value conversions
- **AND** the installed JavaScript accessor SHALL remain callable because the
  JavaScript descriptor retains the host functions

#### Scenario: Descriptor installation fails

- **GIVEN** the runtime context has registered accessor callbacks and
  `Object.defineProperty` throws
- **WHEN** generated registration unwinds
- **THEN** every temporary owned JavaScript wrapper SHALL still be disposed
- **AND** every created managed callback registration SHALL remain owned and
  bounded by the runtime context until its deterministic teardown
- **AND** no registration or wrapper SHALL become an unowned leak

#### Scenario: Runtime context tears down after property installation

- **GIVEN** a runtime context owns generated property registrations
- **WHEN** the context is disposed
- **THEN** it SHALL invalidate those registrations with its other generated
  host-function registrations
- **AND** a later native release callback SHALL not double-free managed state
- **AND** later accessor use SHALL fail loudly without touching released state

#### Scenario: Module property is registered again

- **GIVEN** a generated module property is already installed in an active
  runtime context
- **WHEN** registration installs that property again
- **THEN** the configurable descriptor SHALL let ordinary property lookup use
  the replacement accessor
- **AND** any previously captured accessor function SHALL remain owned by the
  same runtime context until teardown
- **AND** teardown SHALL invalidate both old and current registrations

### MODIFIED Requirement: Unsupported Generated Members Are Build Diagnostics

Invalid property shapes, unsupported property codecs, property-involving name
collisions, and property use of reserved observing-hook names SHALL fail the
consuming compilation. The generator SHALL use the following diagnostics
instead of omitting a member, choosing one by source order, or deferring those
listed failures to runtime. Validation of other pre-existing explicit-name
inputs is unchanged by this delta.

| ID | Condition | Required diagnostic outcome |
| --- | --- | --- |
| `EXPOJSI014` | A `[JS]` property is static, indexed, lacks a public/internal getter, is setter-only, or has an `init` accessor. | Name the property and unsupported shape. |
| `EXPOJSI015` | A readable `[JS]` property's type lacks a generated codec. | Name the property and unsupported type. |
| `EXPOJSI016` | Two properties, or one property and one method, resolve to the same JavaScript name. | Name the module and duplicate JavaScript member name. |
| `EXPOJSI017` | A property resolves to a generated observing-hook name reserved by an `[Events]` module. | Name the property and reserved hook name. |

Existing method-only diagnostics SHALL remain stable: method-method duplicate
names continue to use `EXPOJSI005`, and a method using a reserved observing-hook
name continues to use `EXPOJSI004`. The final member-name check SHALL still
cover collisions between properties and methods.

#### Scenario: Method and property names collide

- **GIVEN** a module declares `[JS] void GetReady()` and
  `[JS("getReady")] bool Ready { get; }`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI016`
- **AND** it SHALL not resolve the collision by declaration order

#### Scenario: Property uses an observing-hook name

- **GIVEN** an `[Events]` module declares `[JS] bool StartObserving { get; }`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI017` because `startObserving` is reserved
- **AND** it SHALL not generate a conflicting accessor

#### Scenario: Unreadable property is annotated

- **GIVEN** a module declares `[JS] public bool Ready { private get; set; }`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI014`
- **AND** it SHALL not expose a write-only JavaScript property

#### Scenario: Unsupported property codec is annotated

- **GIVEN** a module declares a readable `[JS]` property whose type has no
  generated codec
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI015`
- **AND** it SHALL not emit reflection or dynamic conversion fallback code

## Migration

This is a deliberate pre-1.0 JavaScript contract change. Module authors shall
remove redundant explicit lower-camel method names when the implicit mapping
already yields the desired name, but keep genuinely custom explicit names.
They shall update record facade types and values to use lower-camel fields
directly, with no PascalCase translation layer.

The example facade shall retain the Plan 012 pattern:

```ts
declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  add(a: number, b: number): number;
  describeUser(user: ExampleUser): ExampleUserSummary;
  readonly ready: boolean;
}
```

The TypeScript declaration reflects the JavaScript accessor surface; it does
not make a native registry module object an instance of `DotnetModule`.

## Documentation And Acceptance Evidence

Public XML documentation for `JSAttribute` SHALL state that it applies to
methods and properties, describe implicit versus explicit names, and explain
the readable/writable property mapping. The public cross-assembly
generated-property entry point SHALL be documented as generated-glue-only API,
including callback ownership and temporary-wrapper disposal. The
module-authoring guide and living spec SHALL document the lower-camel record
contract, the absence of a PascalCase fallback, property restrictions,
strict-mode read-only behavior, and the `JavaScriptValue` ownership rule for
accessor calls.

Acceptance tests SHALL cover at least:

- implicit and explicit method naming;
- lower-camel record encode/decode and rejected PascalCase-only input for a
  required field whose codec rejects `undefined`;
- readable/writable, getter-only, inaccessible-setter, and explicit-name
  properties;
- descriptor enumerability/configurability and getter/setter arity;
- strict-mode read-only `TypeError`;
- managed getter and codec failures surfacing as catchable JavaScript errors;
- all `EXPOJSI014` through `EXPOJSI017` conditions;
- method/property collision detection;
- a `JavaScriptValue` getter returning an explicit retained copy while the
  author's original wrapper remains valid after host return;
- a `JavaScriptValue` setter proving the invocation-owned decoded wrapper is
  disposed after return while an explicit author-retained copy remains valid;
- accessor replacement, captured-old-accessor behavior, and teardown
  invalidation; and
- ownership cleanup under repeated accessor installation, invocation, and
  runtime teardown.
