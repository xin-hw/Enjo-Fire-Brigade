using System;
using UnityEngine;

namespace FireRescue
{
    public sealed class HoseConnection : MonoBehaviour
    {
        [SerializeField, Min(1)] float maximumLength = 3;
        [SerializeField] float connectDistance = 2;
        [SerializeField] Transform interactionOrigin;
        // Optional explicit geometry keeps historical scenes unchanged. No renderer/bone dependency.
        [SerializeField] SprayWallOcclusion wallOcclusion;
        [SerializeField] Transform hoseInletAnchor;
        [SerializeField] bool automaticWallWrapping;
        [SerializeField] bool breakUnderTension;
        [SerializeField] bool stowInstantly;
        [SerializeField, Min(.1f)] float breakAfterPullSeconds = .65f;
        const float LengthTolerance = .006f;
        readonly HoseWrapPath wrapPath = new HoseWrapPath();
        Vector3 previousInlet;
        Vector3 resolvedInlet;
        bool hasPreviousInlet;
        bool lengthBlocked, pullingOutward;
        float resolvedLength;
        readonly RaycastHit[] reachHits = new RaycastHit[16];
        HoseWindow activeWindow;
        FluidSource source;
        float pullSeconds;
        internal PhysXHoseDriver Physical { get; private set; }
        void Start()
        {
            if (!breakUnderTension) return;
            Physical=GetComponent<PhysXHoseDriver>();
            if (!Physical) Physical=gameObject.AddComponent<PhysXHoseDriver>();
            Physical.Initialize(this);
        }
        public event Action<HoseConnection> Broke;
        public int BreakCount { get; private set; }
        public bool BreakUnderTension => breakUnderTension;
        internal bool StowInstantly => stowInstantly;
        public bool IsPullingTaut => Physical ? Physical.IsPulling : pullSeconds>0f;
        public Vector3 InletPosition => InletAt(transform.position);
        internal Vector3 ResolvedInlet => hasPreviousInlet ? resolvedInlet : InletPosition;
        public FluidSource Source
        {
            get
            {
                if (source && source.isActiveAndEnabled) return source;
                // A disabled/destroyed source must release native material before
                // clearing logical ownership; otherwise reconnecting would Clear it.
                if (Physical && Physical.IsActive && !Physical.IsReeling) Physical.Disconnect();
                source = null; activeWindow = null; IsBlocked = false; wrapPath.Clear(); hasPreviousInlet=false;
                return null;
            }
            private set => source = value;
        }
        public bool IsConnected => Source;
        public bool IsReeling => Physical && Physical.IsReeling;
        public bool IsAtLengthLimit => IsConnected && (Physical ? Physical.IsTaut : UsedLength>=maximumLength-(FullRouteLength ? .015f : .06f));
        public bool IsBlocked { get; private set; }
        public bool HasCollisionGeometry => wallOcclusion && wallOcclusion.isActiveAndEnabled;
        public float MaximumLength => maximumLength;
        public bool AutomaticWallWrapping => automaticWallWrapping;
        bool FullRouteLength => breakUnderTension && automaticWallWrapping && HasCollisionGeometry;
        public int AutomaticBendCount => automaticWallWrapping ? wrapPath.Count : 0;
        public const int MaximumPathPoints=HoseWrapPath.Capacity+3;
        public int PathPointCount=>Source?2+(ActiveWindow?1:0)+AutomaticBendCount:0;
        public Vector3 GetPathPoint(int index)
        {
            if(!Source)return transform.position;
            if(index<=0)return Source.Port;
            HoseWindow window=ActiveWindow;
            if(window){if(index==1)return window.RoutePosition;index--;}
            if(automaticWallWrapping && index<=wrapPath.Count)return wrapPath[index-1];
            return InletAt(transform.position);
        }
        internal void CopyContactPath(HoseWrapPath target) => target.CopyFrom(wrapPath);
        public void SetAutomaticWrapping(bool enabled)
        {automaticWallWrapping=enabled;wrapPath.Clear();previousInlet=InletAt(transform.position);hasPreviousInlet=true;}
        public HoseWindow ActiveWindow
        {
            get
            {
                // Invalid routes fall back to the established direct connection.
                if (!activeWindow || !activeWindow.isActiveAndEnabled) activeWindow = null;
                return activeWindow;
            }
        }
        public float UsedLength
        {
            get
            {
                if (!Source) return 0f;
                if (Physical) return Physical.DeployedLength;
                HoseWindow window = ActiveWindow;
                if(automaticWallWrapping)
                {
                    float length=0;Vector3 a=Source.Port;
                    if(window){length+=RouteDistance(a,window.RoutePosition);a=window.RoutePosition;}
                    for(int i=0;i<wrapPath.Count;i++){length+=RouteDistance(a,wrapPath[i]);a=wrapPath[i];}
                    return length+RouteDistance(a,InletAt(transform.position));
                }
                return window
                    ? PlanarDistance(Source.transform.position, window.RoutePosition) + PlanarDistance(window.RoutePosition, transform.position)
                    : PlanarDistance(transform.position, Source.transform.position);
            }
        }
        public void SetInteractionOrigin(Transform origin) => interactionOrigin = origin;
        public void ConfigurePhysics(SprayWallOcclusion geometry, Transform inletAnchor)
        { wallOcclusion = geometry; hoseInletAnchor = inletAnchor; wrapPath.Clear();hasPreviousInlet=false; }

