"""Blender-only concept for NearbyCraft's existing custom blocks.

Run with Blender 4.5: blender -b -t 4 --python design/nearbycraft_concept.py
This deliberately does not export or install an in-game asset bundle.
"""

import math
import random
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parent
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    if collection.name != "Collection" and collection.users == 0:
        bpy.data.collections.remove(collection)


def rgb(hex_value):
    channels = [int(hex_value[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(value / 12.92 if value < .04045 else ((value + .055) / 1.055) ** 2.4 for value in channels) + (1,)


def material(name, color, metallic=0, roughness=.55, glow=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = rgb(color)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get("Principled BSDF")
    node.inputs["Base Color"].default_value = rgb(color)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    if glow:
        node.inputs["Emission Color"].default_value = rgb(color)
        node.inputs["Emission Strength"].default_value = glow
    return mat


def battered_paint(name, paint, exposed, metallic=.35, roughness=.75):
    """Procedural chipped paint; generated coordinates keep the source self-contained."""
    mat = material(name, paint, metallic, roughness)
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 24
    noise.inputs["Detail"].default_value = 4
    noise.inputs["Roughness"].default_value = .78
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = .48
    ramp.color_ramp.elements[1].position = .66
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MIX"
    mix.inputs[1].default_value = rgb(paint)
    mix.inputs[2].default_value = rgb(exposed)
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], mix.inputs[0])
    links.new(mix.outputs["Color"], nodes.get("Principled BSDF").inputs["Base Color"])
    return mat


steel = battered_paint("01 / old olive machine paint over rusty steel", "#55584B", "#68412E")
steel_dark = battered_paint("02 / smoke-darkened iron", "#353934", "#6F4430", .58)
edge = battered_paint("03 / scuffed bare galvanised steel", "#77766A", "#4F4339", .72, .68)
rubber = material("04 / dirty black rubber", "#1B1E1B", .04, .89)
screen = material("05 / salvaged green monochrome screen", "#263B2C", .10, .49, .08)
screen_dim = material("06 / weak phosphor display line", "#A4AD79", .08, .58, .12)
white = material("07 / faded cream stencil paint", "#BDB7A0", .02, .84)
teal = battered_paint("08 / tier one faded sea-green paint", "#527D74", "#524435", .10, .78)
violet = battered_paint("09 / tier four faded blue-grey paint", "#627489", "#514538", .10, .77)
amber = battered_paint("10 / workshop ochre paint", "#B18A46", "#634A32", .10, .78)
locker_blue = battered_paint("11 / locker faded blue-grey paint", "#607781", "#664738", .14, .79)
off = material("12 / dead indicator glass", "#444D47", .16, .66)
rust = battered_paint("13 / oxidised iron", "#75482E", "#3B302A", .28, .89)
wood = battered_paint("14 / rough salvaged plywood", "#6F634F", "#453B30", .01, .92)
brass = material("15 / tarnished brass contacts", "#84775A", .53, .69)
lamp_green = material("16 / weak green pilot light", "#99B569", .09, .38, .4)
lamp_amber = material("17 / weak amber pilot light", "#D49A53", .08, .38, .45)
plinth_mat = material("Preview only / dark plinth", "#24292D", .30, .64)
floor_mat = material("Preview only / floor", "#1E2429", .10, .79)


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def move_to(obj, target):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    target.objects.link(obj)
    return obj


def box(target, name, at, size, mat, bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj = move_to(bpy.context.object, target)
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Machined edge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 3
        obj.modifiers.new("Weighted corner normals", "WEIGHTED_NORMAL")
    return obj


def cylinder(target, name, at, radius, depth, mat, vertices=24, front=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=at)
    obj = move_to(bpy.context.object, target)
    obj.name = name
    if front:
        obj.rotation_euler[0] = math.pi / 2
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("Soft rim", "BEVEL")
    bevel.width = min(.008, depth / 5)
    bevel.segments = 2
    obj.modifiers.new("Weighted rim normals", "WEIGHTED_NORMAL")
    return obj


def front_text(target, name, caption, at, size, mat, align="CENTER"):
    data = bpy.data.curves.new(name, "FONT")
    data.body = caption
    data.size = size
    data.align_x = align
    data.extrude = .0008
    obj = bpy.data.objects.new(name, data)
    target.objects.link(obj)
    obj.location = at
    obj.rotation_euler[0] = math.pi / 2
    obj.data.materials.append(mat)
    return obj


def bolt(target, name, at, radius=.011):
    cylinder(target, name, at, radius, .008, brass, vertices=8, front=True)
    slot = box(target, f"{name} / screwdriver slot", (at[0], at[1] - .007, at[2]),
               (radius * 1.15, .002, .0025), rubber, .0005)
    slot.rotation_euler[1] = -.25


def scratches(target, name, x, y, z, width, height, count, seed):
    rng = random.Random(seed)
    for index in range(count):
        px = x + rng.uniform(-width * .47, width * .47)
        pz = z + rng.uniform(-height * .43, height * .43)
        length = rng.uniform(.018, .09)
        mark = box(target, f"{name} / scrape {index + 1}", (px, y, pz),
                   (rng.uniform(.0015, .0035), .001, length), edge, 0)
        mark.rotation_euler[1] = rng.uniform(-.29, .29)


def cable(target, name, points, radius=.006):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 2
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, xyz in zip(spline.points, points):
        point.co = (*xyz, 1)
    obj = bpy.data.objects.new(name, curve)
    target.objects.link(obj)
    curve.materials.append(rubber)
    return obj


def struts(target, x, y_front, top):
    for offset in (-.405, .405):
        box(target, "bolted salvaged angle iron", (x + offset, y_front + .025, top / 2), (.05, .09, top), edge, .003)
        for z in (.11, top - .11):
            bolt(target, "angle-iron fixing", (x + offset, y_front - .029, z), .012)
    box(target, "upper steel crossbar", (x, y_front + .018, top - .035), (.86, .10, .055), edge, .008)


def console(x, tier):
    accent = teal if tier == 1 else violet
    target = collection(f"GAME MODEL / Storage Console Tier {tier}")
    box(target, "floor foot cut from old machine frame", (x, 0, .065), (.95, .75, .13), steel_dark, .008)
    box(target, "chipped olive cabinet shell", (x, .025, .48), (.87, .67, .77), steel, .009)
    box(target, "reused instrument housing", (x, .035, .89), (.90, .72, .30), steel, .008)
    box(target, "rusty lid edge", (x, -.325, 1.025), (.89, .025, .025), rust, .002)
    box(target, "salvaged timber side reinforcement", (x + .443, .035, .55), (.018, .51, .57), wood, .003)
    for z in (.32, .76):
        box(target, "mismatched side retaining strap", (x + .458, .032, z), (.022, .55, .027), edge, .002)
    box(target, "welded-on front access plate", (x, -.333, .49), (.69, .023, .13), steel_dark, .004)
    box(target, "recessed control face", (x, -.331, .69), (.74, .025, .50), rubber, .014)
    box(target, "bolted display frame", (x, -.350, .765), (.615, .020, .31), edge, .002)
    box(target, "old monochrome display", (x, -.365, .765), (.585, .012, .28), screen, .002)
    box(target, "faded heading on display", (x - .038, -.374, .858), (.42, .006, .012), screen_dim, .001)
    for row in range(3):
        z = .804 - row * .065
        box(target, f"inventory slot {row + 1}", (x - .235, -.374, z), (.028, .006, .025), screen_dim, .001)
        box(target, f"inventory count line {row + 1}", (x - .055, -.374, z), (.265 - row * .035, .006, .008), screen_dim, .001)
    box(target, "network emblem rail", (x, -.378, 1.02), (.77, .037, .045), steel_dark, .006)
    front_text(target, "STORAGE stencil", "STORAGE", (x, -.400, 1.010), .051, white)
    for index in range(4):
        side_x = x + .345
        z = .868 - index * .075
        box(target, f"tier socket {index + 1}", (side_x, -.375, z), (.043, .021, .047), steel_dark, .003)
        box(target, f"tier lamp {index + 1}", (side_x, -.389, z), (.024, .008, .031), lamp_green if index < tier else off, .002)
    for index in range(5):
        box(target, "lower ventilation louvre", (x - .26 + index * .13, -.33, .315), (.088, .026, .012), edge, .003)
    cylinder(target, "physical access dial", (x - .24, -.367, .425), .052, .036, edge, front=True)
    cylinder(target, "physical dial center", (x - .24, -.391, .425), .029, .01, rubber, front=True)
    for index in range(2):
        cylinder(target, "network socket rim", (x + .08 + index * .13, -.365, .424), .037, .021, edge, front=True)
        cylinder(target, "network socket cavity", (x + .08 + index * .13, -.381, .424), .022, .008, rubber, front=True)
    struts(target, x, -.32, 1.02)
    for side in (-1, 1):
        box(target, "foot mounting tab", (x + side * .38, -.27, .052), (.10, .12, .025), edge, .003)
    for side in (-1, 1):
        for z in (.47, .88):
            bolt(target, "display and access fixing", (x + side * .315, -.376, z), .010)
    front_text(target, "scratched tier stencil", f"T{tier}", (x + .224, -.356, .493), .054, accent)
    scratches(target, "face paint wear", x, -.348, .58, .69, .17, 17, 100 + tier)
    scratches(target, "lower painted panel wear", x, -.337, .25, .67, .22, 12, 200 + tier)
    cable(target, "loosely clipped exterior data cable", [
        (x + .46, .19, .88), (x + .47, .21, .73), (x + .49, .14, .57),
        (x + .48, -.04, .45), (x + .45, -.16, .39)], .007)
    bolt(target, "rough side strap upper fixing", (x + .471, -.18, .76), .014)
    bolt(target, "rough side strap lower fixing", (x + .471, -.18, .32), .014)
    if tier == 4:
        box(target, "added tier four relay box", (x + .10, .26, 1.095), (.43, .22, .11), steel_dark, .004)
        box(target, "blue-grey identification paint", (x, -.30, 1.12), (.53, .028, .025), accent, .002)
        cable(target, "relay retrofit wiring", [
            (x + .30, .28, 1.09), (x + .37, .32, 1.05),
            (x + .44, .34, .98), (x + .45, .29, .85)], .006)
    return target


def workshop(x):
    target = collection("GAME MODEL / Workshop Controller")
    box(target, "reused welder mounting foot", (x, 0, .066), (.95, .76, .13), steel_dark, .006)
    box(target, "weathered automation shell", (x, .018, .50), (.87, .68, .82), steel, .009)
    box(target, "bolt-on controller head", (x, .026, .95), (.90, .72, .28), steel_dark, .007)
    box(target, "rough wooden side repair", (x + .443, .04, .50), (.025, .48, .55), wood, .003)
    box(target, "lower welded patch", (x - .19, -.343, .36), (.30, .014, .16), rust, .002)
    for dx in (-.12, .12):
        bolt(target, "patch screw", (x - .19 + dx, -.354, .36), .011)
    box(target, "black inset for machine status", (x, -.342, .75), (.73, .030, .47), rubber, .014)
    box(target, "status screen frame", (x - .085, -.363, .793), (.50, .023, .30), edge, .011)
    box(target, "status screen", (x - .085, -.378, .793), (.469, .009, .27), screen, .004)
    front_text(target, "PROD stencil", "PROD", (x - .085, -.390, .875), .065, white)
    for row in range(3):
        z = .815 - row * .071
        box(target, f"machine lane {row + 1}", (x - .27, -.389, z), (.043, .009, .034), screen_dim, .002)
        box(target, f"queue line {row + 1}", (x - .085, -.389, z), (.245 - row * .045, .009, .013), screen_dim, .002)
    for row in range(3):
        cylinder(target, f"machine-ready lamp {row + 1}", (x + .29, -.374, .865 - row * .074), .029, .014,
                 lamp_amber if row == 0 else off, front=True)
    box(target, "worn hazard-color status strip", (x, -.389, .505), (.67, .024, .024), amber, .002)
    for index in range(4):
        box(target, "smelter intake grille", (x - .235 + index * .15, -.34, .348), (.102, .025, .017), edge, .003)
    cylinder(target, "manual override dial", (x - .22, -.375, .428), .048, .034, edge, front=True)
    cylinder(target, "override center", (x - .22, -.397, .428), .025, .012, rubber, front=True)
    for offset in (-.39, .39):
        box(target, "yellowed replaceable side rail", (x + offset, -.31, .61), (.032, .075, .80), amber, .003)
    struts(target, x, -.32, 1.08)
    box(target, "warning lens housing", (x + .26, .15, 1.134), (.20, .18, .088), steel_dark, .012)
    box(target, "warning lens", (x + .26, .057, 1.135), (.12, .009, .045), lamp_amber, .004)
    scratches(target, "side rail abuse", x + .39, -.355, .63, .025, .65, 7, 310)
    scratches(target, "lower access plate abuse", x + .05, -.338, .30, .58, .20, 18, 320)
    cable(target, "machine-link cable", [
        (x + .46, .20, .92), (x + .49, .24, .76), (x + .50, .18, .61),
        (x + .48, -.04, .45), (x + .44, -.16, .35)], .009)
    cable(target, "second machine-link cable", [
        (x + .46, .12, .84), (x + .52, .10, .69), (x + .50, -.02, .57),
        (x + .46, -.20, .40)], .006)
    for dx in (-.31, .31):
        bolt(target, "head fixing", (x + dx, -.397, .98), .012)
    return target


def locker(x):
    target = collection("GAME MODEL / Loadout Locker (1 x 2 blocks)")
    box(target, "rusted locker base", (x, .01, .054), (.95, .62, .108), steel_dark, .007)
    box(target, "salvaged tall cabinet shell", (x, .025, .974), (.87, .55, 1.76), steel, .008)
    box(target, "hammered top cap", (x, .035, 1.885), (.91, .60, .08), rust, .005)
    box(target, "door recess", (x, -.270, .990), (.77, .022, 1.63), rubber, .010)
    for side in (-1, 1):
        door_x = x + side * .19
        box(target, "mismatched replacement locker door", (door_x, -.294, .990),
            (.365, .030, 1.59), locker_blue if side < 0 else steel_dark, .005)
        box(target, "bolted upper repair plate", (door_x, -.316, 1.532),
            (.315, .013, .37), steel if side < 0 else locker_blue, .003)
        box(target, "corroded door kick plate", (door_x, -.317, .36), (.315, .015, .18), rust, .004)
        for dx in (-.135, .135):
            for z in (.29, 1.43):
                bolt(target, "door plate fastener", (door_x + dx, -.329, z), .009)
        for row in range(5):
            box(target, "ventilation slot", (door_x, -.327, 1.20 - row * .055), (.24, .009, .013), edge, .003)
        cylinder(target, "rounded handle boss", (door_x - side * .118, -.328, .854), .031, .015, edge, front=True)
        box(target, "vertical grip", (door_x - side * .118, -.350, .854), (.018, .020, .19), edge, .006)
        scratches(target, "scraped locker door", door_x, -.315, .72, .31, .35, 20, 420 + side)
    box(target, "central electronic lock column", (x, -.338, 1.015), (.047, .029, 1.57), edge, .006)
    for index in range(4):
        box(target, f"loadout profile indicator {index + 1}",
            (x, -.357, 1.515 - index * .12), (.019, .010, .047), lamp_green if index == 0 else off, .003)
    box(target, "old painted identification sign", (x, -.335, 1.759), (.70, .018, .071), locker_blue, .003)
    front_text(target, "LOADOUT stencil", "LOADOUT", (x, -.349, 1.749), .063, white)
    for offset in (-.405, .405):
        box(target, "corner crash rail", (x + offset, -.265, .98), (.050, .078, 1.76), edge, .008)
        for z in (.40, 1.55):
            bolt(target, "locker corner fixing", (x + offset, -.313, z), .012)
    box(target, "field-fitted latch tab", (x, -.368, .81), (.11, .025, .045), rust, .003)
    cylinder(target, "padlock hasp", (x, -.390, .765), .030, .012, brass, front=True)
    cable(target, "damaged external lock loom", [
        (x + .44, .11, 1.62), (x + .46, .16, 1.36),
        (x + .48, .08, 1.14), (x + .45, -.08, .98)], .005)
    return target


preview = collection("PREVIEW ONLY / floor, labels and lights")
positions = (-2.40, -.80, .80, 2.40)
console(positions[0], 1)
console(positions[1], 4)
workshop(positions[2])
locker(positions[3])
names = (("STORAGE / T1", teal), ("STORAGE / T4", violet), ("WORKSHOP", amber), ("LOADOUT LOCKER", locker_blue))
for x, (name, accent) in zip(positions, names):
    box(preview, "presentation plinth, not game geometry", (x, .01, -.075), (1.36, .92, .12), plinth_mat, .018)
    box(preview, "color chip", (x, -.47, -.038), (.67, .018, .012), accent, .002)
    front_text(preview, f"presentation label / {name}", name, (x, -.484, -.121), .064, white)

box(preview, "studio floor", (0, 0, -.174), (200, 200, .045), floor_mat, 0)


def point(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


camera_data = bpy.data.cameras.new("Family presentation camera")
camera = bpy.data.objects.new("Family presentation camera", camera_data)
preview.objects.link(camera)
camera.location = (5.4, -10.2, 5.3)
point(camera, (0, -.02, .88))
camera_data.type = "ORTHO"
camera_data.ortho_scale = 7.7
bpy.context.scene.camera = camera

for name, location, power, color, size in (
    ("large cool key", (-3.6, -4.8, 6.5), 950, (0.78, 0.88, 1.0), 5.0),
    ("warm overhead bounce", (3.0, -1.8, 5.8), 740, (1.0, 0.77, 0.59), 4.0),
    ("blue rear rim", (0, 3.0, 5.0), 1050, (0.45, 0.70, 1.0), 3.8),
):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = power
    data.color = color
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    preview.objects.link(obj)
    obj.location = location
    point(obj, (0, 0, .8))

world = bpy.context.scene.world
world.color = (0.035, 0.045, 0.055)
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.06, 0.08, 0.10, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = .65

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 1800
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.view_settings.view_transform = "AgX"
scene.camera.data.lens = 45

# Let the saved blend open directly at the useful presentation camera.
for workspace in bpy.data.workspaces:
    for screen_area in workspace.screens[0].areas if hasattr(workspace, "screens") and workspace.screens else []:
        if screen_area.type == "VIEW_3D":
            screen_area.spaces.active.region_3d.view_perspective = "CAMERA"

bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "nearbycraft_block_family_concept.blend"))
scene.render.filepath = str(ROOT / "nearbycraft_block_family_concept.png")
bpy.ops.render.render(write_still=True)

# A closer review render for the tier progression, without changing the saved scene.
camera.location = (-1.4, -6.0, 3.4)
point(camera, (-1.6, 0, .65))
camera_data.ortho_scale = 3.7
scene.render.resolution_x = 1500
scene.render.resolution_y = 1000
scene.render.filepath = str(ROOT / "nearbycraft_console_detail.png")
bpy.ops.render.render(write_still=True)
print("CONCEPT COMPLETE: blend and two review renders saved under", ROOT)
