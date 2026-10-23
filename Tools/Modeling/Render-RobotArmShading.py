"""Render a fixed-camera RobotArm material preview for shading comparison."""

import sys
from pathlib import Path

import bpy
from mathutils import Vector


output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
scene = bpy.context.scene
camera_data = bpy.data.cameras.new("Shading check")
camera = bpy.data.objects.new("Shading check", camera_data)
scene.collection.objects.link(camera)
target = Vector((0, 0.17, 0.59))
camera.location = Vector((2.3, -3.1, 2.5))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.type = "ORTHO"
camera_data.ortho_scale = 1.58
scene.camera = camera

key_data = bpy.data.lights.new("Shading check softbox", "AREA")
key = bpy.data.objects.new("Shading check softbox", key_data)
scene.collection.objects.link(key)
key.location = Vector((-2.0, -2.7, 4.5))
key.rotation_euler = (target - key.location).to_track_quat("-Z", "Y").to_euler()
key_data.energy = 250
key_data.shape = "DISK"
key_data.size = 4.0

world = bpy.data.worlds.new("Shading check ambient")
world.use_nodes = True
world.node_tree.nodes.get("Background").inputs["Color"].default_value = (0.4, 0.4, 0.4, 1)
world.node_tree.nodes.get("Background").inputs["Strength"].default_value = 0.8
scene.world = world

scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.render.resolution_x = 512
scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.render.film_transparent = False
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.render.filepath = str(output)
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.render.render(write_still=True)
print(f"SHADING_PREVIEW {output}")
