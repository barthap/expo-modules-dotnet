import { StatusBar } from 'expo-status-bar';
import {
  add,
  addStatusListener,
  describeUser,
  emitStatusAsync,
  getMessageAsync,
  transformWithCallback,
} from 'example-module';
import { useEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

type CapabilityKey = 'add' | 'async' | 'record' | 'callback' | 'event';

type Capability = {
  key: CapabilityKey;
  label: string;
  button: string;
};

const capabilities: Capability[] = [
  { key: 'add', label: 'Add', button: 'Run add' },
  { key: 'async', label: 'Async function', button: 'Call async' },
  { key: 'record', label: 'Record', button: 'Send record' },
  { key: 'callback', label: 'Callback', button: 'Invoke callback' },
  { key: 'event', label: 'Event', button: 'Emit event' },
];

const initialResults: Record<CapabilityKey, string> = {
  add: 'Not run yet',
  async: 'Tap to call getMessageAsync()',
  record: 'Tap to send a JS object into a C# record',
  callback: 'Tap to let C# invoke a JS function',
  event: 'Tap to emit onStatus from C#',
};

export default function App() {
  const [results, setResults] = useState(initialResults);

  function setResult(key: CapabilityKey, value: string) {
    setResults(previous => ({ ...previous, [key]: value }));
  }

  function markStillWaiting(key: CapabilityKey, waitingValue: string, nextValue: string) {
    setTimeout(() => {
      setResults(previous =>
        previous[key] === waitingValue ? { ...previous, [key]: nextValue } : previous
      );
    }, 1500);
  }

  function runAdd() {
    try {
      const result = add(22, 20);
      if (result !== 42) {
        throw new Error(`Unexpected C# module result: ${String(result)}`);
      }

      console.log('[ExampleModule] C# add(22, 20) returned', result);
      setResult('add', `22 + 20 = ${result}`);
    } catch (caught) {
      console.error('[ExampleModule] add failed', caught);
      setResult('add', caught instanceof Error ? caught.message : String(caught));
    }
  }

  async function runAsync() {
    const waiting = 'Waiting for Promise...';
    setResult('async', waiting);
    markStillWaiting('async', waiting, 'Still waiting for getMessageAsync()');

    try {
      setResult('async', await getMessageAsync());
    } catch (caught) {
      setResult('async', caught instanceof Error ? caught.message : String(caught));
    }
  }

  function runRecord() {
    try {
      const user = describeUser({ age: 37, name: 'Ada' });
      setResult('record', `${user.name}, ${user.age}: ${user.summary}`);
    } catch (caught) {
      setResult('record', caught instanceof Error ? caught.message : String(caught));
    }
  }

  function runCallback() {
    try {
      const result = transformWithCallback(
        'JS',
        value => `JS callback received "${value}"`
      );
      setResult('callback', result);
    } catch (caught) {
      setResult('callback', caught instanceof Error ? caught.message : String(caught));
    }
  }

  function runEvent() {
    const waiting = 'Waiting for onStatus...';
    setResult('event', waiting);
    markStillWaiting('event', waiting, 'Still waiting for onStatus');
    emitStatusAsync('button').catch(caught => {
      setResult('event', caught instanceof Error ? caught.message : String(caught));
    });
  }

  function runCapability(key: CapabilityKey) {
    switch (key) {
      case 'add':
        runAdd();
        return;
      case 'async':
        void runAsync();
        return;
      case 'record':
        runRecord();
        return;
      case 'callback':
        runCallback();
        return;
      case 'event':
        runEvent();
        return;
    }
  }

  useEffect(() => {
    let removeStatusListener = () => {};
    try {
      const subscription = addStatusListener(payload => {
        setResult('event', payload);
      });
      removeStatusListener = () => subscription.remove();
    } catch (caught) {
      setResult('event', caught instanceof Error ? caught.message : String(caught));
    }

    runAdd();

    return () => {
      removeStatusListener();
    };
  }, []);

  return (
    <View style={styles.screen}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text style={styles.title}>Expo.ModulesCore NativeAOT</Text>
        <Text style={styles.subtitle}>ExampleModule interactive showcase</Text>
        <View style={styles.rows}>
          {capabilities.map(capability => (
            <View key={capability.key} style={styles.row}>
              <View style={styles.rowHeader}>
                <Text style={styles.label}>{capability.label}</Text>
                <Pressable
                  accessibilityRole="button"
                  onPress={() => runCapability(capability.key)}
                  style={({ pressed }) => [
                    styles.button,
                    pressed ? styles.buttonPressed : null,
                  ]}>
                  <Text style={styles.buttonText}>{capability.button}</Text>
                </Pressable>
              </View>
              <Text style={styles.value}>{results[capability.key]}</Text>
            </View>
          ))}
        </View>
      </ScrollView>
      <StatusBar style="dark" />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: '#f4f6f1',
  },
  container: {
    flexGrow: 1,
    justifyContent: 'center',
    padding: 24,
  },
  title: {
    color: '#24515c',
    fontSize: 24,
    fontWeight: '700',
    marginBottom: 6,
  },
  subtitle: {
    color: '#5b6170',
    fontSize: 15,
    marginBottom: 24,
  },
  rows: {
    borderColor: '#cfd8dc',
    borderTopWidth: 1,
  },
  row: {
    borderBottomWidth: 1,
    borderColor: '#cfd8dc',
    paddingVertical: 14,
  },
  rowHeader: {
    alignItems: 'center',
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  label: {
    color: '#716033',
    fontSize: 13,
    fontWeight: '700',
    textTransform: 'uppercase',
  },
  value: {
    color: '#111827',
    fontSize: 18,
    fontWeight: '600',
  },
  button: {
    alignItems: 'center',
    backgroundColor: '#24515c',
    borderRadius: 6,
    minHeight: 36,
    minWidth: 112,
    justifyContent: 'center',
    paddingHorizontal: 14,
  },
  buttonPressed: {
    backgroundColor: '#183a42',
  },
  buttonText: {
    color: '#fff',
    fontSize: 13,
    fontWeight: '700',
  },
});