        // Shared by the read-only tube view. The authored cutaway walls are still solid walls.
        public bool IsCableBlocked(Vector3 start, Vector3 end, float radius = .0475f)
            => IsCableBlocked(start,end,radius,CableSource);

        FluidSource CableSource => breakUnderTension ? Source : null;

        internal bool IsCableBlocked(Vector3 start,Vector3 end,float radius,FluidSource cableSource)
            => HasCollisionGeometry && (automaticWallWrapping ? wallOcclusion.CableBlocked(start,end,radius,cableSource) :
                wallOcclusion.TryBlock(start,end,out _,out _,radius));

        internal bool RouteReelingInlet(HoseWrapPath path,Vector3 lastInlet,Vector3 freeEnd,Vector3 newInlet,FluidSource cableSource)
        {
            if (!HasCollisionGeometry) return true;
            if (!path.BuildFromInlet(wallOcclusion,lastInlet,freeEnd,newInlet,cableSource)) return false;
            path.Commit();
            return true;
        }

        public bool TryConnect(FluidSource source)
        {
            if (!CanConnect(source) || !CablePathClear(source, transform.position, null)) return false;
            // Release the current source and require a separate action to connect the next one.
            if (Physical && Physical.HasDeployedMaterial)
            {
                if (!Physical.IsReeling) Disconnect();
                return false;
            }
            if (Physical && !Physical.Connect(source)) return false;
            Source = source;
            pullSeconds = 0f;
            ClearRoute();
            return true;
        }

        public bool TryRouteThrough(HoseWindow window)
        {
            if (!Source || !Source.isActiveAndEnabled || !window || !window.CanReach(transform.position)) return false;
            float used = FullRouteLength
                ? Vector3.Distance(Source.Port,window.RoutePosition)+Vector3.Distance(window.RoutePosition,InletPosition)
                : PlanarDistance(Source.transform.position,window.RoutePosition)+PlanarDistance(window.RoutePosition,transform.position);
            if (used > maximumLength || !CablePathClear(Source, transform.position, window)) return false;
            activeWindow = window;
            wrapPath.Clear();resolvedInlet=previousInlet=InletPosition;hasPreviousInlet=true;
            return true;
        }

        public void ClearRoute(){activeWindow=null;wrapPath.Clear();resolvedInlet=previousInlet=InletPosition;hasPreviousInlet=true;}

        public bool CanConnect(FluidSource source)
        {
            if (Physical && Physical.IsReeling) return false;
            if (!source || !source.isActiveAndEnabled ||
                Vector3.Distance(transform.position,source.transform.position) > connectDistance) return false;
            // The low wall mesh is a cutaway: check reach below its top, not at the raised bottle mouth.
            Vector3 start = interactionOrigin ? interactionOrigin.position : transform.position + Vector3.up * .35f;
            Vector3 end = source.InteractionPosition;
            Vector3 delta = end-start;
            if (delta.sqrMagnitude < .0001f) return true;
            int count = Physics.RaycastNonAlloc(start,delta.normalized,reachHits,delta.magnitude,1 << 8,QueryTriggerInteraction.Ignore);
            if (count == reachHits.Length) return false;
            for (int i=0;i<count;i++)
            {
                Collider obstacle = reachHits[i].collider;
                // A bottle standing on a counter can be reached; its support and own body are not walls.
                if (obstacle.transform.IsChildOf(source.transform) || obstacle.bounds.Contains(end)) continue;
                return false;
            }
            return true;
        }

