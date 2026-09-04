#include "projectf_player.h"
#include "virtual_joystick.h"

#include <godot_cpp/classes/camera3d.hpp>
#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/classes/input_event_mouse_button.hpp>
#include <godot_cpp/classes/input_event_screen_drag.hpp>
#include <godot_cpp/classes/input_event_screen_touch.hpp>
#include <godot_cpp/classes/input_map.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/classes/project_settings.hpp>
#include <godot_cpp/classes/viewport.hpp>
#include <godot_cpp/classes/global_constants.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include <algorithm>
#include <array>
#include <cmath>

namespace godot {

namespace {

constexpr std::array<const char *, 4> required_input_actions{{
    "move_left",
    "move_right",
    "move_forward",
    "move_backward",
}};

double smooth_damp(
        double current,
        double target,
        double &current_velocity,
        double smooth_time,
        double delta) {
    smooth_time = std::max(0.0001, smooth_time);
    const double omega = 2.0 / smooth_time;
    const double x = omega * std::max(0.0, delta);
    const double exponential = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
    const double original_target = target;
    const double change = current - target;
    const double temporary = (current_velocity + omega * change) * delta;
    current_velocity = (current_velocity - omega * temporary) * exponential;
    double output = target + (change + temporary) * exponential;
    if ((original_target - current > 0.0) == (output > original_target)) {
        output = original_target;
        current_velocity = delta > 0.0 ? (output - original_target) / delta : 0.0;
    }
    return output;
}

} // namespace

void ProjectFPlayer::_bind_methods() {
    ClassDB::bind_method(D_METHOD("set_move_speed", "value"), &ProjectFPlayer::set_move_speed);
    ClassDB::bind_method(D_METHOD("get_move_speed"), &ProjectFPlayer::get_move_speed);
    ClassDB::bind_method(D_METHOD("set_rotation_response", "value"), &ProjectFPlayer::set_rotation_response);
    ClassDB::bind_method(D_METHOD("get_rotation_response"), &ProjectFPlayer::get_rotation_response);
    ClassDB::bind_method(D_METHOD("set_visual_path", "value"), &ProjectFPlayer::set_visual_path);
    ClassDB::bind_method(D_METHOD("get_visual_path"), &ProjectFPlayer::get_visual_path);
    ClassDB::bind_method(D_METHOD("set_joystick_path", "value"), &ProjectFPlayer::set_joystick_path);
    ClassDB::bind_method(D_METHOD("get_joystick_path"), &ProjectFPlayer::get_joystick_path);
    ClassDB::bind_method(D_METHOD("zoom_in"), &ProjectFPlayer::zoom_in);
    ClassDB::bind_method(D_METHOD("zoom_out"), &ProjectFPlayer::zoom_out);
    ClassDB::bind_method(D_METHOD("set_min_orthographic_size", "value"), &ProjectFPlayer::set_min_orthographic_size);
    ClassDB::bind_method(D_METHOD("get_min_orthographic_size"), &ProjectFPlayer::get_min_orthographic_size);
    ClassDB::bind_method(D_METHOD("set_max_orthographic_size", "value"), &ProjectFPlayer::set_max_orthographic_size);
    ClassDB::bind_method(D_METHOD("get_max_orthographic_size"), &ProjectFPlayer::get_max_orthographic_size);
    ClassDB::bind_method(D_METHOD("set_orthographic_wheel_zoom_speed", "value"), &ProjectFPlayer::set_orthographic_wheel_zoom_speed);
    ClassDB::bind_method(D_METHOD("get_orthographic_wheel_zoom_speed"), &ProjectFPlayer::get_orthographic_wheel_zoom_speed);
    ClassDB::bind_method(D_METHOD("set_orthographic_pinch_zoom_speed", "value"), &ProjectFPlayer::set_orthographic_pinch_zoom_speed);
    ClassDB::bind_method(D_METHOD("get_orthographic_pinch_zoom_speed"), &ProjectFPlayer::get_orthographic_pinch_zoom_speed);
    ClassDB::bind_method(D_METHOD("set_zoom_smooth_time", "value"), &ProjectFPlayer::set_zoom_smooth_time);
    ClassDB::bind_method(D_METHOD("get_zoom_smooth_time"), &ProjectFPlayer::get_zoom_smooth_time);
    ClassDB::bind_method(D_METHOD("set_zoom_button_step", "value"), &ProjectFPlayer::set_zoom_button_step);
    ClassDB::bind_method(D_METHOD("get_zoom_button_step"), &ProjectFPlayer::get_zoom_button_step);
    ClassDB::bind_method(D_METHOD("get_target_orthographic_size"), &ProjectFPlayer::get_target_orthographic_size);

    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "move_speed", PROPERTY_HINT_RANGE, "0.1,30.0,0.1,or_greater"),
            "set_move_speed", "get_move_speed");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "rotation_response", PROPERTY_HINT_RANGE, "0.1,30.0,0.1,or_greater"),
            "set_rotation_response", "get_rotation_response");
    ADD_PROPERTY(PropertyInfo(Variant::NODE_PATH, "visual_path", PROPERTY_HINT_NODE_PATH_VALID_TYPES, "Node3D"),
            "set_visual_path", "get_visual_path");
    ADD_PROPERTY(PropertyInfo(Variant::NODE_PATH, "joystick_path", PROPERTY_HINT_NODE_PATH_VALID_TYPES, "VirtualJoystick"),
            "set_joystick_path", "get_joystick_path");
    ADD_GROUP("Camera Zoom", "");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "min_orthographic_size", PROPERTY_HINT_RANGE, "0.1,100,0.1"),
            "set_min_orthographic_size", "get_min_orthographic_size");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "max_orthographic_size", PROPERTY_HINT_RANGE, "0.1,100,0.1"),
            "set_max_orthographic_size", "get_max_orthographic_size");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "orthographic_wheel_zoom_speed", PROPERTY_HINT_RANGE, "0,10,0.05"),
            "set_orthographic_wheel_zoom_speed", "get_orthographic_wheel_zoom_speed");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "orthographic_pinch_zoom_speed", PROPERTY_HINT_RANGE, "0,1,0.001"),
            "set_orthographic_pinch_zoom_speed", "get_orthographic_pinch_zoom_speed");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "zoom_smooth_time", PROPERTY_HINT_RANGE, "0,2,0.01"),
            "set_zoom_smooth_time", "get_zoom_smooth_time");
    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "zoom_button_step", PROPERTY_HINT_RANGE, "0.1,10,0.1"),
            "set_zoom_button_step", "get_zoom_button_step");
}

