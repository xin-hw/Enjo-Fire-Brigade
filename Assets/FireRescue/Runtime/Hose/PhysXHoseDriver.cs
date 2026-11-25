using UnityEngine;

namespace FireRescue
{
    // CharacterController adapter. PhysX owns every deployed hose segment;
    // this component only relays its measured load back to the player motor.
    [DefaultExecutionOrder(60)]
    public sealed class PhysXHoseDriver : MonoBehaviour
    {
        const float Radius = .045f;
        const float ResistingForce = 16f;
        const float ReleaseForce = 20f;
        const float PullDuration = .65f;
        HoseConnection connection;
        FirefighterMotor motor;
        PhysXHoseChain chain;
        SprayEmitter emitter;
        FluidSource attachedSource;
        Vector3 sourcePort, movement;
        float pullSeconds, filteredLoad;
        bool inputAvailable = true;

        public PhysXHoseChain Chain => chain;
        public bool IsActive => chain && chain.IsActive;
        public bool IsReeling => chain && chain.IsReeling;
        public bool HasDeployedMaterial => chain && (chain.IsActive || chain.IsReeling);
        public bool IsTaut => IsActive && !IsReeling && chain.ReserveLength < .01f &&
            filteredLoad > ResistingForce;
        public bool IsPulling => pullSeconds > 0f;
        public float DeployedLength => IsActive ? chain.MaterialLength : 0f;
        public float CollisionRadius => Radius;
        public float SourceLoad => filteredLoad;
        public float PullSeconds => pullSeconds;
        public bool AutomaticPhysics {get;set;}=true;

        public void Initialize(HoseConnection hose)
        {
            connection = hose;
            motor = GetComponent<FirefighterMotor>();
            emitter = GetComponent<SprayEmitter>();
        }

        public bool Connect(FluidSource source)
        {
            // A source swap must release the existing physical material first.
            // Clear is reserved for an already empty/retrieved chain.
            if (!source || HasDeployedMaterial)
            {
                if (HasDeployedMaterial && !IsReeling) Disconnect();
                return false;
            }
            Clear();
            attachedSource = source;
            sourcePort = source.Port;
            Vector3 inlet = connection.InletPosition;
            var root = new GameObject("PhysX hose - deployed material");
            chain = root.AddComponent<PhysXHoseChain>();
            chain.Initialize(Seed(sourcePort, inlet, source), connection.MaximumLength, Radius,
                GetComponent<CharacterController>(), source.GetComponentsInChildren<Collider>());
            chain.SetTargets(sourcePort, inlet);
            pullSeconds = filteredLoad = 0f;
            return true;
        }

        public void Disconnect()
        {
            pullSeconds = 0f;
            attachedSource = null;
            // Stop emission now, but deliberately leave already airborne packets alive.
            if (emitter) emitter.Stop();
            if (connection && connection.StowInstantly)
            {
                Clear();
                filteredLoad = 0f;
            }
            else if (IsActive && !IsReeling) chain.ReleaseAndReel();
        }

        public void SetInputAvailable(bool available)
        {
            inputAvailable = available;
            if (!available)
            {
                movement = Vector3.zero;
                pullSeconds = 0f;
            }
        }

        void FixedUpdate()
        {
            if (AutomaticPhysics) PreparePhysics(Time.fixedDeltaTime);
        }

        public void PreparePhysics(float dt)
        {
            if (!IsActive || !connection) return;
            bool connected = connection.IsConnected;
            if (!connected && !IsReeling) Disconnect();
            if (!IsActive) return;
            chain.SampleState(dt);
            filteredLoad = Mathf.Lerp(filteredLoad, chain.SourceTension, 1f-Mathf.Exp(-dt*18f));
            if (attachedSource) sourcePort = attachedSource.Port;
            Vector3 handTarget=connection.InletPosition;
            chain.AllowPayout = connected;
            if (connected && chain.ReserveLength<.01f)
            {
                Vector3 outward=Vector3.ProjectOnPlane(chain.LastTangent,Vector3.up).normalized;
                float intent=Mathf.Max(0f,Vector3.Dot(movement,outward));
                handTarget+=outward*(.035f*intent);
            }
            chain.SetTargets(sourcePort,handTarget);
            if (!inputAvailable || !connected || !PullsOutward(movement)) pullSeconds=0f;
            else if (IsTaut && filteredLoad>ReleaseForce) pullSeconds+=dt;
            else pullSeconds=Mathf.Max(0f,pullSeconds-dt*.25f);
            if (pullSeconds >= PullDuration) connection.BreakFromPhysicalPull();
            if (IsActive) chain.BeforePhysics(dt);
        }