        public void Disconnect()
        {
            if (Physical) Physical.Disconnect();
            pullSeconds = 0f;
            Source = null;
            ClearRoute();
            IsBlocked = false;
        }
        void OnDisable() => Disconnect();

        public void Interact(FluidSource[] sources) => Interact(sources, null);

        public void Interact(FluidSource[] sources, HoseWindow[] windows)
        {
            FluidSource source = FindNearestSource(sources);
            HoseWindow window = FindReachableWindow(windows);
            // A short hose necessarily puts its source near the window. Prefer the closer
            // window over disconnecting the SAME source; switching sources remains explicit.
            if (Source && window && (!source || (source == Source &&
                PlanarDistance(window.RoutePosition, transform.position) <
                PlanarDistance(source.transform.position, transform.position))))
            {
                if (ActiveWindow == window) { ClearRoute(); return; }
                if (TryRouteThrough(window)) return;
            }
            if (source)
            {
                if (source != Source) TryConnect(source);
                else Disconnect();
                return;
            }

            if (Source && window)
            {
                if (ActiveWindow == window) { ClearRoute(); return; }
                if (TryRouteThrough(window)) return;
            }

            Disconnect();
        }

        public Vector3 ConstrainPosition(Vector3 desired)
        {
            IsBlocked = false;
            lengthBlocked = pullingOutward = false;
            resolvedLength = 0f;
            if (!Finite(desired)) return transform.position;
            if (!Source) return desired;
            if (Physical) return Physical.ConstrainMovement(desired,Vector3.zero);
            if(automaticWallWrapping && HasCollisionGeometry)
                return FullRouteLength ? ConstrainPhysicalRoute(desired) : ConstrainWrapped(desired);
            HoseWindow window = ActiveWindow;
            Vector3 anchor = window ? window.RoutePosition : Source.transform.position;
            float prefix = window ? PlanarDistance(Source.transform.position, anchor) : 0f;
            Vector3 delta = desired - anchor;
            delta.y = 0;
            Vector3 limited = anchor + Vector3.ClampMagnitude(delta, Mathf.Max(0, maximumLength - prefix));
            limited.y = desired.y;
            if (CablePathClear(Source, limited, window))
            {
                lengthBlocked = (desired-limited).sqrMagnitude>.00000001f;
                pullingOutward = lengthBlocked;
                resolvedLength = prefix + PlanarDistance(limited,anchor);
                return limited;
            }

            IsBlocked = true;
            Vector3 current = transform.position; current.y = desired.y;
            // Never teleport a tether through a wall to recover an externally invalid pose.
            // Returning to a clear pose / turning away / explicitly disconnecting can release it.
            if (!CablePathClear(Source, current, window)) return current;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float middle = (lo + hi) * .5f;
                if (CablePathClear(Source, Vector3.Lerp(current, limited, middle), window)) lo = middle;
                else hi = middle;
            }
            return Vector3.Lerp(current, limited, lo);
        }

        public Vector3 ConstrainMovement(Vector3 desired, Vector3 input, float dt)
        {
            if (Physical && Physical.IsActive) return Physical.ConstrainMovement(desired,input);
            Vector3 constrained = ConstrainPosition(desired);
            if (!breakUnderTension) return constrained;
            if (!lengthBlocked || !pullingOutward || resolvedLength<maximumLength-LengthTolerance ||
                !PullsOutward(input))
            {
                pullSeconds = 0f;
                return constrained;
            }

            pullSeconds += dt;
            if (pullSeconds < breakAfterPullSeconds) return constrained;
            BreakCount++;
            Broke?.Invoke(this);
            Disconnect();
            return desired;
        }

        internal void BreakFromPhysicalPull()
        {
            if (!IsConnected) return;
            BreakCount++;
            Broke?.Invoke(this);
            Disconnect();
        }

