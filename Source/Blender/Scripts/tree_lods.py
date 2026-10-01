"""
LOD meshes for the leaf-card trees (run on trees.blend, Blender 5.2 LTS):

  <name>_LOD1   every foliage card collapsed to one quad (its 4 corners), twigs removed
  <name>_LOD2   the same cards, only the trunk left of the wood

UVs and the spherical custom normals are carried over from LOD0, so the crown shades the same at every LOD.
Exports Assets/Art/Environment/Trees/Models/<name>_LOD1.fbx and _LOD2.fbx.

  blender -b Source/Blender/Environment/trees.blend --python Source/Blender/Scripts/tree_lods.py
"""
import bpy, os
from mathutils import Vector

OUT = r"D:\projectgame666\Assets\Art\Environment\Trees\Models"
TREES = ["Tree_Variation_04", "Tree_Variation_04_B", "Tree_Variation_04_C", "Tree_Variation_04_D"]
# wood pieces are separate tubes; how many vertices a tube must have to survive in each LOD
WOOD_MIN_VERTS = {1: 8, 2: 30}

def make_lod(src, level):
    me = src.data; nv = len(me.vertices)
    parent = list(range(nv))
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for p in me.polygons:
        r = find(p.vertices[0])
        for v in p.vertices[1:]: parent[find(v)] = r
    comps = {}
    for p in me.polygons: comps.setdefault(find(p.vertices[0]), []).append(p)
    uvs = me.uv_layers[0].data; cn = me.corner_normals
    verts = []; faces = []; f_uv = []; f_no = []; f_mat = []; remap = {}
    def vid(i):
        if i not in remap: remap[i] = len(verts); verts.append(me.vertices[i].co.copy())
        return remap[i]
    for polys in comps.values():
        if polys[0].material_index == 0:                       # wood: keep the big tubes unchanged
            if len({v for p in polys for v in p.vertices}) < WOOD_MIN_VERTS[level]: continue
            for p in polys:
                faces.append([vid(v) for v in p.vertices])
                f_uv.append([uvs[l].uv.copy() for l in p.loop_indices]); f_no.append([cn[l].vector.copy() for l in p.loop_indices]); f_mat.append(0)
        else:                                                  # foliage card: one quad through its 4 corner vertices
            use = {}; info = {}
            for p in polys:
                for v, l in zip(p.vertices, p.loop_indices):
                    use[v] = use.get(v, 0) + 1; info[v] = (uvs[l].uv.copy(), cn[l].vector.copy())
            corners = [v for v, n in use.items() if n == 1]
            if len(corners) != 4: continue
            c0 = corners[0]; rest = sorted(corners[1:], key=lambda v: (me.vertices[v].co - me.vertices[c0].co).length)
            order = [c0, rest[0], rest[2], rest[1]]             # rest[2] is the diagonal corner
            faces.append([vid(v) for v in order]); f_uv.append([info[v][0] for v in order]); f_no.append([info[v][1] for v in order]); f_mat.append(1)
    name = "%s_LOD%d" % (src.name, level)
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old)
    nm = bpy.data.meshes.new(name); nm.from_pydata(verts, [], faces); nm.update()
    for m in me.materials: nm.materials.append(m)
    uvl = nm.uv_layers.new(name="UVMap"); normals = []
    for p, fu, fn, fm in zip(nm.polygons, f_uv, f_no, f_mat):
        p.material_index = fm; p.use_smooth = True
        for l, u, n in zip(p.loop_indices, fu, fn): uvl.data[l].uv = u
        normals.extend(fn)
    nm.normals_split_custom_set(normals); nm.update()
    ob = bpy.data.objects.new(name, nm); bpy.context.scene.collection.objects.link(ob)
    return ob, sum(len(f)-2 for f in faces), sum(len(f)-2 for f, m in zip(faces, f_mat) if m == 0)

report = []
for tname in TREES:
    src = bpy.data.objects[tname]
    row = {"name": tname, "lod0_tris": sum(len(p.vertices)-2 for p in src.data.polygons)}
    for level in (1, 2):
        ob, tris, wood = make_lod(src, level)
        bpy.context.view_layer.update()
        for o in bpy.context.view_layer.objects:
            if o: o.select_set(False)
        ob.location = (0, 0, 0); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, ob.name + ".fbx"), use_selection=True, object_types={'MESH'},
                                 apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                                 mesh_smooth_type='OFF', use_tspace=True, use_triangles=True, path_mode='STRIP', embed_textures=False, bake_anim=False)
        row["lod%d_tris" % level] = tris; row["lod%d_wood_tris" % level] = wood
        bpy.data.objects.remove(ob)
    report.append(row)
result = report
print("LOD_REPORT", report)