        void LateUpdate()
        {
            if (AutomaticPhysics) AfterPhysics(Time.deltaTime);
        }

        public void AfterPhysics(float dt)
        {
            if (!IsActive || !connection || dt <= 0f) return;
            chain.SampleState(dt);
            // A controller is not a dynamic body. Relay actual endpoint resistance
            // through its collision-aware Move rather than pushing it with a fake route.
            if (!inputAvailable || !connection.IsConnected || !IsTaut || !motor) return;
            Vector3 correction = chain.GetPoint(chain.NodeCount-1) - connection.InletPosition;
            correction.y = 0f;
            if (correction.magnitude > .045f)
                motor.ApplyPhysicalHosePull(Vector3.ClampMagnitude(correction*.65f,.15f));
        }

        public Vector3 ConstrainMovement(Vector3 desired, Vector3 input)
        {
            // Keep the existing taut-movement lock active while an input gate is
            // closed, but do not retain an old outward command as break fatigue.
            movement = inputAvailable ? input : Vector3.zero;
            Vector3 delta = desired-transform.position;
            if (IsReeling)
            {
                // A reel pulling a stuck hose cannot move its endpoint through a wall.
                // Prevent more separation until that material catches up to the inlet.
                Vector3 separation = connection.InletPosition-chain.GetPoint(chain.NodeCount-1);
                separation.y=0;
                float allowed=Mathf.Max(0f,.08f-separation.magnitude);
                Vector3 away=separation.normalized;
                float travel=Vector3.Dot(delta,away);
                return travel>allowed ? desired-away*(travel-allowed) : desired;
            }
            if (!IsTaut) return desired;
            Vector3 outward = Vector3.ProjectOnPlane(chain.LastTangent,Vector3.up).normalized;
            float pull = Vector3.Dot(delta,outward);
            return pull > 0f ? desired-outward*pull : desired;
        }

        bool PullsOutward(Vector3 input)
        {
            if (!chain || input.sqrMagnitude < .01f) return false;
            Vector3 outward = Vector3.ProjectOnPlane(chain.LastTangent,Vector3.up).normalized;
            return Vector3.Dot(input,outward) > .35f;
        }

