declare module 'react-native-windows/Libraries/NativeComponent/NativeComponentRegistry' {
  import type { ComponentType } from 'react';

  export function get<Props extends object>(
    name: string,
    viewConfigProvider: () => {
      uiViewClassName: string;
      validAttributes: Record<string, true>;
    }
  ): ComponentType<Props>;
}
