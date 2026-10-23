"""Render the Unity-matched Long Robot arm assembly for visual verification."""

import sys
from pathlib import Path

import bpy
from mathutils import Vector


output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
scene = bpy.context.scene
points = [obj.matrix_world @ Vector(corner)
          for obj in scene.objects if obj.type == "MESH"
          for corner in obj.bound_box]
if not points:
    raise ValueError("No mesh objects in the assembly")
minimum = Vector(min(point[i] for point in points) for i in range(3))
maximum = Vector(max(point[i] for point in points) for i in range(3))
center = (minimum + maximum) / 2
extent = max(maximum - minimum)

camera_data = bpy.data.cameras.new("Validation camera")
camera = bpy.data.objects.new("Validation camera", camera_data)
scene.collection.objects.link(camera)
camera.location = center + Vector((2.0, -3.0, 2.0)).normalized() * extent * 2.5
camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.type = "ORTHO"
camera_data.ortho_scale = extent * 1.5
scene.camera = camera

light_data = bpy.data.lights.new("Validation softbox", "AREA")
light = bpy.data.objects.new("Validation softbox", light_data)
scene.collection.objects.link(light)
light.location = center + Vector((-2, -2, 3)).normalized() * extent * 2
light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
light_data.energy = 250
light_data.shape = "DISK"
light_data.size = extent * 2

world = bpy.data.worlds.new("Validation ambient")
world.use_nodes = True
world.node_tree.nodes.get("Background").inputs["Color"].default_value = (0.4, 0.4, 0.4, 1)
world.node_tree.nodes.get("Background").inputs["Strength"].default_value = 0.8
scene.world = world

scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.render.resolution_x = 640
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.render.filepath = str(output)
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.render.render(write_still=True)
print(f"LONG_ARM_PREVIEW {output}")
