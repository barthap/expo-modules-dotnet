import { requireDotnetModule } from 'expo-modules-dotnet';

const nativeModule = requireDotnetModule<ExampleModule>('ExampleModule');

export type ExampleModule = {
  add(a: number, b: number): number;
};

export function add(a: number, b: number): number {
  return nativeModule.add(a, b);
}