        bool PullsOutward(Vector3 input)
        {
            var window=ActiveWindow;
            Vector3 anchor=(AutomaticBendCount>0) ? wrapPath[wrapPath.Count-1] :
                (window ? window.RoutePosition : Source.Port);
            Vector3 outward=Vector3.ProjectOnPlane(ResolvedInlet-anchor,Vector3.up).normalized;
            return Vector3.Dot(input,outward)>.35f;
        }

        internal bool TryKeepTurn(out Vector3 correction)
        {
            correction=Vector3.zero;
            if (Physical) return true;
            if (!FullRouteLength || !IsConnected) return true;
            var window=ActiveWindow;
            Vector3 start=window ? window.RoutePosition : Source.Port;
            float prefix=window ? Vector3.Distance(Source.Port,start) : 0f;
            Vector3 inlet=InletPosition;
            if (!wrapPath.Build(wallOcclusion,start,ResolvedInlet,inlet,Source)) return false;
            float length=prefix+wrapPath.TrialLength3D(start,inlet);
            if (length>maximumLength+.0001f)
            {
                // Rotating the body moves its inlet a few centimetres. Let the
                // motor take up that distance toward the final anchor.
                Vector3 anchor=wrapPath.LastTrialAnchor(start);
                Vector3 leg=inlet-anchor;
                float remaining=maximumLength-(length-leg.magnitude);
                if (remaining<=Mathf.Abs(leg.y)) return false;
                Vector3 horizontal=Vector3.ProjectOnPlane(leg,Vector3.up);
                float allowed=Mathf.Sqrt(remaining*remaining-leg.y*leg.y);
                correction=Vector3.ClampMagnitude(horizontal,allowed)-horizontal;
                Vector3 corrected=inlet+correction;
                if (!wrapPath.Build(wallOcclusion,start,ResolvedInlet,corrected,Source)) return false;
                length=prefix+wrapPath.TrialLength3D(start,corrected);
                if (length>maximumLength+.0001f) return false;
                inlet=corrected;
            }
            AcceptPhysicalRoute(transform.position+correction,inlet,length);
            return true;
        }

        Vector3 InletAt(Vector3 actorPosition)=>actorPosition+(hoseInletAnchor?hoseInletAnchor.position-transform.position:Vector3.up*.9f);

        Vector3 ConstrainPhysicalRoute(Vector3 desired)
        {
            HoseWindow window=ActiveWindow;
            Vector3 start=window ? window.RoutePosition : Source.Port;
            float prefix=window ? Vector3.Distance(Source.Port,start) : 0f;
            Vector3 previous=hasPreviousInlet ? previousInlet : InletPosition;
            Vector3 end=InletAt(desired);
            FluidSource cableSource=Source;

            bool clear=wrapPath.Build(wallOcclusion,start,previous,end,cableSource);
            if (clear)
            {
                float requestedLength=prefix+wrapPath.TrialLength3D(start,end);
                if (requestedLength<=maximumLength+.0001f)
                    return AcceptPhysicalRoute(desired,end,requestedLength);
            }

            IsBlocked=true;
            Vector3 current=transform.position;
            Vector3 best=current;
            float low=0f, high=1f;
            for (int i=0;i<10;i++)
            {
                float fraction=(low+high)*.5f;
                Vector3 candidate=Vector3.Lerp(current,desired,fraction);
                Vector3 candidateInlet=InletAt(candidate);
                if (wrapPath.Build(wallOcclusion,start,previous,candidateInlet,cableSource) &&
                    prefix+wrapPath.TrialLength3D(start,candidateInlet)<=maximumLength+.0001f)
                {
                    low=fraction;
                    best=candidate;
                }
                else high=fraction;
            }

            Vector3 bestInlet=InletAt(best);
            if (wrapPath.Build(wallOcclusion,start,previous,bestInlet,cableSource))
            {
                float length=prefix+wrapPath.TrialLength3D(start,bestInlet);
                if (length<=maximumLength+.0001f)
                {
                    Vector3 along=Vector3.ProjectOnPlane(bestInlet-wrapPath.LastTrialAnchor(start),Vector3.up);
                    Vector3 rejected=Vector3.ProjectOnPlane(desired-best,Vector3.up);
                    lengthBlocked=clear && rejected.sqrMagnitude>.00000001f;
                    pullingOutward=lengthBlocked && Vector3.Dot(rejected,along)>0f;
                    return AcceptPhysicalRoute(best,bestInlet,length);
                }
            }
            return current;
        }

