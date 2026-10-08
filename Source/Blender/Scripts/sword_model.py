"""
Bastard sword (hand-and-a-half sword), low-poly, for the first-person view (Blender 5.2 LTS, background).
Real size: 1.19 m overall, blade 0.88 m (4.8 cm at the base, tapering to a point, diamond section with a ridge), long grip
(0.26 m, room for one hand and a half), straight cross guard, pear pommel. The origin is where the leading hand holds the grip,
the blade points along +Z (Blender) = +Y in Unity, the flat of the blade faces +/-Y (Blender) = +/-Z in Unity.
Materials: Sword_Blade, Sword_Guard, Sword_Grip (Unity makes them).

  blender -b --factory-startup --python sword_model.py -- <out.fbx>
"""
import bpy, bmesh, sys, math

OUT = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgba):
    m = bpy.data.materials.new(name); m.diffuse_color = rgba; return m

BLADE, GUARD, GRIP = mat("Sword_Blade", (0.8, 0.8, 0.82, 1)), mat("Sword_Guard", (0.35, 0.33, 0.3, 1)), mat("Sword_Grip", (0.23, 0.13, 0.07, 1))
HAND = 0.07                       # the leading hand holds the grip this far below the guard (0 = the origin)

def obj_from_bmesh(name, bm, material):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    return o

# ---- blade: rings of a flat diamond section (edge, ridge, edge, ridge), tapering, then the point
bm = bmesh.new()
base_z = 0.155 - HAND
stations = [(0.000, 0.048, 0.0085), (0.03, 0.047, 0.0080), (0.25, 0.043, 0.0070), (0.50, 0.038, 0.0060),
            (0.68, 0.033, 0.0052), (0.80, 0.025, 0.0042), (0.86, 0.012, 0.0028)]
rings = []
for z, w, t in stations:
    ring = [bm.verts.new((-w / 2, 0, base_z + z)), bm.verts.new((-w / 4, t / 2, base_z + z)), bm.verts.new((w / 4, t / 2, base_z + z)),
            bm.verts.new((w / 2, 0, base_z + z)), bm.verts.new((w / 4, -t / 2, base_z + z)), bm.verts.new((-w / 4, -t / 2, base_z + z))]
    rings.append(ring)
for a, b in zip(rings, rings[1:]):
    for i in range(6):
        j = (i + 1) % 6
        bm.faces.new((a[i], a[j], b[j], b[i]))
tip = bm.verts.new((0, 0, base_z + 0.88))
for i in range(6):
    bm.faces.new((rings[-1][i], rings[-1][(i + 1) % 6], tip))
bm.faces.new(list(reversed(rings[0])))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
blade = obj_from_bmesh("Blade", bm, BLADE)

# ---- cross guard with rounded ends, two ferrules, the grip, the pommel
def prim(add, name, material, loc, scale=(1, 1, 1), **kw):
    add(location=loc, **kw)
    o = bpy.context.active_object; o.name = name; o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    return o

guard_z = 0.142 - HAND
prim(bpy.ops.mesh.primitive_cube_add, "Guard", GUARD, (0, 0, guard_z), (0.115, 0.0125, 0.011), size=2)
for s in (-1, 1):
    prim(bpy.ops.mesh.primitive_uv_sphere_add, "GuardEnd", GUARD, (s * 0.118, 0, guard_z + 0.003), (1, 1, 1), radius=0.013, segments=10, ring_count=6)
prim(bpy.ops.mesh.primitive_cylinder_add, "Grip", GRIP, (0, 0, 0.0 - HAND + 0.0), (1.0, 0.85, 1.0), vertices=10, radius=0.0165, depth=0.26)
for z in (0.124 - HAND, -0.124 - HAND):
    prim(bpy.ops.mesh.primitive_cylinder_add, "Ferrule", GUARD, (0, 0, z), (1.0, 0.85, 1.0), vertices=10, radius=0.019, depth=0.014)
prim(bpy.ops.mesh.primitive_uv_sphere_add, "Pommel", GUARD, (0, 0, -0.163 - HAND), (1.0, 0.8, 1.25), radius=0.029, segments=12, ring_count=8)

objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.view_layer.objects: o.select_set(False)
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = blade
bpy.ops.object.join()
sword = bpy.context.active_object; sword.name = "BastardSword"; sword.data.name = "BastardSword"
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
tris = sum(len(p.vertices) - 2 for p in sword.data.polygons)
dims = [round(v, 3) for v in sword.dimensions]
print("SWORD_MODEL: %d triangles, size %s m, materials %s" % (tris, dims, [m.name for m in sword.data.materials]))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='OFF', path_mode='STRIP', embed_textures=False)
