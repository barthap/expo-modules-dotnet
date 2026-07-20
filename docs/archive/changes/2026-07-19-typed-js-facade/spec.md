# Typed JavaScript Facade Base Classes

## Goal

Provide a portable, typed JavaScript facade surface for .NET-backed Expo
modules. Module authors can declare a native module as a subclass of
`DotnetModule<TEventsMap>` and get the event API already installed by the
managed module runtime, without importing React Native or `expo-modules-core`
at runtime.

This delta applies to the JavaScript facade only. It does not change native
installation, the generated C# bindings, or the managed event-emitter
implementation.

## Scope

### In scope

- Export `EventsMap`, `EventSubscription`, `DotnetEventEmitter`, and
  `DotnetModule` from `expo-modules-dotnet`.
- Provide a runtime value for both classes so consumer code can write
  `declare class MyModule extends DotnetModule<MyEvents> { ... }`.
- Model the existing module event-prototype API with strongly typed event
  names, listener parameters, and emitted argument tuples.
- Migrate `example-module` to the new facade type without changing its public
  functions, public data types, or `EventSubscription` re-export.
- Add runtime and compile-time TypeScript coverage, then document the author
  pattern and merge this delta into the living module-boundary spec.

### Out of scope

- Any change below the JavaScript facade: the C ABI, native bridge,
  `Expo.JSI`, `Expo.ModulesCore`, generator, and the installed event-emitter
  prototype remain unchanged.
- React Native or `expo-modules-core` imports in the new facade implementation.
- Changing `requireDotnetModule` callers that use plain object types.
- Generating event maps from C# declarations. Plan 014 may later improve the
  authoring story around typed `[Event]` members, but it does not change this
  facade contract.

## Accepted Design

### Exported event types

`expo-modules-dotnet` SHALL export these public types:

```ts
export type EventsMap = Record<string, (...args: any[]) => void>;

export type EventSubscription = {
  remove(): void;
};
```

An events map's keys are the supported event names and each value is the exact
listener signature for that event. `EventSubscription.remove()` is the public,
convenient way to release a listener registration.

### Runtime-value classes

`DotnetEventEmitter<TEventsMap extends EventsMap = Record<never, never>>`
and `DotnetModule<TEventsMap extends EventsMap = Record<never, never>>` SHALL
be real exported JavaScript classes. `DotnetModule` SHALL extend
`DotnetEventEmitter`.

Their constructors SHALL always throw a clear error explaining that .NET module
objects are created by the native module registry and must be obtained through
`requireDotnetModule`. The classes exist as runtime values for TypeScript
heritage clauses and as typed facade bases; they do not construct usable module
objects or provide a JavaScript event-emitter implementation.

Native module objects are created independently by the registry and use the
managed-installed prototype. Consumers SHALL NOT rely on a returned module
being an `instanceof DotnetModule` or `DotnetEventEmitter`. There is no
prototype-identity guarantee between the facade classes and native objects.

### Typed emitter API

`DotnetEventEmitter` SHALL provide these five author-facing typed methods,
matching the existing native prototype's names and arities:

```ts
addListener<EventName extends keyof TEventsMap>(
  eventName: EventName,
  listener: TEventsMap[EventName]
): EventSubscription;

removeListener<EventName extends keyof TEventsMap>(
  eventName: EventName,
  listener: TEventsMap[EventName]
): void;

removeAllListeners(eventName: keyof TEventsMap): void;

emit<EventName extends keyof TEventsMap>(
  eventName: EventName,
  ...args: Parameters<TEventsMap[EventName]>
): void;

listenerCount<EventName extends keyof TEventsMap>(eventName: EventName): number;
```

`emit` SHALL be documented as primarily a runtime-internal operation. It is
typed because the installed prototype exposes it, not as an encouragement for
ordinary JavaScript facades to emit native module events. Listener calls remain
synchronous from the JavaScript prototype's perspective and listener return
values are ignored, as in the existing runtime.

The runtime also installs `removeSubscription(subscription)` with arity one as
a legacy compatibility helper. The facade SHALL deliberately omit that helper
from `DotnetEventEmitter`; it is not a sixth modern author-facing event API.
Existing callers that explicitly model the legacy method with a plain-object
type remain compatible because `requireDotnetModule<T>` stays unconstrained.
New public code SHOULD call `subscription.remove()` instead. The facade SHALL
also omit `startObserving` and `stopObserving`: those are managed module hooks,
not JavaScript author-facing event operations.

### Module lookup compatibility

`requireDotnetModule<T>(name: string): T` SHALL keep its unconstrained generic
signature and existing installer/lookup behavior. Existing plain-object facade
types remain supported. `DotnetModule` is an opt-in authoring base, not a new
runtime registration constraint.

