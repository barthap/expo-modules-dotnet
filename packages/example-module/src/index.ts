import { requireDotnetModule } from 'expo-modules-dotnet';

export type ExampleModule = {
  add(a: number, b: number): number;
};

export function requireExampleModule(): ExampleModule {
  return requireDotnetModule<ExampleModule>('ExampleModule');
}

export function add(a: number, b: number): number {
  return requireExampleModule().add(a, b);
}
