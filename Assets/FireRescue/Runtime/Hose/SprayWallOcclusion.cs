using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    // The cutaway is presentation, not an opening. Project ONLY authored house walls
    // onto a low horizontal slice; furniture keeps its real 3D collision.
    public sealed class SprayWallOcclusion : MonoBehaviour
    {
        [SerializeField] Collider[] walls;
        [SerializeField] HoseWindow[] openings;
        [SerializeField] float wallProbeHeight = .35f;
        [SerializeField] float fullWallHeight = 2.5f;
        readonly RaycastHit[] cableHits = new RaycastHit[64];
        readonly Collider[] cableOverlaps = new Collider[32];
        readonly List<SlicePoint> slicePoints = new List<SlicePoint>();
        bool sliceReady;
        struct SlicePoint { public Collider Wall; public Vector3 Position; }
        public float CableBendHeight => wallProbeHeight - .2f;
        public void Configure(Collider[] wallColliders, HoseWindow[] windows)
        { walls = wallColliders; openings = windows; sliceReady = false; }

        // A live source opts the physical tube into the real fixed obstacles as well
        // as the cutaway walls. The fluid source's own body surrounds its coupling.
        public bool CableBlocked(Vector3 from, Vector3 to, float radius, FluidSource source = null)
        {
            if (!isActiveAndEnabled || walls == null) return false;
            if (TryBlock(from, to, out _, out _, radius)) return true;
            Vector3 a = new Vector3(from.x, wallProbeHeight, from.z);
            Vector3 b = new Vector3(to.x, wallProbeHeight, to.z);
            Vector3 delta = b - a; float length = delta.magnitude;
            if (length < .00001f) return source && FixedCableBlocked(from,to,radius,source);
            int count = Physics.SphereCastNonAlloc(a, radius, delta / length, cableHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == cableHits.Length) return true; // Dense/unsupported geometry fails closed.
            for (int i=0;i<count;i++)
            {
                if (!IsWall(cableHits[i].collider)) continue;
                Vector3 crossing = Vector3.Lerp(from, to, Mathf.Clamp01(cableHits[i].distance / length));
                if (!AllowsWindow(crossing, radius)) return true;
            }
            return source && FixedCableBlocked(from, to, radius, source);
        }

        bool FixedCableBlocked(Vector3 from, Vector3 to, float radius, FluidSource source)
        {
            const int fixedObstacles = 1 << 8;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length > .00001f)
            {
                int hits = Physics.SphereCastNonAlloc(from,radius,delta/length,cableHits,length,
                    fixedObstacles,QueryTriggerInteraction.Ignore);
                if (hits==cableHits.Length) return true;
                for (int i=0;i<hits;i++)
                {
                    RaycastHit hit=cableHits[i];
                    if (!hit.collider.transform.IsChildOf(source.transform) &&
                        !(IsWall(hit.collider)&&AllowsWindow(hit.point,radius))) return true;
                }
            }
            // A sphere cast does not report a collider already overlapped at its start.
            return EndOverlaps(from) || EndOverlaps(to);

            bool EndOverlaps(Vector3 point)
            {
                int count=Physics.OverlapSphereNonAlloc(point,radius,cableOverlaps,fixedObstacles,QueryTriggerInteraction.Ignore);
                if (count==cableOverlaps.Length) return true;
                for (int i=0;i<count;i++)
                {
                    Collider hit=cableOverlaps[i];
                    if (!hit.transform.IsChildOf(source.transform) && !(IsWall(hit)&&AllowsWindow(point,radius))) return true;
                }
                return false;
            }
        }

        // A clear chord alone is insufficient: collapsing a loop could teleport the hose
        // through an entire pillar inside the swept triangle. Test the wall cross-section.
        public bool CableSweepClear(Vector3 a, Vector3 b, Vector3 c, FluidSource source = null)
        {
            if (!sliceReady) BuildSlice();
            float area = Cross(b-a,c-a);
            if (Mathf.Abs(area)<.00001f) return true;
            foreach(var p in slicePoints)
            {
                if (!p.Wall || !p.Wall.enabled || !p.Wall.gameObject.activeInHierarchy) continue;
                float ab=Cross(b-a,p.Position-a)/area;
                float bc=Cross(c-b,p.Position-b)/area;
                float ca=Cross(a-c,p.Position-c)/area;
                if (ab>.0001f && bc>.0001f && ca>.0001f) return false;
            }
            if (source)
            {
                // Also sweep intermediate chords against fixed props. This catches a
                // route straightening through furniture inside the endpoint sweep.
                for (int i=1;i<4;i++)
                {
                    Vector3 edge=Vector3.Lerp(a,c,i*.25f);
                    if (FixedCableBlocked(b,edge,HoseWrapPath.Radius,source)) return false;
                }
            }
            return true;
        }
        bool IsWall(Collider value)
        {
            foreach(var wall in walls) if(wall==value && wall.enabled && wall.gameObject.activeInHierarchy) return true;
            return false;
        }
        static float Cross(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
        void BuildSlice()
        {
            sliceReady=true;slicePoints.Clear();if(walls==null)return;
            foreach(var wall in walls)
            {
                if (!wall) continue;
                if (wall is MeshCollider mc && mc.sharedMesh && mc.sharedMesh.isReadable)
                {
                    var vertices=mc.sharedMesh.vertices;var indices=mc.sharedMesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        Vector3 a=mc.transform.TransformPoint(vertices[indices[i]]),b=mc.transform.TransformPoint(vertices[indices[i+1]]),c=mc.transform.TransformPoint(vertices[indices[i+2]]);
                        SliceEdge(wall,a,b);SliceEdge(wall,b,c);SliceEdge(wall,c,a);
                    }
                }
                else
                {
                    // Box/unsupported wall proxies: conservative footprint; no dependency on Visual.
                    var bounds=wall.bounds;if(bounds.min.y>wallProbeHeight||bounds.max.y<wallProbeHeight)continue;
                    foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.max.z})slicePoints.Add(new SlicePoint{Wall=wall,Position=new Vector3(x,wallProbeHeight,z)});
                }
            }
        }
        void SliceEdge(Collider owner,Vector3 a,Vector3 b)
        {
            if((a.y-wallProbeHeight)*(b.y-wallProbeHeight)>0 || Mathf.Abs(b.y-a.y)<.00001f)return;
            slicePoints.Add(new SlicePoint{Wall=owner,Position=Vector3.Lerp(a,b,(wallProbeHeight-a.y)/(b.y-a.y))});
        }

        public bool TryBlock(Vector3 from, Vector3 to, out Collider obstacle, out Vector3 point, float radius = 0f)
            => TryBlock(from,to,out obstacle,out point,out _,radius);

        public bool TryBlock(Vector3 from, Vector3 to, out Collider obstacle, out Vector3 point, out Vector3 normal, float radius = 0f)
        {
            obstacle = null; point = to; normal=Vector3.up;
            if (!isActiveAndEnabled || walls == null) return false;
            Vector3 a = new Vector3(from.x, wallProbeHeight, from.z);
            Vector3 b = new Vector3(to.x, wallProbeHeight, to.z);
            Vector3 delta = b - a; float length = delta.magnitude;
            if (length < .00001f) return false;
            Vector3 direction = delta / length;
            float closest = float.MaxValue;
            foreach (var wall in walls)
            {
                if (!wall || !wall.enabled || !wall.gameObject.activeInHierarchy) continue;
                // Reverse ray also catches a segment beginning just inside a wall.
                bool forward = wall.Raycast(new Ray(a, direction), out var hit, length);
                float distance;
                if (forward) distance = hit.distance;
                else if (wall.Raycast(new Ray(b, -direction), out hit, length)) distance = length - hit.distance;
                else continue;
                Vector3 crossing = Vector3.Lerp(from, to, distance / length);
                if (crossing.y > fullWallHeight + radius || AllowsWindow(crossing, radius) || distance >= closest) continue;
                closest = distance; obstacle = wall; point = crossing;
                normal=Vector3.Dot(hit.normal,direction)>0?-hit.normal:hit.normal;
            }
            return obstacle;
        }

        bool AllowsWindow(Vector3 point, float radius)
        {
            if (openings == null) return false;
            foreach (var window in openings)
            {
                if (!window || !window.isActiveAndEnabled) continue;
                if (window.AllowsSpray(point, radius)) return true;
            }
            return false;
        }
    }
}
