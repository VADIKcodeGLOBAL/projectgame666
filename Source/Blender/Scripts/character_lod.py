# Blender (background): lighter LOD meshes of a skinned character for the enemy crowds.
#   blender -b --factory-startup --python character_lod.py -- <in.fbx> <out.fbx> [target_tris] [skip,parts,...]
#   blender -b --factory-startup --python character_lod.py -- --jobs <jobs.json>
# jobs.json: [{"src": "<in.fbx>", "outs": [["<out_LOD1.fbx>", 10000], ["<out_LOD2.fbx>", 3000]], "skip": ["chain", ...]}, ...]
# (one import per source, every level decimated from it; a failed job is reported and the next one goes on).
# The character's meshes (minus small parts named in skip: chains, bracelets...) are joined into one mesh on the same armature,
# its base colour textures are sampled into vertex colours (linear, as Unity's linear colour space wants them), and it is
# decimated to about target_tris triangles (vertex groups and colours carry over). One material ("LOD1"), same bone names and
# rest pose as the source: Unity binds it to the full model's skeleton. Units and axes as the source export (Source units,
# -Z forward, Y up).
import bpy, bmesh, sys, os, json, time, traceback
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def material_source(mat):
    """The base colour image of a material as an (h, w, 3) linear array, or its flat base colour."""
    if mat is None or not mat.use_nodes:
        return None, (0.6, 0.6, 0.6)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return None, (0.6, 0.6, 0.6)
    inp = bsdf.inputs["Base Color"]
    if inp.is_linked:
        node = inp.links[0].from_node
        while node is not None and node.type != "TEX_IMAGE":                 # through a mix / colour node to the image
            node = next((i.links[0].from_node for i in node.inputs if i.is_linked), None)
        if node is not None and node.image is not None and node.image.size[0] > 0:
            img = node.image
            w, h = img.size
            px = np.empty(w * h * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            rgb = px.reshape(h, w, 4)[:, :, :3]
            if img.colorspace_settings.name == "sRGB":
                rgb = srgb_to_linear(rgb)
            return rgb, None
    c = inp.default_value
    return None, (c[0], c[1], c[2])                                           # already linear in Blender


def colour_mesh(o, images):
    """Each face corner gets the colour of its material's base texture under its UV (or the material's flat colour)."""
    me = o.data
    n = len(me.loops)
    col = me.color_attributes.new("LODColor", "FLOAT_COLOR", "CORNER")
    out = np.ones((n, 4), dtype=np.float32)
    mat_index = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get("material_index", mat_index)
    totals = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get("loop_total", totals)
    loop_mat = np.repeat(mat_index, totals)                                  # polygons' loops are consecutive, in order
    uv_layer = me.uv_layers.active
    uv = None
    if uv_layer is not None:
        uv = np.empty(n * 2, dtype=np.float32); uv_layer.data.foreach_get("uv", uv); uv = uv.reshape(n, 2)
    for mi in np.unique(loop_mat):
        mat = o.material_slots[mi].material if mi < len(o.material_slots) else None
        key = mat.name if mat else ""
        if key not in images:
            images[key] = material_source(mat)
        tex, flat = images[key]
        sel = loop_mat == mi
        if tex is not None and uv is not None:
            h, w = tex.shape[0], tex.shape[1]
            x = (np.mod(uv[sel, 0], 1.0) * w).astype(np.int64) % w
            y = (np.mod(uv[sel, 1], 1.0) * h).astype(np.int64) % h
            out[sel, :3] = tex[y, x]
        else:
            out[sel, :3] = flat
    col.data.foreach_set("color", out.ravel())
    me.color_attributes.active_color = col


def run(src, outs, skip):
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    keep = [o for o in meshes if not any(k in o.name.lower() for k in skip)]
    for o in meshes:
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in keep:                                                           # no faces to pull at that distance: shape keys go
        if o.data.shape_keys:
            o.shape_key_clear()
    images = {}
    for o in keep:
        colour_mesh(o, images)

    # one mesh on the armature
    bpy.ops.object.select_all(action="DESELECT")
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    if len(keep) > 1:
        bpy.ops.object.join()
    full = bpy.context.view_layer.objects.active
    for a in [a for a in full.data.color_attributes if a.name != "LODColor"]:   # only the colour attribute
        full.data.color_attributes.remove(a)
    # Source meshes come split at every UV / smoothing seam: the loose pieces would stop the collapse long before the target
    bm = bmesh.new(); bm.from_mesh(full.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    merged = before - len(bm.verts)
    bm.to_mesh(full.data); bm.free()
    tris = sum(len(p.vertices) - 2 for p in full.data.polygons)
    full.name = "FULL"; full.data.name = "FULL"                              # the exported mesh is the one named "LOD1"

    for dst, target in sorted(outs, key=lambda o: -o[1]):                   # every level from the full mesh
        lod = full.copy(); lod.data = full.data.copy()
        bpy.context.scene.collection.objects.link(lod)
        lod.name = "LOD1"; lod.data.name = "LOD1"
        bpy.ops.object.select_all(action="DESELECT")
        lod.select_set(True); bpy.context.view_layer.objects.active = lod
        now = tris
        for attempt in range(4):                                             # one collapse can stop short of the target: again
            mod = lod.modifiers.new("Decimate", "DECIMATE")
            mod.decimate_type = "COLLAPSE"; mod.ratio = min(1.0, target / max(1, now)); mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier="Decimate")
            was, now = now, sum(len(p.vertices) - 2 for p in lod.data.polygons)
            if now <= target * 1.1 or now > was * 0.95:
                break
        lod.data.materials.clear()
        mat = bpy.data.materials.new("LOD1")
        lod.data.materials.append(mat)
        after = sum(len(p.vertices) - 2 for p in lod.data.polygons)
        bpy.ops.object.select_all(action="DESELECT")
        lod.select_set(True); arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        bpy.ops.export_scene.fbx(
            filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=False,
            mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False, colors_type="LINEAR",
            apply_scale_options="FBX_SCALE_NONE", axis_forward="-Z", axis_up="Y", path_mode="STRIP")
        print("LOD", os.path.basename(dst), tris, "->", after, "tris,", len(lod.data.vertices), "verts,", len(lod.vertex_groups), "groups", flush=True)
        data = lod.data                                                      # gone, so the next level gets the names again
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(data); bpy.data.materials.remove(mat)
    print("LOD_DONE", os.path.basename(src), "(%d doubles merged)" % merged, "%.1f s" % (time.time() - t0), flush=True)


if args and args[0] == "--jobs":
    with open(args[1], encoding="utf-8-sig") as f:
        jobs = json.load(f)
    failed = 0
    for j in jobs:
        try:
            run(j["src"], j["outs"], [s.lower() for s in j.get("skip", [])])
        except Exception:
            failed += 1
            print("LOD_FAILED", j["src"], flush=True)
            traceback.print_exc()
    print("LOD_BATCH_END", len(jobs) - failed, "ok,", failed, "failed", flush=True)
else:
    src, dst = args[0], args[1]
    target = int(args[2]) if len(args) > 2 else 3000
    skip = [s.strip().lower() for s in args[3].split(",")] if len(args) > 3 and args[3] else []
    run(src, [[dst, target]], skip)
