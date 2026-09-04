#ifndef PROJECTF_FACTORY_SIMULATION_BRIDGE_H
#define PROJECTF_FACTORY_SIMULATION_BRIDGE_H

#include "projectf/core/factory_simulation.hpp"
#include "projectf/core/fixed_tick.hpp"

#include <godot_cpp/classes/node.hpp>
#include <godot_cpp/variant/dictionary.hpp>

#include <cstdint>
#include <vector>

namespace godot {

class FactorySimulationBridge : public Node {
    GDCLASS(FactorySimulationBridge, Node)

public:
    FactorySimulationBridge();

    void _ready() override;
    void _process(double delta) override;

    void start_simulation();
    void stop_simulation();
    void reset_simulation();
    void step_simulation(std::int32_t ticks = 1);
    bool create_test_machines(std::int64_t count);
    void set_test_active_ratio(double ratio);
    Dictionary get_statistics() const;

    void set_prototype_machine_count(std::int64_t value);
    std::int64_t get_prototype_machine_count() const;
    void set_populate_on_ready(bool value);
    bool get_populate_on_ready() const;
    void set_simulation_running(bool value);
    bool get_simulation_running() const;
    void set_ticks_per_second(double value);
    double get_ticks_per_second() const;
    void set_maximum_catch_up_steps(std::int32_t value);
    std::int32_t get_maximum_catch_up_steps() const;

    projectf::core::FactorySimulation &simulation() noexcept { return simulation_; }
    const projectf::core::FactorySimulation &simulation() const noexcept { return simulation_; }

protected:
    static void _bind_methods();

private:
    void configure_clock();

    projectf::core::FactorySimulation simulation_;
    projectf::core::FixedTickClock clock_;
    std::vector<projectf::core::EntityId> test_machine_ids_;
    std::int64_t prototype_machine_count_ = 10000;
    double ticks_per_second_ = 30.0;
    std::int32_t maximum_catch_up_steps_ = 4;
    bool populate_on_ready_ = true;
    bool simulation_running_ = true;
};

} // namespace godot

#endif
