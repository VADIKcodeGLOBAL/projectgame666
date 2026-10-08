"""
Bastard sword model for Unity (Blender 5.2 LTS, background). The source (Assets/Art/Weapons/Models/source/source/Sword.rar ->
Sword_Low_Done.FBX, textures Sword_* in Assets/Art/Weapons/Models/source/textures) comes in inches, rotated, with the blade
along -Z and the origin somewhere on the blade. This puts it the way SwordSetup and MeleeWeapon take a sword:

  - one mesh "Sword", transforms applied, 1.23 m overall (the source's proportions kept);
  - the blade along +Z (Blender) = +Y in Unity, the cross guard across X, the flat of the blade facing +/-Y = +/-Z in Unity;
  - the origin where the leading hand holds the grip: HAND below the guard, on the axis of the grip;
  - material "Sword" (Unity remaps it to M_Sword).

The source is already low-poly (about 1000 triangles), so nothing is decimated.

  blender -b --factory-startup --python sword_import.py -- <Sword_Low_Done.FBX> <out.fbx>
"""
import bpy, sys, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
LENGTH = 1.23                       # overall length, m (pommel to point)
HAND = 0.07                         # the leading hand holds the grip this far below the guard, m

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == 'MESH']
if not meshes: raise SystemExit("SWORD_IMPORT_FAILED: no mesh in " + SRC)

def select(objs, active=None):
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]

select(meshes)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
if len(meshes) > 1: bpy.ops.object.join()
sword = bpy.context.view_layer.objects.active
for o in [x for x in scene.objects if x != sword]: bpy.data.objects.remove(o)
select([sword])
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def points(): return [v.co.copy() for v in sword.data.vertices]

# ---- axes: the longest extent is the blade, the next one the guard, the thinnest one the normal of the flat
pts = points()
mn = mathutils.Vector([min(p[i] for p in pts) for i in range(3)]); mx = mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
ext = mx - mn
thin, mid, along = sorted(range(3), key=lambda i: ext[i])
centre = (mn + mx) / 2
# the guard is where the section is widest; the blade is the longer side of it
guard_at = max(pts, key=lambda p: abs(p[mid] - centre[mid]))[along]
blade_sign = 1.0 if mx[along] - guard_at > guard_at - mn[along] else -1.0

def unit(i, s=1.0):
    v = mathutils.Vector((0.0, 0.0, 0.0)); v[i] = s; return v

z = unit(along, blade_sign); x = unit(mid); y = z.cross(x)          # right-handed: the flat's normal comes out as +/- the thin axis
rot = mathutils.Matrix((x, y, z))                                      # rows: new axes in old coordinates
scale = LENGTH / ext[along]
sword.matrix_world = mathutils.Matrix.Diagonal((scale, scale, scale, 1.0)) @ rot.to_4x4()
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---- the hand: HAND below the lowest part of the guard (anything wider than a third of it), centred on the grip
pts = points()
half = max(abs(p.x) for p in pts)
guard_low = min(p.z for p in pts if abs(p.x) > half / 3)
hand_z = guard_low - HAND
grip = [p for p in pts if abs(p.z - hand_z) < 0.04]
if len(grip) < 3: grip = sorted(pts, key=lambda p: abs(p.z - hand_z))[:16]
hx = (min(p.x for p in grip) + max(p.x for p in grip)) / 2; hy = (min(p.y for p in grip) + max(p.y for p in grip)) / 2
sword.data.transform(mathutils.Matrix.Translation((-hx, -hy, -hand_z)))
sword.data.update()

# ---- one material, names
sword.name = "Sword"; sword.data.name = "Sword"
sword.data.materials.clear()
sword.data.materials.append(bpy.data.materials.new("Sword"))

pts = points()
mn = mathutils.Vector([min(p[i] for p in pts) for i in range(3)]); mx = mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
tris = sum(len(p.vertices) - 2 for p in sword.data.polygons)
print("SWORD_IMPORT: %d triangles, %d uv layers, size %s m, point %.3f m above the hand, pommel %.3f m below, guard %.3f m wide"
      % (tris, len(sword.data.uv_layers), [round(v, 3) for v in (mx - mn)], mx.z, -mn.z, mx.x - mn.x))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='OFF', path_mode='STRIP', embed_textures=False)
