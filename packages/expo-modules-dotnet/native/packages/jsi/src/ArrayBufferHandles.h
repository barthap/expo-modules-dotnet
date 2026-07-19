#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>
#include <stdexcept>
#include <vector>

#include <jsi/jsi.h>

#include "RuntimeState.h"

namespace expo::dotnet {

class OwnedMutableBuffer final : public jsi::MutableBuffer {
public:
  explicit OwnedMutableBuffer(size_t size)
    : bytes_(size, 0)
  {
  }

  explicit OwnedMutableBuffer(std::span<const uint8_t> bytes)
    : bytes_(bytes.begin(), bytes.end())
  {
  }

  size_t size() const override
  {
    return bytes_.size();
  }
  uint8_t *data() override
  {
    return bytes_.data();
  }

private:
  std::vector<uint8_t> bytes_;
};

class MutableBufferHandle final {
public:
  explicit MutableBufferHandle(std::shared_ptr<jsi::MutableBuffer> buffer)
    : buffer_(std::move(buffer))
  {
    if (buffer_ == nullptr) {
      throw std::invalid_argument("MutableBuffer is null.");
    }
  }

  std::shared_ptr<jsi::MutableBuffer> buffer() const noexcept
  {
    return buffer_;
  }
  size_t size() const noexcept
  {
    return buffer_->size();
  }

private:
  std::shared_ptr<jsi::MutableBuffer> buffer_;
};

class ArrayBufferEntry final : public LongLivedObject {
public:
  // The collection owns entries, while each entry keeps the runtime tombstone
  // alive until every managed handle lease is released. Collection transitions
  // erase the entry and break this cycle on both release and abandonment.
  ArrayBufferEntry(std::shared_ptr<RuntimeState> state,
                   std::unique_ptr<jsi::ArrayBuffer> buffer,
                   size_t byteLength)
    : state_(std::move(state)),
      buffer_(std::move(buffer)),
      byteLength_(byteLength)
  {
  }

  bool tryRetainLease() noexcept
  {
    auto leases = leases_.load(std::memory_order_acquire);
    while (leases != 0 && !released_.load(std::memory_order_acquire)) {
      if (leases_.compare_exchange_weak(
            leases, leases + 1, std::memory_order_acq_rel, std::memory_order_acquire)) {
        return true;
      }
    }
    return false;
  }

  bool releaseLease() noexcept
  {
    auto leases = leases_.load(std::memory_order_acquire);
    while (leases != 0) {
      if (leases_.compare_exchange_weak(
            leases, leases - 1, std::memory_order_acq_rel, std::memory_order_acquire)) {
        return leases == 1;
      }
    }
    return false;
  }

  bool isLive() const noexcept
  {
    return state_->isActive() && buffer_ != nullptr && !released_.load(std::memory_order_acquire);
  }

  size_t byteLength() const noexcept
  {
    return byteLength_;
  }

  jsi::ArrayBuffer &buffer()
  {
    if (!isLive()) {
      throw std::runtime_error("ArrayBuffer storage is no longer valid.");
    }
    return *buffer_;
  }

  void release(jsi::Runtime &) noexcept override
  {
    if (released_.exchange(true, std::memory_order_acq_rel)) {
      return;
    }
    buffer_.reset();
    state_->noteArrayBufferReleased();
  }

  void abandon() noexcept override
  {
    if (released_.exchange(true, std::memory_order_acq_rel)) {
      return;
    }
    // Deliberately leak the JSI wrapper: its destructor is runtime-affine.
    (void)buffer_.release();
    state_->noteArrayBufferAbandoned();
  }

private:
  std::shared_ptr<RuntimeState> state_;
  std::unique_ptr<jsi::ArrayBuffer> buffer_;
  size_t byteLength_;
  std::atomic<uint32_t> leases_{1};
  std::atomic<bool> released_{false};
};

class ArrayBufferHandle final {
public:
  ArrayBufferHandle(std::shared_ptr<RuntimeState> state,
                    std::shared_ptr<ArrayBufferEntry> entry,
                    uint64_t entryId)
    : state_(std::move(state)),
      entry_(std::move(entry)),
      entryId_(entryId)
  {
  }

  ~ArrayBufferHandle();

  std::unique_ptr<ArrayBufferHandle> clone() const;
  std::shared_ptr<RuntimeState> state() const noexcept
  {
    return state_;
  }
  std::shared_ptr<ArrayBufferEntry> entry() const noexcept
  {
    return entry_;
  }
  uint64_t entryId() const noexcept
  {
    return entryId_;
  }

private:
  std::shared_ptr<RuntimeState> state_;
  std::shared_ptr<ArrayBufferEntry> entry_;
  uint64_t entryId_;
};

} // namespace expo::dotnet
