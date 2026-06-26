# HostFXR Smoke Experiment

This experiment proves that a native macOS executable can load a
framework-dependent .NET assembly through HostFXR, call unmanaged-callable
managed entry points, receive an explicitly owned UTF-8 buffer, and release that
buffer.

This directory is standalone proof code. It is not a dependency of
`Expo.CSharpJsi`, `Expo.ModulesCore`, native bridge packages, expo-desktop
examples, or future autolinking packages.
