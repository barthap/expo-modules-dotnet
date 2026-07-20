import {
  DotnetModule,
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

declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  add(a: number, b: number): number;
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
