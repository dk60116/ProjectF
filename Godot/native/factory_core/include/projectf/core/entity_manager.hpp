#ifndef PROJECTF_CORE_ENTITY_MANAGER_HPP
#define PROJECTF_CORE_ENTITY_MANAGER_HPP

#include "projectf/core/entity_id.hpp"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace projectf::core {

class EntityManager {
public:
    [[nodiscard]] EntityId allocate(EntityKind kind, std::uint32_t dense_index);
    [[nodiscard]] bool release(EntityId id) noexcept;
    [[nodiscard]] bool is_valid(EntityId id) const noexcept;
    [[nodiscard]] EntityKind kind(EntityId id) const noexcept;
    [[nodiscard]] std::uint32_t dense_index(EntityId id) const noexcept;
    [[nodiscard]] bool update_dense_index(EntityId id, std::uint32_t dense_index) noexcept;

    void reset() noexcept;
    void reserve(std::size_t capacity);

    [[nodiscard]] std::size_t slot_count() const noexcept { return slots_.size(); }
    [[nodiscard]] std::size_t active_count() const noexcept { return active_count_; }
    [[nodiscard]] std::size_t reserved_bytes() const noexcept;
    [[nodiscard]] std::uint64_t capacity_growth_count() const noexcept { return capacity_growth_count_; }

private:
    struct Slot {
        std::uint32_t generation = 1;
        std::uint32_t dense_index = 0;
        EntityKind kind = EntityKind::none;
        bool alive = false;
    };

    std::vector<Slot> slots_;
    std::vector<std::uint32_t> free_indices_;
    std::size_t active_count_ = 0;
    std::uint64_t capacity_growth_count_ = 0;

    template <typename T>
    void track_growth_before_push(const std::vector<T> &values) noexcept {
        if (values.size() == values.capacity()) {
            ++capacity_growth_count_;
        }
    }
};

} // namespace projectf::core

#endif
