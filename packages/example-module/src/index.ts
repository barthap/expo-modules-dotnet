import { requireDotnetModule } from 'expo-modules-dotnet';
import { requireDotnetNativeView } from 'expo-modules-dotnet-windows';
import type { ComponentType } from 'react';
import { Platform, type StyleProp, type ViewStyle } from 'react-native';

const nativeModule = requireDotnetModule<ExampleModule>('ExampleModule');

export type ExampleUser = {
  age: number;
  name: string;
};

export type ExampleUserSummary = ExampleUser & {
  summary: string;
};

export type EventSubscription = {
  remove(): void;
};

export type ExampleColorBoxProps = {
  color?: 'blue' | 'green' | 'red' | 'yellow' | 'purple' | string;
  style?: StyleProp<ViewStyle>;
};

export type ExampleModule = {
  add(a: number, b: number): number;
  addListener(eventName: 'onStatus', listener: (payload: string) => void): EventSubscription;
  describeUser(user: { Age: number; Name: string }): {
    Age: number;
    Name: string;
    Summary: string;
  };
  emitStatusAsync(label: string): Promise<void>;
  getMessageAsync(): Promise<string>;
  transformWithCallback(
    value: string,
    callback: (value: string) => string
  ): string;
};

const NullExampleColorBox: ComponentType<ExampleColorBoxProps> = () => null;

export const ExampleColorBox =
  Platform.OS === 'windows'
    ? requireDotnetNativeView<ExampleColorBoxProps>('ExampleColorBox', ['color'])
    : NullExampleColorBox;

export function add(a: number, b: number): number {
  return nativeModule.add(a, b);
}

export function addStatusListener(listener: (payload: string) => void): EventSubscription {
  return nativeModule.addListener('onStatus', listener);
}

export function describeUser(user: ExampleUser): ExampleUserSummary {
  const result = nativeModule.describeUser({ Age: user.age, Name: user.name });
  return {
    age: result.Age,
    name: result.Name,
    summary: result.Summary,
  };
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
