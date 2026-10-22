"""Remove the obsolete overlapping deck and repair UVs in the authored ASCII FBX.

Dry-run by default. --apply backs up the exact current asset before replacing it.
No Blender/Unity process or scene is opened or changed.
"""
from pathlib import Path
import argparse
import collections
import datetime
import hashlib
import json
import math
import os
import re
import tempfile


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'FactorioProject/Assets/MapObject/Train/Long Flatcar/Long Flatcar.fbx'
TEXTURE = SOURCE.with_name('Long Flatcar_TB.png')
SOURCE_SHA = '84809b5521a6cad3c50175d0674c995829b24f74b0718fa19cf4c91242b2c0cc'
ARRAY = r'(?m)^([\t ]*){label}: \*(\d+)\s*\{{\s*a:\s*([^}}]+)\}}'


def array_match(text, label):
    match = re.search(ARRAY.format(label=re.escape(label)), text)
    assert match, label
    return match


def read_array(text, label, kind=float):
    match = array_match(text, label)
    result = [kind(value) for value in match[3].split(',') if value.strip()]
    assert len(result) == int(match[2]), label
    return result


def groups(values, width):
    assert len(values) % width == 0
    return [tuple(values[i:i + width]) for i in range(0, len(values), width)]


def parse(text):
    start = text.index('\n\tGeometry: ')
    end = text.index('\n\tGeometry: ', start + 1)
    geometry = text[start:end]
    positions = groups(read_array(geometry, 'Vertices'), 3)
    indices = read_array(geometry, 'PolygonVertexIndex', int)
    assert all(indices[i] >= 0 if i % 3 != 2 else indices[i] < 0
               for i in range(len(indices)))
    faces = groups([(-i - 1) if i < 0 else i for i in indices], 3)
    uvs = groups(read_array(geometry, 'UV'), 2)
    uv_indices = read_array(geometry, 'UVIndex', int)
    assert len(uv_indices) == len(indices)
    return start, end, geometry, positions, faces, uvs, uv_indices


def edges(faces):
    return collections.Counter(tuple(sorted((face[i], face[(i + 1) % 3])))
                               for face in faces for i in range(3))


def uv_island(faces, uvs, uv_indices, seed):
    adjacent = collections.defaultdict(set)
    owner = {}
    for i, face in enumerate(faces):
        corners = [(face[j], uvs[uv_indices[i * 3 + j]]) for j in range(3)]
        for j in range(3):
            edge = tuple(sorted((corners[j], corners[(j + 1) % 3])))
            if edge in owner:
                adjacent[i].add(owner[edge])
                adjacent[owner[edge]].add(i)
            else:
                owner[edge] = i
    found, pending = {seed}, [seed]
    while pending:
        for i in adjacent[pending.pop()]:
            if i not in found:
                found.add(i)
                pending.append(i)
    return found


def replace_array(text, label, values, newline):
    match = array_match(text, label)
    indent = match[1]
    # Seventeen significant digits preserve untouched IEEE double values.
    rows = [','.join(format(v, '.17g') for v in values[i:i + 36])
            for i in range(0, len(values), 36)]
    value = (f'{indent}{label}: *{len(values)} {{{newline}{indent}\ta: '
             + (',' + newline + indent + '\t').join(rows)
             + newline + indent + '}')
    return text[:match.start()] + value + text[match.end():]


def flatten(values):
    return [component for value in values for component in value]


def panel_bounds(positions, faces, panel):
    points = [positions[i] for face in panel for i in faces[face]]
    assert max(p[1] for p in points) - min(p[1] for p in points) < 1e-8
    return (min(p[0] for p in points), max(p[0] for p in points),
            min(p[2] for p in points), max(p[2] for p in points), points[0][1])


def surface_height(positions, face, x, z):
    a, b, c = [positions[i] for i in face]
    determinant = ((b[2] - c[2]) * (a[0] - c[0])
                   + (c[0] - b[0]) * (a[2] - c[2]))
    if abs(determinant) < 1e-10:
        return None
    u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / determinant
    v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / determinant
    w = 1 - u - v
    return u * a[1] + v * b[1] + w * c[1] if min(u, v, w) >= -1e-8 else None


