"""
Polygon budget for first-person weapon models (Blender 5.2 LTS, background):
imports an FBX, decimates its meshes (collapse, UVs kept so the original texture set still fits) to a triangle budget,
re-derives smooth / sharp shading by angle and exports a new FBX with the same object and material names.
Parts below KEEP_BELOW triangles (triggers, pins, sights) are left as they are.

  blender -b --factory-startup --python weapon_decimate.py -- <in.fbx> <out.fbx> <target triangles>
"""
import bpy, sys, math

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, OUT, TARGET = argv[0], argv[1], int(argv[2])
KEEP_BELOW = 160          # triangles: small parts lose their shape first, keep them
SMOOTH_ANGLE = 35.0       # degrees: hard-surface edges above this stay sharp

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']

def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

before = {o.name: tris(o) for o in meshes}
total = sum(before.values())
fixed = sum(t for t in before.values() if t < KEEP_BELOW)
big = total - fixed
ratio = min(1.0, max(0.02, (TARGET - fixed) / max(1, big)))
print("DECIMATE %s: %d tris, %d in small parts, ratio %.3f for the rest" % (SRC, total, fixed, ratio))

for o in meshes:
    bpy.context.view_layer.objects.active = o
    for s in bpy.context.view_layer.objects: s.select_set(False)
    o.select_set(True)
    if o.data.has_custom_normals:                       # imported split normals would not survive the collapse
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    if before[o.name] >= KEEP_BELOW and ratio < 1.0:
        m = o.modifiers.new("Decimate", 'DECIMATE')
        m.decimate_type = 'COLLAPSE'; m.ratio = ratio; m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(SMOOTH_ANGLE))

after = {o.name: tris(o) for o in meshes}
for o in meshes:
    mats = [s.material.name if s.material else "-" for s in o.material_slots]
    print("  %-28s %6d -> %6d  mats %s" % (o.name, before[o.name], after[o.name], mats))
print("DECIMATE_RESULT %s %d -> %d" % (OUT, total, sum(after.values())))

bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={'MESH', 'EMPTY', 'ARMATURE'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=False,
                         add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