void ProjectFPlayer::_ready() {
    add_to_group(StringName("player"));
    input_actions_ready = ensure_input_actions();

    visual = Object::cast_to<Node3D>(get_node_or_null(visual_path));
    virtual_joystick = Object::cast_to<VirtualJoystick>(get_node_or_null(joystick_path));
    movement_camera = get_viewport() != nullptr ? get_viewport()->get_camera_3d() : nullptr;
    normalize_zoom_settings();
    if (movement_camera != nullptr) {
        target_orthographic_size = std::clamp(
                static_cast<double>(movement_camera->get_size()),
                min_orthographic_size,
                max_orthographic_size);
        movement_camera->set_size(static_cast<real_t>(target_orthographic_size));
        zoom_initialized = true;
    }
    set_process_input(true);

    const Variant gravity_setting = ProjectSettings::get_singleton()->get_setting("physics/3d/default_gravity", 9.8);
    gravity = static_cast<double>(gravity_setting);
    if (gravity < 0.0) {
        gravity = 0.0;
    }

    if (movement_camera != nullptr) {
        movement_camera->look_at(get_global_position() + Vector3(0.0, 0.9, 0.0), Vector3::UP);
    }
}

void ProjectFPlayer::_process(double delta) {
    update_camera_zoom(delta);
}

void ProjectFPlayer::_physics_process(double delta) {
    if (movement_camera == nullptr && get_viewport() != nullptr) {
        movement_camera = get_viewport()->get_camera_3d();
    }

    Vector3 velocity = get_velocity();
    const Vector3 direction = get_camera_relative_direction();

    velocity.x = direction.x * move_speed;
    velocity.z = direction.z * move_speed;
    if (!is_on_floor()) {
        velocity.y -= gravity * delta;
    } else if (velocity.y < 0.0) {
        velocity.y = 0.0;
    }

    set_velocity(velocity);
    move_and_slide();
    update_visual_rotation(direction, delta);
}

void ProjectFPlayer::_input(const Ref<InputEvent> &event) {
    const Ref<InputEventMouseButton> mouse_button = event;
    if (mouse_button.is_valid() && mouse_button->is_pressed()) {
        const double factor = std::max(0.01, static_cast<double>(mouse_button->get_factor()));
        if (mouse_button->get_button_index() == MOUSE_BUTTON_WHEEL_UP) {
            apply_zoom_delta(orthographic_wheel_zoom_speed * factor);
        } else if (mouse_button->get_button_index() == MOUSE_BUTTON_WHEEL_DOWN) {
            apply_zoom_delta(-orthographic_wheel_zoom_speed * factor);
        }
        return;
    }

    const Ref<InputEventScreenTouch> touch = event;
    if (touch.is_valid()) {
        update_touch_state(touch->get_index(), touch->get_position(), touch->is_pressed());
        return;
    }

    const Ref<InputEventScreenDrag> drag = event;
    if (drag.is_valid()) {
        const double previous_distance = get_active_touch_distance();
        update_touch_state(drag->get_index(), drag->get_position(), true);
        const double current_distance = get_active_touch_distance();
        if (previous_distance > 0.0 && current_distance > 0.0) {
            apply_zoom_delta((current_distance - previous_distance) * orthographic_pinch_zoom_speed);
        }
    }
}

