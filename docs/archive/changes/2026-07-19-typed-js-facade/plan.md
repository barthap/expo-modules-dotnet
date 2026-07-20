# Typed JavaScript Facade Base Classes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export portable, typed `DotnetEventEmitter` and `DotnetModule` facade bases, migrate the example module to use them, and document the supported module-author pattern.

**Architecture:** Add one dependency-free TypeScript module containing real, non-constructible value classes and their type contracts. Native module objects continue to come from `_expoDotnet` and are not instances of these classes; the classes exist so TypeScript consumers can use them in `declare class ... extends` heritage clauses. Test runtime construction in Vitest and type contracts in an ordinary `tsc`-included fixture, then migrate the example facade without changing its public functions.

**Tech Stack:** TypeScript 6, Vitest 3, pnpm workspaces, Expo module facade package, existing Hermes-backed managed test suite.

## Global Constraints

- Keep the existing `requireDotnetModule<T>(name: string): T` unconstrained and its installer/lookup behavior unchanged; plain-object callers remain valid.
- Export real JavaScript values for `DotnetEventEmitter` and `DotnetModule`; `DotnetModule` extends `DotnetEventEmitter`, but native registry objects have no `instanceof` or prototype-identity guarantee.
- Export exactly the modern author-facing event surface: `addListener`, `removeListener`, `removeAllListeners`, `emit`, and `listenerCount`. Do not expose `removeSubscription`, `startObserving`, or `stopObserving` in the new classes.
- Match the names and argument arities installed by `managed/packages/Expo.ModulesCore/EventEmitterPrototype.cs`: 2, 2, 1, 1+, and 1 respectively.
- The new declaration module must not import `react-native` or `expo-modules-core`. The existing installer is the adapter package's only React Native touchpoint.
- Preserve `example-module` public functions, public data types, `ExampleModule` type-only import compatibility, PascalCase wire mapping, and re-export of `EventSubscription`.
- Add public TSDoc to all new exported types, classes, constructors, and author-facing methods. State why direct construction throws, why `instanceof` is unsupported, and that `emit` is primarily runtime-internal.
- Use TDD: observe the intended test failure before production implementation for every production behavior change.
- Do not change managed/native code, generated bindings, the C ABI, or other app facades.
- Before every commit, inspect the staged diff for local absolute paths, usernames, machine names, private hostnames, and machine-specific installation paths. Do not push, publish, or open a PR.

## File Map

| File | Responsibility |
| --- | --- |
| `packages/expo-modules-dotnet/src/ts-declarations/DotnetEventEmitter.ts` | Dependency-free public event-map types and real, non-constructible typed facade classes. |
| `packages/expo-modules-dotnet/src/index.ts` | Re-export the new classes/types alongside the unchanged native-module lookup helper. |
| `packages/expo-modules-dotnet/src/__tests__/index.test.ts` | Runtime proof that both exports are values and direct construction rejects. |
| `packages/expo-modules-dotnet/src/__type_tests__/dotnet-module.ts` | Positive and negative compile-time facade contract fixture, executed by the existing adapter typecheck. |
| `packages/example-module/src/index.ts` | Internal ambient native-module class, public type alias, and imported/re-exported subscription type. |
| `docs/specs/modules-core-boundary.md` | Durable module-boundary contract for the adapter facade types. |
| `docs/module-authoring-guide.md` | Module-author tutorial using the actual typed facade pattern. |
| `docs/plans/README.md` | Plan 012 completion status. |

## Test Configuration Decision

`packages/expo-modules-dotnet/tsconfig.json` currently has `include: ["src"]` and excludes only `src/**/__tests__/**`. Create the compile-time fixture under `src/__type_tests__/`, not `src/__tests__/`; it is therefore included by the already-executed `pnpm --filter expo-modules-dotnet typecheck` command. Do **not** alter `tsconfig.json`, add a second tsconfig, or add a package script. Vitest remains responsible only for runtime constructor/export coverage.

---

### Task 1: Add the portable event facade and its executable contracts

**Files:**
- Create: `packages/expo-modules-dotnet/src/ts-declarations/DotnetEventEmitter.ts`
- Create: `packages/expo-modules-dotnet/src/__type_tests__/dotnet-module.ts`
- Modify: `packages/expo-modules-dotnet/src/index.ts`
- Modify: `packages/expo-modules-dotnet/src/__tests__/index.test.ts`