        Vector3 AcceptPhysicalRoute(Vector3 root,Vector3 inlet,float length)
        {
            resolvedLength=length;
            resolvedInlet=inlet;
            return CommitWrapped(root);
        }
        Vector3 ConstrainWrapped(Vector3 desired)
        {
            var window=ActiveWindow;Vector3 start=window?window.RoutePosition:Source.Port;
            float prefix=window?PlanarDistance(Source.Port,start):0;
            Vector3 previous=hasPreviousInlet?previousInlet:InletAt(transform.position);
            // Clamp against the existing final anchor first. This also resolves tension from
            // turning a hand-held inlet while already at full extension.
            Vector3 anchor=start;float fixedLength=prefix;
            for(int i=0;i<wrapPath.Count;i++){fixedLength+=PlanarDistance(anchor,wrapPath[i]);anchor=wrapPath[i];}
            Vector3 inlet=InletAt(desired),delta=inlet-anchor;delta.y=0;
            Vector3 limited=anchor+Vector3.ClampMagnitude(delta,Mathf.Max(0,maximumLength-fixedLength));limited.y=inlet.y;
            Vector3 limitedRoot=desired+(limited-inlet);
            if(TryWrapped(start,previous,limitedRoot,prefix))return CommitWrapped(limitedRoot);
            IsBlocked=true;
            Vector3 current=transform.position;
            if(!TryWrapped(start,previous,current,prefix))return current;
            float lo=0,hi=1;
            for(int i=0;i<9;i++)
            {
                float mid=(lo+hi)*.5f;
                if(TryWrapped(start,previous,Vector3.Lerp(current,desired,mid),prefix))lo=mid;else hi=mid;
            }
            Vector3 result=Vector3.Lerp(current,desired,lo);
            if(TryWrapped(start,previous,result,prefix))return CommitWrapped(result);
            return current;
        }
        bool TryWrapped(Vector3 start,Vector3 previous,Vector3 root,float prefix)
        {
            Vector3 end=InletAt(root);
            return wrapPath.Build(wallOcclusion,start,previous,end) && prefix+wrapPath.TrialLength(start,end)<=maximumLength+.0001f;
        }
        Vector3 CommitWrapped(Vector3 position)
        {wrapPath.Commit();previousInlet=InletAt(position);hasPreviousInlet=true;return position;}

        bool CablePathClear(FluidSource candidate, Vector3 actorPosition, HoseWindow window)
        {
            if (!HasCollisionGeometry || !candidate) return true;
            Vector3 inlet = actorPosition + (hoseInletAnchor ? hoseInletAnchor.position - transform.position : Vector3.up * .9f);
            FluidSource cableSource=breakUnderTension ? candidate : null;
            return window
                ? !IsCableBlocked(candidate.Port,window.RoutePosition,HoseWrapPath.Radius,cableSource) &&
                  !IsCableBlocked(window.RoutePosition,inlet,HoseWrapPath.Radius,cableSource)
                : !IsCableBlocked(candidate.Port,inlet,HoseWrapPath.Radius,cableSource);
        }

        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        public FluidSource FindNearestSource(FluidSource[] sources)
        {
            FluidSource nearest = null;
            float distance = connectDistance;
            if (sources == null) return null;
            foreach (FluidSource candidate in sources)
            {
                if (!candidate) continue;
                float next = Vector3.Distance(candidate.transform.position, transform.position);
                if (next <= distance && CanConnect(candidate)) { nearest = candidate; distance = next; }
            }
            return nearest;
        }

        HoseWindow FindReachableWindow(HoseWindow[] windows)
        {
            HoseWindow nearest = null;
            float distance = float.MaxValue;
            if (windows == null) return null;
            foreach (HoseWindow candidate in windows)
            {
                if (!candidate || !candidate.CanReach(transform.position)) continue;
                float next = PlanarDistance(candidate.RoutePosition, transform.position);
                if (next < distance) { nearest = candidate; distance = next; }
            }
            return nearest;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        float RouteDistance(Vector3 a,Vector3 b)=>FullRouteLength ? Vector3.Distance(a,b) : PlanarDistance(a,b);
    }
}
