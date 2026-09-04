#include "virtual_joystick.h"

#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/classes/input_event_mouse_button.hpp>
#include <godot_cpp/classes/input_event_mouse_motion.hpp>
#include <godot_cpp/classes/input_event_screen_drag.hpp>
#include <godot_cpp/classes/input_event_screen_touch.hpp>
#include <godot_cpp/classes/texture_rect.hpp>
#include <godot_cpp/classes/viewport.hpp>
#include <godot_cpp/classes/global_constants.hpp>
#include <godot_cpp/core/class_db.hpp>

#include <algorithm>

namespace godot {

void VirtualJoystick::_bind_methods() {
    ClassDB::bind_method(D_METHOD("reset_input"), &VirtualJoystick::reset_input);
    ClassDB::bind_method(D_METHOD("get_input_direction"), &VirtualJoystick::get_input_direction);
    ClassDB::bind_method(D_METHOD("set_handle_range", "value"), &VirtualJoystick::set_handle_range);
    ClassDB::bind_method(D_METHOD("get_handle_range"), &VirtualJoystick::get_handle_range);
    ClassDB::bind_method(D_METHOD("set_background_path", "value"), &VirtualJoystick::set_background_path);
    ClassDB::bind_method(D_METHOD("get_background_path"), &VirtualJoystick::get_background_path);
    ClassDB::bind_method(D_METHOD("set_handle_path", "value"), &VirtualJoystick::set_handle_path);
    ClassDB::bind_method(D_METHOD("get_handle_path"), &VirtualJoystick::get_handle_path);

    ADD_PROPERTY(PropertyInfo(Variant::FLOAT, "handle_range", PROPERTY_HINT_RANGE, "0,300,1,or_greater"),
            "set_handle_range", "get_handle_range");
    ADD_PROPERTY(PropertyInfo(Variant::NODE_PATH, "background_path", PROPERTY_HINT_NODE_PATH_VALID_TYPES, "TextureRect"),
            "set_background_path", "get_background_path");
    ADD_PROPERTY(PropertyInfo(Variant::NODE_PATH, "handle_path", PROPERTY_HINT_NODE_PATH_VALID_TYPES, "TextureRect"),
            "set_handle_path", "get_handle_path");
}

void VirtualJoystick::_ready() {
    set_mouse_filter(Control::MOUSE_FILTER_STOP);
    set_focus_mode(Control::FOCUS_NONE);
    set_process_input(true);
    background = Object::cast_to<TextureRect>(get_node_or_null(background_path));
    handle = Object::cast_to<TextureRect>(get_node_or_null(handle_path));
    if (background != nullptr) {
        background->set_mouse_filter(Control::MOUSE_FILTER_IGNORE);
    }
    if (handle != nullptr) {
        handle->set_mouse_filter(Control::MOUSE_FILTER_IGNORE);
    }
    reset_input();
}

void VirtualJoystick::_process(double delta) {
    (void)delta;
    if (active_pointer_is_touch || active_pointer_id != mouse_pointer) {
        return;
    }

    Input *input = Input::get_singleton();
    if (input == nullptr || !input->is_mouse_button_pressed(MOUSE_BUTTON_LEFT)) {
        reset_input();
        return;
    }

    // Mouse motion events are not guaranteed to retain their button mask on every
    // platform/window transition. Polling while a drag is active keeps the floating
    // joystick responsive and also follows the pointer outside its visual children.
    update_pointer(mouse_pointer, false, get_local_mouse_position());
}

void VirtualJoystick::_input(const Ref<InputEvent> &event) {
    handle_mouse_input(event);
}

void VirtualJoystick::_gui_input(const Ref<InputEvent> &event) {
    const Ref<InputEventScreenTouch> touch = event;
    if (touch.is_valid()) {
        if (touch->is_pressed()) {
            if (active_pointer_id == no_active_pointer) {
                begin_pointer(touch->get_index(), true, touch->get_position());
                accept_event();
            }
        } else if (active_pointer_is_touch && active_pointer_id == touch->get_index()) {
            reset_input();
            accept_event();
        }
        return;
    }

    const Ref<InputEventScreenDrag> touch_drag = event;
    if (touch_drag.is_valid()) {
        if (active_pointer_is_touch && active_pointer_id == touch_drag->get_index()) {
            update_pointer(touch_drag->get_index(), true, touch_drag->get_position());
            accept_event();
        }
        return;
    }

}

void VirtualJoystick::_exit_tree() {
    reset_input();
}

void VirtualJoystick::reset_input() {
    input_direction = Vector2();
    joystick_center = Vector2();
    active_pointer_id = no_active_pointer;
    active_pointer_is_touch = false;
    set_process(false);
    set_visual_center(Vector2());
    set_visual_active(false);
}

Vector2 VirtualJoystick::get_input_direction() const {
    return input_direction;
}

void VirtualJoystick::set_handle_range(double value) {
    handle_range = std::max(0.0, value);
}

double VirtualJoystick::get_handle_range() const {
    return handle_range;
}

void VirtualJoystick::set_background_path(const NodePath &value) {
    background_path = value;
}

NodePath VirtualJoystick::get_background_path() const {
    return background_path;
}

void VirtualJoystick::set_handle_path(const NodePath &value) {
    handle_path = value;
}

NodePath VirtualJoystick::get_handle_path() const {
    return handle_path;
}

void VirtualJoystick::handle_mouse_input(const Ref<InputEvent> &event) {
    const Ref<InputEventMouseButton> mouse_button = event;
    if (mouse_button.is_valid() && mouse_button->get_button_index() == MOUSE_BUTTON_LEFT) {
        const Vector2 local_position = viewport_to_local(mouse_button->get_position());
        if (mouse_button->is_pressed()) {
            if (active_pointer_id == no_active_pointer && can_begin_mouse_pointer(local_position)) {
                begin_pointer(mouse_pointer, false, local_position);
            }
        } else if (!active_pointer_is_touch && active_pointer_id == mouse_pointer) {
            reset_input();
        }
        return;
    }

    const Ref<InputEventMouseMotion> mouse_motion = event;
    if (mouse_motion.is_valid()
            && !active_pointer_is_touch
            && active_pointer_id == mouse_pointer) {
        update_pointer(mouse_pointer, false, viewport_to_local(mouse_motion->get_position()));
    }
}

bool VirtualJoystick::can_begin_mouse_pointer(const Vector2 &local_position) const {
    const Vector2 control_size = get_size();
    if (local_position.x < 0.0 || local_position.y < 0.0
            || local_position.x > control_size.x || local_position.y > control_size.y) {
        return false;
    }

    const Viewport *viewport = get_viewport();
    const Control *hovered = viewport != nullptr ? viewport->gui_get_hovered_control() : nullptr;
    if (hovered == nullptr || hovered == this) {
        return true;
    }

    const Node *node = hovered;
    while (node != nullptr) {
        if (node == this) {
            return true;
        }
        node = node->get_parent();
    }
    return false;
}

Vector2 VirtualJoystick::viewport_to_local(const Vector2 &viewport_position) const {
    return get_global_transform_with_canvas().affine_inverse().xform(viewport_position);
}

void VirtualJoystick::begin_pointer(
        std::int32_t pointer_id,
        bool is_touch,
        const Vector2 &position) {
    active_pointer_id = pointer_id;
    active_pointer_is_touch = is_touch;
    set_process(!is_touch);
    joystick_center = position;
    set_visual_center(position);
    set_visual_active(true);
    update_pointer(pointer_id, is_touch, position);
}

void VirtualJoystick::update_pointer(
        std::int32_t pointer_id,
        bool is_touch,
        const Vector2 &position) {
    if (active_pointer_id != pointer_id || active_pointer_is_touch != is_touch
            || background == nullptr || handle == nullptr) {
        return;
    }

    const Vector2 radius = background->get_size() * 0.5;
    if (radius.x <= 0.0 || radius.y <= 0.0) {
        return;
    }
    const Vector2 offset = position - joystick_center;
    Vector2 visual_direction(offset.x / radius.x, offset.y / radius.y);
    if (visual_direction.length_squared() > 1.0) {
        visual_direction = visual_direction.normalized();
    }
    input_direction = Vector2(visual_direction.x, -visual_direction.y);
    handle->set_position(
            joystick_center + visual_direction * static_cast<real_t>(handle_range)
            - handle->get_size() * 0.5);
}

void VirtualJoystick::set_visual_active(bool active) {
    if (background != nullptr) {
        background->set_visible(active);
    }
    if (handle != nullptr) {
        handle->set_visible(active);
    }
}

void VirtualJoystick::set_visual_center(const Vector2 &center) {
    if (background != nullptr) {
        background->set_position(center - background->get_size() * 0.5);
    }
    if (handle != nullptr) {
        handle->set_position(center - handle->get_size() * 0.5);
    }
}

} // namespace godot
