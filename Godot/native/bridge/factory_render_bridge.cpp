#include "factory_render_bridge.h"

#include <godot_cpp/classes/rendering_server.hpp>
#include <godot_cpp/classes/time.hpp>
#include <godot_cpp/classes/viewport.hpp>
#include <godot_cpp/classes/world3d.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/packed_float32_array.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include <cmath>
#include <utility>

namespace godot {

FactoryRenderBridge::~FactoryRenderBridge() {
    release_batches();
}

void FactoryRenderBridge::_bind_methods() {
    ClassDB::bind_method(D_METHOD("rebuild_render_batches"), &FactoryRenderBridge::rebuild_render_batches);
    ClassDB::bind_method(D_METHOD("get_render_statistics"), &FactoryRenderBridge::get_render_statistics);
    ClassDB::bind_method(D_METHOD("set_simulation_bridge_path", "value"), &FactoryRenderBridge::set_simulation_bridge_path);
    ClassDB::bind_method(D_METHOD("get_simulation_bridge_path"), &FactoryRenderBridge::get_simulation_bridge_path);

    ADD_PROPERTY(PropertyInfo(Variant::NODE_PATH, "simulation_bridge_path", PROPERTY_HINT_NODE_PATH_VALID_TYPES,
                         "FactorySimulationBridge"),
            "set_simulation_bridge_path", "get_simulation_bridge_path");
}

void FactoryRenderBridge::_ready() {
    simulation_bridge_ = Object::cast_to<FactorySimulationBridge>(get_node_or_null(simulation_bridge_path_));
    if (simulation_bridge_ == nullptr) {
        UtilityFunctions::push_error("FactoryRenderBridge: simulation_bridge_path is invalid.");
        set_process(false);
        return;
    }

    placeholder_material_.instantiate();
    placeholder_material_->set_albedo(Color(0.28, 0.58, 0.78, 1.0));
    placeholder_material_->set_roughness(0.72);
    placeholder_mesh_.instantiate();
    placeholder_mesh_->set_size(Vector3(0.82, 0.82, 0.82));
    placeholder_mesh_->set_material(placeholder_material_);
    rebuild_render_batches();
}

void FactoryRenderBridge::_process(double) {
    if (simulation_bridge_ != nullptr
            && rendered_revision_ != simulation_bridge_->simulation().render_revision()) {
        rebuild_render_batches();
    }
    update_visibility();
}

void FactoryRenderBridge::_exit_tree() {
    release_batches();
}

void FactoryRenderBridge::rebuild_render_batches() {
    if (simulation_bridge_ == nullptr || !simulation_bridge_->simulation().initialized()) {
        return;
    }
    const std::uint64_t start_usec = Time::get_singleton()->get_ticks_usec();
    release_batches();
    simulation_bridge_->simulation().build_render_snapshot(snapshot_);
    batches_.reserve(snapshot_.batches.size());

    RenderingServer *rendering_server = RenderingServer::get_singleton();
    const Ref<World3D> world = get_world_3d();
    if (rendering_server == nullptr || world.is_null()) {
        return;
    }

    uploaded_bytes_ = 0;
    for (const projectf::core::RenderBatchRange &source_batch : snapshot_.batches) {
        RuntimeBatch batch;
        batch.instance_count = source_batch.instance_count;
        batch.bounds = AABB(
                Vector3(source_batch.bounds.minimum.x, source_batch.bounds.minimum.y, source_batch.bounds.minimum.z),
                Vector3(
                        source_batch.bounds.maximum.x - source_batch.bounds.minimum.x,
                        source_batch.bounds.maximum.y - source_batch.bounds.minimum.y,
                        source_batch.bounds.maximum.z - source_batch.bounds.minimum.z));
        batch.multi_mesh.instantiate();
        batch.multi_mesh->set_transform_format(MultiMesh::TRANSFORM_3D);
        batch.multi_mesh->set_use_colors(false);
        batch.multi_mesh->set_use_custom_data(false);
        batch.multi_mesh->set_instance_count(static_cast<std::int32_t>(source_batch.instance_count));
        batch.multi_mesh->set_mesh(placeholder_mesh_);

        PackedFloat32Array transform_buffer;
        transform_buffer.resize(static_cast<std::int64_t>(source_batch.instance_count) * 12);
        float *write = transform_buffer.ptrw();
        for (std::uint32_t local_index = 0; local_index < source_batch.instance_count; ++local_index) {
            const projectf::core::RenderInstance &source =
                    snapshot_.instances[source_batch.instance_offset + local_index];
            constexpr float turns_to_radians = 6.28318530717958647692F / 65536.0F;
            const float angle = static_cast<float>(source.yaw) * turns_to_radians;
            const float cosine = std::cos(angle);
            const float sine = std::sin(angle);
            const std::size_t offset = static_cast<std::size_t>(local_index) * 12U;
            write[offset + 0] = cosine;
            write[offset + 1] = 0.0F;
            write[offset + 2] = sine;
            write[offset + 3] = source.x;
            write[offset + 4] = 0.0F;
            write[offset + 5] = 1.0F;
            write[offset + 6] = 0.0F;
            write[offset + 7] = source.y + 0.41F;
            write[offset + 8] = -sine;
            write[offset + 9] = 0.0F;
            write[offset + 10] = cosine;
            write[offset + 11] = source.z;
        }
        batch.multi_mesh->set_buffer(transform_buffer);
        batch.multi_mesh->set_custom_aabb(batch.bounds);
        uploaded_bytes_ += static_cast<std::uint64_t>(source_batch.instance_count) * 12U * sizeof(float);

        batch.render_instance = rendering_server->instance_create();
        rendering_server->instance_set_base(batch.render_instance, batch.multi_mesh->get_rid());
        rendering_server->instance_set_scenario(batch.render_instance, world->get_scenario());
        rendering_server->instance_set_transform(batch.render_instance, get_global_transform());
        rendering_server->instance_set_visible(batch.render_instance, true);
        batches_.push_back(std::move(batch));
    }

    rendered_revision_ = snapshot_.revision;
    visible_instances_ = snapshot_.instances.size();
    visible_batches_ = static_cast<std::uint32_t>(batches_.size());
    last_rebuild_ms_ = static_cast<double>(Time::get_singleton()->get_ticks_usec() - start_usec) / 1000.0;
    update_visibility();
}

Dictionary FactoryRenderBridge::get_render_statistics() const {
    Dictionary result;
    result["render_revision"] = static_cast<std::int64_t>(rendered_revision_);
    result["total_instances"] = static_cast<std::int64_t>(snapshot_.instances.size());
    result["visible_instances"] = static_cast<std::int64_t>(visible_instances_);
    result["batch_count"] = static_cast<std::int64_t>(batches_.size());
    result["visible_batches"] = static_cast<std::int64_t>(visible_batches_);
    result["snapshot_reserved_bytes"] = static_cast<std::int64_t>(snapshot_.reserved_bytes());
    result["uploaded_bytes"] = static_cast<std::int64_t>(uploaded_bytes_);
    result["last_rebuild_ms"] = last_rebuild_ms_;
    result["last_visibility_ms"] = last_visibility_ms_;
    return result;
}

void FactoryRenderBridge::set_simulation_bridge_path(const NodePath &value) {
    simulation_bridge_path_ = value;
}

NodePath FactoryRenderBridge::get_simulation_bridge_path() const {
    return simulation_bridge_path_;
}

void FactoryRenderBridge::release_batches() {
    RenderingServer *rendering_server = RenderingServer::get_singleton();
    if (rendering_server != nullptr) {
        for (RuntimeBatch &batch : batches_) {
            if (batch.render_instance.is_valid()) {
                rendering_server->free_rid(batch.render_instance);
            }
        }
    }
    batches_.clear();
    visible_instances_ = 0;
    visible_batches_ = 0;
}

void FactoryRenderBridge::update_visibility() {
    Camera3D *camera = find_camera();
    RenderingServer *rendering_server = RenderingServer::get_singleton();
    if (camera == nullptr || rendering_server == nullptr) {
        return;
    }
    const std::uint64_t start_usec = Time::get_singleton()->get_ticks_usec();
    const TypedArray<Plane> planes = camera->get_frustum();
    visible_instances_ = 0;
    visible_batches_ = 0;
    for (RuntimeBatch &batch : batches_) {
        const bool visible = intersects_frustum(batch.bounds, planes);
        if (visible != batch.visible) {
            rendering_server->instance_set_visible(batch.render_instance, visible);
            batch.visible = visible;
        }
        if (visible) {
            visible_instances_ += batch.instance_count;
            ++visible_batches_;
        }
    }
    last_visibility_ms_ = static_cast<double>(Time::get_singleton()->get_ticks_usec() - start_usec) / 1000.0;
}

Camera3D *FactoryRenderBridge::find_camera() const {
    const Viewport *viewport = get_viewport();
    return viewport != nullptr ? viewport->get_camera_3d() : nullptr;
}

bool FactoryRenderBridge::intersects_frustum(const AABB &bounds, const TypedArray<Plane> &planes) {
    for (std::int64_t plane_index = 0; plane_index < planes.size(); ++plane_index) {
        const Plane plane = planes[plane_index];
        bool all_over = true;
        for (int endpoint = 0; endpoint < 8; ++endpoint) {
            if (!plane.is_point_over(bounds.get_endpoint(endpoint))) {
                all_over = false;
                break;
            }
        }
        if (all_over) {
            return false;
        }
    }
    return true;
}

} // namespace godot
