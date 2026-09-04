#include "projectf/core/factory_simulation.hpp"

#include <algorithm>
#include <array>
#include <limits>

namespace projectf::core {

FactorySimulation::FactorySimulation() : chunks_(32) {}

FactorySimulation::FactorySimulation(const SimulationConfig &config) : chunks_(32) {
    const bool initialized = initialize(config);
    (void)initialized;
}

bool FactorySimulation::initialize(const SimulationConfig &config) {
    if (config.simulation_chunk_size <= 0
            || config.render_chunk_size <= 0
            || config.simulation_chunk_size % config.render_chunk_size != 0) {
        return false;
    }
    shutdown();
    config_ = config;
    chunks_ = ChunkManager(config_.simulation_chunk_size);
    reserve(config_.initial_entity_capacity);
    initialized_ = true;
    render_revision_ = 1;
    return true;
}

void FactorySimulation::shutdown() noexcept {
    entities_.reset();
    machines_.reset();
    chunks_.reset();
    tick_count_ = 0;
    ++render_revision_;
    initialized_ = false;
}

void FactorySimulation::reset() noexcept {
    entities_.reset();
    machines_.reset();
    chunks_.reset();
    reserve(config_.initial_entity_capacity);
    tick_count_ = 0;
    ++render_revision_;
}

void FactorySimulation::reserve(std::size_t entity_capacity) {
    entities_.reserve(entity_capacity);
    machines_.reserve(entity_capacity);
    chunks_.reserve_entities(entity_capacity);
}

EntityId FactorySimulation::create_machine(const MachineCreateDesc &description) {
    if (!initialized_ || machines_.size() >= EntityId::invalid_index) {
        return {};
    }
    const std::uint32_t dense_index = static_cast<std::uint32_t>(machines_.size());
    const EntityId id = entities_.allocate(EntityKind::machine, dense_index);
    if (!id.is_valid()) {
        return {};
    }
    const std::uint32_t appended_index = machines_.append(id, description);
    if (appended_index != dense_index) {
        const bool released = entities_.release(id);
        (void)released;
        return {};
    }
    if (!chunks_.insert(id, description.transform)) {
        const MachineStorage::RemoveResult rollback = machines_.remove(dense_index);
        const bool released = entities_.release(id);
        (void)rollback;
        (void)released;
        return {};
    }
    ++render_revision_;
    return id;
}

bool FactorySimulation::destroy_machine(EntityId id) noexcept {
    if (!entities_.is_valid(id) || entities_.kind(id) != EntityKind::machine) {
        return false;
    }
    const std::uint32_t dense_index = entities_.dense_index(id);
    if (!chunks_.remove(id)) {
        return false;
    }
    const MachineStorage::RemoveResult result = machines_.remove(dense_index);
    if (!result.removed) {
        return false;
    }
    if (result.moved_entity.is_valid()) {
        if (!entities_.update_dense_index(result.moved_entity, result.moved_dense_index)) {
            return false;
        }
    }
    if (!entities_.release(id)) {
        return false;
    }
    ++render_revision_;
    return true;
}

bool FactorySimulation::set_machine_active(EntityId id, bool active) noexcept {
    if (!entities_.is_valid(id) || entities_.kind(id) != EntityKind::machine) {
        return false;
    }
    return machines_.set_active(entities_.dense_index(id), active);
}

bool FactorySimulation::set_machine_transform(EntityId id, GridTransform transform) {
    if (!entities_.is_valid(id) || entities_.kind(id) != EntityKind::machine) {
        return false;
    }
    MachineState *state = machines_.try_get(entities_.dense_index(id));
    if (state == nullptr) {
        return false;
    }
    const GridTransform previous = state->transform;
    if (!chunks_.move(id, previous, transform)) {
        return false;
    }
    state->transform = transform;
    ++render_revision_;
    return true;
}

bool FactorySimulation::is_valid(EntityId id) const noexcept {
    return entities_.is_valid(id);
}

void FactorySimulation::fixed_tick(MachineTickMode mode) noexcept {
    if (!initialized_) {
        return;
    }
    machines_.tick(mode);
    ++tick_count_;
}

void FactorySimulation::build_render_snapshot(RenderSnapshot &snapshot) const {
    snapshot.clear();
    snapshot.revision = render_revision_;
    snapshot.instances.reserve(machines_.size());

    const std::int32_t subdivisions = config_.simulation_chunk_size / config_.render_chunk_size;
    const std::size_t render_chunks_per_simulation_chunk =
            static_cast<std::size_t>(subdivisions * subdivisions);
    std::vector<std::vector<RenderInstance>> scratch(render_chunks_per_simulation_chunk);

    for (const ChunkState &chunk : chunks_.chunks()) {
        for (auto &instances : scratch) {
            instances.clear();
        }

        for (EntityId entity : chunk.entities) {
            const MachineState *machine = try_get_machine(entity);
            if (machine == nullptr) {
                continue;
            }
            const std::int32_t local_x = machine->transform.x
                    - chunk.coordinate.x * config_.simulation_chunk_size;
            const std::int32_t local_z = machine->transform.z
                    - chunk.coordinate.z * config_.simulation_chunk_size;
            const std::int32_t sub_x = std::clamp(
                    local_x / config_.render_chunk_size, 0, subdivisions - 1);
            const std::int32_t sub_z = std::clamp(
                    local_z / config_.render_chunk_size, 0, subdivisions - 1);
            const std::size_t scratch_index = static_cast<std::size_t>(sub_z * subdivisions + sub_x);

            RenderInstance instance;
            instance.x = static_cast<float>(machine->transform.x);
            instance.y = static_cast<float>(machine->transform.layer);
            instance.z = static_cast<float>(machine->transform.z);
            instance.yaw = static_cast<std::uint16_t>((machine->transform.rotation & 3U) * 16384U);
            scratch[scratch_index].push_back(instance);
        }

        for (std::int32_t sub_z = 0; sub_z < subdivisions; ++sub_z) {
            for (std::int32_t sub_x = 0; sub_x < subdivisions; ++sub_x) {
                const std::size_t scratch_index = static_cast<std::size_t>(sub_z * subdivisions + sub_x);
                const auto &instances = scratch[scratch_index];
                if (instances.empty()) {
                    continue;
                }
                RenderBatchRange batch;
                batch.render_chunk = {
                    chunk.coordinate.x * subdivisions + sub_x,
                    chunk.coordinate.z * subdivisions + sub_z,
                };
                const float min_x = static_cast<float>(batch.render_chunk.x * config_.render_chunk_size);
                const float min_z = static_cast<float>(batch.render_chunk.z * config_.render_chunk_size);
                batch.bounds.minimum = {min_x - 0.5F, -0.5F, min_z - 0.5F};
                batch.bounds.maximum = {
                    min_x + static_cast<float>(config_.render_chunk_size) + 0.5F,
                    3.0F,
                    min_z + static_cast<float>(config_.render_chunk_size) + 0.5F,
                };
                batch.instance_offset = static_cast<std::uint32_t>(snapshot.instances.size());
                batch.instance_count = static_cast<std::uint32_t>(instances.size());
                snapshot.instances.insert(snapshot.instances.end(), instances.begin(), instances.end());
                snapshot.batches.push_back(batch);
            }
        }
    }
}

SimulationStatistics FactorySimulation::query_statistics() const noexcept {
    SimulationStatistics statistics;
    statistics.tick_count = tick_count_;
    statistics.render_revision = render_revision_;
    statistics.entity_count = entities_.active_count();
    statistics.machine_count = machines_.size();
    statistics.active_machine_count = machines_.active_count();
    statistics.sleeping_machine_count = machines_.size() - machines_.active_count();
    statistics.chunk_count = chunks_.size();
    statistics.reserved_bytes = entities_.reserved_bytes()
            + machines_.reserved_bytes()
            + chunks_.reserved_bytes();
    statistics.capacity_growth_count = entities_.capacity_growth_count()
            + machines_.capacity_growth_count()
            + chunks_.capacity_growth_count();
    return statistics;
}

const MachineState *FactorySimulation::try_get_machine(EntityId id) const noexcept {
    if (!entities_.is_valid(id) || entities_.kind(id) != EntityKind::machine) {
        return nullptr;
    }
    return machines_.try_get(entities_.dense_index(id));
}

} // namespace projectf::core
