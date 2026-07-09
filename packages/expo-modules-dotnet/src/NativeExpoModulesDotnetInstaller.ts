import type { TurboModule } from 'react-native';
import { TurboModuleRegistry } from 'react-native';

export interface Spec extends TurboModule {
  installModules(): boolean;
  getLastError(): string;
}

export default TurboModuleRegistry.get<Spec>('ExpoModulesDotnetInstaller');
