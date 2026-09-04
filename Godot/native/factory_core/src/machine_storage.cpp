#include "projectf/core/machine_storage.hpp"

#include <algorithm>

namespace projectf::core {

std::uint32_t MachineStorage::append(EntityId id, const MachineCreateDesc &description) {
    const auto dense_index = static_cast<std::uint32_t>(states_.size());
    MachineState state;
    state.transform = description.transform;
    state.inventory_index = description.inventory_index;
    state.recipe_duration_ticks = std::max<std::uint32_t>(1, description.recipe_duration_ticks);
    state.recipe_id = description.recipe_id;
    state.speed = std::max<std::uint16_t>(1, description.speed);
    state.flags = machine_powered | (description.active ? machine_active : machine_none);

    track_growth_before_push(states_);
    states_.push_back(state);
    track_growth_before_push(entities_);
    entities_.push_back(id);
    track_growth_before_push(active_positions_);
    active_positions_.push_back(inactive_position);
    if (description.active) {
        add_active(dense_index);
    }
    return dense_index;
}

MachineStorage::RemoveResult MachineStorage::remove(std::uint32_t dense_index) noexcept {
    if (dense_index >= states_.size()) {
        return {};
    }

    remove_active(dense_index);
    const std::uint32_t last_index = static_cast<std::uint32_t>(states_.size() - 1);
    RemoveResult result;
    result.removed = true;
    if (dense_index != last_index) {
        states_[dense_index] = states_[last_index];
        entities_[dense_index] = entities_[last_index];
        active_positions_[dense_index] = active_positions_[last_index];
        if (active_positions_[dense_index] != inactive_position) {
            active_indices_[active_positions_[dense_index]] = dense_index;
        }
        result.moved_entity = entities_[dense_index];
        result.moved_dense_index = dense_index;
    }

    states_.pop_back();
    entities_.pop_back();
    active_positions_.pop_back();
    return result;
}

bool MachineStorage::set_active(std::uint32_t dense_index, bool active) noexcept {
    if (dense_index >= states_.size() || active == is_active(dense_index)) {
        return dense_index < states_.size();
    }

    if (active) {
        states_[dense_index].flags |= machine_active;
        add_active(dense_index);
    } else {
        states_[dense_index].flags &= static_cast<std::uint16_t>(~machine_active);
        remove_active(dense_index);
    }
    return true;
}

bool MachineStorage::is_active(std::uint32_t dense_index) const noexcept {
    return dense_index < states_.size()
            && (states_[dense_index].flags & machine_active) != 0;
}

void MachineStorage::tick(MachineTickMode mode) noexcept {
    if (mode == MachineTickMode::active_list) {
        for (std::uint32_t dense_index : active_indices_) {
            tick_state(states_[dense_index]);
        }
        return;
    }

    for (MachineState &state : states_) {
        if ((state.flags & machine_active) != 0) {
            tick_state(state);
        }
    }
}

void MachineStorage::reset() noexcept {
    states_.clear();
    entities_.clear();
    active_indices_.clear();
    active_positions_.clear();
    capacity_growth_count_ = 0;
}

void MachineStorage::reserve(std::size_t capacity) {
    if (capacity > states_.capacity()) {
        states_.reserve(capacity);
        ++capacity_growth_count_;
    }
    if (capacity > entities_.capacity()) {
        entities_.reserve(capacity);
        ++capacity_growth_count_;
    }
    if (capacity > active_indices_.capacity()) {
        active_indices_.reserve(capacity);
        ++capacity_growth_count_;
    }
    if (capacity > active_positions_.capacity()) {
        active_positions_.reserve(capacity);
        ++capacity_growth_count_;
    }
}

MachineState *MachineStorage::try_get(std::uint32_t dense_index) noexcept {
    return dense_index < states_.size() ? &states_[dense_index] : nullptr;
}

const MachineState *MachineStorage::try_get(std::uint32_t dense_index) const noexcept {
    return dense_index < states_.size() ? &states_[dense_index] : nullptr;
}

EntityId MachineStorage::entity_at(std::uint32_t dense_index) const noexcept {
    return dense_index < entities_.size() ? entities_[dense_index] : EntityId{};
}

std::size_t MachineStorage::reserved_bytes() const noexcept {
    return states_.capacity() * sizeof(MachineState)
            + entities_.capacity() * sizeof(EntityId)
            + active_indices_.capacity() * sizeof(std::uint32_t)
            + active_positions_.capacity() * sizeof(std::uint32_t);
}

std::uint64_t MachineStorage::checksum() const noexcept {
    std::uint64_t value = 1469598103934665603ULL;
    for (const MachineState &state : states_) {
        value ^= state.progress;
        value *= 1099511628211ULL;
        value ^= state.completed_cycles;
        value *= 1099511628211ULL;
        value ^= state.flags;
        value *= 1099511628211ULL;
    }
    return value;
}

void MachineStorage::add_active(std::uint32_t dense_index) {
    track_growth_before_push(active_indices_);
    active_positions_[dense_index] = static_cast<std::uint32_t>(active_indices_.size());
    active_indices_.push_back(dense_index);
}

void MachineStorage::remove_active(std::uint32_t dense_index) noexcept {
    if (dense_index >= active_positions_.size()) {
        return;
    }
    const std::uint32_t position = active_positions_[dense_index];
    if (position == inactive_position) {
        return;
    }

    const std::uint32_t moved_dense_index = active_indices_.back();
    active_indices_[position] = moved_dense_index;
    active_positions_[moved_dense_index] = position;
    active_indices_.pop_back();
    active_positions_[dense_index] = inactive_position;
}

void MachineStorage::tick_state(MachineState &state) noexcept {
    if ((state.flags & machine_powered) == 0) {
        return;
    }
    state.progress += state.speed;
    if (state.progress >= state.recipe_duration_ticks) {
        state.completed_cycles += state.progress / state.recipe_duration_ticks;
        state.progress %= state.recipe_duration_ticks;
    }
}

} // namespace projectf::core