**Consumes:**
- The runtime method names/arity from `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventEmitterPrototype.cs`.
- The existing `requireDotnetModule<T>(name: string): T` implementation and Vitest mock setup in `src/__tests__/index.test.ts`.

**Produces:**
- `EventsMap`, `EventSubscription`, `DotnetEventEmitter<TEventsMap>`, and `DotnetModule<TEventsMap>` as public exports from `expo-modules-dotnet`.
- Executed runtime and compile-time evidence for the public contract, including intentional absence of `removeSubscription`.

- [ ] **Step 1: Write the failing runtime constructor/export test**

  Extend the existing `describe('requireDotnetModule', ...)` file with a separate `describe('typed facade base classes', ...)`. Import the two values from `../index` after the existing mock declaration. Test both exports through an explicit table so each class must expose its own complete direct-construction guidance:

  ```ts
  describe('typed facade base classes', () => {
    it.each([
      [
        'DotnetEventEmitter',
        DotnetEventEmitter,
        'DotnetEventEmitter instances are created by the native module registry. Use requireDotnetModule() to obtain a module.',
      ],
      [
        'DotnetModule',
        DotnetModule,
        'DotnetModule instances are created by the native module registry. Use requireDotnetModule() to obtain a module.',
      ],
    ])('%s is an exported value whose constructor directs callers to module lookup', (_, Facade, message) => {
      expect(typeof Facade).toBe('function');
      expect(() => new Facade()).toThrow(message);
    });

    it('keeps only the five modern event methods on the event-emitter prototype', () => {
      expect(Object.getOwnPropertyNames(DotnetEventEmitter.prototype).sort()).toEqual(
        [
          'addListener',
          'constructor',
          'emit',
          'listenerCount',
          'removeAllListeners',
          'removeListener',
        ].sort()
      );
      expect(Object.getOwnPropertyNames(DotnetModule.prototype)).toEqual(['constructor']);

      for (const name of ['removeSubscription', 'startObserving', 'stopObserving', 'unavailable']) {
        expect(DotnetEventEmitter.prototype).not.toHaveProperty(name);
        expect(DotnetModule.prototype).not.toHaveProperty(name);
      }
    });
  });
  ```

  Import `DotnetEventEmitter` and `DotnetModule` as runtime values. Keep the native installer mock unchanged.

- [ ] **Step 2: Verify the runtime test is RED**

  Run:

  ```sh
  pnpm --filter expo-modules-dotnet test -- src/__tests__/index.test.ts
  ```

  Expected: Vitest fails during module loading because `../index` does not yet export `DotnetEventEmitter` and `DotnetModule`.

- [ ] **Step 3: Write the failing compile-time fixture**

  Create `packages/expo-modules-dotnet/src/__type_tests__/dotnet-module.ts` with a type alias, not an interface, so it structurally satisfies the exported `Record<string, (...args: any[]) => void>` constraint. Include valid heritage, lookup, listener, subscription, removal, count, and emitted-tuple usage, plus maintained negative assertions:

  ```ts
  import {
    DotnetEventEmitter,
    DotnetModule,
    requireDotnetModule,
    type EventSubscription,
  } from '../index';

  type FixtureEvents = {
    onFoo(payload: string): void;
    onPair(left: number, right: boolean): void;
  };

  declare class Fixture extends DotnetModule<FixtureEvents> {
    add(left: number, right: number): number;
  }

  declare class EventOnlyFixture extends DotnetEventEmitter<FixtureEvents> {}

  const module = requireDotnetModule<Fixture>('Fixture');
  const subscription: EventSubscription = module.addListener('onFoo', payload => {
    const value: string = payload;
    return value;
  });

  module.removeListener('onFoo', () => {});
  module.removeAllListeners('onFoo');
  module.emit('onPair', 1, true);
  const listenerTotal: number = module.listenerCount('onFoo');
  const sum: number = module.add(1, 2);
  const eventOnlyTotal: number = (null as unknown as EventOnlyFixture).listenerCount('onPair');
  subscription.remove();

  void listenerTotal;
  void sum;
  void eventOnlyTotal;

  // @ts-expect-error Event names must be declared by FixtureEvents.
  module.addListener('onMissing', () => {});
  // @ts-expect-error Listener arguments must match the selected event.
  module.addListener('onFoo', (payload: number) => {});
  // @ts-expect-error Emitted arguments must match the selected event tuple.
  module.emit('onPair', 1, 'not-a-boolean');
  // @ts-expect-error The legacy runtime helper is deliberately not part of the modern facade.
  module.removeSubscription(subscription);
  ```

  Do not put this file below `src/__tests__/`: Vitest test files are excluded from the adapter's `tsc` configuration.

