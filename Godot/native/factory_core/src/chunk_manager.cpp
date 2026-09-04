#include "projectf/core/chunk_manager.hpp"

#include <algorithm>

namespace projectf::core {

ChunkManager::ChunkManager(std::int32_t chunk_size) :
        chunk_size_(std::max<std::int32_t>(1, chunk_size)) {}

bool ChunkManager::insert(EntityId entity, GridTransform transform) {
    if (!entity.is_valid()) {
        return false;
    }
    ensure_entity_location(entity);
    EntityLocation &location = entity_locations_[entity.index];
    if (location.generation == entity.generation && location.chunk_index != invalid_position) {
        return false;
    }

    const ChunkCoord coordinate = world_to_chunk(transform, chunk_size_);
    const std::uint32_t chunk_index = find_or_create_chunk(coordinate);
    ChunkState &chunk = chunks_[chunk_index];
    if (chunk.entities.size() == chunk.entities.capacity()) {
        ++capacity_growth_count_;
    }
    location.generation = entity.generation;
    location.chunk_index = chunk_index;
    location.entity_position = static_cast<std::uint32_t>(chunk.entities.size());
    chunk.entities.push_back(entity);
    chunk.dirty_flags |= chunk_simulation_dirty | chunk_render_dirty | chunk_save_dirty;
    return true;
}

bool ChunkManager::remove(EntityId entity) noexcept {
    if (!contains(entity)) {
        return false;
    }
    EntityLocation &location = entity_locations_[entity.index];
    ChunkState &chunk = chunks_[location.chunk_index];
    const std::uint32_t last_position = static_cast<std::uint32_t>(chunk.entities.size() - 1);
    if (location.entity_position != last_position) {
        const EntityId moved = chunk.entities[last_position];
        chunk.entities[location.entity_position] = moved;
        if (moved.index < entity_locations_.size()) {
            entity_locations_[moved.index].entity_position = location.entity_position;
        }
    }
    chunk.entities.pop_back();
    chunk.dirty_flags |= chunk_simulation_dirty | chunk_render_dirty | chunk_save_dirty;
    location = {};
    return true;
}

bool ChunkManager::move(EntityId entity, GridTransform from, GridTransform to) {
    const ChunkCoord old_coordinate = world_to_chunk(from, chunk_size_);
    const ChunkCoord new_coordinate = world_to_chunk(to, chunk_size_);
    if (old_coordinate == new_coordinate) {
        mark_dirty(old_coordinate, chunk_simulation_dirty | chunk_render_dirty | chunk_save_dirty);
        return contains(entity);
    }
    return remove(entity) && insert(entity, to);
}

bool ChunkManager::contains(EntityId entity) const noexcept {
    if (!entity.is_valid() || entity.index >= entity_locations_.size()) {
        return false;
    }
    const EntityLocation &location = entity_locations_[entity.index];
    return location.generation == entity.generation
            && location.chunk_index != invalid_position
            && location.chunk_index < chunks_.size()
            && location.entity_position < chunks_[location.chunk_index].entities.size()
            && chunks_[location.chunk_index].entities[location.entity_position] == entity;
}

void ChunkManager::set_simulation_active(ChunkCoord coordinate, bool active) {
    const auto found = chunk_indices_.find(coordinate);
    if (found != chunk_indices_.end()) {
        chunks_[found->second].simulation_active = active;
    }
}

void ChunkManager::set_render_visible(ChunkCoord coordinate, bool visible) {
    const auto found = chunk_indices_.find(coordinate);
    if (found != chunk_indices_.end()) {
        chunks_[found->second].render_visible = visible;
    }
}

void ChunkManager::mark_dirty(ChunkCoord coordinate, std::uint8_t flags) {
    const auto found = chunk_indices_.find(coordinate);
    if (found != chunk_indices_.end()) {
        chunks_[found->second].dirty_flags |= flags;
    }
}

void ChunkManager::clear_dirty(ChunkCoord coordinate, std::uint8_t flags) {
    const auto found = chunk_indices_.find(coordinate);
    if (found != chunk_indices_.end()) {
        chunks_[found->second].dirty_flags &= static_cast<std::uint8_t>(~flags);
    }
}

void ChunkManager::reset() noexcept {
    chunk_indices_.clear();
    chunks_.clear();
    entity_locations_.clear();
    capacity_growth_count_ = 0;
}

void ChunkManager::reserve_entities(std::size_t capacity) {
    if (capacity > entity_locations_.capacity()) {
        entity_locations_.reserve(capacity);
        ++capacity_growth_count_;
    }
}

std::size_t ChunkManager::reserved_bytes() const noexcept {
    std::size_t bytes = chunks_.capacity() * sizeof(ChunkState)
            + entity_locations_.capacity() * sizeof(EntityLocation);
    for (const ChunkState &chunk : chunks_) {
        bytes += chunk.entities.capacity() * sizeof(EntityId);
    }
    return bytes;
}

std::uint32_t ChunkManager::find_or_create_chunk(ChunkCoord coordinate) {
    const auto found = chunk_indices_.find(coordinate);
    if (found != chunk_indices_.end()) {
        return found->second;
    }
    if (chunks_.size() == chunks_.capacity()) {
        ++capacity_growth_count_;
    }
    const auto index = static_cast<std::uint32_t>(chunks_.size());
    ChunkState chunk;
    chunk.coordinate = coordinate;
    chunks_.push_back(std::move(chunk));
    chunk_indices_.emplace(coordinate, index);
    return index;
}

void ChunkManager::ensure_entity_location(EntityId entity) {
    if (entity.index < entity_locations_.size()) {
        return;
    }
    const std::size_t required = static_cast<std::size_t>(entity.index) + 1;
    if (required > entity_locations_.capacity()) {
        ++capacity_growth_count_;
    }
    entity_locations_.resize(required);
}

} // namespace projectf::core
