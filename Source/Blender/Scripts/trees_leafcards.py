"""
Branching broadleaf game trees with leaf cards (Blender 5.2 LTS).  4 tree types + 3 alternates of type 04, one texture set:

  Tree_Variation_01  Main/Hero        trunk forks low into heavy limbs, wide airy crown
  Tree_Variation_02  Young/Thin       slender straight leader, many short drooping side branches, hanging foliage
  Tree_Variation_03  Bent/Asymmetric  tall thin leaning trunk, high one-sided crown
  Tree_Variation_04  Full/Oval        short bare trunk, central leader, full oval crown that starts low
  Tree_Variation_04_B / _C / _D       alternates of 04: taller leaning / shorter wider / higher thinner crown

Per tree (one mesh object, origin at the trunk base = 0,0,0):
  * wood: recursive branching (trunk -> limbs -> branches -> twigs), every piece a tapered tube that ends
    in a point, cylindrical UVs for the tiling bark. 500-1000 triangles per tree                 -> M_Tree_Bark
  * crown: a clump of two crossed, bent cards at the branch ends, visible branches in between     -> M_Tree_Leaves
  * crown normals: Data Transfer (Custom Normals, Nearest Face Interpolated) from a temporary smooth
    ellipsoid fitted to the crown, limited to the "Leaves" vertex group, applied; ellipsoid removed.

Run from the Scripting tab:  exec(open(r"D:/projectgame666/Source/Blender/Scripts/trees_leafcards.py").read())
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

# ============================ CONFIG ============================
TREES_DIR = r"D:\projectgame666\Assets\Art\Environment\Trees"
LEAF_TEX = os.path.join(TREES_DIR, "leaf card texture", "orth_top.png")
BARK_DIFF = os.path.join(TREES_DIR, "textures", "eucalyptus_bark_diff_1k.jpg")
BARK_ROUGH = os.path.join(TREES_DIR, "textures", "eucalyptus_bark_rough_1k.exr")
BARK_NOR = os.path.join(TREES_DIR, "textures", "eucalyptus_bark_nor_gl_1k.exr")
EXPORT_DIR = os.path.join(TREES_DIR, "Models")     # None = do not export FBX
BARK_TILE = 1.4        # metres of trunk length per bark texture tile (V)
ALPHA_CLIP = 0.5
SPACING = 8.0          # distance between the trees in the .blend (export is always at 0,0,0)
TRUNK_TRIS = (500, 1000)

# levels[0] = trunk, levels[1] = limbs, ... ; per level:
#   sides/segs  tube resolution          wander  random change of direction per segment
#   trop        pull up (+) / down (-)   taper   radius at the end of the piece relative to its start
#   children    (min, max) count         child_s where on the piece the children sit (0..1)
#   angle       child angle to the parent, degrees     ratio  child length / parent length
#   rad         child radius / parent radius at that point    falloff  children get shorter towards the tip
#   leaf        (min, max) clump size at the tip; None = no foliage on this level
VARIANTS = [
    dict(name="Tree_Variation_01", seed=4, length=2.5, radius=0.17, start_dir=(0.06, 0.03, 1), bias=None, hang=0.0,
         levels=[
             dict(sides=8, segs=5, wander=0.07, trop=0.05, taper=0.62, children=(3, 3), child_s=(0.60, 0.97), angle=(24, 42), ratio=(0.95, 1.2), rad=0.74, falloff=0.0, leaf=None),
             dict(sides=6, segs=4, wander=0.16, trop=0.10, taper=0.45, children=(3, 4), child_s=(0.40, 0.95), angle=(32, 58), ratio=(0.50, 0.70), rad=0.62, falloff=0.2, leaf=(1.2, 1.6)),
             dict(sides=4, segs=3, wander=0.20, trop=0.05, taper=0.40, children=(1, 2), child_s=(0.45, 0.85), angle=(30, 60), ratio=(0.50, 0.70), rad=0.60, falloff=0.0, leaf=(1.1, 1.5)),
             dict(sides=3, segs=2, wander=0.25, trop=0.00, taper=0.40, children=(0, 0), leaf=(0.9, 1.3)),
         ]),
    dict(name="Tree_Variation_02", seed=9, length=6.2, radius=0.085, start_dir=(0.02, -0.03, 1), bias=None, hang=1.0,
         levels=[
             dict(sides=7, segs=9, wander=0.05, trop=0.04, taper=0.12, children=(13, 15), child_s=(0.27, 0.97), angle=(50, 78), ratio=(0.22, 0.30), rad=0.55, falloff=0.6, leaf=(0.8, 1.0)),
             dict(sides=4, segs=4, wander=0.14, trop=-0.20, taper=0.35, children=(1, 2), child_s=(0.40, 0.80), angle=(30, 60), ratio=(0.50, 0.75), rad=0.65, falloff=0.0, leaf=(0.9, 1.25)),
             dict(sides=3, segs=2, wander=0.20, trop=-0.30, taper=0.40, children=(0, 0), leaf=(0.8, 1.1)),
         ]),
    dict(name="Tree_Variation_03", seed=6, length=4.7, radius=0.13, start_dir=(0.38, 0.10, 1), bias=(1.0, 0.3, 0.0), hang=0.35,
         levels=[
             dict(sides=8, segs=7, wander=0.07, trop=0.05, taper=0.40, children=(4, 5), child_s=(0.50, 0.97), angle=(30, 55), ratio=(0.40, 0.55), rad=0.66, falloff=0.25, leaf=None),
             dict(sides=5, segs=4, wander=0.16, trop=0.06, taper=0.45, children=(3, 3), child_s=(0.40, 0.92), angle=(30, 60), ratio=(0.50, 0.70), rad=0.62, falloff=0.0, leaf=(1.0, 1.4)),
             dict(sides=4, segs=3, wander=0.20, trop=0.00, taper=0.40, children=(1, 2), child_s=(0.45, 0.85), angle=(30, 60), ratio=(0.50, 0.70), rad=0.60, falloff=0.0, leaf=(0.9, 1.3)),
             dict(sides=3, segs=2, wander=0.25, trop=-0.08, taper=0.40, children=(0, 0), leaf=(0.8, 1.1)),
         ]),
    dict(name="Tree_Variation_04", seed=5, length=5.0, radius=0.14, start_dir=(-0.03, 0.04, 1), bias=None, hang=0.2,
         levels=[
             dict(sides=8, segs=7, wander=0.06, trop=0.05, taper=0.22, children=(11, 12), child_s=(0.28, 0.97), angle=(40, 62), ratio=(0.34, 0.44), rad=0.60, falloff=0.35, leaf=(1.1, 1.4)),
             dict(sides=4, segs=4, wander=0.15, trop=0.18, taper=0.42, children=(2, 3), child_s=(0.35, 0.90), angle=(30, 58), ratio=(0.50, 0.72), rad=0.62, falloff=0.0, leaf=(1.1, 1.5)),
             dict(sides=3, segs=2, wander=0.22, trop=0.04, taper=0.40, children=(0, 0), leaf=(1.0, 1.35)),
         ]),
]

def _variant_of_04(name, seed, length, radius, start_dir, limbs, limb_s, limb_ratio, hang=0.2):
    """Same species as Tree_Variation_04 (same branching rules), different seed and proportions."""
    import copy
    d = copy.deepcopy(next(v for v in VARIANTS if v["name"] == "Tree_Variation_04"))
    d.update(name=name, seed=seed, length=length, radius=radius, start_dir=start_dir, hang=hang)
    d["levels"][0].update(children=limbs, child_s=limb_s, ratio=limb_ratio)
    return d

VARIANTS += [
    _variant_of_04("Tree_Variation_04_B", seed=12, length=5.7, radius=0.155, start_dir=(0.10, -0.05, 1), limbs=(12, 13), limb_s=(0.30, 0.97), limb_ratio=(0.30, 0.40)),              # taller, slight lean
    _variant_of_04("Tree_Variation_04_C", seed=27, length=4.2, radius=0.130, start_dir=(-0.05, -0.06, 1), limbs=(10, 11), limb_s=(0.24, 0.96), limb_ratio=(0.44, 0.56)),            # shorter, wider
    _variant_of_04("Tree_Variation_04_D", seed=41, length=5.1, radius=0.120, start_dir=(-0.12, 0.08, 1), limbs=(9, 10), limb_s=(0.38, 0.97), limb_ratio=(0.34, 0.46), hang=0.45),   # higher, thinner crown
]
# ============================ MATERIALS ============================
def load_img(path, non_color=False):
    img = bpy.data.images.load(path, check_existing=True)
    if non_color: img.colorspace_settings.name = 'Non-Color'
    return img

def make_bark():
    m = bpy.data.materials.get("M_Tree_Bark") or bpy.data.materials.new("M_Tree_Bark")
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (400, 0)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); b.location = (100, 0)
    d = nt.nodes.new("ShaderNodeTexImage"); d.image = load_img(BARK_DIFF); d.location = (-500, 250)
    r = nt.nodes.new("ShaderNodeTexImage"); r.image = load_img(BARK_ROUGH, True); r.location = (-500, -50)
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = load_img(BARK_NOR, True); n.location = (-500, -350)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.location = (-180, -350)
    nt.links.new(d.outputs["Color"], b.inputs["Base Color"]); nt.links.new(r.outputs["Color"], b.inputs["Roughness"])
    nt.links.new(n.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    nt.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    m.use_backface_culling = True
    return m

def make_leaves():
    m = bpy.data.materials.get("M_Tree_Leaves") or bpy.data.materials.new("M_Tree_Leaves")
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (400, 0)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); b.location = (100, 0)
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = load_img(LEAF_TEX); t.location = (-500, 100)
    t.extension = 'CLIP'
    clip = nt.nodes.new("ShaderNodeMath"); clip.operation = 'GREATER_THAN'; clip.inputs[1].default_value = ALPHA_CLIP
    clip.location = (-180, -150); clip.label = "Alpha Clip"
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"]); nt.links.new(t.outputs["Alpha"], clip.inputs[0])
    nt.links.new(clip.outputs[0], b.inputs["Alpha"]); nt.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    b.inputs["Roughness"].default_value = 0.85
    if "Specular IOR Level" in b.inputs: b.inputs["Specular IOR Level"].default_value = 0.1
    m.use_backface_culling = False                       # two-sided
    for attr, val in (("blend_method", 'CLIP'), ("alpha_threshold", ALPHA_CLIP), ("surface_render_method", 'DITHERED'),
                      ("use_transparency_overlap", False)):
        try: setattr(m, attr, val)
        except Exception: pass
    return m, t.image

def alpha_bbox(img):
    """UV rectangle of the opaque part of the foliage texture."""
    import numpy as np
    w, h = img.size
    if img.channels < 4: return (0, 0, 1, 1), False
    a = np.empty(w*h*4, dtype=np.float32); img.pixels.foreach_get(a); a = a.reshape(h, w, 4)[:, :, 3] > ALPHA_CLIP
    if a.all() or not a.any(): return (0, 0, 1, 1), bool(not a.all())
    ys = np.where(a.any(axis=1))[0]; xs = np.where(a.any(axis=0))[0]
    return (xs[0]/w, ys[0]/h, (xs[-1]+1)/w, (ys[-1]+1)/h), True

# ============================ GEOMETRY HELPERS ============================
def catmull(ctrl, n):
    P = [Vector(c) for c in ctrl]; P = [P[0]*2-P[1]] + P + [P[-1]*2-P[-2]]
    segs = len(P)-3; out = []
    for k in range(n+1):
        x = k/n*segs; i = min(int(x), segs-1); t = x-i
        p0, p1, p2, p3 = P[i], P[i+1], P[i+2], P[i+3]
        out.append(0.5*((2*p1) + (-p0+p2)*t + (2*p0-5*p1+4*p2-p3)*t*t + (-p0+3*p1-3*p2+p3)*t*t*t))
    return out

def spike(bm, uv, pts, radii, tip, sides, v0=0.0, flat_base=False):
    """Tube through pts that ends in a single sharp point `tip`. Cylindrical UVs: U once around, V = length / BARK_TILE."""
    n = len(pts); allp = pts + [tip]
    tans = [(allp[min(i+1, n)] - allp[max(i-1, 0)]).normalized() for i in range(n)]
    nrm = tans[0].orthogonal().normalized(); rings = []; vs = [v0]
    for i in range(n):
        if i:
            ax = tans[i-1].cross(tans[i])
            if ax.length > 1e-6: nrm = Matrix.Rotation(tans[i-1].angle(tans[i]), 3, ax.normalized()) @ nrm
            vs.append(vs[-1] + (pts[i]-pts[i-1]).length/BARK_TILE)
        t = Vector((0, 0, 1)) if (flat_base and i == 0) else tans[i]
        nn = (nrm - t*nrm.dot(t)).normalized(); b = t.cross(nn)
        rings.append([bm.verts.new(pts[i] + radii[i]*(math.cos(a)*nn + math.sin(a)*b))
                      for a in (2*math.pi*k/sides for k in range(sides))])
    faces = []
    for i in range(n-1):
        for k in range(sides):
            k2 = (k+1) % sides
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i+1][k2], rings[i+1][k]))
            for l, (uu, vv) in zip(f.loops, ((k/sides, vs[i]), ((k+1)/sides, vs[i]), ((k+1)/sides, vs[i+1]), (k/sides, vs[i+1]))):
                l[uv].uv = (uu, vv)
            faces.append(f)
    tv = bm.verts.new(tip); vt = vs[-1] + (tip-pts[-1]).length/BARK_TILE
    for k in range(sides):
        k2 = (k+1) % sides
        f = bm.faces.new((rings[-1][k], rings[-1][k2], tv))
        for l, (uu, vv) in zip(f.loops, ((k/sides, vs[-1]), ((k+1)/sides, vs[-1]), ((k+0.5)/sides, vt))):
            l[uv].uv = (uu, vv)
        faces.append(f)
    for f in faces: f.material_index = 0; f.smooth = True
    return faces

def leaf_clump(bm, uv, c, o, size, hang, bbox, rng):
    """Two crossed cards (2x2 quads each, dome-bent) showing the whole foliage texture.
    hang > 0: the planes turn vertical and stretch downwards (drooping foliage)."""
    up = Vector((0, 0, 1)); verts = []
    if rng.random() < hang:
        o = Vector((o.x, o.y, 0)); o = o.normalized() if o.length > 1e-3 else Vector((1, 0, 0))
        axis = (up + Vector((rng.uniform(-.25, .25), rng.uniform(-.25, .25), 0))).normalized()
        axis = (axis - o*axis.dot(o)).normalized(); stretch = 1.3; c = c - up*size*0.3
    else:
        axis = (Matrix.Rotation(rng.uniform(0, 6.28), 3, o) @ o.orthogonal()).normalized(); stretch = 1.0
    for sgn in (1, -1):
        nrm = (Matrix.Rotation(sgn*math.radians(rng.uniform(35, 55)), 3, axis) @ o).normalized()
        b = nrm.cross(axis).normalized(); s = size*rng.uniform(0.85, 1.1)
        rot = rng.randrange(4); flip = rng.random() < 0.5; N = 2; grid = []
        for j in range(N+1):
            row = []
            for i in range(N+1):
                u = i/N-0.5; v = j/N-0.5
                vert = bm.verts.new(c + s*(u*stretch*axis + v*b) - nrm*(0.13*s*4*(u*u+v*v))); verts.append(vert)
                a, bb = (-u if flip else u), v
                for _ in range(rot): a, bb = -bb, a
                row.append((vert, (bbox[0] + (bbox[2]-bbox[0])*(a+0.5), bbox[1] + (bbox[3]-bbox[1])*(bb+0.5))))
            grid.append(row)
        for j in range(N):
            for i in range(N):
                q = (grid[j][i], grid[j][i+1], grid[j+1][i+1], grid[j+1][i])
                f = bm.faces.new([x[0] for x in q]); f.material_index = 1; f.smooth = True
                for l, x in zip(f.loops, q): l[uv].uv = x[1]
    return verts

# ============================ BUILD ONE TREE ============================
def build_tree(cfg, index, bark, leaves, bbox, col):
    rng = random.Random(cfg["seed"])
    for ob in [o for o in bpy.data.objects if o.name == cfg["name"]]:
        d = ob.data; bpy.data.objects.remove(ob); bpy.data.meshes.remove(d)
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    levels = cfg["levels"]; bias = Vector(cfg["bias"]).normalized() if cfg["bias"] else None
    clumps = []; counts = [0]*len(levels)

    def grow(P, d, L, r0, level):
        lv = levels[level]; segs = lv["segs"]; pts = [P]; counts[level] += 1
        for i in range(segs):
            rv = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
            d = (d + rv*lv["wander"] + Vector((0, 0, lv["trop"]))).normalized()
            pts.append(pts[-1] + d*(L/segs))
        def rad(s): return r0*(1 - (1-lv["taper"])*s)
        radii = [rad(i/segs) for i in range(segs)]
        if level == 0: radii[0] *= 1.55; radii[1] *= 1.08          # root flare
        spike(bm, uv, pts[:-1], radii, pts[-1], lv["sides"], v0=rng.random(), flat_base=(level == 0))
        def at(s):
            x = min(0.9999, s)*segs; i = int(x)
            return pts[i].lerp(pts[i+1], x-i), (pts[i+1]-pts[i]).normalized()
        n = rng.randint(*lv["children"]) if level+1 < len(levels) else 0
        if n:
            s0, s1 = lv["child_s"]; az = rng.uniform(0, 6.28)
            for k in range(n):
                s = s0 + (s1-s0)*(k + rng.uniform(0.2, 0.8))/n
                Pc, t = at(s); az += 2.4 + rng.uniform(-0.5, 0.5)
                perp = (Matrix.Rotation(az, 3, t) @ t.orthogonal()).normalized()
                ang = math.radians(rng.uniform(*lv["angle"]))
                cd = (t*math.cos(ang) + perp*math.sin(ang)).normalized()
                if bias is not None: cd = (cd + bias*0.45).normalized()
                cl = L*rng.uniform(*lv["ratio"])*(1 - lv["falloff"]*(s-s0)/(s1-s0))
                grow(Pc, cd, cl, rad(s)*lv["rad"], level+1)
        if lv["leaf"]:
            clumps.append((pts[-1], rng.uniform(*lv["leaf"])))
            if n == 0: clumps.append((at(0.5)[0], rng.uniform(*lv["leaf"])*0.9))

    grow(Vector((0, 0, 0)), Vector(cfg["start_dir"]).normalized(), cfg["length"], cfg["radius"], 0)
    for v in bm.verts:
        if abs(v.co.z) < 1e-4: v.co.z = 0.0                     # base ring exactly on z = 0
    trunk_tris = sum(len(f.verts)-2 for f in bm.faces)

    # ---- crown: clumps at the branch ends, facing away from the crown centre ----
    cc = sum((c for c, _ in clumps), Vector())/len(clumps); leaf_verts = []
    for c, size in clumps:
        o = (c - cc); o = o.normalized() if o.length > 0.05 else Vector((0, 0, 1))
        o = (o + Vector((0, 0, 0.3))).normalized()
        leaf_verts += leaf_clump(bm, uv, c, o, size, cfg["hang"], bbox, rng)
    for v in leaf_verts: v.co.z = max(v.co.z, 0.35)
    bm.verts.index_update(); leaf_idx = [v.index for v in leaf_verts]
    leaf_tris = sum(len(f.verts)-2 for f in bm.faces) - trunk_tris

    me = bpy.data.meshes.new(cfg["name"]); bm.to_mesh(me); bm.free()
    me.materials.append(bark); me.materials.append(leaves)
    ob = bpy.data.objects.new(cfg["name"], me); col.objects.link(ob)     # origin = (0,0,0) = trunk base
    vg = ob.vertex_groups.new(name="Leaves"); vg.add(leaf_idx, 1.0, 'REPLACE')

    # ---- spherical normals: Data Transfer from a temporary smooth ellipsoid fitted to the crown ----
    lc = [me.vertices[i].co for i in leaf_idx]
    mn = Vector((min(c.x for c in lc), min(c.y for c in lc), min(c.z for c in lc)))
    mx = Vector((max(c.x for c in lc), max(c.y for c in lc), max(c.z for c in lc)))
    C = (mn+mx)/2; R = (mx-mn)/2
    sb = bmesh.new(); bmesh.ops.create_uvsphere(sb, u_segments=24, v_segments=12, radius=1.0)
    for v in sb.verts: v.co = C + Vector((v.co.x*R.x, v.co.y*R.y, v.co.z*R.z))
    for f in sb.faces: f.smooth = True
    sme = bpy.data.meshes.new("TMP_NormalSource"); sb.to_mesh(sme); sb.free()
    sph = bpy.data.objects.new("TMP_NormalSource", sme); col.objects.link(sph)
    sph.display_type = 'WIRE'; sph.hide_render = True
    dt = ob.modifiers.new("SphericalNormals", 'DATA_TRANSFER')
    dt.object = sph; dt.use_object_transform = True
    dt.use_loop_data = True; dt.data_types_loops = {'CUSTOM_NORMAL'}; dt.loop_mapping = 'POLYINTERP_NEAREST'
    dt.vertex_group = "Leaves"                                   # wood keeps its own smooth normals
    bpy.context.view_layer.update()
    for o2 in bpy.context.view_layer.objects: o2.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=dt.name)
    bpy.data.objects.remove(sph); bpy.data.meshes.remove(sme)

    # ---- checks ----
    me = ob.data; leaf_set = set(leaf_idx); out_dots = []; tdots = []
    for l in me.loops:
        nv = me.corner_normals[l.index].vector
        if l.vertex_index in leaf_set:
            p = me.vertices[l.vertex_index].co - C
            if p.length > 0.3: out_dots.append(nv.dot(Vector((p.x/R.x**2, p.y/R.y**2, p.z/R.z**2)).normalized()))
        else:
            tdots.append(nv.dot(me.vertices[l.vertex_index].normal))
    zs = [v.co.z for v in me.vertices]
    base = [v.co for v in me.vertices if abs(v.co.z) < 1e-5]
    ob.location = ((index - (len(VARIANTS)-1)/2)*SPACING, 0, 0)
    return ob, dict(name=cfg["name"], trunk_tris=trunk_tris, trunk_tris_in_budget=TRUNK_TRIS[0] <= trunk_tris <= TRUNK_TRIS[1],
                    branches_per_level=counts, clumps=len(clumps), leaf_tris=leaf_tris, total_tris=trunk_tris+leaf_tris,
                    height=round(max(zs), 2), min_z=round(min(zs), 4),
                    base_center=[round(sum(c[i] for c in base)/len(base), 3) for i in (0, 1)],
                    crown_width=[round(R.x*2, 2), round(R.y*2, 2)], crown_bottom=round(mn.z, 2),
                    leaf_normal_vs_ellipsoid_avg=round(sum(out_dots)/len(out_dots), 3),
                    trunk_normal_kept_avg=round(sum(tdots)/len(tdots), 3),
                    materials=[m.name for m in me.materials])

# ============================ MAIN ============================
def main():
    bark = make_bark(); leaves, leaf_img = make_leaves()
    bbox, has_alpha = alpha_bbox(leaf_img)
    col = bpy.data.collections.get("Trees") or bpy.data.collections.new("Trees")
    if col.name not in bpy.context.scene.collection.children: bpy.context.scene.collection.children.link(col)
    report = {"leaf_texture_has_alpha": has_alpha, "trees": [], "fbx": []}
    objs = []
    for i, cfg in enumerate(VARIANTS):
        ob, st = build_tree(cfg, i, bark, leaves, bbox, col); objs.append(ob); report["trees"].append(st)
    if EXPORT_DIR:
        os.makedirs(EXPORT_DIR, exist_ok=True)
        for ob in objs:
            keep = ob.location.copy(); ob.location = (0, 0, 0)          # pivot at the trunk base, world origin
            for o2 in bpy.context.view_layer.objects: o2.select_set(False)
            ob.select_set(True); bpy.context.view_layer.objects.active = ob
            path = os.path.join(EXPORT_DIR, ob.name + ".fbx")
            bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                                     apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                                     bake_space_transform=True, mesh_smooth_type='OFF', use_tspace=True, use_triangles=True,
                                     path_mode='RELATIVE', embed_textures=False, bake_anim=False)
            ob.location = keep; report["fbx"].append(path)
    return report

result = main()
print("TREES_REPORT", result)
