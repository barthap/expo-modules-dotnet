Repo: <https://github.com/shirakaba/expo-desktop>

## Upstream Gaps

Problems found in expo-desktop while integrating it. None of them is filed
upstream yet. Each entry names the local workaround and holds a draft report.

### Template Podfile cannot find `react-native-macos` under pnpm

- Found: 2026-10-22, roadmap item 039.
- Template: `expo-desktop-template-bare-minimum@54.81.5`, `macos/Podfile`.
- Workaround: `apps/desktop-app/plugins/withDesktopAppFixes.js` replaces the
  `:path` value with a Node-resolved path.
- Status: not filed.

Draft report:

> **`pod install` fails for the macOS template in a pnpm workspace**
>
> The `macos/Podfile` in `expo-desktop-template-bare-minimum@54.81.5` builds
> the `react-native-macos` path from the `react-native` path:
>
> ```ruby
> use_react_native!(
>   :path => "#{config[:reactNativePath]}-macos",
> ```
>
> This works when `react-native` and `react-native-macos` are siblings in one
> `node_modules`. pnpm resolves `react-native` to its own folder in the store,
> and that folder has no `react-native-macos` sibling. `pod install` then
> fails:
>
> ```text
> [!] Invalid `Podfile` file: Couldn't find the React Native package.json file at ./../../../node_modules/.pnpm/react-native@0.81.6_<hash>/node_modules/react-native-macos/package.json.
> ```
>
> The same Podfile already resolves `react_native_pods.rb` by walking up to
> `node_modules/react-native-macos`. Resolving the `:path` through Node fixes
> it in our pnpm workspace:
>
> ```ruby
> :path => Pathname.new(File.dirname(`node --print "require.resolve('react-native-macos/package.json')"`)).relative_path_from(Pathname.pwd).to_s,
> ```
