# Podfile helper that wires the expo-modules-dotnet-autolinking CLI into the
# app's Xcode build. Call `use_expo_modules_dotnet!` inside the app target
# block, after `use_expo_modules!`.
require 'pathname'

def use_expo_modules_dotnet!(options = {})
  project_root = Pathname.new(options.fetch(:project_root, File.expand_path('..', __dir__)))
  # The script lands in the checked-in .xcodeproj, so it must reference the app
  # root relative to SRCROOT rather than an absolute machine-local path.
  relative_root = project_root.expand_path.relative_path_from(Pathname.pwd)
  script_phase(
    name: 'Link Expo .NET Modules',
    execution_position: :before_compile,
    always_out_of_date: '1',
    script: <<~SCRIPT
      set -euo pipefail
      app_root="${SRCROOT}/#{relative_root}"
      cd "$app_root"
      mode_args=()
      if [ -n "${EXPO_DOTNET_LOADER:-}" ]; then
        mode_args+=(--mode "$EXPO_DOTNET_LOADER")
      fi
      node --no-warnings --eval "require('expo-modules-dotnet-autolinking').main(process.argv.slice(1))" \
        link --platform macos --project-root "$app_root" ${mode_args[@]+"${mode_args[@]}"}
    SCRIPT
  )
end
