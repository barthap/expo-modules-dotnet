#include "pch.h"

#include "ExpoDotnetViewManager.h"

#include "ManagedViewHost.h"

#include <sstream>

namespace expo::modules::dotnet::windows {
namespace {

using namespace winrt;
using namespace winrt::Microsoft::ReactNative;

std::mutex g_handlesMutex;
std::unordered_map<int64_t, void *> g_handlesByTag;

std::mutex g_visualsMutex;
std::unordered_map<int64_t, winrt::Microsoft::UI::Composition::Visual> g_visualsByTag;

void logMessage(const std::string &message) noexcept
{
  OutputDebugStringA("[ExpoModulesDotnetWindows] ");
  OutputDebugStringA(message.c_str());
  OutputDebugStringA("\n");
}

void storeHandleForTag(int64_t tag, void *handle)
{
  std::lock_guard<std::mutex> lock(g_handlesMutex);
  g_handlesByTag[tag] = handle;
}

void *takeHandleForTag(int64_t tag)
{
  std::lock_guard<std::mutex> lock(g_handlesMutex);
  auto it = g_handlesByTag.find(tag);
  if (it == g_handlesByTag.end()) {
    return nullptr;
  }
  auto *handle = it->second;
  g_handlesByTag.erase(it);
  return handle;
}

void *handleForTag(int64_t tag)
{
  std::lock_guard<std::mutex> lock(g_handlesMutex);
  auto it = g_handlesByTag.find(tag);
  return it == g_handlesByTag.end() ? nullptr : it->second;
}

void storeVisualForTag(int64_t tag, winrt::Microsoft::UI::Composition::Visual const &visual)
{
  std::lock_guard<std::mutex> lock(g_visualsMutex);
  g_visualsByTag.insert_or_assign(tag, visual);
}

winrt::Microsoft::UI::Composition::Visual visualForTag(int64_t tag)
{
  std::lock_guard<std::mutex> lock(g_visualsMutex);
  auto it = g_visualsByTag.find(tag);
  return it == g_visualsByTag.end() ? winrt::Microsoft::UI::Composition::Visual{nullptr}
                                    : it->second;
}

void eraseVisualForTag(int64_t tag)
{
  std::lock_guard<std::mutex> lock(g_visualsMutex);
  g_visualsByTag.erase(tag);
}

bool runOnUiDispatcher(IReactDispatcher const &dispatcher, std::function<void()> action) noexcept
{
  try {
    if (!dispatcher || dispatcher.HasThreadAccess()) {
      action();
      return true;
    }

    auto done = std::make_shared<std::promise<void>>();
    auto error = std::make_shared<std::exception_ptr>();
    auto future = done->get_future();

    dispatcher.Post([action = std::move(action), done, error]() noexcept {
      try {
        action();
      } catch (...) {
        *error = std::current_exception();
      }
      done->set_value();
    });

    future.wait();
    if (*error) {
      std::rethrow_exception(*error);
    }
    return true;
  } catch (...) {
    return false;
  }
}

struct DotnetViewProps : implements<DotnetViewProps, IComponentProps> {
  explicit DotnetViewProps(std::unordered_set<std::string> allowedProps)
    : allowedProps_(std::move(allowedProps))
  {
  }

  void SetProp(uint32_t, hstring const &propName, IJSValueReader const &value)
  {
    auto name = to_string(propName);
    if (!allowedProps_.empty() && allowedProps_.find(name) == allowedProps_.end()) {
      return;
    }

    if (value.ValueType() == JSValueType::Null) {
      values_[name] = std::nullopt;
      return;
    }

    if (value.ValueType() == JSValueType::String) {
      values_[name] = to_string(value.GetString());
    }
  }

  void CopyFrom(DotnetViewProps const &previous)
  {
    values_ = previous.values_;
  }

