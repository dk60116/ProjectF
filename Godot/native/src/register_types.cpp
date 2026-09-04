#include "register_types.h"

#include "factory_render_bridge.h"
#include "factory_simulation_bridge.h"
#include "player_spawner.h"
#include "projectf_player.h"
#include "virtual_joystick.h"

#include <godot_cpp/godot.hpp>

void initialize_projectf_module(godot::ModuleInitializationLevel level) {
    if (level != godot::MODULE_INITIALIZATION_LEVEL_SCENE) {
        return;
    }

    godot::ClassDB::register_class<godot::ProjectFPlayer>();
    godot::ClassDB::register_class<godot::PlayerSpawner>();
    godot::ClassDB::register_class<godot::FactorySimulationBridge>();
    godot::ClassDB::register_class<godot::FactoryRenderBridge>();
    godot::ClassDB::register_class<godot::VirtualJoystick>();
}

void uninitialize_projectf_module(godot::ModuleInitializationLevel level) {
    if (level != godot::MODULE_INITIALIZATION_LEVEL_SCENE) {
        return;
    }
}

extern "C" {

GDExtensionBool GDE_EXPORT projectf_library_init(
        GDExtensionInterfaceGetProcAddress get_proc_address,
        const GDExtensionClassLibraryPtr library,
        GDExtensionInitialization *initialization) {
    godot::GDExtensionBinding::InitObject init_object(get_proc_address, library, initialization);

    init_object.register_initializer(initialize_projectf_module);
    init_object.register_terminator(uninitialize_projectf_module);
    init_object.set_minimum_library_initialization_level(godot::MODULE_INITIALIZATION_LEVEL_SCENE);

    return init_object.init();
}
}
