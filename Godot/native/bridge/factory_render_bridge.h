#ifndef PROJECTF_FACTORY_RENDER_BRIDGE_H
#define PROJECTF_FACTORY_RENDER_BRIDGE_H

#include "factory_simulation_bridge.h"
#include "projectf/core/render_snapshot.hpp"

#include <godot_cpp/classes/box_mesh.hpp>
#include <godot_cpp/classes/camera3d.hpp>
#include <godot_cpp/classes/multi_mesh.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/classes/standard_material3d.hpp>
#include <godot_cpp/variant/aabb.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/node_path.hpp>
#include <godot_cpp/variant/rid.hpp>

#include <cstdint>
#include <vector>

namespace godot {

class FactoryRenderBridge : public Node3D {
    GDCLASS(FactoryRenderBridge, Node3D)

public:
    FactoryRenderBridge() = default;
    ~FactoryRenderBridge() override;

    void _ready() override;
    void _process(double delta) override;
    void _exit_tree() override;

    void rebuild_render_batches();
    Dictionary get_render_statistics() const;
    void set_simulation_bridge_path(const NodePath &value);
    NodePath get_simulation_bridge_path() const;

protected:
    static void _bind_methods();

private:
    struct RuntimeBatch {
        Ref<MultiMesh> multi_mesh;
        RID render_instance;
        AABB bounds;
        std::uint32_t instance_count = 0;
        bool visible = true;
    };

    void release_batches();
    void update_visibility();
    Camera3D *find_camera() const;
    static bool intersects_frustum(const AABB &bounds, const TypedArray<Plane> &planes);

    NodePath simulation_bridge_path_ = NodePath("../FactorySimulation");
    FactorySimulationBridge *simulation_bridge_ = nullptr;
    Ref<BoxMesh> placeholder_mesh_;
    Ref<StandardMaterial3D> placeholder_material_;
    projectf::core::RenderSnapshot snapshot_;
    std::vector<RuntimeBatch> batches_;
    std::uint64_t rendered_revision_ = 0;
    std::uint64_t visible_instances_ = 0;
    std::uint32_t visible_batches_ = 0;
    std::uint64_t uploaded_bytes_ = 0;
    double last_rebuild_ms_ = 0.0;
    double last_visibility_ms_ = 0.0;
};

} // namespace godot

#endif