- [ ] **Step 4: Verify the compile-time fixture is RED for the intended reason**

  Run:

  ```sh
  pnpm --filter expo-modules-dotnet typecheck
  ```

  Expected: `tsc` exits non-zero because `../index` does not yet export the new classes/types or does not provide their event methods. Do not proceed until the fixture is reported by this command; a Vitest-only failure is insufficient.

- [ ] **Step 5: Implement the smallest dependency-free public surface**

  Create `packages/expo-modules-dotnet/src/ts-declarations/DotnetEventEmitter.ts`. Use these exact public signatures and real class values:

  ```ts
  function facadeUnavailable(message: string): never {
    throw new Error(message);
  }

  export type EventsMap = Record<string, (...args: any[]) => void>;

  export type EventSubscription = {
    remove(): void;
  };

  export class DotnetEventEmitter<
    TEventsMap extends EventsMap = Record<never, never>,
  > {
    public constructor() {
      facadeUnavailable(
        `${new.target?.name ?? 'DotnetEventEmitter'} instances are created by the native module registry. Use requireDotnetModule() to obtain a module.`
      );
    }

    public addListener<EventName extends keyof TEventsMap>(
      eventName: EventName,
      listener: TEventsMap[EventName]
    ): EventSubscription {
      return facadeUnavailable(
        'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
      );
    }

    public removeListener<EventName extends keyof TEventsMap>(
      eventName: EventName,
      listener: TEventsMap[EventName]
    ): void {
      facadeUnavailable(
        'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
      );
    }

    public removeAllListeners(eventName: keyof TEventsMap): void {
      facadeUnavailable(
        'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
      );
    }

    public emit<EventName extends keyof TEventsMap>(
      eventName: EventName,
      ...args: Parameters<TEventsMap[EventName]>
    ): void {
      facadeUnavailable(
        'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
      );
    }

    public listenerCount<EventName extends keyof TEventsMap>(eventName: EventName): number {
      return facadeUnavailable(
        'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
      );
    }
  }

  export class DotnetModule<
    TEventsMap extends EventsMap = Record<never, never>,
  > extends DotnetEventEmitter<TEventsMap> {
    public constructor() {
      super();
    }
  }
  ```

  Add TSDoc before both types, both classes, both constructors, and all five public methods. Explain `EventsMap` keys/listeners; `EventSubscription.remove`; that native registry objects are not `instanceof` either class; that direct construction always throws; and that `emit` is typed only because the native prototype exposes it and is primarily runtime-internal. Do not implement, declare, document, or test `removeSubscription` as a class method.

  Re-export the classes and types from `packages/expo-modules-dotnet/src/index.ts` with a type-only re-export for types:

  ```ts
  export { DotnetEventEmitter, DotnetModule } from './ts-declarations/DotnetEventEmitter';
  export type { EventSubscription, EventsMap } from './ts-declarations/DotnetEventEmitter';
  ```

  Leave `requireDotnetModule<T>` text and generic constraint unchanged. `facadeUnavailable` is module-local, so it does not become an own or inherited member of either public class prototype. Normal modules are separate native registry objects and never inherit this JavaScript class prototype.

- [ ] **Step 6: Verify GREEN and prove negative assertions are active**

  Run:

  ```sh
  pnpm --filter expo-modules-dotnet test -- src/__tests__/index.test.ts
  pnpm --filter expo-modules-dotnet typecheck
  ```

  Expected: Vitest passes all existing lookup tests, the two exact constructor-guidance cases, and the prototype-name assertion; `tsc` exits 0 and consumes the four `@ts-expect-error` comments.

  Temporarily remove only the `// @ts-expect-error Event names must be declared by FixtureEvents.` line, rerun the typecheck, and restore the exact comment before continuing:

  ```sh
  pnpm --filter expo-modules-dotnet typecheck
  ```

  Expected while removed: non-zero exit with an `onMissing` event-name type error. Expected after restore: exit 0. This confirms the fixture is executable and is not a passive source file.

