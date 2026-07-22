import {
  DotnetModule,
  DotnetSharedObject,
  requireDotnetModule,
  type EventSubscription,
} from 'expo-modules-dotnet';

export type { EventSubscription } from 'expo-modules-dotnet';

const nativeModule = requireDotnetModule<ExampleModule>('ExampleModule');

export type ExampleUser = {
  age: number;
  name: string;
};

export type ExampleUserSummary = ExampleUser & {
  summary: string;
};

type ExampleModuleEvents = {
  onStatus(payload: string): void;
};

type ExampleCounterEvents = {
  onProgress(payload: { count: number }): void;
};

/**
 * Shared counter handle: the same instance is visible to C# and JavaScript. Call `release()`
 * when done; using a released counter throws a catchable error.
 */
export declare class ExampleCounter extends DotnetSharedObject<ExampleCounterEvents> {
  constructor(start: number);
  readonly count: number;
  increment(by: number): number;
  incrementAndEmitAsync(by: number): Promise<number>;
}

declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  ExampleCounter: typeof ExampleCounter;
  add(a: number, b: number): number;
  echoCounter(counter: ExampleCounter): ExampleCounter;
  makeCounter(start: number): ExampleCounter;
  describeUser(user: ExampleUser): ExampleUserSummary;
  emitStatusAsync(label: string): Promise<void>;
  getMessageAsync(): Promise<string>;
  readonly ready: boolean;
  transformWithCallback(value: string, callback: (value: string) => string): string;
}

export type ExampleModule = ExampleModuleType;

export function add(a: number, b: number): number {
  return nativeModule.add(a, b);
}

export function addStatusListener(listener: (payload: string) => void): EventSubscription {
  return nativeModule.addListener('onStatus', listener);
}

export function describeUser(user: ExampleUser): ExampleUserSummary {
  return nativeModule.describeUser(user);
}

export function emitStatusAsync(label: string): Promise<void> {
  return nativeModule.emitStatusAsync(label);
}

export function getMessageAsync(): Promise<string> {
  return nativeModule.getMessageAsync();
}

export function transformWithCallback(
  value: string,
  callback: (value: string) => string
): string {
  return nativeModule.transformWithCallback(value, callback);
}

export function createCounter(start: number): ExampleCounter {
  return new nativeModule.ExampleCounter(start);
}

export function makeCounter(start: number): ExampleCounter {
  return nativeModule.makeCounter(start);
}

export function echoCounter(counter: ExampleCounter): ExampleCounter {
  return nativeModule.echoCounter(counter);
}
