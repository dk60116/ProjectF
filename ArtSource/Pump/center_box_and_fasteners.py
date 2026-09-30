"""Center the actuator and regularize existing fasteners in live Blender.

Specific to the inspected 4146-vertex port-symmetry revision. Retains topology,
UVs and original body material; no generated port vertex is moved.
"""
import bpy
import bmesh
import math
from mathutils import Vector

obj = bpy.context.active_object
mesh = obj.data
assert len(mesh.vertices) == 4146 and obj.get('pump_ports_symmetric')
assert not obj.get('pump_body_centered'), 'Already centered'
bpy.ops.wm.save_as_mainfile(filepath='C:/Git/ProjectF/ArtSource/Pump/original/before_body_alignment_20260929/Pump_shape_cleanup.blend', copy=True)
world = obj.matrix_world.copy()
inverse = world.inverted()
old = [world @ v.co for v in mesh.vertices]
points = [p.copy() for p in old]
cy = .0009765625
dy = cy - .0234375
body = {i for f in mesh.polygons if f.material_index == 0 for i in f.vertices}
ports = {i for f in mesh.polygons if f.material_index > 0 for i in f.vertices}

# Full rigid translation above the support, smoothly blended into the housing.
for i in body - ports:
    p = points[i]
    if -.275 < p.x < .09 and -.15 < p.y < .185 and p.z > .34:
        wz = min(1., max(0., (p.z-.34)/.08))
        wx = min(1., max(0., (.09-p.x)/.08))
        p.y += dy * wz * wx

bolt_vertices = set()
ring_errors = []

def ring(ids, axis, depth, center, radius, phase=math.pi/6):
    """Match existing angular order to an exact regular ring, without welding."""
    axes = [a for a in range(3) if a != axis]
    a, b = axes
    n = len(ids)
    oc = [sum(points[i][k] for i in ids)/n for k in axes]
    ordered = sorted(ids, key=lambda i: math.atan2(points[i][b]-oc[1], points[i][a]-oc[0]))
    angles = [phase+2*math.pi*k/n for k in range(n)]
    def cost(offset):
        return sum((points[i][a]-oc[0]-radius*math.cos(angles[(j+offset)%n]))**2 +
                   (points[i][b]-oc[1]-radius*math.sin(angles[(j+offset)%n]))**2
                   for j,i in enumerate(ordered))
    offset = min(range(n), key=cost)
    for j,i in enumerate(ordered):
        t = angles[(j+offset)%n]
        points[i][axis] = depth
        points[i][a] = center[0]+radius*math.cos(t)
        points[i][b] = center[1]+radius*math.sin(t)
    bolt_vertices.update(ids)
    ring_errors.append(abs(sum(points[i][a] for i in ids)/n-center[0]))

# Four mounting feet: common height, mirrored pitch and identical hex heads.
feet = [
 ([2250,2251,2252,2253,2254,2256], list(range(2333,2339)), list(range(2598,2604)), -1,-1),
 ([1015,1016,1017,1018,1019,1021], list(range(1049,1055)), list(range(2616,2622)), -1,1),
 ([43,44,45,46,47,50], list(range(60,66)), list(range(2634,2640)), 1,1),
 (list(range(2005,2011)), list(range(2017,2023)), list(range(2604,2610)), 1,-1)]
for root, middle, tip, sx, sy in feet:
    center = (.0234375+sx*.20, cy+sy*.181640625)
    for ids,z,r in [(root,.056640625,.0293),(middle,.078125,.0293),(tip,.083984375,.0234375)]:
        ring(ids,2,z,center,r)

# Opposed central housing hex heads.
main = [
 (1,[[0,1,4,7,12,14],[2,3,6,8,13,15],[5,9,10,11,16,17]]),
 (-1,[[2201,2202,2205,2209,2212,2214],[2200,2203,2208,2210,2213,2215],[2204,2206,2207,2211,2216,2217]])]
for side,rings in main:
    for ids,d,r in zip(rings,[.24609375,.26171875,.267578125],[.0332,.0332,.0254]):
        ring(ids,1,cy+side*d,(.0205078125,.1943359375),r)

# Two matching actuator end fasteners, translated with the box.
for rings,depths in [
 ([[1900,1901,1902,1904,1906,1907],list(range(1962,1968)),list(range(2610,2616))],[-.230,-.250,-.256]),
 ([[1594,1595,1596,1598,1601,1603],list(range(1874,1880)),list(range(2586,2592))],[-.020,0.,.006])]:
    for ids,d,r in zip(rings,depths,[.032,.032,.0265]):
        ring(ids,0,d,(cy+.070,.470703125),r)

# Actuator mounting-lug pair, mirrored about the port centerline.
for side,rings in [
 (1,[[1431,1433,1468,1469,1470,1473],list(range(1929,1935)),list(range(2467,2473))]),
 (-1,[[1919,2344,2345,2347,2348,2353],list(range(2356,2362)),list(range(2414,2420))])]:
    for ids,d,r in zip(rings,[-.147,-.164,-.168],[.0254,.0254,.021]):
        ring(ids,0,d,(cy+side*.12,.41015625),r)

