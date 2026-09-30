"""Rebuild identical Pump port assemblies in the interactive Blender session.

The original body's name, transform, material and UVs are retained. New flange
assemblies are closed shells overlapping the capped original necks, not a
watertight simulation of internal fluid passages. Eight bolts per outer face.
"""
import bpy
import bmesh
import math
from mathutils import Vector

obj = bpy.context.active_object
assert obj and obj.type == 'MESH'
assert not obj.get('pump_ports_symmetric'), 'Already rebuilt'
bpy.ops.wm.save_as_mainfile(
    filepath='C:/Git/ProjectF/ArtSource/Pump/original/before_port_symmetry_20260929/Pump_shape_cleanup.blend',
    copy=True)
mesh = obj.data
bm = bmesh.new()
bm.from_mesh(mesh)
bm.transform(obj.matrix_world)
original_materials = list(mesh.materials)
base_index = len(original_materials)

# Both cuts lie beyond the pump body, upper actuator and mounting feet.
for side in (-1, 1):
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
        dist=1e-7, plane_co=(side*.32,0,0), plane_no=(side,0,0), clear_outer=True)
    cut = [e for e in bm.edges if e.is_boundary and all(abs(v.co.x-side*.32)<1e-6 for v in e.verts)]
    filled = bmesh.ops.holes_fill(bm, edges=cut, sides=0)['faces']
    for f in filled:
        f.material_index = base_index+2
assert all(e.is_manifold for e in bm.edges), 'Body cut failed; original mesh retained'

cy, cz = .0009765625, .1962890625
port_vertices = {-1:[], 1:[]}
bolt_centers = {-1:[], 1:[]}

def lathe(profile, side, segments, center_y=cy, center_z=cz,
          materials=None, phase=0., caps=False):
    rings = []
    for x,r in profile:
        ring = []
        for k in range(segments):
            angle = phase + 2*math.pi*k/segments
            v = bm.verts.new((side*x,center_y+r*math.cos(angle),center_z+r*math.sin(angle)))
            ring.append(v)
            port_vertices[side].append(v)
        rings.append(ring)
    count = len(rings)-1 if caps else len(rings)
    for j in range(count):
        next_ring = rings[(j+1)%len(rings)]
        for k in range(segments):
            q = (k+1)%segments
            f = bm.faces.new((rings[j][k],rings[j][q],next_ring[q],next_ring[k]))
            f.material_index = base_index+(materials[j] if materials else 0)
            f.smooth = True
    if caps:
        for ring in (rings[0], rings[-1]):
            f = bm.faces.new(ring)
            f.material_index = base_index+1

# Common stepped flange, brass gasket, beveled circular mouth, dark inner bore.
profile = [(.310,.176),(.332,.171),(.341,.171),(.348,.177),
           (.355,.185),(.384,.185),(.388,.183),(.398,.183),
           (.402,.185),(.416,.185),(.424,.177),(.424,.130),
           (.434,.130),(.440,.138),(.491,.138),(.499,.130),
           (.499,.100),(.491,.094),(.310,.094)]
bands = [0,0,0,0,0,1,1,1,0,0,0,0,0,0,0,0,0,2,2]
for side in (-1,1):
    lathe(profile,side,24,materials=bands,phase=math.pi/24)
    for index in range(8):
        angle = math.pi/8 + index*math.pi/4
        y,z = cy+.157*math.cos(angle),cz+.157*math.sin(angle)
        bolt_centers[side].append((side*.447,y,z))
        # Small steel washer, then identical beveled brass hexagonal head.
        lathe([(.424,.022),(.428,.022),(.430,.020),(.430,.010),(.424,.010)],
              side,12,y,z,materials=[0]*5,phase=angle)
        lathe([(.428,.015),(.431,.019),(.450,.019),(.454,.015)],
              side,6,y,z,materials=[1]*3,phase=angle,caps=True)

bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
for edge in bm.edges:
    if edge.is_manifold and all(f.material_index >= base_index for f in edge.link_faces):
        edge.smooth = edge.calc_face_angle() < math.radians(30)
assert all(e.is_manifold for e in bm.edges), 'Nonmanifold assembly'
assert all(f.calc_area()>1e-12 for f in bm.faces), 'Degenerate face'
symmetry = max((Vector((-a.co.x,a.co.y,a.co.z))-b.co).length
               for a,b in zip(port_vertices[-1],port_vertices[1]))
assert symmetry < 1e-7
print('PORT CHECK',len(port_vertices[-1]),'vertices per side; mirror error',symmetry)
print('BOLTS: 8 per port, 45-degree pitch, radius 0.157 m, mirrored axial depth')

def material(name,color,metallic,roughness):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metallic
    p.inputs['Roughness'].default_value=roughness
    m.diffuse_color=(*color,1)
    return m

new_materials = [
    material('Pump_Port_Steel',(.23,.27,.32),.35,.48),
    material('Pump_Port_Brass',(.52,.32,.085),.45,.42),
    material('Pump_Port_Bore',(.035,.044,.055),.15,.75)]
bm.transform(obj.matrix_world.inverted())
bm.to_mesh(mesh)
bm.free()
for m in new_materials:
    mesh.materials.append(m)
mesh.normals_split_custom_set([(0.,0.,0.)]*len(mesh.loops))
mesh.update()
mesh.calc_loop_triangles()
obj['pump_ports_symmetric']=True
obj['pump_port_bolts']=8
obj['pump_port_mirror_error_m']=symmetry
obj.select_set(True)
print('RESULT',len(mesh.vertices),'vertices',len(mesh.loop_triangles),'triangles')