        Vector3[] Seed(Vector3 source, Vector3 inlet, FluidSource owner)
        {
            const int samples = 25;
            float floorY = Mathf.Min(source.y,inlet.y)-.6f;
            Vector3 middle = (source+inlet)*.5f;
            if (Physics.Raycast(middle+Vector3.up*2f,Vector3.down,out var hit,5f,1,QueryTriggerInteraction.Ignore))
                floorY = hit.point.y+Radius+.015f;

            Collider[] sourceColliders = owner ? owner.GetComponentsInChildren<Collider>() : new Collider[0];
            bool hasSourceBounds = false;
            Bounds sourceBounds = default;
            for (int i = 0; i < sourceColliders.Length; i++)
            {
                Collider collider = sourceColliders[i];
                if (!collider || !collider.enabled) continue;
                if (!hasSourceBounds) { sourceBounds = collider.bounds; hasSourceBounds = true; }
                else sourceBounds.Encapsulate(collider.bounds);
            }

            const float ExitClearance = .015f;
            float horizontalClearance = Radius + .02f;
            bool portNearSource = hasSourceBounds &&
                source.x >= sourceBounds.min.x-horizontalClearance && source.x <= sourceBounds.max.x+horizontalClearance &&
                source.z >= sourceBounds.min.z-horizontalClearance && source.z <= sourceBounds.max.z+horizontalClearance &&
                source.y >= sourceBounds.min.y-horizontalClearance && source.y <= sourceBounds.max.y+horizontalClearance;
            if (portNearSource)
            {
                // The port may sit just inside the box top. Leave vertically before
                // beginning the U so later source-colliding segments are born clear.
                Vector3 lifted = source;
                lifted.y = Mathf.Max(source.y,sourceBounds.max.y+Radius+ExitClearance);
                Vector3 horizontal = inlet-source;
                horizontal.y = 0f;
                if (horizontal.sqrMagnitude < .0001f && owner)
                {
                    horizontal = owner.transform.forward;
                    horizontal.y = 0f;
                }
                if (horizontal.sqrMagnitude < .0001f) horizontal = Vector3.forward;
                horizontal.Normalize();

                Bounds expanded = sourceBounds;
                expanded.Expand(horizontalClearance*2f);
                float leave = float.PositiveInfinity;
                if (Mathf.Abs(horizontal.x) > .0001f)
                {
                    float edge = horizontal.x > 0f ? expanded.max.x : expanded.min.x;
                    float distance = (edge-lifted.x)/horizontal.x;
                    if (distance >= 0f) leave = Mathf.Min(leave,distance);
                }
                if (Mathf.Abs(horizontal.z) > .0001f)
                {
                    float edge = horizontal.z > 0f ? expanded.max.z : expanded.min.z;
                    float distance = (edge-lifted.z)/horizontal.z;
                    if (distance >= 0f) leave = Mathf.Min(leave,distance);
                }
                if (float.IsPositiveInfinity(leave)) leave = horizontalClearance;
                Vector3 outside = lifted + horizontal*(leave+.002f);
                var prefix = new System.Collections.Generic.List<Vector3>(3) { source };
                if ((lifted-source).sqrMagnitude > .000001f) prefix.Add(lifted);
                if ((outside-prefix[prefix.Count-1]).sqrMagnitude > .000001f) prefix.Add(outside);

                bool prefixClear = true;
                for (int i = 1; i < prefix.Count && prefixClear; i++)
                {
                    bool sourceDeparture = i == 1;
                    foreach (Collider contact in Physics.OverlapCapsule(prefix[i-1],prefix[i],Radius,~0,QueryTriggerInteraction.Ignore))
                    {
                        bool isSource = owner && contact.transform.IsChildOf(owner.transform);
                        if ((sourceDeparture && isSource) || (!isSource && contact.gameObject.layer != 8)) continue;
                        prefixClear = false;
                        break;
                    }
                }

                Vector3[] fallback = null;
                foreach (float sag in new[] { 1f,.65f,.3f,0f })
                {
                    var exitPoints = new System.Collections.Generic.List<Vector3>(prefix.Count+samples);
                    exitPoints.AddRange(prefix);
                    bool clear = prefixClear;
                    for (int i=0;i<samples;i++)
                    {
                        float t=i/(float)(samples-1);
                        Vector3 p=Vector3.Lerp(outside,inlet,t);
                        p.y=Mathf.Lerp(p.y,Mathf.Min(p.y,floorY),Mathf.Sin(t*Mathf.PI)*sag);
                        if (i==0) continue;
                        foreach (Collider contact in Physics.OverlapCapsule(exitPoints[exitPoints.Count-1],p,Radius,~0,QueryTriggerInteraction.Ignore))
                        {
                            bool isSource = owner && contact.transform.IsChildOf(owner.transform);
                            if (!isSource && contact.gameObject.layer != 8) continue;
                            clear=false;
                            break;
                        }
                        exitPoints.Add(p);
                    }
                    fallback = exitPoints.ToArray();
                    if (clear) return fallback;
                }
                // Keep the established shallow fallback if the authored room gives
                // no collision-free U; it still starts from the source exit.
                return fallback;
            }

            var points = new Vector3[samples];
            // Try a resting U, then progressively shallower curves if furniture
            // occupies it. This is initialization only, never the simulated route.
            foreach (float sag in new[] { 1f,.65f,.3f,0f })
            {
                bool clear = true;
                for (int i=0;i<samples;i++)
                {
                    float t=i/(float)(samples-1);
                    Vector3 p=Vector3.Lerp(source,inlet,t);
                    p.y=Mathf.Lerp(p.y,Mathf.Min(p.y,floorY),Mathf.Sin(t*Mathf.PI)*sag);
                    points[i]=p;
                    if (i==0) continue;
                    foreach (var contact in Physics.OverlapCapsule(points[i-1],p,Radius,1<<8,QueryTriggerInteraction.Ignore))
                        if (!contact.transform.IsChildOf(owner.transform)) { clear=false; break; }
                }
                if (clear) break;
            }
            return points;
        }

        void Clear()
        {
            if (chain) { chain.Dispose(); Destroy(chain.gameObject); }
            chain=null;
        }

        void OnDisable()
        {
            if (connection && connection.IsConnected) connection.Disconnect();
            Clear();
        }
        void OnDestroy() => Clear();
    }
}
