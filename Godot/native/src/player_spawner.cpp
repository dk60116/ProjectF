#include "player_spawner.h"

#include <godot_cpp/classes/scene_tree.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

namespace godot {

void PlayerSpawner::_bind_methods() {
    ClassDB::bind_method(D_METHOD("spawn_player"), &PlayerSpawner::spawn_player);
    ClassDB::bind_method(D_METHOD("set_player_scene", "value"), &PlayerSpawner::set_player_scene);
    ClassDB::bind_method(D_METHOD("get_player_scene"), &PlayerSpawner::get_player_scene);
    ClassDB::bind_method(D_METHOD("set_spawn_position", "value"), &PlayerSpawner::set_spawn_position);
    ClassDB::bind_method(D_METHOD("get_spawn_position"), &PlayerSpawner::get_spawn_position);

    ADD_PROPERTY(PropertyInfo(Variant::OBJECT, "player_scene", PROPERTY_HINT_RESOURCE_TYPE, "PackedScene"),
            "set_player_scene", "get_player_scene");
    ADD_PROPERTY(PropertyInfo(Variant::VECTOR3, "spawn_position"),
            "set_spawn_position", "get_spawn_position");
}

void PlayerSpawner::_ready() {
    call_deferred(StringName("spawn_player"));
}

Node3D *PlayerSpawner::spawn_player() {
    SceneTree *tree = get_tree();
    Node *existing_player = tree != nullptr
            ? tree->get_first_node_in_group(StringName("player"))
            : nullptr;
    if (existing_player != nullptr) {
        return Object::cast_to<Node3D>(existing_player);
    }

    if (player_scene.is_null()) {
        UtilityFunctions::push_error("PlayerSpawner: player_scene is not assigned.");
        return nullptr;
    }

    Node *instance = player_scene->instantiate();
    Node3D *player = Object::cast_to<Node3D>(instance);
    if (player == nullptr) {
        UtilityFunctions::push_error("PlayerSpawner: the player scene root must inherit Node3D.");
        if (instance != nullptr) {
            instance->queue_free();
        }
        return nullptr;
    }

    Node *host = get_parent();
    if (host == nullptr) {
        UtilityFunctions::push_error("PlayerSpawner: a parent node is required before spawning.");
        instance->queue_free();
        return nullptr;
    }

    host->add_child(player);
    player->set_global_position(spawn_position);
    return player;
}

void PlayerSpawner::set_player_scene(const Ref<PackedScene> &value) {
    player_scene = value;
}

Ref<PackedScene> PlayerSpawner::get_player_scene() const {
    return player_scene;
}

void PlayerSpawner::set_spawn_position(const Vector3 &value) {
    spawn_position = value;
}

Vector3 PlayerSpawner::get_spawn_position() const {
    return spawn_position;
}

} // namespace godot
