"""
Low-poly game landscape generator for Blender (tested on 5.2 LTS).

Pipeline
  1. heightfield = tilted ground plane (continuous incline) + chaotic massif (domain-warped noise,
     several asymmetric peaks, ridged crags, ledges, clefts), built as a CLOSED solid box
  2. through tunnel (tall vault, follows the incline) = Boolean DIFFERENCE (Exact)
  3. Voxel Remesh  -> uniform polygon size everywhere, no stretched faces on cliffs / tunnel walls
  4. clip to the map rectangle, flat shading, planar UVs for the grass texture, gray rock material

Run:  exec(open(r"<path>/landscape_lowpoly.py").read())   (Scripting tab or MCP)
If an object called "Plane" exists it is used as the map footprint / ground level / grass material.
"""
import bpy, bmesh, math, random, statistics, collections
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

# ============================ PARAMETERS ============================
P = dict(
    MOUNT_CENTER=(-40.0, 20.0),   # mountain centre (XY)
    MOUNT_RADII=(190.0, 195.0),   # footprint half-axes (mockup cone was ~125 x 137)
    MOUNT_HEIGHT=150.0,           # height above local ground (mockup: ~104)
    MOUNT_ROT_DEG=25.0,           # footprint rotation (breaks symmetry)
    WARP=(55.0, 18.0),            # domain-warp amplitude: large / small scale
    CRAG_AMP=0.45,                # ridged-noise amplitude (fraction of height)
    LEDGE_STEP=16.0, LEDGE_MIX=0.40,
    CLEFT_DEPTH=18.0,
    INCLINE=0.085,                # tan(slope) of the whole map: ~5 deg, from lowest corner up to the mountain
    TUNNEL_HALF_WIDTH=11.0, TUNNEL_HEIGHT=28.0,  # high vault (player model is 3.6 m tall)
    TUNNEL_LENGTH_HALF=230.0, TUNNEL_SWAY=40.0,
    VOXEL=6.0,                  # Voxel Remesh size = edge length of the final mesh (metres)
    HF_CELL=4.0,                  # heightfield sample spacing for the pre-remesh solid
    GRASS_TILE=8.0,               # metres per grass texture tile
    SEED=11,
)

# ============================ SCENE INPUT ============================
sc = bpy.context.scene
plane = bpy.data.objects.get("Plane")
if plane:
    ws = [plane.matrix_world @ Vector(c) for c in plane.bound_box]
    XMIN, XMAX = min(w.x for w in ws), max(w.x for w in ws)
    YMIN, YMAX = min(w.y for w in ws), max(w.y for w in ws)
    ZG = ws[0].z
    grass_mat = plane.material_slots[0].material if plane.material_slots else None
else:
    XMIN, XMAX, YMIN, YMAX, ZG, grass_mat = -250.0, 250.0, -230.0, 230.0, 0.0, None
if grass_mat is None:
    grass_mat = bpy.data.materials.new("Grass_Fallback"); grass_mat.diffuse_color = (0.25, 0.4, 0.12, 1)
rock_mat = bpy.data.materials.get("Rock_Gray")
if rock_mat is None:
    rock_mat = bpy.data.materials.new("Rock_Gray"); rock_mat.use_nodes = True
    b = rock_mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.33, 0.33, 0.35, 1); b.inputs["Roughness"].default_value = 0.9
    rock_mat.diffuse_color = (0.33, 0.33, 0.35, 1)

for n in ("Landscape_LowPoly", "LP_TunnelCutter"):
    o = bpy.data.objects.get(n)
    if o:
        d = o.data; bpy.data.objects.remove(o); bpy.data.meshes.remove(d)

# ============================ HEIGHT FUNCTIONS ============================
CX, CY = P["MOUNT_CENTER"]; RX, RY = P["MOUNT_RADII"]; HM = P["MOUNT_HEIGHT"]
ROT = math.radians(P["MOUNT_ROT_DEG"]); cR, sR = math.cos(ROT), math.sin(ROT)
PLOW = Vector((XMAX, YMIN))                                  # lowest corner
DIRV = (Vector((CX, CY)) - PLOW).normalized()                # incline direction: lowest corner -> mountain

def sstep(a, b, x):
    t = min(1.0, max(0.0, (x-a)/(b-a))); return t*t*(3-2*t)
def nz(x, y, z): return noise.noise(Vector((x, y, z)))

