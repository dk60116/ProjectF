#include "projectf/core/chunk_manager.hpp"
#include "projectf/core/entity_manager.hpp"
#include "projectf/core/factory_simulation.hpp"
#include "projectf/core/fixed_tick.hpp"

#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

int failures = 0;

void check(bool condition, std::string_view message) {
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        ++failures;
    }
}

void test_entity_id_lifecycle() {
    using namespace projectf::core;
    EntityManager entities;
    entities.reserve(4);
    const EntityId first = entities.allocate(EntityKind::machine, 10);
    check(first.is_valid(), "allocated EntityId is valid");
    check(entities.is_valid(first), "manager accepts allocated EntityId");
    check(entities.dense_index(first) == 10, "dense index is retained");
    check(entities.release(first), "release succeeds");
    check(!entities.is_valid(first), "released EntityId is stale");

    const EntityId reused = entities.allocate(EntityKind::machine, 3);
    check(reused.index == first.index, "free-list reuses slot");
    check(reused.generation != first.generation, "reused slot increments generation");
    check(!entities.release(first), "stale handle cannot release reused slot");
}

void test_chunk_coordinates_and_membership() {
    using namespace projectf::core;
    check(world_to_chunk({0, 0, 0, 0, 0}, 32) == ChunkCoord{0, 0}, "origin chunk");
    check(world_to_chunk({31, 31, 0, 0, 0}, 32) == ChunkCoord{0, 0}, "positive local edge");
    check(world_to_chunk({32, 32, 0, 0, 0}, 32) == ChunkCoord{1, 1}, "positive next chunk");
    check(world_to_chunk({-1, -1, 0, 0, 0}, 32) == ChunkCoord{-1, -1}, "negative floor division");
    check(world_to_chunk({-32, -32, 0, 0, 0}, 32) == ChunkCoord{-1, -1}, "negative exact boundary");
    check(world_to_chunk({-33, -33, 0, 0, 0}, 32) == ChunkCoord{-2, -2}, "negative next chunk");

    ChunkManager chunks(32);
    const EntityId id{7, 3};
    check(chunks.insert(id, {-1, -1, 0, 0, 0}), "chunk insert succeeds");
    check(chunks.contains(id), "chunk contains inserted entity");
    check(chunks.move(id, {-1, -1, 0, 0, 0}, {64, 0, 0, 0, 0}), "cross-chunk move succeeds");
    check(chunks.contains(id), "chunk contains moved entity");
    check(chunks.remove(id), "chunk removal succeeds");
    check(!chunks.contains(id), "removed entity is absent");
}

void test_machine_lifecycle_and_repeatability() {
    using namespace projectf::core;
    SimulationConfig config;
    config.initial_entity_capacity = 8;
    FactorySimulation first(config);
    FactorySimulation second(config);

    MachineCreateDesc description;
    description.transform = {-10, 18, 1, 2, 0};
    description.recipe_duration_ticks = 11;
    description.speed = 3;
    const EntityId first_id = first.create_machine(description);
    const EntityId second_id = second.create_machine(description);
    check(first_id.is_valid() && second_id.is_valid(), "machine creation succeeds");

    for (int tick = 0; tick < 100; ++tick) {
        first.fixed_tick();
        second.fixed_tick();
    }
    check(first.checksum() == second.checksum(), "fixed tick is repeatable for equal inputs");
    check(first.query_statistics().tick_count == 100, "fixed tick count is recorded");
    check(first.set_machine_active(first_id, false), "machine can sleep");
    const auto sleeping_stats = first.query_statistics();
    check(sleeping_stats.active_machine_count == 0 && sleeping_stats.sleeping_machine_count == 1,
            "sleeping machine leaves active list");
    check(first.set_machine_active(first_id, true), "machine can wake");
    check(first.destroy_machine(first_id), "machine destruction succeeds");
    check(!first.is_valid(first_id), "destroyed machine handle is stale");
}

void test_dense_swap_remove() {
    using namespace projectf::core;
    FactorySimulation simulation({32, 16, 4});
    MachineCreateDesc description;
    const EntityId first = simulation.create_machine(description);
    description.transform.x = 1;
    const EntityId middle = simulation.create_machine(description);
    description.transform.x = 2;
    const EntityId last = simulation.create_machine(description);
    check(simulation.destroy_machine(middle), "middle machine removal succeeds");
    check(simulation.is_valid(first) && simulation.is_valid(last), "swap-remove preserves other handles");
    check(simulation.try_get_machine(last) != nullptr, "moved dense machine remains addressable");
}

void test_fixed_tick_clock() {
    using projectf::core::FixedTickClock;
    FixedTickClock clock(30.0, 4);
    check(clock.consume(1.0 / 60.0) == 0, "half tick stays accumulated");
    check(clock.consume(1.0 / 60.0) == 1, "two half frames produce one tick");
    check(clock.consume(1.0) == 4, "catch-up is capped");
    check(clock.dropped_seconds() > 0.0, "excess catch-up time is reported");
}

void test_visibility_and_snapshot() {
    using namespace projectf::core;
    Frustum frustum;
    frustum.planes = {{
        {{1, 0, 0}, -1}, {{-1, 0, 0}, -1},
        {{0, 1, 0}, -1}, {{0, -1, 0}, -1},
        {{0, 0, 1}, -1}, {{0, 0, -1}, -1},
    }};
    check(frustum.intersects({{-0.5F, -0.5F, -0.5F}, {0.5F, 0.5F, 0.5F}}),
            "frustum accepts intersecting bounds");
    check(!frustum.intersects({{2, 2, 2}, {3, 3, 3}}), "frustum rejects separated bounds");

    FactorySimulation simulation({32, 16, 8});
    MachineCreateDesc description;
    for (int i = 0; i < 5; ++i) {
        description.transform.x = i * 17;
        description.transform.z = i * -17;
        check(simulation.create_machine(description).is_valid(), "snapshot test machine created");
    }
    RenderSnapshot snapshot;
    simulation.build_render_snapshot(snapshot);
    check(snapshot.instances.size() == 5, "snapshot contains every renderable machine");
    check(snapshot.batches.size() == 5, "snapshot separates occupied render chunks");
    check(snapshot.revision == simulation.render_revision(), "snapshot revision matches simulation");
}

} // namespace

int main() {
    test_entity_id_lifecycle();
    test_chunk_coordinates_and_membership();
    test_machine_lifecycle_and_repeatability();
    test_dense_swap_remove();
    test_fixed_tick_clock();
    test_visibility_and_snapshot();

    if (failures != 0) {
        std::cerr << "FACTORY_CORE_TESTS_FAILED count=" << failures << '\n';
        return EXIT_FAILURE;
    }
    std::cout << "FACTORY_CORE_TESTS_OK\n";
    return EXIT_SUCCESS;
}
