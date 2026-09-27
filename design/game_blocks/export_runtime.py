"""Export the approved game_blocks meshes for DeepBore-style runtime loading.

Run with Blender 4.5 from the repository root:
  blender -b -t 4 --python design/game_blocks/export_runtime.py

The FBX/Unity project remains an interchange option; no Unity Editor is needed
for these compact, validated mesh files.
"""

import gzip
import json
import math
import shutil
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parent
DEST = ROOT.parents[1] / "package" / "Assets" / "NearbyCraftBlocks"
ICONS = ROOT.parents[1] / "package" / "UIAtlases" / "ItemIconAtlas"
MANIFEST = json.loads((ROOT / "exports" / "manifest.json").read_text())
bpy.ops.wm.open_mainfile(filepath=str(ROOT / "NearbyCraft_Blocks.blend"))
DEST.mkdir(parents=True, exist_ok=True)
ICONS.mkdir(parents=True, exist_ok=True)


def export_mesh(obj, expected_triangles):
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active
    assert uv_layer is not None and len(mesh.loop_triangles) == expected_triangles, obj.name
    vertices, normals, uvs, triangles = [], [], [], []
    dedupe = {}
    for triangle in mesh.loop_triangles:
        face = []
        for loop_index in triangle.loops:
            loop = mesh.loops[loop_index]
            point = mesh.vertices[loop.vertex_index].co
            normal = mesh.corner_normals[loop_index].vector
            uv = uv_layer.data[loop_index].uv
            # Blender and Unity have opposite handedness. Reflect X, retain
            # the existing +Z game front, then reverse triangle winding below.
            # A pure rotation mirrors the keypad and every printed label.
            key = tuple(round(float(v), 6) for v in
                        (-point.x, point.z, -point.y, -normal.x, normal.z, -normal.y, uv.x, uv.y))
            if key not in dedupe:
                dedupe[key] = len(vertices) // 3
                vertices.extend(key[:3])
                normals.extend(key[3:6])
                uvs.extend(key[6:])
            face.append(dedupe[key])
        triangles.extend((face[0], face[2], face[1]))
    assert len(triangles) == expected_triangles * 3
    assert all(math.isfinite(value) for value in vertices + normals + uvs)
    assert all(-.0001 <= value <= 1.0001 for value in uvs)
    return {"vertices": vertices, "normals": normals, "uv": uvs, "triangles": triangles}


for entry in MANIFEST["models"]:
    lods = [export_mesh(bpy.data.objects[f"{entry['name']}_LOD{level}"], entry["triangles"][level])
            for level in range(3)]
    payload = {"format": 1, "name": entry["name"], "family": entry["family"],
               "bounds_min": entry["bounds_min"], "bounds_max": entry["bounds_max"],
               "collider": entry["colliders"][0], "lods": lods}
    path = DEST / f"{entry['name']}.mesh.json.gz"
    with gzip.open(path, "wt", encoding="utf-8", compresslevel=6) as output:
        json.dump(payload, output, separators=(",", ":"), allow_nan=False)
    shutil.copy2(ROOT / "exports" / f"{entry['name']}_icon.png",
                 ICONS / f"{entry['name']}_icon.png")
    print(f"RUNTIME_MESH {entry['name']} {entry['triangles']} {path.stat().st_size} bytes", flush=True)

print("NEARBYCRAFT_RUNTIME_MESHES_READY: run prepare_runtime_textures.py for optimized texture atlases", flush=True)
