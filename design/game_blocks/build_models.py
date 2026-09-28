"""Build the BaseWorks salvage-industrial block family in Blender 4.5.

blender -b -t 8 --python design/game_blocks/build_models.py
Outputs editable .blend, six FBXs with three LODs, manifests and actual mesh renders.
Coordinates: meters, Z up, front -Y; all runtime origins at floor center.
"""
import bpy
import math
import json
import random
import sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parent
TILES=['paint','secondary','steel','rubber','ivory','rust','screen','keypad','title','service','gauge','legend','hazard','vents','tiers','lamps']
random.seed(79)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name!='Collection': bpy.data.collections.remove(c)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
scene.render.engine='CYCLES'
scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.render.image_settings.file_format='PNG'
scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.film_transparent=False
scene.world.color=(.16,.16,.16)
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.19,.23,.27,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45


def collection(name):
    c=bpy.data.collections.new(name); scene.collection.children.link(c); return c


def put(o,c):
    for old in list(o.users_collection): old.objects.unlink(o)
    c.objects.link(o)
    return o


def material(family):
    mat=bpy.data.materials.new('NC_'+family)
    mat.use_nodes=True
    n=mat.node_tree.nodes; l=mat.node_tree.links
    p=n.get('Principled BSDF')
    for suffix,socket in [('BaseColor','Base Color'),('Roughness','Roughness'),('Metallic','Metallic'),('Emission','Emission Color'),('Normal','Normal')]:
        t=n.new('ShaderNodeTexImage'); t.image=bpy.data.images.load(str(ROOT/'textures'/f'{family}_{suffix}.png'),check_existing=True)
        t.label=suffix
        if suffix not in ('BaseColor','Emission'): t.image.colorspace_settings.name='Non-Color'
        if suffix=='Normal':
            norm=n.new('ShaderNodeNormalMap'); norm.inputs['Strength'].default_value=.65
            l.new(t.outputs['Color'],norm.inputs['Color']); l.new(norm.outputs['Normal'],p.inputs['Normal'])
        else: l.new(t.outputs['Color'],p.inputs[socket])
    p.inputs['Emission Strength'].default_value=.40
    p.inputs['Specular IOR Level'].default_value=.28
    return mat


MATS={k:material(k) for k in ['console','workshop','locker']}
CURRENT=None
PARTS=[]
MAT=None


