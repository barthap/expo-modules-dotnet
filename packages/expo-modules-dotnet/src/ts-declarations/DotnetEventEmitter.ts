function facadeUnavailable(message: string): never {
  throw new Error(message);
}

/**
 * Maps supported native event names to their listener signatures.
 *
 * Each key identifies an event installed by the managed module runtime and each value describes
 * the arguments passed to listeners for that event.
 */
export type EventsMap = Record<string, (...args: any[]) => void>;

/**
 * Represents a registration returned by `DotnetEventEmitter.addListener`.
 *
 * Calling `EventSubscription.remove` releases the listener registration from the native
 * module object.
 */
export type EventSubscription = {
  /**
   * Removes this listener registration from its native module object.
   */
  remove(): void;
};

/**
 * Provides the typed event surface installed on native .NET module objects.
 *
 * Native module objects are created by the registry and are not guaranteed to be instances of
 * this class. This class exists for TypeScript facade heritage clauses and does not implement a
 * JavaScript event emitter.
 */
export class DotnetEventEmitter<
  TEventsMap extends EventsMap = Record<never, never>,
> {
  /**
   * Always throws because usable module objects are created by the native module registry.
   */
  public constructor() {
    facadeUnavailable(
      `${new.target?.name ?? 'DotnetEventEmitter'} instances are created by the native module registry. Use requireDotnetModule() to obtain a module.`
    );
  }

  /**
   * Adds a listener for a declared native event.
   *
   * The returned subscription removes the listener through `EventSubscription.remove`.
   * The native runtime invokes listener callbacks synchronously and ignores their return values.
   * Native module objects provide this method at runtime.
   *
   * @param eventName Name of a declared native event.
   * @param listener Function invoked with the event's declared arguments.
   * @return A subscription that removes the listener.
   */
  public addListener<EventName extends keyof TEventsMap>(
    eventName: EventName,
    listener: TEventsMap[EventName]
  ): EventSubscription {
    return facadeUnavailable(
      'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
    );
  }

  /**
   * Removes listener registrations matching a declared native event and listener.
   *
   * Native module objects provide this method at runtime.
   *
   * @param eventName Name of a declared native event.
   * @param listener Listener function to remove.
   */
  public removeListener<EventName extends keyof TEventsMap>(
    eventName: EventName,
    listener: TEventsMap[EventName]
  ): void {
    facadeUnavailable(
      'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
    );
  }

  /**
   * Removes every listener registered for a declared native event.
   *
   * Native module objects provide this method at runtime.
   *
   * @param eventName Name of a declared native event.
   */
  public removeAllListeners(eventName: keyof TEventsMap): void {
    facadeUnavailable(
      'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
    );
  }

  /**
   * Emits a declared native event with its declared argument tuple.
   *
   * This method is primarily runtime-internal. It is typed because the native module prototype
   * exposes it, not as an encouragement for ordinary JavaScript facades to emit module events.
   * Native module objects provide this method at runtime.
   *
   * @param eventName Name of a declared native event.
   * @param args Arguments passed to listeners for the selected event.
   */
  public emit<EventName extends keyof TEventsMap>(
    eventName: EventName,
    ...args: Parameters<TEventsMap[EventName]>
  ): void {
    facadeUnavailable(
      'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
    );
  }

  /**
   * Gets the number of listeners registered for a declared native event.
   *
   * Native module objects provide this method at runtime.
   *
   * @param eventName Name of a declared native event.
   * @return The number of registered listeners.
   */
  public listenerCount<EventName extends keyof TEventsMap>(eventName: EventName): number {
    return facadeUnavailable(
      'Dotnet event facade methods are provided by native module objects returned from requireDotnetModule().'
    );
  }
}

/**
 * Provides a typed base class for declaring a native .NET module facade.
 *
 * Native module objects are created by the registry and are not guaranteed to be instances of
 * this class or `DotnetEventEmitter`. This class exists for TypeScript facade heritage
 * clauses and does not construct a usable module object.
 */
export class DotnetModule<
  TEventsMap extends EventsMap = Record<never, never>,
> extends DotnetEventEmitter<TEventsMap> {
  /**
   * Always throws because usable module objects are created by the native module registry.
   */
  public constructor() {
    super();
  }
}
