"""Read-only FBX/texture validation and offline mesh previews (not Unity captures)."""
from pathlib import Path
import argparse
import json
import re
import numpy as np
from PIL import Image
from portable_mesh import PortableMesh, ROOT, cross, unit, dot

def values(text, label, kind=float):
    match = re.search(r'\b' + label + r': \*\d+\s*\{\s*a:\s*([^}]+)', text)
    return [kind(v) for v in match[1].split(',') if v.strip()]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('texture', type=Path)
    parser.add_argument('--name', default='Pump_StylePreview')
    args = parser.parse_args()
    source = ROOT / 'FactorioProject/Assets/MapObject/Fluid/Pump/Pump.fbx'
    text = source.read_text(encoding='utf-8')
    positions = np.array(values(text, 'Vertices')).reshape(-1, 3)
    indices = values(text, 'PolygonVertexIndex', int)
    normals = np.array(values(text, 'Normals')).reshape(-1, 3)
    uv = np.array(values(text, 'UV')).reshape(-1, 2)
    uv_indices = values(text, 'UVIndex', int)
    mesh = PortableMesh(args.name, 'MapObject/Fluid/Pump', '')
    mesh.atlas = args.texture
    face = []
    for corner, index in enumerate(indices):
        face.append(len(mesh.vertices))
        mesh.vertices.append(tuple(positions[-index-1 if index < 0 else index]))
        mesh.normals.append(tuple(normals[corner]))
        mesh.uvs.append(tuple(uv[uv_indices[corner]]))
        if index < 0:
            for j in range(1, len(face)-1):
                mesh.triangles.append((face[0], face[j], face[j+1]))
            face = []
    assert not face
    mesh.origin = tuple((positions.min(axis=0)+positions.max(axis=0))/2)
    mesh.eye = (1.3, 0.9, 1.2)
    eye = unit(mesh.eye)
    right = unit(cross((0,1,0), eye))
    up = cross(eye, right)
    span = max(np.ptp(positions @ right), np.ptp(positions @ up))
    mesh.scale = 620 / span
    # Sample strictly inside each triangle, away from border padding.
    pixels = np.array(Image.open(args.texture).convert('RGB'))
    samples = []
    for tri in mesh.triangles:
        texcoords = np.array([mesh.uvs[i] for i in tri])
        for weights in ((1/3,1/3,1/3),(.6,.2,.2),(.2,.6,.2),(.2,.2,.6)):
            u,v = np.array(weights) @ texcoords
            x = min(pixels.shape[1]-1, max(0,int(u*pixels.shape[1])))
            y = min(pixels.shape[0]-1, max(0,int((1-v)*pixels.shape[0])))
            samples.append(pixels[y,x])
    samples = np.array(samples)
    print(json.dumps({'texture':str(args.texture), 'size':[pixels.shape[1],pixels.shape[0]],
        'vertices':len(positions), 'triangles':len(mesh.triangles),
        'near_black_interior_fraction':float(np.mean(samples.max(axis=1)<8)),
        'mean_surface_rgb':samples.mean(axis=0).tolist()}))
    mesh.preview()

if __name__ == '__main__':
    main()
