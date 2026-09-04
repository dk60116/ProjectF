#ifndef PLAYER_SPAWNER_H
#define PLAYER_SPAWNER_H

#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/classes/packed_scene.hpp>

namespace godot {

class PlayerSpawner : public Node3D {
    GDCLASS(PlayerSpawner, Node3D)

private:
    Ref<PackedScene> player_scene;
    Vector3 spawn_position;

protected:
    static void _bind_methods();

public:
    void _ready() override;
    Node3D *spawn_player();

    void set_player_scene(const Ref<PackedScene> &value);
    Ref<PackedScene> get_player_scene() const;

    void set_spawn_position(const Vector3 &value);
    Vector3 get_spawn_position() const;
};

} // namespace godot

#endif
