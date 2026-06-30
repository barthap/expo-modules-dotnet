import { StatusBar } from 'expo-status-bar';
import { ensureInstalled } from 'expo-csharp-v2';
import { useEffect, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';

declare global {
  // eslint-disable-next-line no-var
  var expo:
    | {
        modules?: {
          ExpoCSharpV2?: {
            add(a: number, b: number): number;
          };
          [name: string]: unknown;
        };
      }
    | undefined;

}

export default function App() {
  const [message, setMessage] = useState('Loading C# module...');

  useEffect(() => {
    try {
      const globalExpo =
        globalThis.expo ?? (globalThis as typeof globalThis & { global?: typeof globalThis }).global?.expo;
      console.log('[ExpoCSharpV2] expo keys', Object.keys(globalExpo ?? {}));
      console.log('[ExpoCSharpV2] module keys', Object.keys(globalExpo?.modules ?? {}));

      const installed = ensureInstalled();
      console.log('[ExpoCSharpV2] TurboModule install trigger returned', installed);
      console.log('[ExpoCSharpV2] module keys after install', Object.keys(globalExpo?.modules ?? {}));

      const result = globalExpo?.modules?.ExpoCSharpV2?.add?.(20, 22);
      if (result !== 42) {
        throw new Error(`Unexpected C# module result: ${String(result)}`);
      }

      console.log('[ExpoCSharpV2] C# add(20, 22) returned', result);
      setMessage(`C# add result: ${result}`);
    } catch (error) {
      console.error('[ExpoCSharpV2] module call failed', error);
      setMessage(error instanceof Error ? error.message : String(error));
    }
  }, []);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>Expo.ModulesCore NativeAOT</Text>
      <Text style={styles.result}>{message}</Text>
      <StatusBar style="dark" />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
    backgroundColor: '#f7f7f2',
  },
  label: {
    color: '#24515c',
    fontSize: 18,
    fontWeight: '600',
    marginBottom: 12,
  },
  result: {
    color: '#111',
    fontSize: 28,
    fontWeight: '700',
    textAlign: 'center',
  },
});
