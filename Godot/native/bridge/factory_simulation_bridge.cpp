#include "factory_simulation_bridge.h"

#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include <algorithm>
#include <cmath>

namespace godot {

FactorySimulationBridge::FactorySimulationBridge() : clock_(ticks_per_second_, 4) {}

void FactorySimulationBridge::_bind_methods() {
    ClassDB::bind_method(D_METHOD("start_simulation"), &FactorySimulationBridge::start_simulation);
    ClassDB::bind_method(D_METHOD("stop_simulation"), &FactorySimulationBridge::stop_simulation);
    ClassDB::bind_method(D_METHOD("reset_simulation"), &FactorySimulationBridge::reset_simulation);
    ClassDB::bind_method(D_METHOD("step_simulation", "ticks"), &FactorySimulationBridge::step_simulation, DEFVAL(1));
    ClassDB::bind_method(D_METHOD("create_test_machines", "count"), &FactorySimulationBridge::create_test_machines);
    ClassDB::bind_method(D_METHOD("set_test_active_ratio", "ratio"), &FactorySimulationBridge::set_test_active_ratio);
    ClassDB::bind_method(D_METHOD("get_statistics"), &FactorySimulationBridge::get_statistics);

    ClassDB::bind_method(D_METHOD("set_prototype_machine_count", "value"), &FactorySimulationBridge::set_prototype_machine_count);
    ClassDB::bind_method(D_METHOD("get_prototype_machine_count"), &FactorySimulationBridge::get_prototype_machine_count);
    ClassDB::bind_method(D_METHOD("set_populate_on_ready", "value"), &FactorySimulationBridge::set_populate_on_ready);
    ClassDB::bind_method(D_METHOD("get_populate_on_ready"), &FactorySimulationBridge::get_populate_on_ready);
    ClassDB::bind_method(D_METHOD("set_simulation_running", "value"), &FactorySimulationBridge::set_simulation_running);
    ClassDB::bind_method(D_METHOD("get_simulation_running"), &FactorySimulationBridge::get_simulation_running);
    ClassDB::bind_method(D_METHOD("set_ticks_per_second", "value"), &FactorySimulationBridge::set_ticks_per_second);
    ClassDB::bind_method(D_METHOD("get_ticks_per_second"), &FactorySimulationBridge::get_ticks_per_second);
    ClassDB::bind_method(D_METHOD("set_maximum_catch_up_steps", "value"), &FactorySimulationBridge::set_maximum_catch_up_steps);
    ClassDB::bind_method(D_METHOD("get_maximum_catch_up_steps"), &FactorySimulationBridge::get_maximum_catch_up_steps);

    ADD_PROPERTY(PropertyInfo(Variant::INT, "prototype_machine_count", PROPERTY_HINT_RANGE, "0,1000000,1,or_greater"),
            "set_prototype_machine_count", "get_prototype_machine_count");
    ADD_PROPERTY(PropertyInfo(Variant::BOOL, "populate_on_ready"), "set_populate_on_ready", "get_populate_on_ready");
    ADD_PROPERTY(PropertyInfo(Variant::BOOL, "simulation_running"), "set_simulation_running", "get_simulation_running");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "ticks_per_second", PROPERTY_HINT_RANGE, "1,120,1"),
            "set_ticks_per_second", "get_ticks_per_second");
    ADD_PROPERTY(PropertyInfo(Variant::INT, "maximum_catch_up_steps", PROPERTY_HINT_RANGE, "1,16,1"),
            "set_maximum_catch_up_steps", "get_maximum_catch_up_steps");
}

void FactorySimulationBridge::_ready() {
    projectf::core::SimulationConfig config;
    config.initial_entity_capacity = static_cast<std::size_t>(prototype_machine_count_);
    if (!simulation_.initialize(config)) {
        UtilityFunctions::push_error("FactorySimulationBridge: invalid simulation configuration.");
        set_process(false);
        return;
    }
    configure_clock();
    if (populate_on_ready_ && !create_test_machines(prototype_machine_count_)) {
        UtilityFunctions::push_error("FactorySimulationBridge: failed to create prototype machines.");
    }
}

void FactorySimulationBridge::_process(double delta) {
    if (!simulation_running_) {
        return;
    }
    const std::uint32_t steps = clock_.consume(delta);
    for (std::uint32_t step = 0; step < steps; ++step) {
        simulation_.fixed_tick(projectf::core::MachineTickMode::active_list);
    }
}

void FactorySimulationBridge::start_simulation() {
    simulation_running_ = true;
}

void FactorySimulationBridge::stop_simulation() {
    simulation_running_ = false;
}

void FactorySimulationBridge::reset_simulation() {
    simulation_.reset();
    test_machine_ids_.clear();
    clock_.reset();
}

