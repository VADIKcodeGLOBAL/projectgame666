"""
Convert a static .glb character/prop into Unity-ready files (Blender 5.2 LTS, background):

  <out>/Models/<Name>.fbx                one mesh, one sub-mesh per material, origin on the ground under the model
  <out>/Textures/T_<Name>_<Mat>_*.png    BaseColor (alpha kept), Normal, Emission, MetallicSmoothness
                                         (glTF packs roughness in G / metallic in B; Unity's Standard wants metallic in RGB, smoothness in A)
  <out>/<Name>_materials.json            material table read by Assets/Scripts/Editor/ModelMaterialSetup.cs

  blender -b --factory-startup --python glb_to_unity.py -- <file.glb> <out dir> <Name>
"""
import bpy, os, sys, json, re
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
SRC, OUT, NAME = argv[0], argv[1], argv[2]
TEX = os.path.join(OUT, "Textures"); MOD = os.path.join(OUT, "Models")
os.makedirs(TEX, exist_ok=True); os.makedirs(MOD, exist_ok=True)

for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=SRC)
bpy.context.view_layer.update()
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']

# ---- one object, transforms applied, origin on the ground under the lowest part of the model
for o in bpy.context.view_layer.objects: o.select_set(False)
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active; ob.name = NAME; ob.data.name = NAME
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
co = np.empty(len(ob.data.vertices)*3, dtype=np.float32); ob.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
zmin = float(co[:, 2].min()); foot = co[co[:, 2] < zmin + 0.04]
off = Vector((float(foot[:, 0].mean()), float(foot[:, 1].mean()), zmin))
ob.data.transform(Matrix.Translation(-off)); ob.data.update()
for o in [o for o in bpy.context.scene.objects if o.type == 'EMPTY']: bpy.data.objects.remove(o)

def clean(s): return re.sub(r"[^A-Za-z0-9]+", "_", re.sub(r"\.\d+$", "", s)).strip("_")

saved = {}
def save_image(img, fname, convert=None):
    key = (img.name, convert)
    if key in saved: return saved[key]
    path = os.path.join(TEX, fname)
    if convert == "metallic_smoothness":
        w, h = img.size; px = np.empty(w*h*4, dtype=np.float32); img.pixels.foreach_get(px); px = px.reshape(-1, 4)
        out = np.empty_like(px); out[:, 0] = out[:, 1] = out[:, 2] = px[:, 2]; out[:, 3] = 1.0 - px[:, 1]
        n = bpy.data.images.new(fname, w, h, alpha=True); n.alpha_mode = 'CHANNEL_PACKED'; n.colorspace_settings.name = 'Non-Color'
        n.pixels.foreach_set(out.ravel()); n.filepath_raw = path; n.file_format = 'PNG'; n.save(); bpy.data.images.remove(n)
    else:
        img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    saved[key] = fname
    return fname

def alpha_stats(img):
    w, h = img.size; px = np.empty(w*h*4, dtype=np.float32); img.pixels.foreach_get(px); a = px.reshape(-1, 4)[:, 3]
    return dict(min=round(float(a.min()), 3), mean=round(float(a.mean()), 3), soft=round(float(((a > 0.1) & (a < 0.9)).mean()), 3))

table = []
for slot in ob.material_slots:
    m = slot.material; cname = clean(m.name); m.name = "M_%s_%s" % (NAME, cname)
    bsdf = next(n for n in m.node_tree.nodes if n.bl_idname == 'ShaderNodeBsdfPrincipled')
    row = dict(name=m.name, baseColor="", normal="", metallicSmoothness="", emission="",
               color=[round(v, 4) for v in bsdf.inputs["Base Color"].default_value],
               metallic=round(bsdf.inputs["Metallic"].default_value, 3), roughness=round(bsdf.inputs["Roughness"].default_value, 3),
               alpha=round(bsdf.inputs["Alpha"].default_value, 3), alphaLinked=bsdf.inputs["Alpha"].is_linked,
               blend=(getattr(m, "surface_render_method", "") == 'BLENDED'), doubleSided=not m.use_backface_culling,
               emissionStrength=round(bsdf.inputs["Emission Strength"].default_value, 3),
               emissionColor=[round(v, 4) for v in bsdf.inputs["Emission Color"].default_value], alphaSoft=0.0, alphaMin=1.0)
    for n in m.node_tree.nodes:
        if n.bl_idname != 'ShaderNodeTexImage' or not n.image: continue
        targets = [(l.to_node.bl_idname, l.to_socket.name) for out in n.outputs for l in out.links]
        base = "T_%s_%s" % (NAME, cname)
        if any(s == "Base Color" for _, s in targets):
            row["baseColor"] = save_image(n.image, base + "_BaseColor.png")
            st = alpha_stats(n.image); row["alphaSoft"] = st["soft"]; row["alphaMin"] = st["min"]
        elif any(t == 'ShaderNodeNormalMap' for t, _ in targets):
            row["normal"] = save_image(n.image, base + "_Normal.png")
        elif any(s == "Emission Color" for _, s in targets):
            row["emission"] = save_image(n.image, base + "_Emission.png")
        else:
            row["metallicSmoothness"] = save_image(n.image, base + "_MetallicSmoothness.png", "metallic_smoothness")
            row["linkedTo"] = targets
    table.append(row)

bpy.context.view_layer.update()
for o in bpy.context.view_layer.objects:
    if o: o.select_set(False)
ob.select_set(True); bpy.context.view_layer.objects.active = ob
fbx =os.path.join(MOD, NAME + ".fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='OFF', use_tspace=True,
                         use_triangles=True, path_mode='STRIP', embed_textures=False, bake_anim=False)
zs = [v.co.z for v in ob.data.vertices]
info = dict(name=NAME, height=round(max(zs), 3), tris=sum(len(p.vertices)-2 for p in ob.data.polygons), verts=len(ob.data.vertices),
            dims=[round(d, 3) for d in ob.dimensions], materials=table)
json.dump(info, open(os.path.join(OUT, NAME + "_materials.json"), "w"), indent=1)
print("CONVERT_OK", fbx, len(table), "materials", len(saved), "textures")