void ProjectFPlayer::zoom_in() {
    apply_zoom_delta(zoom_button_step);
}

void ProjectFPlayer::zoom_out() {
    apply_zoom_delta(-zoom_button_step);
}

Vector3 ProjectFPlayer::get_camera_relative_direction() const {
    static const StringName move_left_action("move_left");
    static const StringName move_right_action("move_right");
    static const StringName move_forward_action("move_forward");
    static const StringName move_backward_action("move_backward");

    const Input *input = Input::get_singleton();
    Vector2 input_vector;
    if (input_actions_ready && input != nullptr) {
        input_vector = Vector2(
                input->get_action_strength(move_right_action)
                        - input->get_action_strength(move_left_action),
                input->get_action_strength(move_forward_action)
                        - input->get_action_strength(move_backward_action));
    }

    if (virtual_joystick != nullptr) {
        input_vector += virtual_joystick->get_input_direction();
    }

    if (input_vector.length_squared() > 1.0) {
        input_vector = input_vector.normalized();
    }
    if (input_vector.length_squared() <= 0.0001) {
        return Vector3();
    }

    Vector3 forward(0.0, 0.0, -1.0);
    Vector3 right(1.0, 0.0, 0.0);
    if (movement_camera != nullptr) {
        const Basis camera_basis = movement_camera->get_global_transform().basis;
        forward = -camera_basis.get_column(2);
        right = camera_basis.get_column(0);
        forward.y = 0.0;
        right.y = 0.0;
        forward = forward.normalized();
        right = right.normalized();
    }

    Vector3 direction = right * input_vector.x + forward * input_vector.y;
    return direction.length_squared() > 1.0 ? direction.normalized() : direction;
}

void ProjectFPlayer::update_camera_zoom(double delta) {
    if (movement_camera == nullptr && get_viewport() != nullptr) {
        movement_camera = get_viewport()->get_camera_3d();
    }
    if (movement_camera == nullptr || movement_camera->get_projection() != Camera3D::PROJECTION_ORTHOGONAL) {
        return;
    }
    if (!zoom_initialized) {
        target_orthographic_size = std::clamp(
                static_cast<double>(movement_camera->get_size()),
                min_orthographic_size,
                max_orthographic_size);
        zoom_initialized = true;
    }

    if (zoom_smooth_time <= 0.0) {
        movement_camera->set_size(static_cast<real_t>(target_orthographic_size));
        orthographic_zoom_velocity = 0.0;
        return;
    }
    const double smoothed_size = smooth_damp(
            movement_camera->get_size(),
            target_orthographic_size,
            orthographic_zoom_velocity,
            zoom_smooth_time,
            delta);
    movement_camera->set_size(static_cast<real_t>(
            std::clamp(smoothed_size, min_orthographic_size, max_orthographic_size)));
}

void ProjectFPlayer::apply_zoom_delta(double zoom_delta) {
    if (!zoom_initialized && movement_camera != nullptr) {
        target_orthographic_size = movement_camera->get_size();
        zoom_initialized = true;
    }
    target_orthographic_size = std::clamp(
            target_orthographic_size - zoom_delta,
            min_orthographic_size,
            max_orthographic_size);
}

void ProjectFPlayer::update_touch_state(
        std::int32_t index,
        const Vector2 &position,
        bool active) {
    TouchPoint *free_slot = nullptr;
    for (TouchPoint &touch : zoom_touches) {
        if (touch.active && touch.index == index) {
            touch.position = position;
            touch.active = active;
            if (!active) {
                touch.index = -1;
            }
            return;
        }
        if (!touch.active && free_slot == nullptr) {
            free_slot = &touch;
        }
    }
    if (active && free_slot != nullptr) {
        free_slot->position = position;
        free_slot->index = index;
        free_slot->active = true;
    }
}

double ProjectFPlayer::get_active_touch_distance() const {
    const TouchPoint *first = nullptr;
    const TouchPoint *second = nullptr;
    for (const TouchPoint &touch : zoom_touches) {
        if (!touch.active) {
            continue;
        }
        if (first == nullptr) {
            first = &touch;
        } else {
            second = &touch;
            break;
        }
    }
    return first != nullptr && second != nullptr
            ? static_cast<double>(first->position.distance_to(second->position))
            : 0.0;
}