def repair(text):
    start, end, geometry, positions, faces, uvs, uv_indices = parse(text)
    panels = ((1857, 1858), (1976, 1979))
    remove = set()
    for seed, expected_faces, expected_vertices in ((71, 122, 70), (2192, 130, 75)):
        island = uv_island(faces, uvs, uv_indices, seed)
        assert len(island) == expected_faces
        vertices = {j for i in island for j in faces[i]}
        assert len(vertices) == expected_vertices
        assert all(32.5 < positions[j][1] < 34.1 for j in vertices)
        # Include seam-split slivers on the obsolete panels, not just the main UV islands.
        remove.update(i for i, face in enumerate(faces) if set(face) <= vertices)
    assert len(remove) == 258
    keep = [i for i in range(len(faces)) if i not in remove]
    before_edges, after_edges = edges(faces), edges([faces[i] for i in keep])
    assert not any(count == 1 and before_edges[edge] != 1
                   for edge, count in after_edges.items()), 'Repair opens a new mesh boundary'

    attributes = {}
    for label, width in (('Normals', 3), ('NormalsW', 1), ('Binormals', 3),
                         ('BinormalsW', 1), ('Tangents', 3), ('TangentsW', 1)):
        values = read_array(geometry, label)
        assert len(values) == len(faces) * 3 * width, label
        attributes[label] = (values, width)
    new_uvs, new_indices = list(uvs), list(uv_indices)
    for panel in panels:
        x0, x1, z0, z1, _ = panel_bounds(positions, faces, panel)
        cache = {}
        for face in panel:
            for corner, vertex in enumerate(faces[face]):
                x, _, z = positions[vertex]
                # Reuse the clean tread island, not the ghosted legacy panel under the frame.
                # Both equal-sized panels share the same rectangular texel density/orientation.
                uv = ((447 - (x - x0) / (x1 - x0) * 83) / 512,
                      1 - (424 + (z1 - z) / (z1 - z0) * 86) / 512)
                if vertex not in cache:
                    cache[vertex] = len(new_uvs)
                    new_uvs.append(uv)
                offset = face * 3 + corner
                new_indices[offset] = cache[vertex]
                for label, value in (('Normals', (0, 1, 0)),
                                     ('Tangents', (-1, 0, 0)), ('Binormals', (0, 0, 1))):
                    attributes[label][0][offset * 3:offset * 3 + 3] = value

    used_vertices = sorted({vertex for i in keep for vertex in faces[i]})
    remap = {old: new for new, old in enumerate(used_vertices)}
    used_uvs = sorted({new_indices[i * 3 + j] for i in keep for j in range(3)})
    uv_remap = {old: new for new, old in enumerate(used_uvs)}
    updated = {
        'Vertices': flatten([positions[i] for i in used_vertices]),
        'PolygonVertexIndex': [remap[vertex] if j != 2 else -remap[vertex] - 1
                               for i in keep for j, vertex in enumerate(faces[i])],
        'UV': flatten([new_uvs[i] for i in used_uvs]),
        'UVIndex': [uv_remap[new_indices[i * 3 + j]] for i in keep for j in range(3)],
    }
    for label, (values, width) in attributes.items():
        updated[label] = [value for i in keep
                          for value in values[i * 3 * width:(i + 1) * 3 * width]]
    newline = '\r\n' if '\r\n' in text else '\n'
    result = geometry
    for label, values in updated.items():
        result = replace_array(result, label, values, newline)
    result = text[:start] + result + text[end:]
    report = verify(text, result, keep, panels)
    return result, report


