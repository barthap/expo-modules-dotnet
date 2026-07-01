import { StatusBar } from 'expo-status-bar';
import { requireDotnetModule } from 'expo-modules-dotnet';
import { useEffect, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';

type ExampleModule = {
  add(a: number, b: number): number;
};

export default function App() {
  const [message, setMessage] = useState('Loading C# module...');

  useEffect(() => {
    try {
      const exampleModule = requireDotnetModule<ExampleModule>('ExampleModule');
      const result = exampleModule.add(20, 22);
      if (result !== 42) {
        throw new Error(`Unexpected C# module result: ${String(result)}`);
      }

      console.log('[ExampleModule] C# add(20, 22) returned', result);
      setMessage(`C# add result: ${result}`);
    } catch (error) {
      console.error('[ExampleModule] module call failed', error);
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