def ground_base(x, y):                                       # continuous incline + very soft undulation
    s = (x-PLOW.x)*DIRV.x + (y-PLOW.y)*DIRV.y
    return ZG + P["INCLINE"]*s + 3.0*nz(x/140, y/140, 0.3)

def ridged(x, y, f, octs=5):
    a = 1.0; s = 0.0; tot = 0.0
    for i in range(octs):
        n = 1 - abs(nz(x*f, y*f, i*3.7+1)); n *= n
        s += n*a; tot += a; a *= 0.5; f *= 2.05
    return s/tot

PEAKS = [(CX+30, CY+40, 1.00, 95), (CX-70, CY-20, 0.78, 75), (CX+60, CY-70, 0.62, 68),
         (CX-20, CY+95, 0.70, 72), (CX-110, CY+60, 0.45, 60)]   # x, y, relative height, sigma

def mountain(x, y):
    dx, dy = x-CX, y-CY
    w1, w2 = P["WARP"]
    wx = w1*nz(x/130, y/130, 11.0) + w2*nz(x/45, y/45, 5.0)
    wy = w1*nz(x/130, y/130, 23.0) + w2*nz(x/45, y/45, 9.0)
    u = (dx+wx)*cR + (dy+wy)*sR; v = -(dx+wx)*sR + (dy+wy)*cR
    e = 1.0 - math.hypot(u/RX, v/RY)                          # irregular footprint
    if e <= 0: return 0.0
    env = sstep(0.0, 0.8, e)
    pk = max(ph*math.exp(-((x-px)**2 + (y-py)**2)/(2*sg*sg)) for (px, py, ph, sg) in PEAKS)
    h = HM*(0.30*env + 0.75*pk*env**0.6)                      # massif + asymmetric summits
    h += P["CRAG_AMP"]*HM*(ridged(x, y, 1/70.0) - 0.5)*env**0.8   # rocky ridges
    h += 0.10*HM*(ridged(x, y, 1/28.0, 4) - 0.5)*env**0.8     # small-scale rock breakup
    st = P["LEDGE_STEP"]; t = h/st; fr = t - math.floor(t)    # ledges / shelves
    h += ((math.floor(t) + sstep(0.55, 1.0, fr))*st - h)*P["LEDGE_MIX"]
    cl = 1 - sstep(0.0, 0.05, abs(nz(u/55, v/90, 4.4)))       # fissures
    h -= P["CLEFT_DEPTH"]*cl*sstep(0.15, 0.5, e)
    return max(0.0, h)

def height(x, y):
    m = mountain(x, y)
    g = ground_base(x, y) + 0.6*nz(x/30, y/30, 9.9)*(1 - min(1.0, m/5))
    return g + m, m

# ============================ 1. CLOSED HEIGHTFIELD SOLID ============================
random.seed(P["SEED"])
CELL = P["HF_CELL"]; MARGIN = 15.0                            # margin: edge is clipped after remesh
x0, x1, y0, y1 = XMIN-MARGIN, XMAX+MARGIN, YMIN-MARGIN, YMAX+MARGIN
nx = round((x1-x0)/CELL); ny = round((y1-y0)/CELL); sx = (x1-x0)/nx; sy = (y1-y0)/ny
ZB = ZG - 30.0
bm = bmesh.new(); grid = []
for j in range(ny+1):
    row = []
    for i in range(nx+1):
        x = x0 + i*sx; y = y0 + j*sy
        if 0 < i < nx: x += random.uniform(-0.3, 0.3)*sx
        if 0 < j < ny: y += random.uniform(-0.3, 0.3)*sy
        row.append(bm.verts.new((x, y, height(x, y)[0])))
    grid.append(row)
for j in range(ny):
    for i in range(nx):
        a, b, c, d = grid[j][i], grid[j][i+1], grid[j+1][i+1], grid[j+1][i]
        for vs in (((a, b, c), (a, c, d)) if random.random() < 0.5 else ((a, b, d), (b, c, d))):
            bm.faces.new(vs)
border = [grid[0][i] for i in range(nx)] + [grid[j][nx] for j in range(ny)] + \
         [grid[ny][i] for i in range(nx, 0, -1)] + [grid[j][0] for j in range(ny, 0, -1)]
