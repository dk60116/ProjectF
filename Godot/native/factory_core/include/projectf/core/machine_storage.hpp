#ifndef PROJECTF_CORE_MACHINE_STORAGE_HPP
#define PROJECTF_CORE_MACHINE_STORAGE_HPP

#include "projectf/core/entity_id.hpp"
#include "projectf/core/sim_types.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <type_traits>
#include <vector>

namespace projectf::core {

enum MachineFlags : std::uint16_t {
    machine_none = 0,
    machine_active = 1U << 0U,
    machine_powered = 1U << 1U,
};

struct MachineCreateDesc {
    GridTransform transform;
    std::uint32_t inventory_index = 0;
    std::uint32_t recipe_duration_ticks = 60;
    std::uint16_t recipe_id = 0;
    std::uint16_t speed = 1;
    bool active = true;
};

struct MachineState {
    GridTransform transform;
    std::uint32_t inventory_index = 0;
    std::uint32_t progress = 0;
    std::uint32_t completed_cycles = 0;
    std::uint32_t recipe_duration_ticks = 60;
    std::uint16_t recipe_id = 0;
    std::uint16_t speed = 1;
    std::uint16_t flags = machine_active | machine_powered;
    std::uint16_t reserved = 0;
};

static_assert(std::is_trivially_copyable_v<MachineState>);

enum class MachineTickMode : std::uint8_t {
    scan_all = 0,
    active_list = 1,
};

class MachineStorage {
public:
    struct RemoveResult {
        bool removed = false;
        EntityId moved_entity;
        std::uint32_t moved_dense_index = EntityId::invalid_index;
    };

    [[nodiscard]] std::uint32_t append(EntityId id, const MachineCreateDesc &description);
    [[nodiscard]] RemoveResult remove(std::uint32_t dense_index) noexcept;
    [[nodiscard]] bool set_active(std::uint32_t dense_index, bool active) noexcept;
    [[nodiscard]] bool is_active(std::uint32_t dense_index) const noexcept;
    void tick(MachineTickMode mode) noexcept;

    void reset() noexcept;
    void reserve(std::size_t capacity);

    [[nodiscard]] MachineState *try_get(std::uint32_t dense_index) noexcept;
    [[nodiscard]] const MachineState *try_get(std::uint32_t dense_index) const noexcept;
    [[nodiscard]] EntityId entity_at(std::uint32_t dense_index) const noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return states_.size(); }
    [[nodiscard]] std::size_t active_count() const noexcept { return active_indices_.size(); }
    [[nodiscard]] std::size_t reserved_bytes() const noexcept;
    [[nodiscard]] std::uint64_t capacity_growth_count() const noexcept { return capacity_growth_count_; }
    [[nodiscard]] std::uint64_t checksum() const noexcept;

private:
    static constexpr std::uint32_t inactive_position = std::numeric_limits<std::uint32_t>::max();

    std::vector<MachineState> states_;
    std::vector<EntityId> entities_;
    std::vector<std::uint32_t> active_indices_;
    std::vector<std::uint32_t> active_positions_;
    std::uint64_t capacity_growth_count_ = 0;

    void add_active(std::uint32_t dense_index);
    void remove_active(std::uint32_t dense_index) noexcept;
    static void tick_state(MachineState &state) noexcept;

    template <typename T>
    void track_growth_before_push(const std::vector<T> &values) noexcept {
        if (values.size() == values.capacity()) {
            ++capacity_growth_count_;
        }
    }
};

} // namespace projectf::core

#endif
