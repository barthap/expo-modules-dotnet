#pragma once

#include <memory>

#include <hermes/hermes.h>

#include "JsiRuntimeConnector.h"

namespace expo::jsi {

class HermesConsoleRuntimeConnector final : public JsiRuntimeConnector {
public:
  HermesConsoleRuntimeConnector();
  ~HermesConsoleRuntimeConnector() override;

  facebook::jsi::Runtime &runtime() override;
  JsiScheduler &scheduler() override;
  bool isRuntimeValid() const override;
  void invalidate() override;

private:
  ImmediateJsiScheduler scheduler_;
  std::unique_ptr<facebook::jsi::Runtime> runtime_;
};

} // namespace expo::jsi
