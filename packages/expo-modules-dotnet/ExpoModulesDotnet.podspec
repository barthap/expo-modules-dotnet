Pod::Spec.new do |s|
  s.name           = 'ExpoModulesDotnet'
  s.version        = '0.1.0'
  s.summary        = 'Expo adapter for .NET-backed Expo modules'
  s.description    = 'Expo adapter for .NET-backed Expo modules'
  s.author         = ''
  s.homepage       = 'https://docs.expo.dev/modules/'
  s.platforms      = {
    :ios => '15.1',
    :tvos => '15.1'
  }
  s.source         = { git: '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'
  s.vendored_libraries = 'ios/NativeLibs/libExampleModule.dylib'
  install_modules_dependencies(s)

  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES',
    'HEADER_SEARCH_PATHS' => [
      '$(PODS_TARGET_SRCROOT)/native/include',
      '$(PODS_TARGET_SRCROOT)/native/packages/jsi/include'
    ].join(' '),
  }

  s.source_files = 'ios/**/*.{h,m,mm,swift,hpp,cpp}'
end
