"""
Low-poly supply models for the field pickups (Blender 5.2 LTS, background), one FBX each, Z up in Blender = Y up in Unity:

  Medkit.fbx   red case with white crosses on both faces and a carry handle      materials Medkit_Body, Medkit_Cross, Medkit_Handle
  Syringe.fbx  standing syringe: glass barrel, glowing liquid, plunger, needle    materials Syringe_Glass, Syringe_Liquid,
                                                                                  Syringe_Plunger, Syringe_Metal

Sizes are real (medkit 32 cm wide, syringe 16 cm tall); the pickup prefab scales them up. Unity makes the materials by name.

  blender -b --factory-startup --python supply_models.py -- <out dir>
"""
import bpy, sys, os, math

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0]
os.makedirs(OUT, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgba):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = rgba
    return m


def finish(o, mat, bevel=0.0, segments=2):
    if bevel > 0.0:
        b = o.modifiers.new("Bevel", 'BEVEL'); b.width = bevel; b.segments = segments; b.limit_method = 'ANGLE'
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=b.name)
    o.data.materials.append(mat)
    return o


def box(name, size, loc, mat, bevel=0.0, segments=2):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, mat, bevel, segments)


def cylinder(name, radius, depth, z, mat, verts=16, radius2=None):
    if radius2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=(0, 0, z))
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius, radius2=radius2, depth=depth, location=(0, 0, z))
    o = bpy.context.active_object; o.name = name
    return finish(o, mat)


def join_and_export(name):
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(30))
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    print("SUPPLY_MODEL %s: %d triangles, materials %s" % (name, tris, [m.name for m in o.data.materials]))
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=False, mesh_smooth_type='OFF', path_mode='STRIP', embed_textures=False)


# ---- medkit: 32 x 12 x 22 cm case
reset()
red = material("Medkit_Body", (0.75, 0.07, 0.05, 1))
white = material("Medkit_Cross", (0.95, 0.95, 0.95, 1))
dark = material("Medkit_Handle", (0.08, 0.08, 0.09, 1))
box("Case", (0.32, 0.12, 0.22), (0, 0, 0.11), red, bevel=0.018, segments=2)
box("Seam", (0.325, 0.125, 0.008), (0, 0, 0.15), dark)                       # lid line
for s in (1, -1):                                                           # a cross on each face
    y = s * 0.0615
    box("CrossV", (0.045, 0.006, 0.14), (0, y, 0.105), white)
    box("CrossH", (0.14, 0.006, 0.045), (0, y, 0.105), white)
for x in (-0.065, 0.065):                                                   # handle posts and grip
    box("Post", (0.018, 0.03, 0.03), (x, 0, 0.235), dark)
box("Grip", (0.16, 0.032, 0.022), (0, 0, 0.258), dark, bevel=0.006, segments=1)
for x in (-0.11, 0.11):                                                     # latches
    box("Latch", (0.03, 0.012, 0.025), (x, 0.064, 0.15), dark)
join_and_export("Medkit")

# ---- syringe: 16 cm, needle down, plunger up
reset()
glass = material("Syringe_Glass", (0.85, 0.95, 1.0, 0.35))
liquid = material("Syringe_Liquid", (0.1, 1.0, 0.7, 1))
plunger = material("Syringe_Plunger", (0.92, 0.92, 0.95, 1))
metal = material("Syringe_Metal", (0.7, 0.72, 0.75, 1))
cylinder("Barrel", 0.0135, 0.085, 0.0425, glass, verts=16)
cylinder("Liquid", 0.0112, 0.058, 0.034, liquid, verts=12)
cylinder("Stopper", 0.0115, 0.008, 0.067, metal, verts=12)
cylinder("Rod", 0.0035, 0.052, 0.097, plunger, verts=8)
cylinder("Thumb", 0.013, 0.004, 0.125, plunger, verts=16)
box("Flange", (0.05, 0.02, 0.004), (0, 0, 0.087), plunger, bevel=0.002, segments=1)
cylinder("Hub", 0.0075, 0.012, -0.006, plunger, verts=12, radius2=0.0035)
cylinder("Needle", 0.0009, 0.038, -0.031, metal, verts=6)
join_and_export("Syringe")
