#pragma once

#include <cstdint>
#include <mutex>

namespace expo::dotnet {

class RuntimeState;
class ValueHandle;

// Guards the link state of every owned value handle in the process. A handle
// can outlive its RuntimeState, so this lock cannot belong to the state.
//
// Lock order: this mutex first, RuntimeState::mutex_ second. No code path may
// hold RuntimeState::mutex_ while it takes this mutex. The only nested lock is
// OwnedValueList::unlinkOnDestroy, which asks RuntimeState whether JSI is
// still safe to touch.
std::mutex &ownedValueLinkMutex() noexcept;

// Intrusive list of the owned non-primitive value handles of one runtime. It
// tracks handles without owning them: managed wrappers own the handles. At
// teardown, sweep() destroys their JSI values on the runtime thread, or
// abandonAll() leaks them when JSI is no longer valid. Either step closes the
// list, and a closed list rejects new links.
class OwnedValueList final {
public:
  explicit OwnedValueList(RuntimeState &state) noexcept;
  ~OwnedValueList();
  OwnedValueList(const OwnedValueList &) = delete;
  OwnedValueList &operator=(const OwnedValueList &) = delete;

  void link(ValueHandle &handle);
  static void unlinkOnDestroy(ValueHandle &handle) noexcept;
  void sweep() noexcept;
  void abandonAll() noexcept;
  uint32_t tracked() const noexcept;
  uint32_t swept() const noexcept;
  uint32_t abandoned() const noexcept;
  void resetCounters() noexcept;

private:
  void unlinkLocked(ValueHandle &handle) noexcept;

  RuntimeState &state_;
  // All fields below are guarded by ownedValueLinkMutex().
  ValueHandle *head_ = nullptr;
  bool closed_ = false;
  uint32_t tracked_ = 0;
  uint32_t swept_ = 0;
  uint32_t abandoned_ = 0;
};

} // namespace expo::dotnet
