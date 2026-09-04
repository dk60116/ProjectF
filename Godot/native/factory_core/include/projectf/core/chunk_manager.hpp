#ifndef PROJECTF_CORE_CHUNK_MANAGER_HPP
#define PROJECTF_CORE_CHUNK_MANAGER_HPP

#include "projectf/core/entity_id.hpp"
#include "projectf/core/sim_types.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <unordered_map>
#include <vector>

namespace projectf::core {

enum ChunkDirtyFlags : std::uint8_t {
    chunk_clean = 0,
    chunk_simulation_dirty = 1U << 0U,
    chunk_render_dirty = 1U << 1U,
    chunk_save_dirty = 1U << 2U,
};

enum class ChunkStreamingState : std::uint8_t {
    resident = 0,
    pending_load = 1,
    pending_unload = 2,
};

struct ChunkState {
    ChunkCoord coordinate;
    std::vector<EntityId> entities;
    std::uint8_t dirty_flags = chunk_clean;
    std::uint8_t render_lod = 0;
    bool simulation_active = false;
    bool render_visible = false;
    ChunkStreamingState streaming_state = ChunkStreamingState::resident;
};

class ChunkManager {
public:
    explicit ChunkManager(std::int32_t chunk_size = 32);

    [[nodiscard]] bool insert(EntityId entity, GridTransform transform);
    [[nodiscard]] bool remove(EntityId entity) noexcept;
    [[nodiscard]] bool move(EntityId entity, GridTransform from, GridTransform to);
    [[nodiscard]] bool contains(EntityId entity) const noexcept;

    void set_simulation_active(ChunkCoord coordinate, bool active);
    void set_render_visible(ChunkCoord coordinate, bool visible);
    void mark_dirty(ChunkCoord coordinate, std::uint8_t flags);
    void clear_dirty(ChunkCoord coordinate, std::uint8_t flags);
    void reset() noexcept;
    void reserve_entities(std::size_t capacity);

    [[nodiscard]] std::int32_t chunk_size() const noexcept { return chunk_size_; }
    [[nodiscard]] std::size_t size() const noexcept { return chunks_.size(); }
    [[nodiscard]] const std::vector<ChunkState> &chunks() const noexcept { return chunks_; }
    [[nodiscard]] std::size_t reserved_bytes() const noexcept;
    [[nodiscard]] std::uint64_t capacity_growth_count() const noexcept { return capacity_growth_count_; }

private:
    static constexpr std::uint32_t invalid_position = std::numeric_limits<std::uint32_t>::max();

    struct EntityLocation {
        std::uint32_t generation = 0;
        std::uint32_t chunk_index = invalid_position;
        std::uint32_t entity_position = invalid_position;
    };

    std::int32_t chunk_size_ = 32;
    std::unordered_map<ChunkCoord, std::uint32_t, ChunkCoordHash> chunk_indices_;
    std::vector<ChunkState> chunks_;
    std::vector<EntityLocation> entity_locations_;
    std::uint64_t capacity_growth_count_ = 0;

    [[nodiscard]] std::uint32_t find_or_create_chunk(ChunkCoord coordinate);
    void ensure_entity_location(EntityId entity);
};

} // namespace projectf::core

#endif
