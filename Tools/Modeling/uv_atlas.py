"""Shared Blender UV overlap validation for model authoring."""
import math

def overlapping_faces(mesh, new_uv):
    triangles, owners = [], []
    mesh.calc_loop_triangles()
    for triangle in mesh.loop_triangles:
        triangles.append([tuple(new_uv.data[i].uv) for i in triangle.loops])
        owners.append(triangle.polygon_index)
    def intersects(a, b):
        for triangle in (a, b):
            for i in range(3):
                p, q = triangle[i], triangle[(i+1) % 3]
                axis = (p[1]-q[1], q[0]-p[0])
                length = math.hypot(*axis)
                if length < 1e-12:
                    continue
                aa = [(v[0]*axis[0]+v[1]*axis[1])/length for v in a]
                bb = [(v[0]*axis[0]+v[1]*axis[1])/length for v in b]
                if min(max(aa), max(bb))-max(min(aa), min(bb)) < 1e-7:
                    return False
        return True
    bins, pairs, bad = {}, set(), set()
    for i, triangle in enumerate(triangles):
        lo = [int(min(v[d] for v in triangle)*64) for d in range(2)]
        hi = [int(max(v[d] for v in triangle)*64) for d in range(2)]
        for x in range(lo[0], hi[0]+1):
            for y in range(lo[1], hi[1]+1):
                bucket = bins.setdefault((x, y), [])
                for j in bucket:
                    if (j, i) not in pairs:
                        pairs.add((j, i))
                        if intersects(triangles[j], triangle):
                            bad.update((owners[j], owners[i]))
                bucket.append(i)
    return bad

