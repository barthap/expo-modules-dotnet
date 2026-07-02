Pod::Spec.new do |s|
  s.name           = 'ExpoModulesDotnet'
  s.version        = '0.1.0'
  s.summary        = 'Expo adapter for .NET-backed Expo modules'
  s.description    = 'Expo adapter for .NET-backed Expo modules'
  s.author         = ''
  s.homepage       = 'https://docs.expo.dev/modules/'
  s.platforms      = {
    :ios => '15.1',
    :tvos => '15.1',
    :osx => '14.0'
  }
  s.source         = { git: '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'
  s.ios.vendored_libraries = 'ios/NativeLibs/libExampleModule.dylib'
  s.tvos.vendored_libraries = 'ios/NativeLibs/libExampleModule.dylib'
  install_modules_dependencies(s)

  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES',
    'HEADER_SEARCH_PATHS' => [
      '$(PODS_TARGET_SRCROOT)/native/include',
      '$(PODS_TARGET_SRCROOT)/native/packages/jsi/include'
    ].join(' '),
  }

  native_headers = [
    'native/include/**/*.h',
    'native/packages/jsi/include/**/*.{h,hpp}',
  ]

  s.private_header_files = native_headers
  s.ios.source_files = native_headers + ['ios/**/*.{m,mm,swift,cpp,h,hpp}']
  s.ios.private_header_files = 'ios/**/*.{h,hpp}'
  s.tvos.source_files = native_headers + ['ios/**/*.{m,mm,swift,cpp,h,hpp}']
  s.tvos.private_header_files = 'ios/**/*.{h,hpp}'
  s.osx.source_files = native_headers + [
    'ios/ExpoModulesDotnetModule.swift',
    'ios/ExpoJsiBridgeForward.cpp',
    'ios/ReactNativeRuntimeConnectorForward.cpp',
    'macos/**/*.{m,mm,swift,cpp,h,hpp}',
  ]
  s.osx.private_header_files = 'macos/**/*.{h,hpp}'
end