low = [bm.verts.new((v.co.x, v.co.y, ZB)) for v in border]; nb = len(border)
for i in range(nb): bm.faces.new((border[(i+1) % nb], border[i], low[i], low[(i+1) % nb]))
bm.faces.new(low)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
me = bpy.data.meshes.new("Landscape_LowPoly"); bm.to_mesh(me); bm.free()
col = bpy.data.collections.get("Landscape_LowPoly") or bpy.data.collections.new("Landscape_LowPoly")
if col.name not in sc.collection.children: sc.collection.children.link(col)
terrain = bpy.data.objects.new("Landscape_LowPoly", me); col.objects.link(terrain)

# ============================ 2. THROUGH TUNNEL ============================
C = Vector((CX, CY)); perp = Vector((-DIRV.y, DIRV.x)); L = P["TUNNEL_LENGTH_HALF"]; SW = P["TUNNEL_SWAY"]
P0 = C - DIRV*L + perp*30; P3 = C + DIRV*L - perp*10
P1 = P0 + (P3-P0)*0.33 + perp*SW; P2 = P0 + (P3-P0)*0.66 - perp*SW
def bez(t):
    s = 1-t
    return P0*s*s*s + P1*3*s*s*t + P2*3*s*t*t + P3*t*t*t
def floor_z(t):                                               # floor follows the incline -> walkable
    p = bez(t); return ground_base(p.x, p.y) + 0.3

random.seed(5)
W, H = P["TUNNEL_HALF_WIDTH"], P["TUNNEL_HEIGHT"]; NR = 70
prof = [(-0.8, 0), (0.8, 0), (1, .3), (.85, .65), (.45, .92), (0, 1), (-.45, .92), (-.85, .65), (-1, .3)]
K = len(prof); bm = bmesh.new(); rings = []
for i in range(NR+1):
    t = i/NR; p = bez(t); tan = (bez(min(1, t+0.01)) - bez(max(0, t-0.01))).normalized()
    right = Vector((tan.y, -tan.x)); k = 1.0 + 0.25*nz(i*0.37, 2.2, 5.5)
    w = W*k; h = H*k; zf = floor_z(t); ring = []
    for q, (s, z) in enumerate(prof):
        if q < 2: s *= random.uniform(0.95, 1.1); zz = random.uniform(-0.2, 0.2)
        else:     s *= random.uniform(0.88, 1.12); zz = z*h*random.uniform(0.92, 1.08)
        ring.append(bm.verts.new((p.x + right.x*s*w, p.y + right.y*s*w, zf + zz)))
    rings.append(ring)
for i in range(NR):
    for q in range(K):
        bm.faces.new((rings[i][q], rings[i][(q+1) % K], rings[i+1][(q+1) % K], rings[i+1][q]))
bm.faces.new(rings[0]); bm.faces.new(rings[-1])
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
cme = bpy.data.meshes.new("LP_TunnelCutter"); bm.to_mesh(cme); bm.free()
cutter = bpy.data.objects.new("LP_TunnelCutter", cme); col.objects.link(cutter)

mod = terrain.modifiers.new("TunnelCut", 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.object = cutter; mod.solver = 'EXACT'
bpy.context.view_layer.update()
new_me = bpy.data.meshes.new_from_object(terrain.evaluated_get(bpy.context.evaluated_depsgraph_get()))
terrain.modifiers.remove(mod)
old = terrain.data; terrain.data = new_me; bpy.data.meshes.remove(old); new_me.name = "Landscape_LowPoly"; me = new_me
cbvh = BVHTree.FromPolygons([v.co for v in cme.vertices], [p.vertices for p in cme.polygons])
bm = bmesh.new(); bm.from_mesh(me)
for f in bm.faces:                                            # material id BEFORE remesh: 1 = rock (mountain + cave), 0 = grass
    c = f.calc_center_median(); near = cbvh.find_nearest(c)
    f.material_index = 1 if ((near[0] is not None and near[3] < 0.2) or mountain(c.x, c.y) > 1.5) else 0
bm.to_mesh(me); bm.free()
bpy.data.objects.remove(cutter); bpy.data.meshes.remove(cme)
me.update()
old_bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [p.vertices[:] for p in me.polygons])
old_mat = [p.material_index for p in me.polygons]

# ============================ 3. VOXEL REMESH (uniform density) ============================
for ob in bpy.context.view_layer.objects: ob.select_set(False)
terrain.hide_set(False); terrain.select_set(True); bpy.context.view_layer.objects.active = terrain
me.remesh_voxel_size = P["VOXEL"]; me.remesh_voxel_adaptivity = 0.0; me.use_remesh_fix_poles = True
bpy.ops.object.voxel_remesh()

