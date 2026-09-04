#include "projectf/core/entity_manager.hpp"

#include <limits>

namespace projectf::core {

EntityId EntityManager::allocate(EntityKind kind_value, std::uint32_t dense_index_value) {
    if (kind_value == EntityKind::none) {
        return {};
    }

    std::uint32_t index;
    if (!free_indices_.empty()) {
        index = free_indices_.back();
        free_indices_.pop_back();
    } else {
        if (slots_.size() >= EntityId::invalid_index) {
            return {};
        }
        track_growth_before_push(slots_);
        index = static_cast<std::uint32_t>(slots_.size());
        slots_.push_back({});
    }

    Slot &slot = slots_[index];
    slot.alive = true;
    slot.kind = kind_value;
    slot.dense_index = dense_index_value;
    ++active_count_;
    return {index, slot.generation};
}

bool EntityManager::release(EntityId id) noexcept {
    if (!is_valid(id)) {
        return false;
    }

    Slot &slot = slots_[id.index];
    slot.alive = false;
    slot.kind = EntityKind::none;
    slot.dense_index = 0;
    ++slot.generation;
    if (slot.generation == 0) {
        slot.generation = 1;
    }
    track_growth_before_push(free_indices_);
    free_indices_.push_back(id.index);
    --active_count_;
    return true;
}

bool EntityManager::is_valid(EntityId id) const noexcept {
    return id.is_valid()
            && id.index < slots_.size()
            && slots_[id.index].alive
            && slots_[id.index].generation == id.generation;
}

EntityKind EntityManager::kind(EntityId id) const noexcept {
    return is_valid(id) ? slots_[id.index].kind : EntityKind::none;
}

std::uint32_t EntityManager::dense_index(EntityId id) const noexcept {
    return is_valid(id) ? slots_[id.index].dense_index : EntityId::invalid_index;
}

bool EntityManager::update_dense_index(EntityId id, std::uint32_t dense_index_value) noexcept {
    if (!is_valid(id)) {
        return false;
    }
    slots_[id.index].dense_index = dense_index_value;
    return true;
}

void EntityManager::reset() noexcept {
    slots_.clear();
    free_indices_.clear();
    active_count_ = 0;
    capacity_growth_count_ = 0;
}

void EntityManager::reserve(std::size_t capacity) {
    if (capacity > slots_.capacity()) {
        slots_.reserve(capacity);
        ++capacity_growth_count_;
    }
    if (capacity > free_indices_.capacity()) {
        free_indices_.reserve(capacity);
        ++capacity_growth_count_;
    }
}

std::size_t EntityManager::reserved_bytes() const noexcept {
    return slots_.capacity() * sizeof(Slot)
            + free_indices_.capacity() * sizeof(std::uint32_t);
}

} // namespace projectf::core
