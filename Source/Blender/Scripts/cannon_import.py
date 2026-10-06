"""
Field cannon model for Unity (Blender 5.2 LTS, background). The source (Assets/Art/Weapons/cannon/source/cannon.rar -> canon.fbx)
is 13 loose objects; this makes one rig out of them:

  Cannon (empty, on the ground between the wheels)
    Carriage      every static part joined (cheeks, bed, axles, handle)
    Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR   origins at the wheel centres (they turn about the axle)
    Barrel        origin at the pivot it is raised about; children: Muzzle (bore end), Breech (back end)

The source is already low-poly (6720 triangles), so nothing is decimated. Material "cannon" (textures 1001_*).

  blender -b --factory-startup --python cannon_import.py -- <canon.fbx> <out.fbx>
"""
import bpy, sys, mathutils, numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
PIVOT_AT = 0.42                     # pivot of the barrel: this far from the breech towards the muzzle

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == 'MESH']

def select(objs, active=None):
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]

select(meshes)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in [x for x in scene.objects if x.type != 'MESH']: bpy.data.objects.remove(o)

def bounds(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    mn = mathutils.Vector([min(p[i] for p in pts) for i in range(3)]); mx = mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
    return mn, mx

def set_origin(o, point):
    scene.cursor.location = point
    select([o]); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')

# ---- barrel: the biggest object; its axis from the vertices (principal direction), muzzle = the thin end
barrel = max(meshes, key=lambda o: len(o.data.vertices))
P = np.array([[v.co.x, v.co.y, v.co.z] for v in barrel.data.vertices])
m = P.mean(0); ax = np.linalg.svd(P - m, full_matrices=False)[2][0]
proj = (P - m) @ ax
def end_radius(sel):
    Q = P[sel]; d = (Q - m) - np.outer((Q - m) @ ax, ax); return np.sqrt((d ** 2).sum(1)).max()
lo_end, hi_end = m + ax * proj.min(), m + ax * proj.max()
r_lo, r_hi = end_radius(proj < proj.min() + 0.15), end_radius(proj > proj.max() - 0.15)
muzzle, breech = (lo_end, hi_end) if r_lo > r_hi * 0.6 and lo_end[2] > hi_end[2] else (hi_end, lo_end)
if muzzle[2] < breech[2]: muzzle, breech = breech, muzzle    # a field gun's muzzle is the raised end
muzzle = mathutils.Vector(muzzle); breech = mathutils.Vector(breech)
pivot = breech + (muzzle - breech) * PIVOT_AT
barrel.name = "Barrel"; barrel.data.name = "Barrel"
set_origin(barrel, pivot)

# ---- wheels: cylinders with a horizontal axis next to the ground
others = [o for o in meshes if o != barrel]
wheels = []
for o in others:
    mn, mx = bounds(o); size = mx - mn
    if o.name.lower().startswith("pcylinder") and abs(size.x - size.z) < 0.05 and size.y < size.x and mn.z < 0.0:
        wheels.append(o)
wmid = sum((sum(bounds(w), mathutils.Vector()) / 2 for w in wheels), mathutils.Vector()) / max(1, len(wheels))
dir_fwd = (muzzle - breech); dir_fwd.z = 0; dir_fwd.normalize()
side = mathutils.Vector((0, 0, 1)).cross(dir_fwd)            # to the left of the muzzle direction
for w in wheels:
    mn, mx = bounds(w); c = (mn + mx) / 2
    front = (c - wmid).dot(dir_fwd) > 0; left = (c - wmid).dot(side) > 0
    w.name = "Wheel_" + ("F" if front else "R") + ("L" if left else "R"); w.data.name = w.name
    set_origin(w, c)

# ---- carriage: everything else, joined
parts = [o for o in others if o not in wheels]
select(parts); bpy.ops.object.join()
carriage = bpy.context.active_object; carriage.name = "Carriage"; carriage.data.name = "Carriage"
ground = min(bounds(w)[0].z for w in wheels)
root_pt = mathutils.Vector((wmid.x, wmid.y, ground))
set_origin(carriage, root_pt)

root = bpy.data.objects.new("Cannon", None); scene.collection.objects.link(root); root.location = root_pt
for o in [carriage, barrel] + wheels:
    o.parent = root; o.matrix_parent_inverse = root.matrix_world.inverted()
for name, pt in (("Muzzle", muzzle), ("Breech", breech)):
    e = bpy.data.objects.new(name, None); scene.collection.objects.link(e); e.location = pt
    e.parent = barrel; e.matrix_parent_inverse = barrel.matrix_world.inverted()

for o in [carriage, barrel] + wheels:
    for s in o.material_slots:
        if s.material: s.material.name = "cannon"

elev = np.degrees(np.arctan2(muzzle.z - breech.z, ((muzzle - breech).xy).length))
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in [carriage, barrel] + wheels)
print("CANNON_IMPORT: %d triangles, wheels %s, barrel %.2f m long, rest elevation %.1f deg, pivot %s, muzzle %s, ground %.3f"
      % (tris, sorted(w.name for w in wheels), (muzzle - breech).length, elev, [round(v, 3) for v in pivot], [round(v, 3) for v in muzzle], ground))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={'MESH', 'EMPTY'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
                         mesh_smooth_type='FACE', use_tspace=False, path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
