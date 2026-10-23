"""Rebuild the visible Robot arm prefab meshes in Blender at prefab transforms.

Run with Blender: blender --background --python Open-RobotArmAssembly.py -- output.blend
The prefab's nested PortableObject has its MeshFilter explicitly cleared, so it
does not contribute a visible mesh.
"""

import re
import sys
from pathlib import Path

import bpy
from bpy_extras.io_utils import axis_conversion
from mathutils import Matrix, Quaternion, Vector


ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / "FactorioProject/Assets/MapObject/InputOutputModule/Robot arm"
PREFAB = ASSET / "Robot arm.prefab"
OUTPUT = Path(sys.argv[sys.argv.index("--") + 1]) if "--" in sys.argv else None
EXPECTED_MESH_COUNTS = (7, 6)

# Unity's FBX mesh subasset IDs, as referenced by Robot arm.prefab.
# The name/texture pairing comes from the source FBX and prefab materials.
MESHES = {
    ("a4a3f084a69b09849aea75285cf8eca5", "-932648827855117281"): ("Robot arm_Body.fbx", "tripo_part_15", "Robot arm_TB_0.png"),
    ("a4a3f084a69b09849aea75285cf8eca5", "3581797078889487372"): ("Robot arm_Body.fbx", "tripo_part_17", "Robot arm_TB_1.png"),
    ("a4a3f084a69b09849aea75285cf8eca5", "4400702161093148050"): ("Robot arm_Body.fbx", "tripo_part_new_0", "Robot arm_TB_2.png"),
    ("fec67bb49e4d6ce45b06e979c1d60fb3", "6047364576717079604"): ("Robot arm_Hand.fbx", "tripo_part_1", "Robot arm Hand_TB_0.png"),
    ("fec67bb49e4d6ce45b06e979c1d60fb3", "-3488966222827643449"): ("Robot arm_Hand.fbx", "tripo_part_7", "Robot arm Hand_TB_1.png"),
    ("4834165d2c2f7f34c9a88b566d9d7eb4", "1105320367578091304"): ("Robot arm_Bottom.fbx", "Robot_Arm_Bottom", "Robot arm_Bottom_TB.png"),
}

AXIS = axis_conversion(from_forward="Z", from_up="Y", to_forward="-Y", to_up="Z").to_4x4()


def sections(source, kind):
    for match in re.finditer(r"^\t" + kind + r': (\d+), "([^"\n]+)", "([^"\n]+)" \{', source, re.M):
        end = match.end()
        depth = 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        yield int(match[1]), match[2], source[match.start():end]


def array(block, name, cast=float):
    match = re.search(r"\b" + name + r": \*(\d+)\s*\{\s*a:\s*([^}]+)\}", block)
    if match is None:
        raise ValueError(f"Missing {name} array")
    data = [cast(value.strip()) for value in match[2].split(",") if value.strip()]
    if len(data) != int(match[1]):
        raise ValueError(f"Wrong {name} array length")
    return data


def source_geometry(filename, model_name):
    source = (ASSET / filename).read_text(errors="replace")
    models = {
        match[2].removeprefix("Model::"): int(match[1])
        for match in re.finditer(r'^\tModel: (\d+), "([^"\n]+)", "Mesh" \{', source, re.M)
    }
    model_id = models[model_name]
    geometry_ids = {geometry_id for geometry_id, _, _ in sections(source, "Geometry")}
    links = {
        int(match[1]): int(match[2])
        for match in re.finditer(r'^\s*C: "OO",(\d+),(\d+)', source, re.M)
        if int(match[1]) in geometry_ids
    }
    matching = [block for geometry_id, _, block in sections(source, "Geometry") if links.get(geometry_id) == model_id]
    if len(matching) != 1:
        raise ValueError(f"Expected one geometry for {filename}:{model_name}, got {len(matching)}")
    return matching[0]


def build_mesh(filename, model_name, texture_name):
    block = source_geometry(filename, model_name)
    vertices = array(block, "Vertices")
    polygon_indices = array(block, "PolygonVertexIndex", int)
    uv = array(block, "UV")
    uv_indices = array(block, "UVIndex", int)
    normals = array(block, "Normals")
    faces = []
    face_uv = []
    face_normals = []
    face = []
    current_uv = []
    current_normals = []
    for loop_index, index in enumerate(polygon_indices):
        face.append(-index - 1 if index < 0 else index)
        current_uv.append(uv_indices[loop_index])
        current_normals.append(normals[3 * loop_index:3 * loop_index + 3])
        if index < 0:
            # Unity imports these FBX meshes with the X axis mirrored. Reverse
            # polygon winding too, so Blender keeps the same outward normals.
            faces.append(face[::-1])
            face_uv.extend(current_uv[::-1])
            face_normals.extend(current_normals[::-1])
            face = []
            current_uv = []
            current_normals = []
    if face or len(uv_indices) != len(polygon_indices) or len(normals) != 3 * len(polygon_indices):
        raise ValueError(f"Invalid polygon attributes in {filename}:{model_name}")

    basis = AXIS.to_3x3()
    coordinates = [basis @ Vector((-vertices[i], vertices[i + 1], vertices[i + 2])) * 0.01
                   for i in range(0, len(vertices), 3)]
    mesh = bpy.data.meshes.new(model_name)
    mesh.from_pydata(coordinates, [], faces)
    mesh.update()
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    uv_layer = mesh.uv_layers.new(name="UV")
    for loop, index in zip(uv_layer.data, face_uv):
        loop.uv = uv[2 * index:2 * index + 2]
    mesh.normals_split_custom_set([basis @ Vector((-normal[0], normal[1], normal[2]))
                                   for normal in face_normals])

    image = bpy.data.images.load(str(ASSET / texture_name), check_existing=True)
    image.pack()
    material = bpy.data.materials.new(texture_name.removesuffix(".png"))
    material.use_nodes = True
    nodes = material.node_tree.nodes
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    principal = nodes.get("Principled BSDF")
    material.node_tree.links.new(texture.outputs["Color"], principal.inputs["Base Color"])
    # The Unity source material is matte (_Smoothness = 0, _UseSpecular = 0).
    principal.inputs["Roughness"].default_value = 1.0
    principal.inputs["Specular IOR Level"].default_value = 0.0
    mesh.materials.append(material)
    return mesh