def verify(before, after, keep, panels):
    bs, be, bg, bp, bf, bu, bi = parse(before)
    ns, ne, ng, np, nf, nu, ni = parse(after)
    assert before[:bs] == after[:ns] and before[be:] == after[ne:]
    # All other FBX fields/objects/materials/connections are byte-for-byte unchanged.
    scrub = lambda g: re.sub(r'(?m)^([\t ]*)\w+: \*\d+\s*\{\s*a:\s*[^}]+\}', '', g)
    assert scrub(bg) == scrub(ng)
    assert len(nf) == len(bf) - 258
    assert all(math.isfinite(x) for p in np + nu for x in p)
    assert all(0 <= x <= 1 for uv in nu for x in uv)
    assert all(0 <= i < len(np) for face in nf for i in face)
    assert all(0 <= i < len(nu) for i in ni)
    flat = {i for panel in panels for i in panel}
    for new, old in enumerate(keep):
        assert [np[j] for j in nf[new]] == [bp[j] for j in bf[old]]
        if old not in flat:
            assert [nu[ni[new * 3 + j]] for j in range(3)] == [bu[bi[old * 3 + j]] for j in range(3)]
    for label, width in (('Normals', 3), ('NormalsW', 1), ('Tangents', 3),
                         ('TangentsW', 1), ('Binormals', 3), ('BinormalsW', 1)):
        old_data, new_data = read_array(bg, label), read_array(ng, label)
        assert len(new_data) == len(nf) * 3 * width
        for new, old in enumerate(keep):
            if old not in flat:
                assert new_data[new * 3 * width:(new + 1) * 3 * width] == old_data[old * 3 * width:(old + 1) * 3 * width]
    probes = 0
    for panel in panels:
        x0, x1, z0, z1, height = panel_bounds(bp, bf, panel)
        # Ray sampling checks both exposed surface coverage and hidden duplicate layers.
        for i in range(31):
            for j in range(31):
                x = x0 + (x1 - x0) * (i + .37) / 31
                z = z0 + (z1 - z0) * (j + .61) / 31
                hits = [y for face in nf if (y := surface_height(np, face, x, z)) is not None and y > 30]
                assert len(hits) == 1 and abs(hits[0] - height) < 1e-7, (x, z, hits)
                probes += 1
        for old in panel:
            new = keep.index(old)
            assert read_array(ng, 'Normals')[new * 9:(new + 1) * 9] == [0, 1, 0] * 3
    return dict(removed_triangles=len(bf) - len(nf), vertices_before=len(bp), vertices_after=len(np),
                triangles_before=len(bf), triangles_after=len(nf), deck_surface_probes=probes,
                new_open_edges=0, preserved_other_geometry=True, preserved_non_deck_uvs=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--verify-backup', type=Path, help='Verify the repaired asset against an original FBX backup')
    args = parser.parse_args()
    assert not (args.apply and args.verify_backup), 'Choose apply or verification, not both'
    original = (args.verify_backup or SOURCE).read_bytes()
    digest = hashlib.sha256(original).hexdigest()
    assert digest == SOURCE_SHA, 'Source changed; re-audit before applying this asset-specific repair'
    repaired, report = repair(original.decode('utf-8'))
    data = repaired.encode('utf-8')
    report.update(source_sha256=digest, repaired_sha256=hashlib.sha256(data).hexdigest(),
                  texture_sha256=hashlib.sha256(TEXTURE.read_bytes()).hexdigest())
    if args.verify_backup:
        assert SOURCE.read_bytes() == data, 'The on-disk FBX differs from the verified repair'
        assert TEXTURE.read_bytes() == args.verify_backup.with_name(TEXTURE.name).read_bytes()
        report['matches_disk'] = True
    if args.apply:
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        backup = Path.home() / '.codex/backups/ProjectF' / ('long-flatcar-deck-' + stamp)
        backup.mkdir(parents=True)
        (backup / SOURCE.name).write_bytes(original)
        (backup / TEXTURE.name).write_bytes(TEXTURE.read_bytes())
        (backup / 'audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        assert SOURCE.read_bytes() == original, 'Source changed during validation'
        descriptor, temporary = tempfile.mkstemp(prefix='deck-repair-', suffix='.tmp', dir=SOURCE.parent)
        try:
            with os.fdopen(descriptor, 'wb') as stream:
                stream.write(data)
            os.replace(temporary, SOURCE)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
        assert SOURCE.read_bytes() == data
        report['backup'] = str(backup)
    report['applied'] = args.apply
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
