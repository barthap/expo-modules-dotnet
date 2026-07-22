import { DotnetEventEmitter, type EventsMap } from './DotnetEventEmitter';

const unavailableMessage =
  'DotnetSharedObject instances are created by a generated shared-object class or a module return. Construct one with a generated class exposed on a module, or receive one from a module method.';

/**
 * Provides the TypeScript facade for generated .NET shared-object instances.
 *
 * Native shared-object instances are created by generated classes exposed on module objects or
 * returned from module methods; they supply their own prototype `release` method at runtime and
 * are not guaranteed to be instances of this class. This class exists for TypeScript facade
 * heritage clauses only.
 */
export class DotnetSharedObject<
  TEventsMap extends EventsMap = Record<never, never>,
> extends DotnetEventEmitter<TEventsMap> {
  /**
   * Always throws because usable shared objects come from a generated class or a module return.
   */
  public constructor() {
    super(unavailableMessage);
  }

  /**
   * Releases the native shared-object pairing exactly once; repeated calls are no-ops.
   *
   * Native instances provide this method at runtime. After release, calling generated methods or
   * accessors on the instance throws a catchable error.
   */
  public release(): void {
    throw new Error(unavailableMessage);
  }
}