### Example-module migration

The example facade SHALL replace its local object type with an internal
`declare class ExampleModuleType` that extends `DotnetModule` using an
`onStatus` events map, then preserve its existing type export with
`export type ExampleModule = ExampleModuleType`. It SHALL NOT use
`export declare class`, because that would advertise a runtime class value that
the facade does not emit. Its existing functions, input/output types,
property-name mapping, and behavior SHALL stay unchanged. It SHALL re-export
`EventSubscription` from `expo-modules-dotnet` instead of declaring a duplicate
local type, preserving downstream imports.

### Documentation

All newly exported values, types, constructors, and author-facing methods
SHALL have TSDoc that explains their intended use and the constructor/
`instanceof` limitation. The module authoring guide and the living
`modules-core-boundary` spec SHALL show the `declare class ... extends
DotnetModule<...>` pattern and subscription cleanup via `.remove()`.

Plan 014's future typed `[Event]` work SHALL update the authoring examples if
it can derive or otherwise improve the events-map declaration. Until then,
facades declare the map explicitly and this API remains stable.

## Acceptance Scenarios

### Scenario: Consumer declares a typed native module

- **GIVEN** a consumer declares
  `declare class Fixture extends DotnetModule<FixtureEvents>`
  where `FixtureEvents` defines `onFoo(payload: string): void`
- **WHEN** it calls `requireDotnetModule<Fixture>('Fixture')`
- **THEN** `addListener('onFoo', listener)` accepts a string listener and
  returns `EventSubscription`
- **AND** the returned subscription exposes `remove(): void`
- **AND** the module's declared methods remain available on the returned type

### Scenario: Type checking rejects an invalid event contract

- **GIVEN** the same typed module facade
- **WHEN** a facade uses an unknown event name, a listener with an incompatible
  argument list, or invalid arguments for `emit`
- **THEN** TypeScript SHALL reject the code through maintained negative type
  tests
- **AND** valid event names, listeners, and emitted tuples SHALL compile through
  maintained positive type tests
- **AND** those fixtures SHALL be included in an executed `tsc` configuration;
  placing them only under the currently excluded `src/__tests__` tree is not
  sufficient

### Scenario: Classes are usable in a heritage clause but not constructible

- **GIVEN** a consumer imports `DotnetModule` or `DotnetEventEmitter`
- **WHEN** TypeScript evaluates a `declare class ... extends` clause
- **THEN** the exported class value SHALL make the heritage clause valid
- **WHEN** JavaScript calls either constructor directly
- **THEN** it SHALL throw an error that directs the caller to native module
  lookup
- **AND** tests SHALL NOT assert that a native registry object is an instance
  of either facade class

### Scenario: Legacy subscription helper remains a runtime detail

- **GIVEN** a native module object with the installed event prototype
- **WHEN** existing compatibility code invokes `removeSubscription` with one
  argument
- **THEN** the runtime behavior remains unchanged
- **AND** `DotnetEventEmitter` SHALL NOT declare the legacy helper
- **AND** existing callers MAY continue to model it explicitly with a
  plain-object type passed to `requireDotnetModule<T>`
- **AND** public examples use `EventSubscription.remove()` instead

### Scenario: Example facade preserves its public surface

- **GIVEN** a caller imports public functions, data types, or
  `EventSubscription` from `example-module`
- **WHEN** the example facade adopts `DotnetModule`
- **THEN** those imports and function signatures remain compatible
- **AND** `import type { ExampleModule } from 'example-module'` SHALL remain
  valid without advertising an `ExampleModule` runtime value
- **AND** the local duplicate `EventSubscription` declaration no longer exists

### Scenario: Facade stays portable

- **GIVEN** `expo-modules-dotnet` builds its JavaScript facade
- **WHEN** the new base-class module is loaded
- **THEN** it SHALL not import React Native or `expo-modules-core`
- **AND** only the existing installer path continues to touch React Native

## Verification Requirements

- Adapter runtime tests SHALL confirm direct construction of both classes
  throws and that the classes are exported values.
- Maintained compile-time tests SHALL cover positive typed listener/module
  usage and negative event-name, listener, and emit-argument cases using
  `@ts-expect-error` or equivalent checked assertions. They SHALL live in a
  source tree included by the adapter's executed TypeScript configuration, for
  example `src/__type_tests__/`, or use a dedicated type-test configuration and
  script that is executed by verification. Vitest execution alone SHALL NOT be
  treated as compile-time coverage.
- Adapter tests and package typecheck SHALL pass, as shall the mobile app
  typecheck after the example migration.
- The canonical managed suite, formatting check, whitespace check, and docs
  privacy scan SHALL pass before this delta is merged into the living specs.
