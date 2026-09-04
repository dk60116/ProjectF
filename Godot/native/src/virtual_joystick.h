#ifndef PROJECTF_VIRTUAL_JOYSTICK_H
#define PROJECTF_VIRTUAL_JOYSTICK_H

#include <godot_cpp/classes/control.hpp>
#include <godot_cpp/classes/input_event.hpp>
#include <godot_cpp/variant/node_path.hpp>

#include <cstdint>

namespace godot {

class TextureRect;

class VirtualJoystick : public Control {
    GDCLASS(VirtualJoystick, Control)

private:
    static constexpr std::int32_t no_active_pointer = -2147483647;
    static constexpr std::int32_t mouse_pointer = -1;

    NodePath background_path = NodePath("Background");
    NodePath handle_path = NodePath("Handle");
    TextureRect *background = nullptr;
    TextureRect *handle = nullptr;
    Vector2 input_direction;
    Vector2 joystick_center;
    double handle_range = 100.0;
    std::int32_t active_pointer_id = no_active_pointer;
    bool active_pointer_is_touch = false;

    void begin_pointer(std::int32_t pointer_id, bool is_touch, const Vector2 &position);
    void update_pointer(std::int32_t pointer_id, bool is_touch, const Vector2 &position);
    void handle_mouse_input(const Ref<InputEvent> &event);
    bool can_begin_mouse_pointer(const Vector2 &local_position) const;
    Vector2 viewport_to_local(const Vector2 &viewport_position) const;
    void set_visual_active(bool active);
    void set_visual_center(const Vector2 &center);

protected:
    static void _bind_methods();

public:
    void _ready() override;
    void _process(double delta) override;
    void _input(const Ref<InputEvent> &event) override;
    void _gui_input(const Ref<InputEvent> &event) override;
    void _exit_tree() override;

    void reset_input();
    Vector2 get_input_direction() const;

    void set_handle_range(double value);
    double get_handle_range() const;
    void set_background_path(const NodePath &value);
    NodePath get_background_path() const;
    void set_handle_path(const NodePath &value);
    NodePath get_handle_path() const;
};

} // namespace godot

#endif
