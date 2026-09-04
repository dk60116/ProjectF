#ifndef PROJECTF_CORE_FIXED_TICK_HPP
#define PROJECTF_CORE_FIXED_TICK_HPP

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace projectf::core {

class FixedTickClock {
public:
    FixedTickClock(double ticks_per_second = 30.0, std::uint32_t maximum_catch_up_steps = 4) noexcept {
        configure(ticks_per_second, maximum_catch_up_steps);
    }

    void configure(double ticks_per_second, std::uint32_t maximum_catch_up_steps) noexcept {
        ticks_per_second_ = std::max(1.0, ticks_per_second);
        fixed_delta_seconds_ = 1.0 / ticks_per_second_;
        maximum_catch_up_steps_ = std::max<std::uint32_t>(1, maximum_catch_up_steps);
        accumulator_seconds_ = std::min(accumulator_seconds_, fixed_delta_seconds_);
    }

    [[nodiscard]] std::uint32_t consume(double frame_delta_seconds) noexcept {
        if (frame_delta_seconds <= 0.0) {
            return 0;
        }
        accumulator_seconds_ += frame_delta_seconds;
        // Avoid missing an exact boundary such as two 1/60 frames for a 1/30 tick.
        constexpr double boundary_epsilon = 1.0e-12;
        const auto requested_steps = static_cast<std::uint64_t>(
                std::floor((accumulator_seconds_ + boundary_epsilon) / fixed_delta_seconds_));
        const std::uint32_t steps = static_cast<std::uint32_t>(
                std::min<std::uint64_t>(requested_steps, maximum_catch_up_steps_));
        accumulator_seconds_ -= static_cast<double>(steps) * fixed_delta_seconds_;
        if (accumulator_seconds_ < 0.0 && accumulator_seconds_ > -boundary_epsilon) {
            accumulator_seconds_ = 0.0;
        }
        if (requested_steps > maximum_catch_up_steps_) {
            const double retained = std::fmod(accumulator_seconds_, fixed_delta_seconds_);
            dropped_seconds_ += accumulator_seconds_ - retained;
            accumulator_seconds_ = retained;
        }
        return steps;
    }

    void reset() noexcept {
        accumulator_seconds_ = 0.0;
        dropped_seconds_ = 0.0;
    }

    [[nodiscard]] double ticks_per_second() const noexcept { return ticks_per_second_; }
    [[nodiscard]] double fixed_delta_seconds() const noexcept { return fixed_delta_seconds_; }
    [[nodiscard]] double interpolation_alpha() const noexcept {
        return fixed_delta_seconds_ > 0.0 ? accumulator_seconds_ / fixed_delta_seconds_ : 0.0;
    }
    [[nodiscard]] double dropped_seconds() const noexcept { return dropped_seconds_; }

private:
    double ticks_per_second_ = 30.0;
    double fixed_delta_seconds_ = 1.0 / 30.0;
    double accumulator_seconds_ = 0.0;
    double dropped_seconds_ = 0.0;
    std::uint32_t maximum_catch_up_steps_ = 4;
};

} // namespace projectf::core

#endif
