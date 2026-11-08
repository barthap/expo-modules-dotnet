#include "RuntimeState.h"

#include <stdexcept>

#include "ValueHandle.h"

namespace expo::dotnet {

std::shared_ptr<RuntimeState> RuntimeState::create(JsiRuntimeConnector &connector)
{
  // The constructor is private so callers must establish the collection's
  // weak back-reference through this factory.
  auto state = std::shared_ptr<RuntimeState>(new RuntimeState(connector));
  state->longLivedObjects_.setRuntimeState(state);
  return state;
}

RuntimeState::RuntimeState(JsiRuntimeConnector &connector)
  : connector_(&connector)
{
}

jsi::Runtime &RuntimeState::runtime()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (state_.load(std::memory_order_acquire) != State::Active || connector_ == nullptr ||
      !connector_->isRuntimeValid()) {
    throw std::runtime_error("Runtime connector is invalid.");
  }
  return connector_->runtime();
}

JsiRuntimeExecutor &RuntimeState::executor()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (state_.load(std::memory_order_acquire) != State::Active || connector_ == nullptr ||
      !connector_->isRuntimeValid()) {
    throw std::runtime_error("Runtime connector is invalid.");
  }
  return connector_->runtimeExecutor();
}

void RuntimeState::drainDeferredReleases(jsi::Runtime &runtime) noexcept
{
  longLivedObjects_.drainDeferredReleases(runtime);
}

void RuntimeState::prepareForInvalidation()
{
  State expected = State::Active;
  if (!state_.compare_exchange_strong(expected, State::Closing, std::memory_order_acq_rel)) {
    return;
  }

  JsiRuntimeConnector *connector = nullptr;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    connector = connector_;
  }
  if (connector == nullptr || !connector->isRuntimeValid()) {
    invalidateWithoutRuntime();
    return;
  }

  try {
    connector->runtimeExecutor().executeSync([this](jsi::Runtime &runtime) {
      longLivedObjects_.sweep(runtime);
      ownedValues_.sweep();
    });
  } catch (...) {
    invalidateWithoutRuntime();
    return;
  }
  invalidateWithoutRuntime();
}

void RuntimeState::invalidateWithoutRuntime() noexcept
{
  State previous = state_.exchange(State::Invalid, std::memory_order_acq_rel);
  if (previous == State::Invalid) {
    return;
  }
  longLivedObjects_.invalidateWithoutRuntime();
  ownedValues_.abandonAll();
  std::lock_guard<std::mutex> lock(mutex_);
  connector_ = nullptr;
}

void RuntimeState::releaseLongLivedObject(uint64_t id) noexcept
{
  bool shouldInvalidateWithoutRuntime = false;
  {
    // Keep the connector borrow protected through executeAsync. A concurrent
    // prepare waits on this mutex before it can sweep, invalidate, and let the
    // host destroy the connector.
    std::lock_guard<std::mutex> lock(mutex_);
    if (state_.load(std::memory_order_acquire) == State::Active) {
      try {
        if (connector_ != nullptr && connector_->isRuntimeValid()) {
          longLivedObjects_.requestRelease(id, connector_->runtimeExecutor());
          return;
        }
      } catch (...) {
        // Preserve the late-invalidation fallback below.
      }
      shouldInvalidateWithoutRuntime = true;
    }
  }

  if (shouldInvalidateWithoutRuntime && tryInvalidateWithoutRuntime()) {
    return;
  }
  if (isValid()) {
    longLivedObjects_.deferRelease(id);
  } else {
    longLivedObjects_.invalidateWithoutRuntime();
  }
}

bool RuntimeState::tryInvalidateWithoutRuntime() noexcept
{
  State expected = State::Active;
  if (!state_.compare_exchange_strong(expected, State::Invalid, std::memory_order_acq_rel)) {
    return false;
  }
  longLivedObjects_.invalidateWithoutRuntime();
  ownedValues_.abandonAll();
  std::lock_guard<std::mutex> lock(mutex_);
  connector_ = nullptr;
  return true;
}

LongLivedObjectCollection &RuntimeState::longLivedObjects() noexcept
{
  return longLivedObjects_;
}

OwnedValueList &RuntimeState::ownedValues() noexcept
{
  return ownedValues_;
}

// Called with ownedValueLinkMutex() held, so this takes mutex_ second, in the
// documented lock order. It only reads: changing state here would call
// abandonAll(), which takes the link mutex again.
//
// The reverse order cannot happen today. releaseLongLivedObject holds mutex_
// through requestRelease, but no LongLivedObject type (PromiseHandles.h,
// WeakObjectHandles.h, ArrayBufferHandles.h) creates or destroys a
// ValueHandle. A change that puts a ValueHandle inside a long-lived entry must
// revisit this.
//
// Closing counts as safe: the host destroys the runtime only after
// preparation returns.
bool RuntimeState::canReleaseOwnedValue() noexcept
{
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    return state_.load(std::memory_order_acquire) != State::Invalid && connector_ != nullptr &&
           connector_->isRuntimeValid();
  } catch (...) {
    return false;
  }
}

bool RuntimeState::isActive() const noexcept
{
  return state_.load(std::memory_order_acquire) == State::Active;
}

bool RuntimeState::isValid() const noexcept
{
  return state_.load(std::memory_order_acquire) != State::Invalid;
}

void RuntimeState::noteArrayBufferReleased() noexcept
{
  released_.fetch_add(1, std::memory_order_relaxed);
}

void RuntimeState::noteArrayBufferAbandoned() noexcept
{
  abandoned_.fetch_add(1, std::memory_order_relaxed);
}

uint32_t RuntimeState::arrayBuffersReleased() const noexcept
{
  return released_.load();
}