void ProjectFPlayer::normalize_zoom_settings() {
    min_orthographic_size = std::max(0.1, min_orthographic_size);
    max_orthographic_size = std::max(min_orthographic_size, max_orthographic_size);
    orthographic_wheel_zoom_speed = std::max(0.0, orthographic_wheel_zoom_speed);
    orthographic_pinch_zoom_speed = std::max(0.0, orthographic_pinch_zoom_speed);
    zoom_smooth_time = std::max(0.0, zoom_smooth_time);
    zoom_button_step = std::max(0.1, zoom_button_step);
    target_orthographic_size = std::clamp(
            target_orthographic_size, min_orthographic_size, max_orthographic_size);
}

bool ProjectFPlayer::ensure_input_actions() {
    InputMap *input_map = InputMap::get_singleton();
    if (input_map == nullptr) {
        return false;
    }

    bool needs_reload = false;
    for (const char *action_name : required_input_actions) {
        if (!input_map->has_action(StringName(action_name))) {
            needs_reload = true;
            break;
        }
    }

    if (needs_reload) {
        // The editor can keep an older InputMap after project.godot changes on disk.
        // Reload the single source of truth instead of duplicating key bindings here.
        input_map->load_from_project_settings();
    }

    for (const char *action_name : required_input_actions) {
        if (!input_map->has_action(StringName(action_name))) {
            UtilityFunctions::push_error(
                    String("ProjectFPlayer: required input action is missing: ") + action_name);
            return false;
        }
    }
    return true;
}

void ProjectFPlayer::update_visual_rotation(const Vector3 &direction, double delta) {
    if (visual == nullptr || direction.length_squared() <= 0.0001) {
        return;
    }

    Vector3 rotation = visual->get_rotation();
    const double target_yaw = std::atan2(direction.x, direction.z);
    const double interpolation = 1.0 - std::exp(-rotation_response * delta);
    constexpr double pi = 3.14159265358979323846;
    constexpr double tau = pi * 2.0;
    double yaw_delta = std::fmod(target_yaw - rotation.y + pi, tau);
    if (yaw_delta < 0.0) {
        yaw_delta += tau;
    }
    yaw_delta -= pi;
    rotation.y += yaw_delta * interpolation;
    visual->set_rotation(rotation);
}

void ProjectFPlayer::set_move_speed(double value) {
    move_speed = value > 0.0 ? value : 0.0;
}

double ProjectFPlayer::get_move_speed() const {
    return move_speed;
}

void ProjectFPlayer::set_rotation_response(double value) {
    rotation_response = value > 0.01 ? value : 0.01;
}

double ProjectFPlayer::get_rotation_response() const {
    return rotation_response;
}

void ProjectFPlayer::set_visual_path(const NodePath &value) {
    visual_path = value;
    visual = is_inside_tree()
            ? Object::cast_to<Node3D>(get_node_or_null(visual_path))
            : nullptr;
}

NodePath ProjectFPlayer::get_visual_path() const {
    return visual_path;
}

void ProjectFPlayer::set_joystick_path(const NodePath &value) {
    joystick_path = value;
    virtual_joystick = is_inside_tree()
            ? Object::cast_to<VirtualJoystick>(get_node_or_null(joystick_path))
            : nullptr;
}

NodePath ProjectFPlayer::get_joystick_path() const {
    return joystick_path;
}

void ProjectFPlayer::set_min_orthographic_size(double value) {
    min_orthographic_size = std::max(0.1, value);
    max_orthographic_size = std::max(min_orthographic_size, max_orthographic_size);
    normalize_zoom_settings();
}

double ProjectFPlayer::get_min_orthographic_size() const {
    return min_orthographic_size;
}

void ProjectFPlayer::set_max_orthographic_size(double value) {
    max_orthographic_size = std::max(min_orthographic_size, value);
    normalize_zoom_settings();
}

double ProjectFPlayer::get_max_orthographic_size() const {
    return max_orthographic_size;
}

void ProjectFPlayer::set_orthographic_wheel_zoom_speed(double value) {
    orthographic_wheel_zoom_speed = std::max(0.0, value);
}

double ProjectFPlayer::get_orthographic_wheel_zoom_speed() const {
    return orthographic_wheel_zoom_speed;
}

void ProjectFPlayer::set_orthographic_pinch_zoom_speed(double value) {
    orthographic_pinch_zoom_speed = std::max(0.0, value);
}

double ProjectFPlayer::get_orthographic_pinch_zoom_speed() const {
    return orthographic_pinch_zoom_speed;
}

void ProjectFPlayer::set_zoom_smooth_time(double value) {
    zoom_smooth_time = std::max(0.0, value);
}

double ProjectFPlayer::get_zoom_smooth_time() const {
    return zoom_smooth_time;
}

void ProjectFPlayer::set_zoom_button_step(double value) {
    zoom_button_step = std::max(0.1, value);
}

double ProjectFPlayer::get_zoom_button_step() const {
    return zoom_button_step;
}

double ProjectFPlayer::get_target_orthographic_size() const {
    return target_orthographic_size;
}

} // namespace godot
