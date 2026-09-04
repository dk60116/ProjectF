#include "projectf/core/factory_simulation.hpp"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <vector>

namespace {

using Clock = std::chrono::steady_clock;
volatile std::uint64_t benchmark_checksum = 0;

struct TimingResult {
    double median_ms = 0.0;
    double p95_ms = 0.0;
    double maximum_ms = 0.0;
    std::uint64_t checksum = 0;
};

TimingResult measure(
        projectf::core::FactorySimulation &simulation,
        projectf::core::MachineTickMode mode,
        int iterations) {
    for (int i = 0; i < 20; ++i) {
        simulation.fixed_tick(mode);
    }

    std::vector<double> samples;
    samples.reserve(static_cast<std::size_t>(iterations));
    for (int i = 0; i < iterations; ++i) {
        const auto start = Clock::now();
        simulation.fixed_tick(mode);
        const auto end = Clock::now();
        samples.push_back(std::chrono::duration<double, std::milli>(end - start).count());
    }
    std::sort(samples.begin(), samples.end());
    TimingResult result;
    result.median_ms = samples[samples.size() / 2];
    result.p95_ms = samples[static_cast<std::size_t>(
            std::floor(static_cast<double>(samples.size() - 1) * 0.95))];
    result.maximum_ms = samples.back();
    result.checksum = simulation.checksum();
    benchmark_checksum ^= result.checksum;
    return result;
}

int iteration_count(std::size_t machine_count) {
    if (machine_count >= 1'000'000) {
        return 80;
    }
    if (machine_count >= 100'000) {
        return 240;
    }
    return 800;
}

void set_active_ratio(
        projectf::core::FactorySimulation &simulation,
        std::vector<projectf::core::EntityId> &ids,
        double ratio) {
    const std::size_t active_count = static_cast<std::size_t>(
            std::floor(static_cast<double>(ids.size()) * ratio));
    for (std::size_t index = 0; index < ids.size(); ++index) {
        if (!simulation.set_machine_active(ids[index], index < active_count)) {
            std::cerr << "Failed to change machine activity at index " << index << '\n';
            std::terminate();
        }
    }
}

} // namespace

int main() {
    using namespace projectf::core;
    constexpr std::size_t counts[] = {10'000, 100'000, 1'000'000};
    constexpr double active_ratios[] = {1.0, 0.1, 0.01};

    std::cout << "machines,active_percent,mode,median_ms,p95_ms,max_ms,reserved_bytes,capacity_growths,checksum\n";
    std::cout << std::fixed << std::setprecision(6);
    for (std::size_t count : counts) {
        SimulationConfig config;
        config.initial_entity_capacity = count;
        FactorySimulation simulation(config);
        std::vector<EntityId> ids;
        ids.reserve(count);

        const auto width = static_cast<std::int32_t>(std::ceil(std::sqrt(static_cast<double>(count))));
        MachineCreateDesc description;
        description.recipe_duration_ticks = 90;
        for (std::size_t index = 0; index < count; ++index) {
            description.transform.x = static_cast<std::int32_t>(index % static_cast<std::size_t>(width));
            description.transform.z = static_cast<std::int32_t>(index / static_cast<std::size_t>(width));
            description.transform.rotation = static_cast<std::uint8_t>(index & 3U);
            description.inventory_index = static_cast<std::uint32_t>(index);
            description.recipe_id = static_cast<std::uint16_t>(index % 32U);
            description.speed = static_cast<std::uint16_t>(1U + index % 4U);
            const EntityId id = simulation.create_machine(description);
            if (!id.is_valid()) {
                std::cerr << "Failed to create machine at index " << index << '\n';
                return 2;
            }
            ids.push_back(id);
        }

        for (double ratio : active_ratios) {
            set_active_ratio(simulation, ids, ratio);
            for (MachineTickMode mode : {MachineTickMode::scan_all, MachineTickMode::active_list}) {
                const TimingResult timing = measure(simulation, mode, iteration_count(count));
                const SimulationStatistics stats = simulation.query_statistics();
                std::cout << count << ','
                          << static_cast<int>(ratio * 100.0) << ','
                          << (mode == MachineTickMode::scan_all ? "scan_all" : "active_list") << ','
                          << timing.median_ms << ',' << timing.p95_ms << ',' << timing.maximum_ms << ','
                          << stats.reserved_bytes << ',' << stats.capacity_growth_count << ','
                          << timing.checksum << '\n';
            }
        }
    }
    std::cerr << "benchmark_checksum=" << benchmark_checksum << '\n';
    return 0;
}
