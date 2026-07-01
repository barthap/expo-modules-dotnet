import { TurboModuleRegistry } from 'react-native';

export function ensureInstalled(): boolean {
  return TurboModuleRegistry.get('ExpoCSharpV2Installer') != null;
}
