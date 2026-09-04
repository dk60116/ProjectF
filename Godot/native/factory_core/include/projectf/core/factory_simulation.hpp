#ifndef PROJECTF_CORE_FACTORY_SIMULATION_HPP
#define PROJECTF_CORE_FACTORY_SIMULATION_HPP

#include "projectf/core/chunk_manager.hpp"
#include "projectf/core/entity_manager.hpp"
#include "projectf/core/machine_storage.hpp"
#include "projectf/core/render_snapshot.hpp"

#include <cstddef>
#include <cstdint>

namespace projectf::core {

struct SimulationConfig {
    std::int32_t simulation_chunk_size = 32;
    std::int32_t render_chunk_size = 16;
    std::size_t initial_entity_capacity = 0;
};

struct SimulationStatistics {
    std::uint64_t tick_count = 0;
    std::uint64_t render_revision = 0;
    std::size_t entity_count = 0;
    std::size_t machine_count = 0;
    std::size_t active_machine_count = 0;
    std::size_t sleeping_machine_count = 0;
    std::size_t chunk_count = 0;
    std::size_t reserved_bytes = 0;
    std::uint64_t capacity_growth_count = 0;
};

class FactorySimulation {
public:
    FactorySimulation();
    explicit FactorySimulation(const SimulationConfig &config);

    [[nodiscard]] bool initialize(const SimulationConfig &config);
    void shutdown() noexcept;
    void reset() noexcept;
    void reserve(std::size_t entity_capacity);

    [[nodiscard]] EntityId create_machine(const MachineCreateDesc &description);
    [[nodiscard]] bool destroy_machine(EntityId id) noexcept;
    [[nodiscard]] bool set_machine_active(EntityId id, bool active) noexcept;
    [[nodiscard]] bool set_machine_transform(EntityId id, GridTransform transform);
    [[nodiscard]] bool is_valid(EntityId id) const noexcept;

    void fixed_tick(MachineTickMode mode = MachineTickMode::active_list) noexcept;
    void build_render_snapshot(RenderSnapshot &snapshot) const;

    [[nodiscard]] SimulationStatistics query_statistics() const noexcept;
    [[nodiscard]] const MachineState *try_get_machine(EntityId id) const noexcept;
    [[nodiscard]] std::uint64_t checksum() const noexcept { return machines_.checksum(); }
    [[nodiscard]] std::uint64_t render_revision() const noexcept { return render_revision_; }
    [[nodiscard]] bool initialized() const noexcept { return initialized_; }
    [[nodiscard]] std::int32_t simulation_chunk_size() const noexcept { return config_.simulation_chunk_size; }
    [[nodiscard]] std::int32_t render_chunk_size() const noexcept { return config_.render_chunk_size; }

private:
    SimulationConfig config_;
    EntityManager entities_;
    MachineStorage machines_;
    ChunkManager chunks_;
    std::uint64_t tick_count_ = 0;
    std::uint64_t render_revision_ = 1;
    bool initialized_ = false;
};

} // namespace projectf::core

#endif