def uvrect(tile, sub=None):
    i=TILES.index(tile); x,y=(i%4)*512,(i//4)*512
    a,b,c,d=sub if sub else (0,0,1,1)
    # 6 px inset guards atlas bleed at normal texture filtering levels.
    return ((x+6+a*500)/2048,1-(y+6+d*500)/2048,(x+6+c*500)/2048,1-(y+6+b*500)/2048)


def map_box(o,tile,sub=None):
    mesh=o.data
    layer=mesh.uv_layers.active or mesh.uv_layers.new(name='UVMap')
    coords=[v.co for v in mesh.vertices]
    mins=[min(v[i] for v in coords) for i in range(3)]
    maxs=[max(v[i] for v in coords) for i in range(3)]
    u0,v0,u1,v1=uvrect(tile,sub)
    for p in mesh.polygons:
        axis=max(range(3),key=lambda a:abs(p.normal[a]))
        axes=(0,2) if axis==1 else (0,1) if axis==2 else (1,2)
        for li in p.loop_indices:
            co=mesh.vertices[mesh.loops[li].vertex_index].co
            u=(co[axes[0]]-mins[axes[0]])/max(1e-6,maxs[axes[0]]-mins[axes[0]])
            v=(co[axes[1]]-mins[axes[1]])/max(1e-6,maxs[axes[1]]-mins[axes[1]])
            layer.data[li].uv=(u0+(u1-u0)*u,v0+(v1-v0)*v)


def finish(o,name,tile,bevel=.004,detail=0,sub=None):
    global PARTS
    o.name=name
    put(o,CURRENT)
    o.data.materials.clear();o.data.materials.append(MAT)
    o['detail_level']=detail
    bpy.context.view_layer.objects.active=o
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Fabricated edge radius','BEVEL')
        # Stay below the thin dimension's half-width so opposing bevels cannot
        # collapse into coincident vertices and zero-area export triangles.
        mod.width=min(bevel,min(o.dimensions)*.45);mod.segments=2
        bpy.ops.object.modifier_apply(modifier=mod.name)
        for f in o.data.polygons: f.use_smooth=True
        mod=o.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL');mod.keep_sharp=True;mod.weight=40
        bpy.ops.object.modifier_apply(modifier=mod.name)
    map_box(o,tile,sub)
    PARTS.append(o)
    return o


def box(name,loc,size,tile='paint',bevel=.004,detail=0,rot=None,sub=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object;o.dimensions=size
    finish(o,name,tile,bevel,detail,sub)
    if rot:o.rotation_euler=rot
    return o


def cyl(name,loc,radius,depth,tile='steel',vertices=12,front=True,detail=1,sub=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
    o=bpy.context.object
    finish(o,name,tile,.0012,detail,sub)
    if front:o.rotation_euler.x=math.pi/2
    # Explicit circular front UV, required for printed dial faces.
    if tile=='gauge':
        u0,v0,u1,v1=uvrect(tile)
        for p in o.data.polygons:
            if abs(p.normal.z)>.9:
                for li in p.loop_indices:
                    co=o.data.vertices[o.data.loops[li].vertex_index].co
                    o.data.uv_layers.active.data[li].uv=(u0+(u1-u0)*(.5+co.x/(2*radius)),v0+(v1-v0)*(.5+co.y/(2*radius)))
    return o


def panel(name,loc,w,h,tile,sub=None,rot=0,detail=0):
    # Opaque surface, no alpha sorting or additional material slot.
    return box(name,loc,(w,.003,h),tile,.0005,detail,(rot,0,0),sub)


def nameplate(name,loc,w,h):
    # The square atlas tile is cropped to the physical plate's aspect ratio.
    # Equal texels per meter in both axes preserve the lettering proportions.
    half_strip=h/(2*w)
    return panel(name,loc,w,h,'title',sub=(0,.5-half_strip,1,.5+half_strip))


def bolt(loc):
    return cyl('Captive hex fastener',loc,.010,.009,'steel',6,True,2)


def bolts(x,y,z,w,h):
    for dx in (-w/2,w/2):
        for dz in (-h/2,h/2):bolt((x+dx,y,z+dz))


def tube(name,points,r=.009,tile='rubber',detail=1):
    # Low segment round cable, with a durable triangulated game mesh.
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=2
    data.bevel_depth=r;data.bevel_resolution=1;data.resolution_u=2
    sp=data.splines.new('BEZIER');sp.bezier_points.add(len(points)-1)
    for p,co in zip(sp.bezier_points,points):p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    o=bpy.data.objects.new(name,data);CURRENT.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH')
    return finish(bpy.context.object,name,tile,0,detail)


def profile(name,width,points,tile='paint'):
    n=len(points)
    verts=[(x,y,z) for x in (-width/2,width/2) for y,z in points]
    faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);CURRENT.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    # Normalize winding for the extrusion.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    return finish(o,name,tile,.008)


def start(name,family):
    global CURRENT,PARTS,MAT
    CURRENT=collection('SOURCE / '+name);PARTS=[];MAT=MATS[family]


def feet(width,depth):
    for x in (-width/2,width/2):
        box('Pressed steel foot',(x,0,.041),(.13,depth,.072),'steel',.009)
    box('Raised plinth',(0,0,.105),(width+.15,depth,.075),'secondary',.009)


def console(tier):
    name=f'NC_Storage_T{tier}';start(name,'console');feet(.68,.65)
    profile('Tapered equipment pedestal',.70,[(-.30,.14),(-.34,.51),(.30,.51),(.27,.14)],'secondary')
    box('Recessed service gasket',(0,-.326,.315),(.55,.030,.30),'rubber',.012)
    panel('Lower access panel',(0,-.347,.315),.52,.275,'paint')
    panel('Vent bank',(-.095,-.351,.324),.24,.155,'vents')
    panel('Electrical service notice',(.169,-.351,.325),.17,.17,'service')
    bolts(0,-.358,.315,.46,.225)
    profile('Folded sloping control enclosure',.90,[(-.405,.485),(-.425,.595),(-.203,.936),(.335,.936),(.355,.49)])
    box('Rear lid seam',(0,.307,.942),(.90,.055,.02),'steel',.003,1)
    # Front sloped instrument deck. All its controls share the same panel frame.
    a=-math.atan2(.222,.341)
    R=Matrix.Rotation(a,4,'X')
    origin=Vector((0,-.318,.765))
    def face(fn,*args,**kwargs):
        o=fn(*args,**kwargs)
        o.location=origin+R.to_3x3()@o.location
        o.rotation_euler=(R.to_3x3()@o.rotation_euler.to_matrix()).to_euler()
        return o
    face(box,'Instrument deck gasket',(0,-.006,0),(.84,.025,.381),'rubber',.007)
    face(box,'Cream enamel instrument fascia',(-.113,-.025,.018),(.582,.035,.338),'ivory',.015)
    face(box,'CRT recessed rubber surround',(-.13,-.050,.023),(.502,.028,.268),'rubber',.026)
    face(panel,'Phosphor glass / storage and production',(-.13,-.067,.023),.447,.219,'screen')
    face(panel,'Numeric input',(.309,-.034,-.084),.142,.161,'keypad')
    face(panel,'Tier and network capacity',(.30,-.033,.126),.169,.05,'tiers',sub=(0,(tier-1)/4,1,tier/4))
    for i in range(4):
        x=.24+i*.041
        face(cyl,'Tier indicator bezel',(x,-.032,.067),.015,.011,'steel',12)
        face(cyl,'Tier indicator glass',(x,-.041,.067),.009,.008,'lamps' if i<tier else 'rubber',12,sub=(0,0,.25,1) if i<tier else None)
    for x in (-.393,.17):
        for z in (-.133,.17):face(bolt,(x,-.048,z))
    nameplate('Storage identifier',(0,-.430,.552),.67,.067)
    for x in (-.38,.38):bolt((x,-.442,.551))
    # Vertical cooling fins on both sides, closed backing avoids see-through sides.
    for side in (-1,1):
        for i in range(7):
            box('Side cooling louvre',(side*.454,.008+i*.033,.728),(.017,.019,.164),'secondary',.003,1)
        box('Side tie plate',(side*.361,.063,.325),(.017,.29,.13),'steel',.003,1)
    tube('Rear power conduit',[(-.30,.358,.77),(-.33,.391,.57),(-.26,.375,.27),(-.22,.287,.21)],.013)
    if tier>=2:
        box('Bolted relay cassette',(0,.14,.964),(.40,.26,.061),'secondary',.006,1)
        for i in range(tier-1):
            box('Relay cartridge',(-.135+i*.132,.12,.967),(.112,.17,.06),'steel',.004,1)
    return name,PARTS[:]


def workshop():
    name='NC_Workshop';start(name,'workshop');feet(.59,.58)
    box('Pedestal rear cable chase',(0,.12,.34),(.37,.25,.40),'secondary',.009)
    box('Pedestal front service box',(0,-.045,.35),(.51,.40,.36),'paint',.012)
    panel('Service recess',(0,-.251,.353),.40,.255,'rubber')
    panel('Service plate',(0,-.255,.353),.373,.228,'secondary')
    panel('Warning stencil',(0,-.259,.357),.21,.18,'service')
    bolts(0,-.264,.353,.32,.18)
    box('Upper control cabinet',(0,0,.735),(.79,.59,.457),'paint',.018)
    box('Weather hood',(0,-.008,.978),(.84,.65,.037),'secondary',.006)
    box('Recessed panel seal',(0,-.304,.737),(.718,.027,.38),'rubber',.012)
    panel('Instrument mounting plate',(0,-.321,.738),.684,.351,'ivory')
    nameplate('Workshop identity',(0,-.329,.876),.61,.056)
    for i,x in enumerate((-.226,-.008)):
        cyl('Gauge bezel',(x,-.338,.756),.089,.028,'steel',24)
        cyl('Gauge glass and dial',(x,-.357,.756),.075,.015,'gauge',24)
    for i,z in enumerate((.814,.751,.688)):
        panel('Circuit label',(.203,-.329,z),.105,.028,'legend',sub=(0,i/8,1,(i+1)/8),detail=1)
        cyl('Circuit lamp rim',(.307,-.341,z),.022,.018,'steel',12)
        cyl('Circuit lamp',(.307,-.354,z),.015,.012,'lamps',12,sub=((i%2)/4,0,((i%2)+1)/4,1))
    for x in (-.246,-.12,.005):
        cyl('Switch mounting ring',(x,-.341,.617),.025,.018,'steel')
        box('Toggle lever',(x,-.365,.621),(.009,.039,.013),'steel',.002,1,rot=(.38,0,0))
    cyl('Emergency stop guard',(.225,-.349,.591),.057,.025,'hazard',20)
    cyl('Emergency stop stem',(.225,-.379,.591),.024,.04,'rubber',16)
    cyl('Emergency stop mushroom',(.225,-.402,.591),.039,.029,'lamps',20,sub=(.50,0,.75,1))
    for x in (-.327,.327):
        for z in (.572,.903):bolt((x,-.335,z))
    tube('Armored return conduit',[(.32,.263,.77),(.407,.275,.66),(.36,.281,.43),(.208,.227,.35)],.014,'steel')
    panel('Foot caution strip',(0,-.296,.111),.55,.038,'hazard')
    for side in (-1,1):
        for i in range(5):box('Cabinet side louvre',(side*.397,.04+i*.04,.737),(.014,.021,.185),'secondary',.002,1)
    return name,PARTS[:]


def locker():
    name='NC_LoadoutLocker';start(name,'locker');feet(.68,.62)
    box('Folded cabinet shell',(0,.02,1.019),(.883,.603,1.76),'secondary',.012)
    box('Dark recessed door reveal',(0,-.293,1.014),(.824,.031,1.679),'rubber',.006)
    box('Upper overhanging cap',(0,.017,1.924),(.931,.669,.047),'paint',.007)
    for x in (-.424,.424):box('Vertical corner post',(x,-.310,1.015),(.044,.045,1.704),'steel',.003)
    for i,x in enumerate((-.207,.207)):
        tile='secondary' if i==0 else 'paint'
        box('Left replacement door' if i==0 else 'Right equipment door',(x,-.319,1.021),(.389,.039,1.630),tile,.006)
        panel('Stamped equipment number',(x,-.342,1.704),.31,.066,'legend',sub=(0,(4+i)/8,1,(5+i)/8))
        for z in (1.505,.467):
            panel('Recessed ventilation backing',(x,-.344,z),.281,.227,'rubber')
            for j in range(5):
                box('Folded louvre blade',(x,-.359,z-.087+j*.041),(.276,.026,.024),tile,.002,1,rot=(-.23,0,0))
        box('Door pressed reinforcing rib',(x,-.344,1.01),(.303,.017,.034),tile,.003,1)
        panel('Door lower kick plate',(x,-.344,.266),.33,.094,'steel')
        for z in (.345,1.031,1.731):
            bx=x+(-.176 if i==0 else .176)
            box('Hinge mounting plate',(bx,-.348,z),(.032,.016,.082),'steel',.002,1)
            cyl('Hinge barrel',(bx,-.365,z),.014,.084,'steel',12,False)
        hx=x+(.106 if i==0 else -.106)
        box('Handle backing',(hx,-.352,1.15),(.052,.016,.225),'steel',.005,1)
        tube('Bent steel pull handle',[(hx,-.364,1.225),(hx,-.394,1.209),(hx,-.394,1.099),(hx,-.364,1.081)],.010,'steel')
        for z in (1.244,1.060):bolt((hx,-.365,z))
    nameplate('Cabinet title',(0,-.324,1.863),.75,.066)
    # A field repair with distinct fastening and an uneven seam.
    box('Riveted lower repair patch',(-.230,-.353,.778),(.252,.012,.201),'rust',.003,1,rot=(0,-.055,0))
    bolts(-.230,-.367,.778,.203,.151)
    box('Receiver box',(.245,-.371,1.281),(.207,.071,.137),'steel',.009)
    panel('Receiver ID display',(.229,-.409,1.296),.133,.055,'tiers',sub=(0,0,1,.25))
    cyl('Receiver pilot',(.305,-.414,1.255),.009,.008,'lamps',12,sub=(0,0,.25,1))
    box('Door retaining hasp',(0,-.365,.953),(.173,.025,.039),'steel',.003,1)
    cyl('Hasp pivot',(-.07,-.384,.952),.014,.012,'steel',10)
    tube('Padlock shackle',[(-.019,-.396,.949),(-.018,-.396,.981),(.020,-.396,.981),(.021,-.396,.947)],.006,'steel')
    box('Padlock body',(0,-.397,.930),(.058,.031,.049),'rust',.007,1)
    # Rear reinforcement makes the prop usable free-standing.
    for z in (.31,1.17,1.72):box('Rear stiffening rib',(0,.327,z),(.82,.018,.039),'steel',.003,1)
    return name,PARTS[:]


def combined(parts,name,level):
    copies=[]
    for src in parts:
        if src['detail_level']> (2 if level==0 else 1 if level==1 else 0):continue
        o=src.copy();o.data=src.data.copy();EXPORT.objects.link(o);copies.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in copies:o.select_set(True)
    bpy.context.view_layer.objects.active=copies[0]
    bpy.ops.object.join();o=bpy.context.object;o.name=f'{name}_LOD{level}'
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    if level:
        dec=o.modifiers.new('Reduced distant silhouette','DECIMATE');dec.ratio=.68 if level==1 else .34
        bpy.ops.object.modifier_apply(modifier=dec.name)
    tri=o.modifiers.new('Export triangles','TRIANGULATE');tri.keep_custom_normals=True
    bpy.ops.object.modifier_apply(modifier=tri.name)
    # Remove redundant material slots created during the join.
    mat=o.data.materials[0];o.data.materials.clear();o.data.materials.append(mat)
    for p in o.data.polygons:p.material_index=0
    return o


def collider_specs(mins,maxs):
    # One root collider: 7DTD derives selection bounds from its first collider.
    # Local Blender coordinates; Unity builder converts these to (x,z,-y).
    return [dict(center=[(a+b)/2 for a,b in zip(mins,maxs)],
                 size=[b-a for a,b in zip(mins,maxs)])]


EXPORT=collection('EXPORTS / hidden game meshes')
ASSEMBLIES=[]
manifest={'units':'meters','blender_front':'-Y','unity_front':'+Z','origin':'floor center',
          'texture_size':2048,'unity_version':'2022.3.62f2','models':[]}
for build in [lambda:console(1),lambda:console(2),lambda:console(3),lambda:console(4),workshop,locker]:
    name,parts=build()
    family='console' if 'Storage' in name else 'workshop' if name=='NC_Workshop' else 'locker'
    lods=[combined(parts,name,i) for i in range(3)]
    mins=[min(v.co[i] for v in lods[0].data.vertices) for i in range(3)]
    maxs=[max(v.co[i] for v in lods[0].data.vertices) for i in range(3)]
    assert maxs[0]-mins[0] < 1 and maxs[1]-mins[1] < 1
    assert mins[2]>=-0.001 and maxs[2]< (2 if 'Locker' in name else 1.01),(name,mins,maxs)
    entry={'name':name,'family':family,'triangles':[len(o.data.polygons) for o in lods],
           'bounds_min':mins,'bounds_max':maxs,'material_slots':1,'colliders':collider_specs(mins,maxs)}
    manifest['models'].append(entry)
    bpy.ops.object.select_all(action='DESELECT')
    for o in lods:o.select_set(True)
    bpy.context.view_layer.objects.active=lods[0]
    bpy.ops.export_scene.fbx(filepath=str(ROOT/'exports'/f'{name}.fbx'),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_space_transform=True,
        bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,
        add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    for o in lods:o.hide_render=True;o.hide_set(True)
    for o in parts:o.hide_render=True;o.hide_set(True)
    ASSEMBLIES.append((name,parts,lods))
    print('MODEL',entry,flush=True)

(ROOT/'exports'/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

# Presentation contains only actual LOD0 meshes, with clean neutral studio lighting.
STAGE=collection('PRESENTATION / excluded from FBX')
displays=[]
for name,parts,lods in ASSEMBLIES:
    if name not in ('NC_Storage_T1','NC_Workshop','NC_LoadoutLocker'):continue
    o=lods[0].copy();o.data=lods[0].data;STAGE.objects.link(o)
    o.name='DISPLAY / '+name;o.hide_render=False;o.hide_set(False)
    o.location.x={'NC_Storage_T1':-1.20,'NC_Workshop':0,'NC_LoadoutLocker':1.20}[name]
    displays.append(o)


def plain_mat(name,color,rough=.8,metal=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes['Principled BSDF'];p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    return m


ground=plain_mat('Studio concrete',(0.069,0.075,0.069))
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.007));floor=put(bpy.context.object,STAGE);floor.name='STUDIO / ground';floor.data.materials.append(ground)


def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()


def light(name,loc,power,size,color):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
    o=bpy.data.objects.new(name,d);STAGE.objects.link(o);o.location=loc;aim(o,(0,0,.70))


light('Large warm workshop window',(-3,-4.5,6),850,5,(1,.86,.70))
light('Cool wall bounce',(4,-2.0,3.5),610,4,(.76,.86,1))
light('Top rim',(1,3,4.5),1000,3,(1,.94,.82))
data=bpy.data.cameras.new('Product camera');cam=bpy.data.objects.new('Product camera',data);STAGE.objects.link(cam);scene.camera=cam
cam.data.type='ORTHO';cam.data.ortho_scale=4.92
cam.location=(3.5,-7,3.10);aim(cam,(0,0,.84))
scene.render.resolution_x=2000;scene.render.resolution_y=1400
scene.render.filepath=str(ROOT/'previews'/'family.png')

# Pack textures for portable Blender source. External atlas files also remain for Unity.
for img in bpy.data.images:
    if img.source=='FILE':img.pack()
scene['asset_notes']='Actual game LOD0 presentation. SOURCE collections retain named editable parts. EXPORTS contains all three mesh LODs; presentation never exported.'
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'BaseWorks_Blocks.blend'))
bpy.ops.render.render(write_still=True)

# Individual views and transparent inventory icon renders.
for name,parts,lods in ASSEMBLIES:
    for o in displays:o.hide_render=True
    o=lods[0];o.hide_render=False
    locker_view='Locker' in name
    height=1.94 if locker_view else 1.0
    cam.location=(2.8,-5,2.75 if locker_view else 2.25);aim(cam,(0,0,height*.49))
    cam.data.ortho_scale=2.4 if locker_view else 1.53
    scene.render.resolution_x=1100;scene.render.resolution_y=1300 if locker_view else 1100
    scene.render.filepath=str(ROOT/'previews'/f'{name}.png')
    bpy.ops.render.render(write_still=True)
    floor.hide_render=True;scene.render.film_transparent=True
    scene.render.resolution_x=256;scene.render.resolution_y=256
    cam.data.ortho_scale=2.30 if locker_view else 1.50
    scene.render.filepath=str(ROOT/'exports'/f'{name}_icon.png')
    bpy.ops.render.render(write_still=True)
    floor.hide_render=False;scene.render.film_transparent=False;o.hide_render=True

print('COMPLETE: source, six game meshes, LODs, colliders, textures, renders and icons.',flush=True)
