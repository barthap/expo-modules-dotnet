#pragma once

#include <functional>

#include <jsi/jsi.h>

namespace expo::jsi {

class JsiScheduler {
public:
  virtual ~JsiScheduler() = default;
  virtual void schedule(std::function<void()> work) = 0;
};

class ImmediateJsiScheduler final : public JsiScheduler {
public:
  void schedule(std::function<void()> work) override
  {
    work();
  }
};

class JsiRuntimeConnector {
public:
  virtual ~JsiRuntimeConnector() = default;

  virtual facebook::jsi::Runtime &runtime() = 0;
  virtual JsiScheduler &scheduler() = 0;
  virtual bool isRuntimeValid() const = 0;
  virtual void invalidate() = 0;
};

} // namespace expo::jsi