- [ ] **Step 7: Commit the first independently verified slice**

  Inspect only these staged paths and check privacy before committing:

  ```sh
  git add packages/expo-modules-dotnet/src
  git diff --cached --check
  git diff --cached -- packages/expo-modules-dotnet/src
  if git diff --cached | rg -n '/Users/|[A-Za-z]:\\\\Users\\\\'; then exit 1; fi
  git commit -m "feat(dotnet-js): add typed module facade bases"
  ```

  Expected: staged diff has only the four Task 1 paths, contains no local-machine identifiers, and the commit succeeds.

### Task 2: Migrate the example facade without changing its public surface

**Files:**
- Modify: `packages/example-module/src/index.ts`

**Consumes:**
- `DotnetModule`, `EventSubscription`, and unconstrained `requireDotnetModule` from Task 1.
- The `ExampleModule` public functions and PascalCase native record fields already used by `apps/mobile-app`.

**Produces:**
- An internal ambient `ExampleModuleType` extending the typed event base.
- `export type ExampleModule = ExampleModuleType` and a stable `EventSubscription` re-export.

- [ ] **Step 1: Write the migration as a type-checking failure before production conversion**

  In `packages/example-module/src/index.ts`, first replace only the local `EventSubscription` declaration with the intended import/re-export pair, leaving the hand-written `ExampleModule` object in place:

  ```ts
  import {
    DotnetModule,
    requireDotnetModule,
    type EventSubscription,
  } from 'expo-modules-dotnet';

  export type { EventSubscription } from 'expo-modules-dotnet';
  ```

  Then replace the hand-written object type with this temporarily incomplete declaration, which intentionally does not include the native methods:

  ```ts
  type ExampleModuleEvents = {
    onStatus(payload: string): void;
  };

  declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {}

  export type ExampleModule = ExampleModuleType;
  ```

  Keep `const nativeModule = requireDotnetModule<ExampleModuleType>('ExampleModule');`. The existing public wrapper calls should now make the facade incomplete.

- [ ] **Step 2: Verify the migration is RED**

  Run:

  ```sh
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  ```

  Expected: both typechecks exit non-zero reporting that wrapper-called members such as `add`, `describeUser`, `emitStatusAsync`, `getMessageAsync`, and `transformWithCallback` do not exist on `ExampleModuleType`. This proves the ambient class owns the complete native shape for both configured app consumers.

- [ ] **Step 3: Complete the ambient native class and preserve aliases**

  Add the existing native method shapes directly to `ExampleModuleType`:

  ```ts
  declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
    add(a: number, b: number): number;
    describeUser(user: { Age: number; Name: string }): {
      Age: number;
      Name: string;
      Summary: string;
    };
    emitStatusAsync(label: string): Promise<void>;
    getMessageAsync(): Promise<string>;
    transformWithCallback(value: string, callback: (value: string) => string): string;
  }
  ```

  Keep every existing public wrapper function and its signature byte-for-byte where possible. `addStatusListener` must return the imported `EventSubscription` and call inherited `nativeModule.addListener('onStatus', listener)`. Keep the `ExampleUser` and `ExampleUserSummary` definitions, `describeUser` PascalCase-to-camelCase translation, and `export type ExampleModule = ExampleModuleType`.

  Do not use `export declare class ExampleModuleType`: it would add an incorrect runtime value to the public facade. Do not add `removeSubscription`; downstream code uses the `EventSubscription.remove()` return value.

- [ ] **Step 4: Verify GREEN and public compatibility**

  Run:

  ```sh
  pnpm --filter expo-modules-dotnet typecheck
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  rg -n "export type EventSubscription = \\{" packages/example-module/src/index.ts
  rg -n "export type ExampleModule = ExampleModuleType" packages/example-module/src/index.ts
  ```

  Expected: all three typechecks exit 0; the first `rg` finds no local duplicate subscription type (exit 1 is expected); the second finds exactly the public type alias. Also inspect `apps/mobile-app/App.tsx` to confirm it consumes only the unchanged public wrapper functions.

- [ ] **Step 5: Commit the example migration**

  Run the same staged whitespace/privacy checks, then commit only the facade migration:

  ```sh
  git add packages/example-module/src/index.ts
  git diff --cached --check
  git diff --cached -- packages/example-module/src/index.ts
  if git diff --cached | rg -n '/Users/|[A-Za-z]:\\\\Users\\\\'; then exit 1; fi
  git commit -m "refactor(example-module): extend DotnetModule in facade"
  ```

  Expected: one changed facade file and no public function/data-type changes.

