#ifndef PROJECTF_PLAYER_H
#define PROJECTF_PLAYER_H

#include <godot_cpp/classes/character_body3d.hpp>
#include <godot_cpp/variant/node_path.hpp>

#include <array>
#include <cstdint>

namespace godot {

class Camera3D;
class Node3D;
class VirtualJoystick;

class ProjectFPlayer : public CharacterBody3D {
    GDCLASS(ProjectFPlayer, CharacterBody3D)

private:
    double move_speed = 5.0;
    double rotation_response = 12.0;
    NodePath visual_path = NodePath("Visual");
    NodePath joystick_path = NodePath("HUD/VirtualJoystick");
    Node3D *visual = nullptr;
    Camera3D *movement_camera = nullptr;
    VirtualJoystick *virtual_joystick = nullptr;
    double gravity = 9.8;
    bool input_actions_ready = false;

    double min_orthographic_size = 3.0;
    double max_orthographic_size = 10.0;
    double orthographic_wheel_zoom_speed = 0.5;
    double orthographic_pinch_zoom_speed = 0.01;
    double zoom_smooth_time = 0.08;
    double zoom_button_step = 0.5;
    double target_orthographic_size = 3.0;
    double orthographic_zoom_velocity = 0.0;
    bool zoom_initialized = false;

    struct TouchPoint {
        Vector2 position;
        std::int32_t index = -1;
        bool active = false;
    };
    std::array<TouchPoint, 10> zoom_touches{};

    bool ensure_input_actions();
    Vector3 get_camera_relative_direction() const;
    void update_visual_rotation(const Vector3 &direction, double delta);
    void update_camera_zoom(double delta);
    void apply_zoom_delta(double zoom_delta);
    void update_touch_state(std::int32_t index, const Vector2 &position, bool active);
    double get_active_touch_distance() const;
    void normalize_zoom_settings();

protected:
    static void _bind_methods();

public:
    void _ready() override;
    void _process(double delta) override;
    void _physics_process(double delta) override;
    void _input(const Ref<InputEvent> &event) override;

    void zoom_in();
    void zoom_out();

    void set_move_speed(double value);
    double get_move_speed() const;

    void set_rotation_response(double value);
    double get_rotation_response() const;

    void set_visual_path(const NodePath &value);
    NodePath get_visual_path() const;

    void set_joystick_path(const NodePath &value);
    NodePath get_joystick_path() const;
    void set_min_orthographic_size(double value);
    double get_min_orthographic_size() const;
    void set_max_orthographic_size(double value);
    double get_max_orthographic_size() const;
    void set_orthographic_wheel_zoom_speed(double value);
    double get_orthographic_wheel_zoom_speed() const;
    void set_orthographic_pinch_zoom_speed(double value);
    double get_orthographic_pinch_zoom_speed() const;
    void set_zoom_smooth_time(double value);
    double get_zoom_smooth_time() const;
    void set_zoom_button_step(double value);
    double get_zoom_button_step() const;
    double get_target_orthographic_size() const;
};

} // namespace godot

#endif