def prefab_blocks():
    source = PREFAB.read_text(encoding="utf-8")
    return {
        int(match[2]): (int(match[1]), match[4])
        for match in re.finditer(r"^--- !u!(\d+) &(\d+)([^\n]*)\n(.*?)(?=^--- !u!|\Z)", source, re.M | re.S)
    }


def property_value(block, name):
    match = re.search(r"^  " + name + r": (.*)$", block, re.M)
    return match[1] if match else None


def components(block):
    return [int(value) for value in re.findall(r"^  - component: \{fileID: (\d+)\}", block, re.M)]


def xyz(raw, default):
    if raw is None:
        return default
    return Vector(tuple(float(re.search(r"\b" + axis + r": ([^,}]+)", raw)[1]) for axis in "xyz"))


def local_matrix(transform):
    position = xyz(property_value(transform, "m_LocalPosition"), Vector((0, 0, 0)))
    scale = xyz(property_value(transform, "m_LocalScale"), Vector((1, 1, 1)))
    raw = property_value(transform, "m_LocalRotation")
    quaternion = Quaternion(tuple(float(re.search(r"\b" + axis + r": ([^,}]+)", raw)[1]) for axis in "wxyz"))
    unity = Matrix.Translation(position) @ quaternion.to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))
    return AXIS @ unity @ AXIS.inverted()


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    blocks = prefab_blocks()
    transforms = {}
    for game_object_id, (kind, block) in blocks.items():
        if kind != 1:
            continue
        name = property_value(block, "m_Name")
        component_ids = components(block)
        transform_ids = [fid for fid in component_ids if blocks[fid][0] == 4]
        if len(transform_ids) != 1:
            raise ValueError(f"Expected one transform for {name}")
        mesh_refs = []
        for fid in component_ids:
            component_kind, component = blocks[fid]
            if component_kind == 33:
                match = re.search(r"m_Mesh: \{fileID: (-?\d+), guid: (\w+)", component)
                if match:
                    mesh_refs.append((match[2], match[1]))
        if len(mesh_refs) > 1:
            raise ValueError(f"Multiple meshes on {name}")
        transforms[transform_ids[0]] = (name, blocks[transform_ids[0]][1], mesh_refs[0] if mesh_refs else None)

    loaded_meshes = {}
    objects = {}
    for transform_id, (name, block, mesh_ref) in transforms.items():
        if mesh_ref:
            if mesh_ref not in MESHES:
                raise ValueError(f"Unknown mesh reference {mesh_ref} on {name}")
            if mesh_ref not in loaded_meshes:
                loaded_meshes[mesh_ref] = build_mesh(*MESHES[mesh_ref])
            data = loaded_meshes[mesh_ref]
        else:
            data = None
        obj = bpy.data.objects.new(name, data)
        obj["UnityTransformID"] = str(transform_id)
        if mesh_ref:
            obj["UnityMeshFileID"] = mesh_ref[1]
        bpy.context.collection.objects.link(obj)
        objects[transform_id] = obj

    for transform_id, (_, block, _) in transforms.items():
        parent = int(re.search(r"m_Father: \{fileID: (-?\d+)\}", block)[1])
        obj = objects[transform_id]
        if parent:
            obj.parent = objects[parent]
        obj.matrix_basis = local_matrix(block)

    visible = [obj for obj in objects.values() if obj.type == "MESH"]
    if (len(visible), len(loaded_meshes)) != EXPECTED_MESH_COUNTS:
        raise ValueError(f"Unexpected mesh counts: {len(visible)} objects, {len(loaded_meshes)} unique meshes")
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in visible:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = visible[0]
    bpy.context.view_layer.update()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.shading.type = "MATERIAL"
                area.spaces.active.overlay.show_relationship_lines = False
                area.spaces.active.overlay.show_extras = False
                area.spaces.active.region_3d.view_distance = 3.2
                area.spaces.active.region_3d.view_location = Vector((0, 0, 0.4))
    if OUTPUT is None:
        raise ValueError("Missing output .blend path")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT))
    print(f"ASSEMBLY_OK {OUTPUT} objects={len(objects)} visible_meshes={len(visible)} unique_meshes={len(loaded_meshes)}")
    for obj in visible:
        print(f"  {obj.name} -> {obj.data.name} world={tuple(round(value, 4) for value in obj.matrix_world.translation)}")


if __name__ == "__main__":
    main()