void FactorySimulationBridge::step_simulation(std::int32_t ticks) {
    const std::int32_t bounded_ticks = std::clamp<std::int32_t>(ticks, 0, 10000);
    for (std::int32_t tick = 0; tick < bounded_ticks; ++tick) {
        simulation_.fixed_tick(projectf::core::MachineTickMode::active_list);
    }
}

bool FactorySimulationBridge::create_test_machines(std::int64_t count) {
    const std::size_t machine_count = static_cast<std::size_t>(std::clamp<std::int64_t>(count, 0, 1000000));
    simulation_.reset();
    simulation_.reserve(machine_count);
    test_machine_ids_.clear();
    test_machine_ids_.reserve(machine_count);

    const std::int32_t width = machine_count == 0
            ? 0
            : static_cast<std::int32_t>(std::ceil(std::sqrt(static_cast<double>(machine_count))));
    const std::int32_t half_width = width / 2;
    projectf::core::MachineCreateDesc description;
    description.recipe_duration_ticks = 90;
    for (std::size_t index = 0; index < machine_count; ++index) {
        description.transform.x = static_cast<std::int32_t>(index % static_cast<std::size_t>(width)) - half_width;
        description.transform.z = static_cast<std::int32_t>(index / static_cast<std::size_t>(width)) - half_width;
        description.transform.rotation = static_cast<std::uint8_t>(index & 3U);
        description.inventory_index = static_cast<std::uint32_t>(index);
        description.recipe_id = static_cast<std::uint16_t>(index % 32U);
        description.speed = static_cast<std::uint16_t>(1U + index % 4U);
        const projectf::core::EntityId id = simulation_.create_machine(description);
        if (!id.is_valid()) {
            return false;
        }
        test_machine_ids_.push_back(id);
    }
    return true;
}

void FactorySimulationBridge::set_test_active_ratio(double ratio) {
    const double bounded_ratio = std::clamp(ratio, 0.0, 1.0);
    const std::size_t active_count = static_cast<std::size_t>(
            std::floor(static_cast<double>(test_machine_ids_.size()) * bounded_ratio));
    for (std::size_t index = 0; index < test_machine_ids_.size(); ++index) {
        if (!simulation_.set_machine_active(test_machine_ids_[index], index < active_count)) {
            UtilityFunctions::push_warning("FactorySimulationBridge: stale test machine handle.");
            break;
        }
    }
}

Dictionary FactorySimulationBridge::get_statistics() const {
    const projectf::core::SimulationStatistics statistics = simulation_.query_statistics();
    Dictionary result;
    result["tick_count"] = static_cast<std::int64_t>(statistics.tick_count);
    result["render_revision"] = static_cast<std::int64_t>(statistics.render_revision);
    result["entity_count"] = static_cast<std::int64_t>(statistics.entity_count);
    result["machine_count"] = static_cast<std::int64_t>(statistics.machine_count);
    result["active_machine_count"] = static_cast<std::int64_t>(statistics.active_machine_count);
    result["sleeping_machine_count"] = static_cast<std::int64_t>(statistics.sleeping_machine_count);
    result["chunk_count"] = static_cast<std::int64_t>(statistics.chunk_count);
    result["reserved_bytes"] = static_cast<std::int64_t>(statistics.reserved_bytes);
    result["capacity_growth_count"] = static_cast<std::int64_t>(statistics.capacity_growth_count);
    result["dropped_simulation_seconds"] = clock_.dropped_seconds();
    result["checksum"] = static_cast<std::int64_t>(simulation_.checksum());
    return result;
}

void FactorySimulationBridge::set_prototype_machine_count(std::int64_t value) {
    prototype_machine_count_ = std::clamp<std::int64_t>(value, 0, 1000000);
}

std::int64_t FactorySimulationBridge::get_prototype_machine_count() const {
    return prototype_machine_count_;
}

void FactorySimulationBridge::set_populate_on_ready(bool value) {
    populate_on_ready_ = value;
}

bool FactorySimulationBridge::get_populate_on_ready() const {
    return populate_on_ready_;
}

void FactorySimulationBridge::set_simulation_running(bool value) {
    simulation_running_ = value;
}

bool FactorySimulationBridge::get_simulation_running() const {
    return simulation_running_;
}

void FactorySimulationBridge::set_ticks_per_second(double value) {
    ticks_per_second_ = std::clamp(value, 1.0, 120.0);
    configure_clock();
}

double FactorySimulationBridge::get_ticks_per_second() const {
    return ticks_per_second_;
}

void FactorySimulationBridge::set_maximum_catch_up_steps(std::int32_t value) {
    maximum_catch_up_steps_ = std::clamp<std::int32_t>(value, 1, 16);
    configure_clock();
}

std::int32_t FactorySimulationBridge::get_maximum_catch_up_steps() const {
    return maximum_catch_up_steps_;
}

void FactorySimulationBridge::configure_clock() {
    clock_.configure(ticks_per_second_, static_cast<std::uint32_t>(maximum_catch_up_steps_));
}

} // namespace godot
