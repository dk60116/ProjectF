#ifndef PROJECTF_CORE_ENTITY_ID_HPP
#define PROJECTF_CORE_ENTITY_ID_HPP

#include <cstdint>
#include <limits>
#include <type_traits>

namespace projectf::core {

struct EntityId {
    static constexpr std::uint32_t invalid_index = std::numeric_limits<std::uint32_t>::max();

    std::uint32_t index = invalid_index;
    std::uint32_t generation = 0;

    [[nodiscard]] constexpr bool is_valid() const noexcept {
        return index != invalid_index && generation != 0;
    }

    [[nodiscard]] constexpr std::uint64_t packed() const noexcept {
        return (static_cast<std::uint64_t>(generation) << 32U) | index;
    }

    friend constexpr bool operator==(EntityId left, EntityId right) noexcept {
        return left.index == right.index && left.generation == right.generation;
    }

    friend constexpr bool operator!=(EntityId left, EntityId right) noexcept {
        return !(left == right);
    }
};

static_assert(std::is_trivially_copyable_v<EntityId>);
static_assert(sizeof(EntityId) == 8);

enum class EntityKind : std::uint8_t {
    none = 0,
    machine = 1,
    belt = 2,
    inserter = 3,
};

} // namespace projectf::core

#endif
