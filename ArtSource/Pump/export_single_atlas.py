"""Validate and export the textured Pump mesh without renaming its geometry."""

import bpy
import bmesh

obj = next(item for item in bpy.context.scene.objects if item.type == 'MESH')
mesh = obj.data
assert mesh.name == 'Scene', mesh.name
assert len(mesh.vertices) == 4242
assert len(mesh.polygons) == 4851
assert obj.get('pump_port_surface_repaired')
assert obj.get('pump_bores_capped')
assert len(mesh.materials) == 1
assert len(mesh.uv_layers) == 1
assert all(face.material_index == 0 for face in mesh.polygons)
assert all(0 < corner.uv.x < 1 and 0 < corner.uv.y < 1
           for corner in mesh.uv_layers.active.data)

check = bmesh.new()
check.from_mesh(mesh)
assert all(edge.is_manifold for edge in check.edges)
assert all(face.calc_area() > 1e-12 for face in check.faces)
check.free()

obj.select_set(True)
bpy.context.view_layer.objects.active = obj
result = bpy.ops.export_scene.fbx(
    filepath='C:/Git/ProjectF/ArtSource/Pump/Pump_shape_cleanup.fbx',
    use_selection=True,
    object_types={'MESH'},
    use_mesh_modifiers=False,
    use_triangles=False,
    add_leaf_bones=False,
    bake_anim=False,
    axis_forward='-Z',
    axis_up='Y',
    apply_scale_options='FBX_SCALE_UNITS',
    path_mode='ABSOLUTE',
)
assert result == {'FINISHED'}, result
print('SINGLE_ATLAS_EXPORT', len(mesh.vertices), len(mesh.polygons),
      len(mesh.uv_layers.active.data), mesh.materials[0].name)