uint32_t RuntimeState::arrayBuffersAbandoned() const noexcept
{
  return abandoned_.load();
}

void RuntimeState::resetArrayBufferCounters() noexcept
{
  released_.store(0, std::memory_order_relaxed);
  abandoned_.store(0, std::memory_order_relaxed);
}

void RuntimeState::noteWeakObjectReleased() noexcept
{
  weakReleased_.fetch_add(1, std::memory_order_relaxed);
}

void RuntimeState::noteWeakObjectAbandoned() noexcept
{
  weakAbandoned_.fetch_add(1, std::memory_order_relaxed);
}

uint32_t RuntimeState::weakObjectsReleased() const noexcept
{
  return weakReleased_.load(std::memory_order_relaxed);
}

uint32_t RuntimeState::weakObjectsAbandoned() const noexcept
{
  return weakAbandoned_.load(std::memory_order_relaxed);
}

void RuntimeState::resetWeakObjectCounters() noexcept
{
  weakReleased_.store(0, std::memory_order_relaxed);
  weakAbandoned_.store(0, std::memory_order_relaxed);
}

void RuntimeState::notePromiseReleased() noexcept
{
  promiseReleased_.fetch_add(1, std::memory_order_relaxed);
}

void RuntimeState::notePromiseAbandoned() noexcept
{
  promiseAbandoned_.fetch_add(1, std::memory_order_relaxed);
}

uint32_t RuntimeState::promisesReleased() const noexcept
{
  return promiseReleased_.load(std::memory_order_relaxed);
}

uint32_t RuntimeState::promisesAbandoned() const noexcept
{
  return promiseAbandoned_.load(std::memory_order_relaxed);
}

void RuntimeState::resetPromiseCounters() noexcept
{
  promiseReleased_.store(0, std::memory_order_relaxed);
  promiseAbandoned_.store(0, std::memory_order_relaxed);
}

uint32_t RuntimeState::longLivedObjectCount() const noexcept
{
  return longLivedObjects_.size();
}

std::mutex &ownedValueLinkMutex() noexcept
{
  // Never destroyed: a managed wrapper may release its handle while static
  // destructors run at process exit.
  static auto *mutex = new std::mutex();
  return *mutex;
}

OwnedValueList::OwnedValueList(RuntimeState &state) noexcept
  : state_(state)
{
}

OwnedValueList::~OwnedValueList()
{
  // Handles can outlive the runtime state. Leave none pointing at this list.
  abandonAll();
}

void OwnedValueList::link(ValueHandle &handle)
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  if (closed_) {
    throw std::runtime_error("Runtime connector is invalid.");
  }
  handle.list_ = this;
  handle.prev_ = nullptr;
  handle.next_ = head_;
  if (head_ != nullptr) {
    head_->prev_ = &handle;
  }
  head_ = &handle;
  ++tracked_;
}

void OwnedValueList::unlinkLocked(ValueHandle &handle) noexcept
{
  if (handle.prev_ != nullptr) {
    handle.prev_->next_ = handle.next_;
  } else {
    head_ = handle.next_;
  }
  if (handle.next_ != nullptr) {
    handle.next_->prev_ = handle.prev_;
  }
  handle.list_ = nullptr;
  handle.prev_ = nullptr;
  handle.next_ = nullptr;
  --tracked_;
}

void OwnedValueList::unlinkOnDestroy(ValueHandle &handle) noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  auto *list = handle.list_;
  if (list == nullptr) {
    return;
  }
  list->unlinkLocked(handle);
  if (list->state_.canReleaseOwnedValue()) {
    // Destroy under the link mutex, so a release never overlaps a sweep.
    handle.ownedValue_.reset();
  } else {
    // JSI is no longer valid, so the value's destructor must never run.
    (void)handle.ownedValue_.release();
    ++list->abandoned_;
  }
}

void OwnedValueList::sweep() noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  closed_ = true;
  while (head_ != nullptr) {
    auto &handle = *head_;
    // Runs on the runtime thread while JSI is still valid.
    handle.ownedValue_.reset();
    unlinkLocked(handle);
    ++swept_;
  }
}

void OwnedValueList::abandonAll() noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  closed_ = true;
  while (head_ != nullptr) {
    auto &handle = *head_;
    // JSI is no longer valid, so the value's destructor must never run.
    (void)handle.ownedValue_.release();
    unlinkLocked(handle);
    ++abandoned_;
  }
}

uint32_t OwnedValueList::tracked() const noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  return tracked_;
}

uint32_t OwnedValueList::swept() const noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  return swept_;
}

uint32_t OwnedValueList::abandoned() const noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  return abandoned_;
}

void OwnedValueList::resetCounters() noexcept
{
  std::lock_guard<std::mutex> lock(ownedValueLinkMutex());
  swept_ = 0;
  abandoned_ = 0;
}

std::unique_ptr<ValueHandle> ValueHandle::owned(RuntimeState &state, jsi::Value value)
{
  // A primitive jsi::Value holds no runtime pointer, so its destructor never
  // touches the runtime and it needs no teardown tracking.
  const bool needsLink =
    !(value.isUndefined() || value.isNull() || value.isBool() || value.isNumber());
  auto handle =
    std::unique_ptr<ValueHandle>(new ValueHandle(std::make_unique<jsi::Value>(std::move(value))));
  if (needsLink) {
    state.ownedValues().link(*handle);
    handle->linkedAtCreation_ = true;
  }
  return handle;
}

ValueHandle::~ValueHandle()
{
  if (linkedAtCreation_) {
    OwnedValueList::unlinkOnDestroy(*this);
  }
}

} // namespace expo::dotnet
