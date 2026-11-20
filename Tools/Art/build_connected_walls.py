"""Derive a connected, externally beveled Visual from a Unity mesh snapshot.

Run with Blender --background --python ...; never opens or rewrites the user's source .blend.
The wall collider remains the original unmodified full-height Unity mesh.
"""
from pathlib import Path
import json
import bpy
import bmesh
from math import radians

ROOT = Path(__file__).resolve().parents[2]
source = ROOT / "Logs/ConnectedWallWork/wall-input.json"
output = ROOT / "ArtSource/Procedural/ConnectedWalls"
output.mkdir(parents=True, exist_ok=True)
data = json.loads(source.read_text(encoding="utf-8"))
source_points = [(p["x"], p["y"], p["z"]) for p in data["vertices"]]
indices = data["triangles"]
source_faces = [indices[i:i+3] for i in range(0, len(indices), 3)]
# The imported cutaway collapses window lintels into its top plane. Do not bevel
# those collapsed triangles or voxelize them: both create slivers/spikes. The
# underside is the actual wall footprint, with the authored door apertures.
footprint = [f for f in source_faces if all(source_points[i][1] < .103 for i in f)]
used = sorted({i for f in footprint for i in f})
lookup = {old: new for new, old in enumerate(used)}
points = [(source_points[i][0], .1, source_points[i][2]) for i in used]
faces = [[lookup[i] for i in f] for f in footprint]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
mesh = bpy.data.meshes.new("Connected wall derivative")
mesh.from_pydata(points, [], faces)
mesh.update()
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.005)
bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=0.0001)
bmesh.ops.dissolve_limit(bm, angle_limit=radians(0.5), use_dissolve_boundaries=False,
                         verts=list(bm.verts), edges=list(bm.edges))
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
# Extrude a watertight solid from the clean underside, not the collapsed roof.
extruded = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces), use_keep_orig=True)
new_vertices = [v for v in extruded["geom"] if isinstance(v, bmesh.types.BMVert)]
bmesh.ops.translate(bm, verts=new_vertices, vec=(0, .72, 0))
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
bm.to_mesh(mesh)
bm.free()
mesh.update()
obj = bpy.data.objects.new("Connected beveled wall visual", mesh)
bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
for face in mesh.polygons:
    face.use_smooth = False
bevel = obj.modifiers.new("Exposed edges only - no separated blocks", "BEVEL")
bevel.width = 0.035
bevel.segments = 3
bevel.limit_method = "ANGLE"
bevel.angle_limit = radians(15)
bevel.affect = "EDGES"
bevel.use_clamp_overlap = True
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated = obj.evaluated_get(depsgraph)
derived = evaluated.to_mesh()
# Acute jamb miters can protrude beyond the imported exterior envelope even
# with overlap clamping. Constrain the visual to that envelope, never the
# other way round. This does not expand the accepted Unity bounds tolerance.
minimum = [min(p[axis] for p in source_points) for axis in range(3)]
maximum = [max(p[axis] for p in source_points) for axis in range(3)]
for vertex in derived.vertices:
    for axis in range(3):
        vertex.co[axis] = max(minimum[axis], min(maximum[axis], vertex.co[axis]))
derived.update()
derived.calc_loop_triangles()
vertices, normals, triangles = [], [], []
for tri in derived.loop_triangles:
    for corner in tri.loops:
        coordinate = derived.vertices[derived.loops[corner].vertex_index].co
        # Broad wall faces stay truly flat. Interpolating weighted corner normals
        # over the cutaway's long triangulated ngons produced visible diagonal bands.
        normal = tri.normal
        vertices.append(dict(zip(("x", "y", "z"), coordinate)))
        normals.append(dict(zip(("x", "y", "z"), normal)))
        triangles.append(len(vertices) - 1)
result = {"vertices": vertices, "normals": normals, "triangles": triangles,
          "inputVertices": len(source_points), "inputTriangles": len(source_faces), "bevelMeters": 0.035}
(output / "connected-wall.geometry.json").write_text(json.dumps(result, separators=(",", ":")), encoding="utf-8")
# Store the same constrained result that Unity receives, not the pre-clamp
# modifier stack; the generator remains the non-destructive source recipe.
final_mesh = derived.copy()
obj.modifiers.clear()
obj.data = final_mesh
bpy.ops.wm.save_as_mainfile(filepath=str(output / "ConnectedWall.blend"))
print(f"CONNECTED_WALL_EXPORT vertices={len(vertices)} triangles={len(triangles)//3}")
evaluated.to_mesh_clear()
