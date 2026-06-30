Pod::Spec.new do |s|
  s.name           = 'ExpoCSharpV2'
  s.version        = '1.0.0'
  s.summary        = 'A sample project summary'
  s.description    = 'A sample project description'
  s.author         = ''
  s.homepage       = 'https://docs.expo.dev/modules/'
  s.platforms      = {
    :ios => '15.1',
    :tvos => '15.1'
  }
  s.source         = { git: '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'
  s.vendored_libraries = "NativeLibs/libExpoMobileV2Module.dylib"

  # Swift/Objective-C compatibility
  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES',
    'HEADER_SEARCH_PATHS' => [
      '$(PODS_TARGET_SRCROOT)/../../../../../native/include',
      '$(PODS_TARGET_SRCROOT)/../../../../../native/packages/jsi/include'
    ].join(' '),
  }

  s.source_files = "**/*.{h,m,mm,swift,hpp,cpp}"
end
