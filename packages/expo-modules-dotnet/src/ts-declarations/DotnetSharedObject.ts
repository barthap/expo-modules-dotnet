import type { EventSubscription, EventsMap } from './DotnetEventEmitter';

function facadeUnavailable(): never {
  throw new Error(
    'DotnetSharedObject instances are created by a generated shared-object class or a module return. Construct one with a generated class exposed on a module, or receive one from a module method.'
  );
}

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
> {
  /**
   * Always throws because usable shared objects come from a generated class or a module return.
   */
  public constructor() {
    facadeUnavailable();
  }

  /**
   * Adds a listener for an event emitted by this shared-object instance.
   * Native instances provide this method at runtime.
   */
  public addListener<EventName extends keyof TEventsMap>(
    eventName: EventName,
    listener: TEventsMap[EventName]
  ): EventSubscription {
    return facadeUnavailable();
  }

  /**
   * Removes listener registrations matching an event and listener on this shared-object instance.
   * Native instances provide this method at runtime.
   */
  public removeListener<EventName extends keyof TEventsMap>(
    eventName: EventName,
    listener: TEventsMap[EventName]
  ): void {
    return facadeUnavailable();
  }

  /**
   * Removes every listener for an event on this shared-object instance.
   * Native instances provide this method at runtime.
   */
  public removeAllListeners(eventName: keyof TEventsMap): void {
    return facadeUnavailable();
  }

  /**
   * Emits an event through this shared-object instance's native event surface.
   * Native instances provide this method at runtime.
   */
  public emit<EventName extends keyof TEventsMap>(
    eventName: EventName,
    ...args: Parameters<TEventsMap[EventName]>
  ): void {
    return facadeUnavailable();
  }

  /**
   * Gets the number of listeners for an event on this shared-object instance.
   * Native instances provide this method at runtime.
   */
  public listenerCount<EventName extends keyof TEventsMap>(eventName: EventName): number {
    return facadeUnavailable();
  }

  /**
   * Releases the native shared-object pairing exactly once; repeated calls are no-ops.
   *
   * Native instances provide this method at runtime. After release, calling generated methods or
   * accessors on the instance throws a catchable error.
   */
  public release(): void {
    facadeUnavailable();
  }
}
