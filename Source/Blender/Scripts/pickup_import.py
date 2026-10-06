"""
Field pickup models from AI-generated (Tripo) FBX files (Blender 5.2 LTS, background):
imports the FBX, joins it into one mesh, decimates it to a triangle budget (collapse, UVs kept so the texture set still fits),
re-derives smooth / sharp shading by angle, renames the object and the material to <Name> and exports <out dir>/<Name>.fbx.
The PBR textures next to the source (<file>.fbm: Color, Normal, metallic, roughness) are copied to <tex dir>/<Name>_*.
Unity (SupplySetup.cs) builds the material (ProjectGame/WeaponPBR) from them by these names.

  blender -b --factory-startup --python pickup_import.py -- <in.fbx> <Name> <target triangles> <out dir> <tex dir>
"""
import bpy, sys, os, math, shutil

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, NAME, TARGET, OUT, TEX = argv[0], argv[1], int(argv[2]), argv[3], argv[4]
SMOOTH_ANGLE = 40.0
os.makedirs(OUT, exist_ok=True); os.makedirs(TEX, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.view_layer.objects: o.select_set(False)
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
o = bpy.context.active_object
for e in [x for x in bpy.context.scene.objects if x.type != 'MESH']: bpy.data.objects.remove(e)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def tris(ob): return sum(len(p.vertices) - 2 for p in ob.data.polygons)

before = tris(o)
if o.data.has_custom_normals: bpy.ops.mesh.customdata_custom_splitnormals_clear()
if before > TARGET:
    m = o.modifiers.new("Decimate", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'; m.ratio = TARGET / before; m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(SMOOTH_ANGLE))

o.name = NAME; o.data.name = NAME
for s in o.material_slots:
    if s.material: s.material.name = NAME                 # one material per model; Unity remaps it by this name

# textures: the .fbm folder Tripo writes next to the FBX
fbm = os.path.splitext(SRC)[0] + ".fbm"
copied = []
if os.path.isdir(fbm):
    for f in os.listdir(fbm):
        low = f.lower(); ext = os.path.splitext(f)[1].lower()
        kind = ("BaseColor" if low.startswith("color") else "Normal" if low.startswith("normal")
                else "Metallic" if "metallic" in low else "Roughness" if "roughness" in low else None)
        if kind is None: continue
        dst = os.path.join(TEX, "%s_%s%s" % (NAME, kind, ".jpg" if ext == ".jpeg" else ext))
        shutil.copyfile(os.path.join(fbm, f), dst); copied.append(os.path.basename(dst))

size = [round(v, 3) for v in o.dimensions]
print("PICKUP_IMPORT %s: %d -> %d triangles, size %s, textures %s" % (NAME, before, tris(o), size, sorted(copied)))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=False, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=False, mesh_smooth_type='OFF', use_tspace=False, path_mode='STRIP', embed_textures=False)