### Task 3: Merge the living spec, document the pattern, and close the change package

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/module-authoring-guide.md`
- Modify: `docs/plans/README.md`
- Move after the living-spec merge: `docs/changes/2026-07-19-typed-js-facade/` → `docs/archive/changes/2026-07-19-typed-js-facade/`

**Consumes:**
- The accepted delta in `docs/changes/2026-07-19-typed-js-facade/spec.md`.
- The verified public exports and example facade from Tasks 1–2.

**Produces:**
- Durable normative and tutorial documentation matching the implemented facade.
- Archived transient change package and Plan 012 marked DONE.

- [ ] **Step 1: Update the living module-boundary specification**

  Add a requirement adjacent to the existing public adapter lookup requirement in `docs/specs/modules-core-boundary.md`. It must state all of the following, with GIVEN/WHEN/THEN scenarios in the file's existing style:

  - `expo-modules-dotnet` exports `EventsMap`, `EventSubscription`, real `DotnetEventEmitter`, and real `DotnetModule` values.
  - `DotnetModule` extends `DotnetEventEmitter`; both constructors throw and native registry objects are not guaranteed to be instances.
  - The five typed methods, their event-map relationship, and that `emit` is runtime-internal in ordinary facades.
  - `removeSubscription`, `startObserving`, and `stopObserving` are not modern facade members; cleanup is `subscription.remove()`.
  - `requireDotnetModule<T>` remains compatible with plain object types.
  - Module authors use an internal `declare class ... extends DotnetModule<Events>` plus a public type alias, as in `example-module`.

- [ ] **Step 2: Replace the authoring-guide interim example**

  Replace section 8 of `docs/module-authoring-guide.md` with an example that matches the final `example-module` facade. It must include:

  ```ts
  import {
    DotnetModule,
    requireDotnetModule,
    type EventSubscription,
  } from 'expo-modules-dotnet';

  type ExampleModuleEvents = {
    onStatus(payload: string): void;
  };

  declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
    add(a: number, b: number): number;
    getMessageAsync(): Promise<string>;
  }

  export type ExampleModule = ExampleModuleType;
  export type { EventSubscription } from 'expo-modules-dotnet';

  const nativeModule = requireDotnetModule<ExampleModuleType>('ExampleModule');

  export function addStatusListener(listener: (payload: string) => void): EventSubscription {
    return nativeModule.addListener('onStatus', listener);
  }
  ```

  Explain that the classes are type bases and not runtime module constructors; obtain modules through `requireDotnetModule`, do not use `instanceof`, and release listeners with `.remove()`. Replace the obsolete statement that typed facade base classes are merely planned. Retain the existing explanation of record field names and JS-facing camelCase translation.

- [ ] **Step 3: Run targeted documentation and contract checks before closure**

  Run:

  ```sh
  rg -n "DotnetModule|DotnetEventEmitter|EventSubscription|removeSubscription" docs/specs/modules-core-boundary.md docs/module-authoring-guide.md packages/expo-modules-dotnet/src packages/example-module/src
  rg -n 'Dedicated facade base types.*planned|planned direction for .*expo-modules-dotnet' docs/module-authoring-guide.md
  git diff --check
  ```

  Expected: the first search shows the new class/subscription documentation and implementation; the second has no matches (exit 1 expected); whitespace check is clean.

- [ ] **Step 4: Run the complete required verification set**

  Run in this order:

  ```sh
  pnpm --filter expo-modules-dotnet test
  pnpm --filter expo-modules-dotnet typecheck
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/test-managed.sh
  scripts/format.sh --check --all
  git diff --check
  git diff --check d754492a -- docs/changes/2026-07-19-typed-js-facade docs/archive/changes/2026-07-19-typed-js-facade docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md packages/expo-modules-dotnet/src packages/example-module/src
  git diff d754492a -- docs/changes/2026-07-19-typed-js-facade docs/archive/changes/2026-07-19-typed-js-facade docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md packages/expo-modules-dotnet/src packages/example-module/src
  if git diff d754492a -- docs/changes/2026-07-19-typed-js-facade docs/archive/changes/2026-07-19-typed-js-facade docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md packages/expo-modules-dotnet/src packages/example-module/src | rg -n '/Users/|[A-Za-z]:\\\\Users\\\\'; then exit 1; fi
  if rg -n "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills; then exit 1; fi
  ```

  Expected: each test/typecheck/format command exits 0; all diff/privacy checks are clean; the complete working-tree diff from accepted delta commit `d754492a` contains only the intended facade, example migration, docs, status, and archived change-package changes; the stale-doc scan has no unintended matches. If a command is skipped or fails, do not mark this plan complete.

- [ ] **Step 5: Merge docs, archive the transient package, and mark the plan complete**

  After all verification succeeds, update the Plan 012 status row in `docs/plans/README.md` to `DONE` with concise verification evidence. Then archive both the approved delta and this implementation plan only after their requirements have been merged into the living spec:

  ```sh
  git mv docs/changes/2026-07-19-typed-js-facade docs/archive/changes/2026-07-19-typed-js-facade
  ```

  After the README edit and archive move, re-run the final formatting and diff/privacy checks before staging the closure commit:

  ```sh
  scripts/format.sh --check --all
  git diff --check
  if git diff d754492a -- docs/changes/2026-07-19-typed-js-facade docs/archive/changes/2026-07-19-typed-js-facade docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md packages/expo-modules-dotnet/src packages/example-module/src | rg -n '/Users/|[A-Za-z]:\\\\Users\\\\'; then exit 1; fi
  ```

  Expected: formatting and whitespace checks exit 0 and the full accepted-baseline diff contains no local-machine identifiers.

- [ ] **Step 6: Commit documentation closure**

  Stage only the durable docs, plan index, and archived change package:

  ```sh
  git add docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md docs/archive/changes/2026-07-19-typed-js-facade
  git diff --cached --check
  if git diff --cached | rg -n '/Users/|[A-Za-z]:\\\\Users\\\\'; then exit 1; fi
  git commit -m "docs(specs): merge typed JS facade delta"
  ```

  Expected: the committed docs describe only the implemented contract, the transient package is no longer under `docs/changes/`, and Plan 012 is DONE.

## Stop Conditions

Stop and report instead of improvising if any of these occur:

- The installed runtime methods or their arities differ from `EventEmitterPrototype.cs`; revise the spec/plan only after resolving the mismatch.
- Exporting real facade values requires a new React Native, Expo Modules Core, or other runtime dependency in `DotnetEventEmitter.ts`.
- `src/__type_tests__/` is not compiled by the adapter typecheck, or negative assertions are not rejected when their suppression comment is removed; establish an executed explicit configuration before continuing.
- The existing unconstrained `requireDotnetModule<T>` signature would need to change, or an existing plain-object caller ceases to compile.
- Migrating `example-module` changes a function signature, public data type, record-field mapping, or its type-only `ExampleModule` / `EventSubscription` import surface used by the mobile app.
- The desktop-app typecheck fails after the example facade migration, even when the adapter and mobile-app typechecks pass.
- The user changes the accepted delta or Plan 014 lands first with generated event maps that materially supersede this explicit-map contract.
- Any required verification command fails, is skipped, or exposes unrelated dirty files that cannot be cleanly separated.

## Final Acceptance Checklist

- [x] `expo-modules-dotnet` exports the four agreed facade names, with real class values and public TSDoc.
- [x] The class declarations cover exactly five modern methods; legacy `removeSubscription` is intentionally absent.
- [x] Runtime tests prove complete class-specific constructor guidance, exactly five event-emitter prototype methods, and no legacy/helper prototype members, without asserting native-module `instanceof` behavior.
- [x] Positive and negative type fixtures are run by the existing adapter typecheck configuration.
- [x] `example-module` uses the internal ambient-class plus public-alias pattern and retains its public exports/functions.
- [x] The living spec and authoring guide match implementation; the change package is archived and Plan 012 is marked DONE.
- [x] Adapter test/typecheck, mobile and desktop typechecks, managed suite, post-archive formatting, diff checks, and privacy scans have fresh passing evidence.

## Closure Evidence (2026-07-19)

- Adapter runtime tests: 1 test file, 7 passed, 0 failed, 0 skipped.
- Adapter, mobile-app, and desktop-app TypeScript checks passed.
- Managed suite: generator 46 passed, Expo.JSI 189 passed, and ModulesCore 106
  passed; 341 total passed, 0 failed, 0 skipped.
- Formatting, whitespace, intended-baseline diff, precise absolute-path privacy,
  and stale-documentation scans passed after archiving.