# Small head's top and end fasteners.
for ids,z,r in [
 ([1639,1640,1641,1643,1645,1647],.48046875,.0293),
 ([1636,1637,1638,1642,1644,1646],.48535156,.0332),
 (list(range(2473,2479)),.501953125,.0332),
 (list(range(2479,2485)),.5078125,.026)]:
    ring(ids,2,z,(.1748046875,cy),r)
for ids,x,r in [
 ([937,939,959,960,961,967,969,971],.2861328125,.0254),
 ([962,963,964,965,966,968,970,972],.3056640625,.0254),
 (list(range(2490,2498)),.3095703125,.021)]:
    ring(ids,0,x,(cy,.392578125),r,math.pi/8)
for ids,x,r in [
 (list(range(1756,1762)),.0595703125,.021),
 ([1753,1754,1762,1764,1766,1768],.0634765625,.0254),
 ([1751,1752,1755,1763,1765,1767],.0830078125,.0254),
 (list(range(2592,2598)),.0869140625,.021)]:
    ring(ids,0,x,(cy+.09375,.427734375),r)

# Irregular triangulated housing bolts retain angular ordering and attachments.
# Project onto matching octagonal profiles without merging unequal ring counts.
for sx,ox,oz in [(-1,-.185546875,.2470703125),(1,.2177734375,.2587890625)]:
    for sy in (-1,1):
        for i in body-ports:
            p=old[i]
            radial=math.hypot(p.x-ox,p.z-oz)
            if not (.160 < sy*(p.y-cy) < .194 and radial < .037):
                continue
            depth=sy*(p.y-cy)
            layer=0 if depth<.17 else 1 if depth<.184 else 2
            theta=math.atan2(p.z-oz,p.x-ox)
            radius=[.034,.031,.026][layer]
            sector=(theta-math.pi/8+math.pi/8)%(math.pi/4)-math.pi/8
            radius*=math.cos(math.pi/8)/math.cos(sector)
            points[i]=Vector((.0234375+sx*.20+radius*math.cos(theta),cy+sy*[.1640625,.17578125,.189453125][layer],.2529296875+radius*math.sin(theta)))
            bolt_vertices.add(i)

# FINAL_CONNECTION_REFINEMENT
# Align the exposed actuator rod's two circular sections to the common axis.
rod_a=[1808,1809,1810,1811,1812,1813,1816,1818,1821,1822,1824,1840]
rod_b=[1783,1784,1785,1786,1787,1788,1791,1864,1865,1866,1867,1868]
for ids,x in [(rod_a,.009),(rod_b,.0908203125)]:
    ring(ids,0,x,(cy,.4277342856),.03125,0.)

# Existing small head lug bolts have extra vertices along hexagon flats.
# Preserve those triangulation splits while standardizing their outline.
for side,ids in [
 (1,[834,835,836,842,844,1568,1570,1571,1572,1574,1651,1652,1653,1654,1655,2485,2486,2487,2488,2489]),
 (-1,[841,843,846,848,1656,1657,1737,1738,1739,1740,1741,1742,1743,1744,1745,1746,1747,1748,1749,1750])]:
    oy=.0732421875 if side>0 else -.0732421875
    for i in ids:
        p=old[i]
        theta=math.atan2(p.z-.3876953125,p.y-oy)
        r=.0195 if p.x<.082 else .0235
        sector=(theta+math.pi/6)%(math.pi/3)-math.pi/6
        r*=math.cos(math.pi/6)/math.cos(sector)
        points[i]=Vector((.0791015625 if p.x<.082 else .0830078125 if p.x<.09 else .1044921875,
                          cy+side*.0732421875+r*math.cos(theta),.3876953125+r*math.sin(theta)))
        bolt_vertices.add(i)

# Validate in a detached BMesh before committing any coordinate changes.
bm=bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
for v,p in zip(bm.verts,points):
    v.co=p
assert all(e.is_manifold for e in bm.edges), 'Nonmanifold input'
bad=[f.index for f in bm.faces if f.calc_area()<1e-12]
assert not bad, ('Degenerate faces; original mesh retained',bad)
assert all((points[i]-old[i]).length==0 for i in ports), 'Port geometry changed'
top=[1943,1941,1949,1952,1953,1950,1944,1946]
error=abs((min(points[i].y for i in top)+max(points[i].y for i in top))/2-cy)
assert error<1e-7
for f in bm.faces:
    if all(v.index in bolt_vertices for v in f.verts):
        f.material_index=2
        f.smooth=False
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.transform(inverse)
bm.to_mesh(mesh)
bm.free()
mesh.normals_split_custom_set([(0.,0.,0.)]*len(mesh.loops))
mesh.update()
obj['pump_body_centered']=True
obj['pump_box_centerline_error_m']=error
obj['pump_box_lateral_translation_m']=dy
print('CENTER CHECK',error,'m; box shift',dy,'m')
print('FASTENERS',len(bolt_vertices),'vertices normalized; ring center error',max(ring_errors))
print('TOPOLOGY',len(mesh.vertices),'vertices; manifold; no zero-area faces; ports unchanged')