# ============================ 4. CLIP, MATERIALS, UV ============================
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if not (XMIN <= f.calc_center_median().x <= XMAX and YMIN <= f.calc_center_median().y <= YMAX)
                           or f.calc_center_median().z < ZG-16], context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
for v in bm.verts:
    v.co.x = min(max(v.co.x, XMIN), XMAX); v.co.y = min(max(v.co.y, YMIN), YMAX)
uv = bm.loops.layers.uv.verify(); TILE = P["GRASS_TILE"]
me.materials.clear(); me.materials.append(grass_mat); me.materials.append(rock_mat)
for f in bm.faces:
    hit = old_bvh.find_nearest(f.calc_center_median())
    f.material_index = old_mat[hit[2]] if hit[2] is not None else 0
    f.smooth = False
    for l in f.loops: l[uv].uv = (l.vert.co.x/TILE, l.vert.co.y/TILE)
bm.to_mesh(me); bm.free(); me.update()
for p in me.polygons: p.use_smooth = False
for n in ("Plane", "mount"):
    o = bpy.data.objects.get(n)
    if o: o.hide_set(True); o.hide_render = True

# ============================ CHECKS ============================
bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [p.vertices for p in me.polygons])
blocked = 0; clear = []; N_ = 400
for i in range(N_):
    a = Vector((*bez(i/N_), floor_z(i/N_)+2.5)); b = Vector((*bez((i+1)/N_), floor_z((i+1)/N_)+2.5)); d = b-a
    if bvh.ray_cast(a, d.normalized(), d.length)[0] is not None: blocked += 1
    dn = bvh.ray_cast(a, Vector((0, 0, -1)), 40); up = bvh.ray_cast(a, Vector((0, 0, 1)), 400)
    if dn[0] is not None and up[0] is not None: clear.append(up[0].z-dn[0].z)
G = 4.0; gx = int((XMAX-XMIN-1)/G); gy = int((YMAX-YMIN-1)/G)
Z = [[(lambda h: h[0].z if h[0] is not None else None)(bvh.ray_cast(Vector((XMIN+0.5+i*G, YMIN+0.5+j*G, 900)), Vector((0, 0, -1)), 2000))
      for i in range(gx+1)] for j in range(gy+1)]
tl = math.tan(math.radians(50))
start = next((i, j) for j in range(gy+1) for i in range(gx, -1, -1) if Z[j][i] is not None)   # walkable cell nearest the lowest corner
seen = {start}; dq = collections.deque([start])
while dq:
    i, j = dq.popleft()
    for a, b in ((i+1, j), (i-1, j), (i, j+1), (i, j-1)):
        if 0 <= a <= gx and 0 <= b <= gy and (a, b) not in seen and Z[b][a] is not None and Z[j][i] is not None and abs(Z[b][a]-Z[j][i])/G <= tl:
            seen.add((a, b)); dq.append((a, b))
summit = max(z for r in Z for z in r if z is not None); reach = max(Z[b][a] for a, b in seen)
el = sorted((me.vertices[e.vertices[0]].co - me.vertices[e.vertices[1]].co).length for e in me.edges)
ar = sorted(p.area for p in me.polygons)
ups = [p for p in me.polygons if p.normal.z > 0]
result = {
    "faces": len(me.polygons), "verts": len(me.vertices), "dims": [round(d, 1) for d in terrain.dimensions],
    "edge_len_p5_median_p95": [round(el[int(len(el)*.05)], 2), round(statistics.median(el), 2), round(el[int(len(el)*.95)], 2)],
    "face_area_median_p95_max": [round(statistics.median(ar), 1), round(ar[int(len(ar)*.95)], 1), round(ar[-1], 1)],
    "tunnel_through_blocked_segments": blocked, "tunnel_min_floor_to_ceiling": round(min(clear), 1) if clear else None,
    "summit_z": round(summit, 1), "max_z_reachable_by_walking_50deg": round(reach, 1),
    "faces_steeper_50deg_pct": round(100*sum(1 for p in ups if p.normal.z < math.cos(math.radians(50)))/len(ups), 1),
    "lowest_corner_z": round(min(z for r in Z[:3] for z in r[-3:] if z is not None), 1),
    "far_corner_z": round(max(z for r in Z[-3:] for z in r[:3] if z is not None), 1),
}
