#pragma once

#include <memory>

#include <jsi/jsi.h>

namespace expo::dotnet {

namespace jsi = facebook::jsi;

class RuntimeState;
class OwnedValueList;

// Opaque ABI value handle. An owned handle that holds an object, string,
// symbol, or bigint links into its runtime state's OwnedValueList, so runtime
// teardown can release or abandon its JSI value. After that, the handle reads
// as undefined and its destruction frees only native memory.
class ValueHandle final {
public:
  static std::unique_ptr<ValueHandle> owned(RuntimeState &state, jsi::Value value);

  static std::unique_ptr<ValueHandle> borrowed(const jsi::Value &value)
  {
    return std::unique_ptr<ValueHandle>(new ValueHandle(&value));
  }

  ~ValueHandle();
  ValueHandle(const ValueHandle &) = delete;
  ValueHandle &operator=(const ValueHandle &) = delete;

  const jsi::Value &value() const
  {
    if (ownedValue_ != nullptr) {
      return *ownedValue_;
    }
    if (borrowedValue_ != nullptr) {
      return *borrowedValue_;
    }
    static const jsi::Value undefined;
    return undefined;
  }

  bool isOwned() const noexcept
  {
    return owned_;
  }

private:
  friend class OwnedValueList;

  explicit ValueHandle(std::unique_ptr<jsi::Value> value)
    : ownedValue_(std::move(value)),
      owned_(true)
  {
  }

  explicit ValueHandle(const jsi::Value *value)
    : borrowedValue_(value)
  {
  }

  std::unique_ptr<jsi::Value> ownedValue_;
  const jsi::Value *borrowedValue_ = nullptr;
  bool owned_ = false;
  // Set once in owned() after the link succeeds and never changed, so
  // primitive handles can skip the link mutex in the destructor.
  bool linkedAtCreation_ = false;
  // Guarded by ownedValueLinkMutex(). Non-null while the handle is linked.
  OwnedValueList *list_ = nullptr;
  ValueHandle *prev_ = nullptr;
  ValueHandle *next_ = nullptr;
};

} // namespace expo::dotnet
