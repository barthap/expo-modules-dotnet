# Podfile helper that wires the expo-modules-dotnet-autolinking CLI into the
# app's Xcode build. Call `use_expo_modules_dotnet!` inside the app target
# block, after `use_expo_modules!`.
require 'pathname'

def use_expo_modules_dotnet!(options = {})
  project_root = Pathname.new(options.fetch(:project_root, File.expand_path('..', __dir__)))
  platform = options.fetch(:platform, :macos).to_sym
  unless [:macos, :ios].include?(platform)
    raise ArgumentError, "unsupported expo-modules-dotnet platform: #{platform.inspect}"
  end

  # The script lands in the checked-in .xcodeproj, so it must reference the app
  # root relative to SRCROOT rather than an absolute machine-local path.
  relative_root = project_root.expand_path.relative_path_from(Pathname.pwd)
  ios_bundle_script = if platform == :ios
    <<~SCRIPT
      frameworks_dir="${TARGET_BUILD_DIR}/${FRAMEWORKS_FOLDER_PATH}"
      mkdir -p "$frameworks_dir"
      cp "$app_root/ios/Managed/libExpoDotnetHost.dylib" "$frameworks_dir/"
      if [ -n "${EXPANDED_CODE_SIGN_IDENTITY:-}" ]; then
        /usr/bin/codesign --force --sign "$EXPANDED_CODE_SIGN_IDENTITY" \
          ${OTHER_CODE_SIGN_FLAGS:-} --preserve-metadata=identifier,entitlements \
          "$frameworks_dir/libExpoDotnetHost.dylib"
      fi
    SCRIPT
  else
    ''
  end

  script_phase(
    name: 'Link Expo .NET Modules',
    execution_position: :before_compile,
    always_out_of_date: '1',
    script: <<~SCRIPT
      set -euo pipefail
      app_root="${SRCROOT}/#{relative_root}"
      cd "$app_root"
      extra_args=()
      if [ -n "${EXPO_DOTNET_LOADER:-}" ]; then
        extra_args+=(--mode "$EXPO_DOTNET_LOADER")
      fi
      if [ -n "${CONFIGURATION:-}" ]; then
        extra_args+=(--configuration "$CONFIGURATION")
      fi
      node --no-warnings --eval "require('expo-modules-dotnet-autolinking').main(process.argv.slice(1))" \
        link --platform #{platform} --project-root "$app_root" ${extra_args[@]+"${extra_args[@]}"}
      #{ios_bundle_script}
    SCRIPT
  )
end
