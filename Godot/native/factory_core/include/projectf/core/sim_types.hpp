#ifndef PROJECTF_CORE_SIM_TYPES_HPP
#define PROJECTF_CORE_SIM_TYPES_HPP

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <functional>

namespace projectf::core {

struct GridTransform {
    std::int32_t x = 0;
    std::int32_t z = 0;
    std::int16_t layer = 0;
    std::uint8_t rotation = 0;
    std::uint8_t flags = 0;
};

static_assert(sizeof(GridTransform) == 12);

struct ChunkCoord {
    std::int32_t x = 0;
    std::int32_t z = 0;

    friend constexpr bool operator==(ChunkCoord left, ChunkCoord right) noexcept {
        return left.x == right.x && left.z == right.z;
    }

    friend constexpr bool operator!=(ChunkCoord left, ChunkCoord right) noexcept {
        return !(left == right);
    }
};

struct ChunkCoordHash {
    [[nodiscard]] std::size_t operator()(ChunkCoord value) const noexcept {
        const std::uint64_t packed =
                (static_cast<std::uint64_t>(static_cast<std::uint32_t>(value.x)) << 32U)
                | static_cast<std::uint32_t>(value.z);
        return std::hash<std::uint64_t>{}(packed);
    }
};

[[nodiscard]] constexpr std::int32_t floor_divide(std::int32_t value, std::int32_t divisor) noexcept {
    const std::int32_t quotient = value / divisor;
    const std::int32_t remainder = value % divisor;
    return remainder != 0 && ((remainder < 0) != (divisor < 0)) ? quotient - 1 : quotient;
}

[[nodiscard]] constexpr ChunkCoord world_to_chunk(GridTransform transform, std::int32_t chunk_size) noexcept {
    return {
        floor_divide(transform.x, chunk_size),
        floor_divide(transform.z, chunk_size),
    };
}

struct Float3 {
    float x = 0.0F;
    float y = 0.0F;
    float z = 0.0F;
};

struct Aabb {
    Float3 minimum;
    Float3 maximum;
};

struct Plane {
    Float3 normal;
    float d = 0.0F;
};

struct Frustum {
    std::array<Plane, 6> planes{};

    [[nodiscard]] bool intersects(const Aabb &bounds) const noexcept {
        for (const Plane &plane : planes) {
            const Float3 positive{
                plane.normal.x >= 0.0F ? bounds.maximum.x : bounds.minimum.x,
                plane.normal.y >= 0.0F ? bounds.maximum.y : bounds.minimum.y,
                plane.normal.z >= 0.0F ? bounds.maximum.z : bounds.minimum.z,
            };
            const float distance = plane.normal.x * positive.x
                    + plane.normal.y * positive.y
                    + plane.normal.z * positive.z
                    - plane.d;
            if (distance < 0.0F) {
                return false;
            }
        }
        return true;
    }
};

} // namespace projectf::core

#endif
