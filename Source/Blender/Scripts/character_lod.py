# Blender (background): a far LOD of a skinned character for the enemy crowds.
#   blender -b --factory-startup --python character_lod.py -- <in.fbx> <out.fbx> [target_tris] [skip,parts,...]
# The character's meshes (minus small parts named in skip: chains, bracelets...) are joined into one mesh on the same armature,
# its base colour textures are sampled into vertex colours (linear, as Unity's linear colour space wants them), and it is
# decimated to about target_tris triangles (vertex groups and colours carry over). One material ("LOD1"), same bone names and
# rest pose as the source: Unity binds it to the full model's skeleton. Units and axes as the source export (Source units,
# -Z forward, Y up).
import bpy, sys, os, math

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
target = int(args[2]) if len(args) > 2 else 3000
skip = [s.strip().lower() for s in args[3].split(",")] if len(args) > 3 and args[3] else []

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
keep = [o for o in meshes if not any(k in o.name.lower() for k in skip)]
for o in meshes:
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
print("LOD parts", [o.name for o in keep])
for o in keep:                                                                 # no faces to pull at that distance: shape keys go
    if o.data.shape_keys:
        o.shape_key_clear()


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def material_source(mat):
    """The base colour image of a material (and its pixels), or its flat base colour."""
    if mat is None or not mat.use_nodes:
        return None, (0.6, 0.6, 0.6)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return None, (0.6, 0.6, 0.6)
    inp = bsdf.inputs["Base Color"]
    if inp.is_linked:
        node = inp.links[0].from_node
        while node is not None and node.type != "TEX_IMAGE":                 # through a mix / colour node to the image
            nxt = next((i.links[0].from_node for i in node.inputs if i.is_linked), None)
            node = nxt
        if node is not None and node.image is not None and node.image.size[0] > 0:
            img = node.image
            return (img, list(img.pixels), img.size[0], img.size[1], img.colorspace_settings.name), None
    c = inp.default_value
    return None, (c[0], c[1], c[2])                                           # already linear in Blender


images = {}
for o in keep:
    me = o.data
    uv = me.uv_layers.active
    col = me.color_attributes.new("LODColor", "FLOAT_COLOR", "CORNER")
    for poly in me.polygons:
        mat = o.material_slots[poly.material_index].material if poly.material_index < len(o.material_slots) else None
        key = mat.name if mat else ""
        if key not in images:
            images[key] = material_source(mat)
        tex, flat = images[key]
        for li in poly.loop_indices:
            if tex is not None and uv is not None:
                img, px, w, h, space = tex
                u, v = uv.data[li].uv
                x = int((u % 1.0) * w) % w; y = int((v % 1.0) * h) % h
                k = (y * w + x) * 4
                r, g, b = px[k], px[k + 1], px[k + 2]
                if space == "sRGB":
                    r, g, b = srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b)
                col.data[li].color = (r, g, b, 1.0)
            else:
                col.data[li].color = (flat[0], flat[1], flat[2], 1.0)
    me.color_attributes.active_color = col

# one mesh on the armature
bpy.ops.object.select_all(action="DESELECT")
for o in keep:
    o.select_set(True)
bpy.context.view_layer.objects.active = keep[0]
if len(keep) > 1:
    bpy.ops.object.join()
lod = bpy.context.view_layer.objects.active
lod.name = "LOD1"
lod.data.name = "LOD1"
tris = sum(len(p.vertices) - 2 for p in lod.data.polygons)
ratio = min(1.0, target / max(1, tris))
mod = lod.modifiers.new("Decimate", "DECIMATE")
mod.decimate_type = "COLLAPSE"; mod.ratio = ratio; mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier="Decimate")
lod.data.materials.clear()
lod.data.materials.append(bpy.data.materials.new("LOD1"))
# keep only the colour attribute and the first UV map
for a in [a for a in lod.data.color_attributes if a.name != "LODColor"]:
    lod.data.color_attributes.remove(a)
after = sum(len(p.vertices) - 2 for p in lod.data.polygons)
print("LOD tris", tris, "->", after, "verts", len(lod.data.vertices), "groups", len(lod.vertex_groups))

bpy.ops.object.select_all(action="DESELECT")
lod.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False, colors_type="LINEAR",
    apply_scale_options="FBX_SCALE_NONE", axis_forward="-Z", axis_up="Y", path_mode="STRIP")
print("LOD written", dst, os.path.getsize(dst))
