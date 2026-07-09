/// <reference path="./react-native-windows-native-component-registry.d.ts" />

import * as NativeComponentRegistry from 'react-native-windows/Libraries/NativeComponent/NativeComponentRegistry';

export function requireDotnetNativeView<Props extends object>(
  name: string,
  propNames: readonly string[]
) {
  const validAttributes: Record<string, true> = {};
  for (const propName of propNames) {
    validAttributes[propName] = true;
  }

  return NativeComponentRegistry.get<Props>(name, () => ({
    uiViewClassName: name,
    validAttributes,
  }));
}