  const std::unordered_map<std::string, std::optional<std::string>> &Values() const
  {
    return values_;
  }

private:
  std::unordered_set<std::string> allowedProps_;
  std::unordered_map<std::string, std::optional<std::string>> values_;
};

void updateProps(winrt::Microsoft::ReactNative::ComponentView const &source,
                 const std::string &componentName,
                 IComponentProps const &newProps)
{
  auto *viewHandle = handleForTag(source.Tag());
  if (viewHandle == nullptr) {
    return;
  }

  auto *props = get_self<DotnetViewProps>(newProps);
  if (props == nullptr) {
    return;
  }

  auto dispatcher = source.ReactContext().UIDispatcher();
  auto reactContext = source.ReactContext();
  auto values = props->Values();
  runOnUiDispatcher(dispatcher,
                    [reactContext, viewHandle, componentName, values = std::move(values)]() {
                      auto &host = ManagedViewHost::Instance();
                      for (const auto &[name, value] : values) {
                        host.UpdateStringProp(reactContext, viewHandle, componentName, name, value);
                      }
                    });
}

void updateLayout(winrt::Microsoft::ReactNative::ComponentView const &source,
                  winrt::Microsoft::ReactNative::LayoutMetrics const &layoutMetrics)
{
  auto *viewHandle = handleForTag(source.Tag());
  if (viewHandle == nullptr) {
    return;
  }

  const auto width = layoutMetrics.Frame.Width;
  const auto height = layoutMetrics.Frame.Height;
  if (width <= 0 || height <= 0) {
    return;
  }

  auto dispatcher = source.ReactContext().UIDispatcher();
  runOnUiDispatcher(dispatcher, [tag = source.Tag(), viewHandle, width, height]() {
    if (auto visual = visualForTag(tag)) {
      visual.Size({width, height});
    }
    ManagedViewHost::Instance().UpdateLayout(viewHandle, width, height);
  });
}

winrt::Microsoft::UI::Composition::Visual createVisual(
  const std::string &componentName, winrt::Microsoft::ReactNative::ComponentView const &source)
{
  auto compositionView = source.try_as<winrt::Microsoft::ReactNative::Composition::ComponentView>();
  if (!compositionView) {
    return nullptr;
  }

  auto *viewHandle = ManagedViewHost::Instance().CreateView(source.ReactContext(), componentName);
  if (viewHandle == nullptr) {
    return nullptr;
  }

  void *compositorPtr = nullptr;
  copy_to_abi(compositionView.Compositor(), compositorPtr);
  auto visualPtr = ManagedViewHost::Instance().InitializeComposition(
    viewHandle, reinterpret_cast<intptr_t>(compositorPtr));
  if (visualPtr == 0) {
    ManagedViewHost::Instance().DestroyView(viewHandle);
    return nullptr;
  }

  winrt::Microsoft::UI::Composition::Visual visual{nullptr};
  attach_abi(visual, reinterpret_cast<void *>(visualPtr));
  storeHandleForTag(source.Tag(), viewHandle);
  storeVisualForTag(source.Tag(), visual);
  return visual;
}

} // namespace

void RegisterDotnetViewComponents(IReactPackageBuilder const &packageBuilder) noexcept
{
  try {
    auto fabricBuilder = packageBuilder.try_as<IReactPackageBuilderFabric>();
    if (!fabricBuilder) {
      return;
    }

    for (const auto &definition : ManagedViewHost::Instance().ViewDefinitions()) {
      const auto componentName = definition.componentName;
      std::unordered_set<std::string> propNames;
      for (const auto &prop : definition.props) {
        if (prop.kind == "String") {
          propNames.insert(prop.name);
        }
      }

      fabricBuilder.AddViewComponent(
        winrt::to_hstring(componentName),
        [componentName,
         propNames = std::move(propNames)](IReactViewComponentBuilder const &viewBuilder) {
          viewBuilder.SetCreateProps(
            [propNames](ViewProps const &, IComponentProps const &cloneFrom) {
              auto props = make_self<DotnetViewProps>(propNames);
              if (cloneFrom) {
                if (auto *previous = get_self<DotnetViewProps>(cloneFrom)) {
                  props->CopyFrom(*previous);
                }
              }
              return props.as<IComponentProps>();
            });

          viewBuilder.SetUpdatePropsHandler(
            [componentName](winrt::Microsoft::ReactNative::ComponentView const &source,
                            IComponentProps const &newProps,
                            IComponentProps const &) {
              updateProps(source, componentName, newProps);
            });

          auto compositionBuilder = viewBuilder.try_as<
            winrt::Microsoft::ReactNative::Composition::IReactCompositionViewComponentBuilder>();
          if (!compositionBuilder) {
            return;
          }

          compositionBuilder.SetViewFeatures(
            winrt::Microsoft::ReactNative::Composition::ComponentViewFeatures::Default &
            ~winrt::Microsoft::ReactNative::Composition::ComponentViewFeatures::Background);

          compositionBuilder.SetUpdateLayoutMetricsHandler(
            [](winrt::Microsoft::ReactNative::ComponentView const &source,
               winrt::Microsoft::ReactNative::LayoutMetrics const &newLayoutMetrics,
               winrt::Microsoft::ReactNative::LayoutMetrics const &) {
              updateLayout(source, newLayoutMetrics);
            });

          compositionBuilder.SetViewComponentViewInitializer(
            [](winrt::Microsoft::ReactNative::Composition::ViewComponentView const &view) {
              auto tag = view.Tag();
              auto dispatcher = view.ReactContext().UIDispatcher();
              view.Destroying([tag, dispatcher](auto const &, auto const &) {
                runOnUiDispatcher(dispatcher, [tag]() {
                  auto *viewHandle = takeHandleForTag(tag);
                  if (viewHandle != nullptr) {
                    ManagedViewHost::Instance().DestroyView(viewHandle);
                  }
                  eraseVisualForTag(tag);
                });
              });
            });

          compositionBuilder.SetCreateVisualHandler(
            [componentName](winrt::Microsoft::ReactNative::ComponentView const &source) {
              return createVisual(componentName, source);
            });
        });
    }
  } catch (const std::exception &ex) {
    logMessage(std::string("View registration failed: ") + ex.what());
  } catch (...) {
    logMessage("View registration failed.");
  }
}

} // namespace expo::modules::dotnet::windows
