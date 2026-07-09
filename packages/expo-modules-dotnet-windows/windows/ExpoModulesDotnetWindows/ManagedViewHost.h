#pragma once

#include "ManagedLoader.h"

namespace expo::modules::dotnet::windows {

struct DotnetViewPropDefinition {
  std::string name;
  std::string kind;
};

struct DotnetViewDefinition {
  std::string moduleName;
  std::string componentName;
  std::vector<DotnetViewPropDefinition> props;
};

class ManagedViewHost final {
 public:
  static ManagedViewHost &Instance();

  const std::vector<DotnetViewDefinition> &ViewDefinitions();
  void *CreateView(const std::string &componentName);
  intptr_t InitializeComposition(void *viewHandle, intptr_t compositor) noexcept;
  void UpdateLayout(void *viewHandle, float width, float height) noexcept;
  void UpdateStringProp(
    void *viewHandle,
    const std::string &componentName,
    const std::string &propName,
    const std::optional<std::string> &value) noexcept;
  void DestroyView(void *viewHandle) noexcept;

 private:
  ManagedViewHost() = default;
  void EnsureInitialized();

  std::once_flag initFlag_;
  std::vector<DotnetViewDefinition> viewDefinitions_;
  expo::modules::dotnet::ManagedWindowsViewEntryPoints entryPoints_;
};

} // namespace expo::modules::dotnet::windows
