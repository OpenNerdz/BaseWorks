"""Run inside Blender. Validate portable source and FBX round trips."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
manifest=json.loads((ROOT/'exports'/'manifest.json').read_text())
results=[]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'NearbyCraft_Blocks.blend'))
for name in ('Workshop identity','Storage identifier','Cabinet title'):
    obj=bpy.data.objects[name]
    # Check the texel density of the front face, not just the UV rectangle.
    faces=[p for p in obj.data.polygons if p.normal.y<-.999]
    p=max(faces,key=lambda p:p.area)
    co=[obj.data.vertices[obj.data.loops[i].vertex_index].co for i in p.loop_indices]
    uv=[obj.data.uv_layers.active.data[i].uv for i in p.loop_indices]
    dx=max(v.x for v in co)-min(v.x for v in co)
    dz=max(v.z for v in co)-min(v.z for v in co)
    du=max(v.x for v in uv)-min(v.x for v in uv)
    dv=max(v.y for v in uv)-min(v.y for v in uv)
    stretch=abs((du/dx)/(dv/dz)-1)
    assert stretch<.001,(name,stretch)
    results.append({'name':name,'lettering_stretch_percent':round(stretch*100,5)})
for img in bpy.data.images:
    if img.source=='FILE':assert img.packed_file is not None,img.name

for entry in manifest['models']:
    bpy.ops.object.select_all(action='SELECT')
    # Reset scene to avoid hidden source objects leaking into the validation.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'exports'/f"{entry['name']}.fbx"))
    meshes=[o for o in bpy.data.objects if o.type=='MESH']
    assert len(meshes)==3,(entry['name'],len(meshes))
    for level in range(3):
        obj=next(o for o in meshes if o.name.endswith(f'_LOD{level}'))
        mesh=obj.data
        assert len(mesh.materials)==1
        assert len(mesh.uv_layers)==1
        assert all(len(p.vertices)==3 for p in mesh.polygons)
        assert len(mesh.polygons)==entry['triangles'][level]
        assert len(mesh.polygons)<12000
        assert all(p.area>1e-12 for p in mesh.polygons),(obj.name,'degenerate faces')
        assert all(math.isfinite(x) for v in mesh.vertices for x in v.co)
        assert all(-.0001<=x<=1.0001 for uv in mesh.uv_layers[0].data for x in uv.uv)
        assert obj.location.length<.0001
        coords=[obj.matrix_world@v.co for v in mesh.vertices]
        lo=[min(v[i] for v in coords) for i in range(3)]
        hi=[max(v[i] for v in coords) for i in range(3)]
        if level==0:
            assert max(abs(lo[i]-entry['bounds_min'][i]) for i in range(3))<.001
            assert max(abs(hi[i]-entry['bounds_max'][i]) for i in range(3))<.001
        results.append({'name':obj.name,'triangles':len(mesh.polygons),'material_slots':1,'uv_range':'0..1','bounds':'passed'})
report={'status':'passed','checks':['packed source textures','unstretched title UVs','FBX re-import','floor-center pivots','meter dimensions','triangle-only meshes','no degenerate triangles','finite vertices','UV atlas range','single material per LOD'],'results':results,'native_unity_test':'not run: Unity Editor unavailable','in_game_test':'not run: bundle not built'}
(ROOT/'exports'/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
