#ifndef PROJECTF_CORE_RENDER_SNAPSHOT_HPP
#define PROJECTF_CORE_RENDER_SNAPSHOT_HPP

#include "projectf/core/sim_types.hpp"

#include <cstddef>
#include <cstdint>
#include <type_traits>
#include <vector>

namespace projectf::core {

struct RenderInstance {
    float x = 0.0F;
    float y = 0.0F;
    float z = 0.0F;
    std::uint16_t yaw = 0;
    std::uint16_t mesh_id = 0;
    std::uint16_t material_variant = 0;
    std::uint8_t lod = 0;
    std::uint8_t flags = 0;
};

static_assert(std::is_trivially_copyable_v<RenderInstance>);

struct RenderBatchRange {
    ChunkCoord render_chunk;
    Aabb bounds;
    std::uint32_t instance_offset = 0;
    std::uint32_t instance_count = 0;
    std::uint16_t mesh_id = 0;
    std::uint16_t material_variant = 0;
    std::uint8_t lod = 0;
};

struct RenderSnapshot {
    std::uint64_t revision = 0;
    std::vector<RenderInstance> instances;
    std::vector<RenderBatchRange> batches;

    void clear() noexcept {
        revision = 0;
        instances.clear();
        batches.clear();
    }

    [[nodiscard]] std::size_t reserved_bytes() const noexcept {
        return instances.capacity() * sizeof(RenderInstance)
                + batches.capacity() * sizeof(RenderBatchRange);
    }
};

} // namespace projectf::core

#endif
