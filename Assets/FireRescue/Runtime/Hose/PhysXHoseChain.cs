using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// A small, self contained PhysX hose.  The bodies are the hose: this class
    /// deliberately does not solve or render an alternate spline/wrap path.
    /// Call <see cref="BeforePhysics"/> immediately before the owning fixed step
    /// and <see cref="SampleState"/> after it when driving it manually.
    /// </summary>
    public sealed class PhysXHoseChain : MonoBehaviour
    {
        const float SegmentLength = .14f;
        const float MinimumSegmentLength = .018f;
        const float MaximumSourceCapacity = 10f;
        const int MaximumSegments = 80;
        const float PayoutSpeed = 8f;
        const float ReelApproachDistance = .04f;
        const float SourceReleaseDelay = .08f;
        const float ReelSpeedMultiplier = 3f;
        const float FastReelSpeedMultiplier = 2f * ReelSpeedMultiplier;
        const float ReelMaximumSpeed = 2.5f;
        const float ReelAccelerationTime = .7f;
        const float ReelBrakeAcceleration = 3f;
        const float ReelMotionCooldown = .18f;
        const float FastReelLead = .102f;
        const float FastReelGripLimit = .132f;
        const float ObstacleProbeInterval = .04f;
        const int FixedObstacleMask = 1 << 8;

        enum ReelDrive { Soft, Standard, Fast }

        sealed class Segment
        {
            public GameObject GameObject;
            public Rigidbody Body;
            public CapsuleCollider Capsule;
            // The link is owned by this segment and connects its proximal end to
            // the preceding segment's distal end.
            public ConfigurableJoint Link;
            public float Length;
            public bool SourceIgnored;
        }

        readonly List<Segment> segments = new List<Segment>(MaximumSegments);
        readonly Collider[] emptyColliders = Array.Empty<Collider>();
        Rigidbody sourceAnchor;
        Rigidbody handAnchor;
        PhysicsMaterial hoseContact;
        ConfigurableJoint sourceJoint;
        ConfigurableJoint gripJoint;
        Collider ownerCollider;
        Collider[] ownerColliders=Array.Empty<Collider>();
        Collider[] sourceColliders = Array.Empty<Collider>();
        Vector3 sourceTarget;
        Vector3 handTarget;
        Vector3 lastTangent = Vector3.forward;
        float maximumLength;
        float reelElapsed;
        float reelCooldown;
        float reelObstacleClock;
        float reelCapture;
        float acceptedCapture;
        float sourceTension;
        float gripForce;
        float gripError;
        float maximumJointGap;
        bool initialized;
        bool reeling;
        ReelDrive reelDrive;
        bool nearReelObstacle;

        public bool IsActive => initialized && segments.Count > 0;
        public bool IsReeling => reeling;
        public bool AllowPayout { get; set; } = true;
        public int ActiveBodyCount => segments.Count;
        // Point zero is the source.  Every following point is a physically
        // simulated segment tip; the final point is therefore the hand-side tip.
        public int NodeCount => segments.Count + (initialized ? 1 : 0);
        public float SourceTension => sourceTension;
        public float GripForce => gripForce;
        public float GripError => gripError;
        public Vector3 LastTangent => lastTangent;
        public float MaterialLength { get; private set; }
        public float ReserveLength => Mathf.Max(0f, maximumLength - MaterialLength);
        public float MaximumJointGap => maximumJointGap;

        /// <summary>
        /// Creates only enough physical material to cover the supplied centreline.
        /// Any remaining source capacity stays in the player-side reel reserve.
        /// </summary>
        public void Initialize(Vector3[] seedPoints, float maximumLength, float radius, Collider owner, Collider[] sourceColliders)
        {
            Dispose();
            if (!Finite(radius) || radius <= .002f || seedPoints == null || seedPoints.Length < 2) return;

            float requested = Finite(maximumLength) ? maximumLength : 0f;
            this.maximumLength = Mathf.Clamp(requested, 0f, MaximumSourceCapacity);
            if (this.maximumLength < MinimumSegmentLength) return;

            ownerCollider = owner;
            ownerColliders=owner ? owner.GetComponentsInChildren<Collider>() : emptyColliders;
            this.sourceColliders = sourceColliders ?? emptyColliders;
            sourceTarget = seedPoints[0];
            handTarget = seedPoints[seedPoints.Length - 1];
            if (!Finite(sourceTarget) || !Finite(handTarget)) return;

            hoseContact = new PhysicsMaterial("Hose fabric contact")
            {
                dynamicFriction = .18f,
                staticFriction = .24f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f
            };
            CreateAnchors();
            float seedLength = MeasureSeed(seedPoints);
            float deployed = Mathf.Min(this.maximumLength, seedLength);
            if (deployed < MinimumSegmentLength) deployed = Mathf.Min(this.maximumLength, MinimumSegmentLength);
            if (deployed < MinimumSegmentLength) { Dispose(); return; }

            Vector3 cursor = sourceTarget;
            float along = 0f;
            while (deployed > .0001f && segments.Count < MaximumSegments)
            {
                float length = Mathf.Min(SegmentLength, deployed);
                Vector3 next = SeedPoint(seedPoints, along+length);
                Vector3 direction = SafeDirection(next-cursor,lastTangent);
                Segment segment = CreateSegment(cursor, direction, length, radius);
                if (segment == null) break;
                cursor += direction * length;
                along += length;
                deployed -= length;
            }

            if (segments.Count == 0) { Dispose(); return; }
            BuildSourceJoint();
            BuildGripJoint();
            MaterialLength = SumMaterial();
            initialized = true;
            SampleState(0f);
        }

        public void SetTargets(Vector3 sourcePort, Vector3 handTarget)
        {
            if (Finite(sourcePort)) sourceTarget = sourcePort;
            if (Finite(handTarget)) this.handTarget = handTarget;
        }

        /// <summary>Moves the two kinematic anchors and advances the reel.</summary>
        public void BeforePhysics(float dt)
        {
            if (!IsActive) return;
            float step = ValidStep(dt);
            float inletTravel = reeling ? Vector3.Distance(handAnchor.position,handTarget) : 0f;
            MoveAnchor(sourceAnchor, sourceTarget);
            MoveAnchor(handAnchor, handTarget);

            if (reeling)
            {
                RestoreReleasedSourceContacts();
                Reel(step,inletTravel);
                return;
            }

            UpdateGripJoint(false);
            if (AllowPayout && ReserveLength > .0001f && ShouldPayout())
            {
                float budget = Mathf.Min(ReserveLength, step*PayoutSpeed);
                for (int operation=0;operation<4 && budget>.0001f;operation++)
                {
                    float before=MaterialLength;
                    Payout(budget);
                    budget-=Mathf.Max(0f,MaterialLength-before);
                }
            }
        }

        /// <summary>Samples values produced by the just-completed PhysX simulation step.</summary>
        public void SampleState(float dt)
        {
            if (!IsActive) return;
            MaterialLength = SumMaterial();
            sourceTension = sourceJoint ? sourceJoint.currentForce.magnitude : 0f;
            gripForce = gripJoint ? gripJoint.currentForce.magnitude : 0f;
            Vector3 end = GetPoint(NodeCount - 1);
            gripError = Finite(end) ? Vector3.Distance(end, handTarget) : 0f;
            lastTangent = GetLastDirection();
            maximumJointGap = MeasureMaximumGap();
        }

        public Vector3 GetPoint(int index)
        {
            if (!initialized || segments.Count == 0) return transform.position;
            if (index <= 0) return ProximalPoint(segments[0]);
            Segment segment = segments[Mathf.Clamp(index - 1, 0, segments.Count - 1)];
            if (reeling && index >= segments.Count)
                return segment.Body.position+segment.Body.rotation*Vector3.up*(segment.Length*(1f-acceptedCapture));
            return DistalPoint(segment);
        }

        /// <summary>
        /// Lets go of the source and retrieves real bodies one by one at the hand.
        /// A body is never deleted simply because a timer elapsed: a collision snag
        /// leaves it in the world until it reaches the hand-side capture distance.
        /// </summary>
        public void ReleaseAndReel()
        {
            if (!IsActive || reeling) return;
            reeling = true;
            reelDrive = ReelDrive.Soft;
            reelCooldown = 0f;
            reelObstacleClock = 0f;
            nearReelObstacle = false;
            AllowPayout = false;
            reelElapsed = 0f;
            reelCapture = 0f;
            acceptedCapture=0f;
            if (sourceJoint) DestroyJoint(sourceJoint);
            sourceJoint = null;
            if (gripJoint) ConfigureGripDrive(gripJoint, true, reelDrive);
            UpdateGripJoint(true);
        }

        public void Dispose()
        {
            initialized = false;
            reeling = false;
            MaterialLength = 0f;
            sourceTension = gripForce = gripError = maximumJointGap = 0f;
            if (sourceJoint) DestroyJoint(sourceJoint);
            if (gripJoint) DestroyJoint(gripJoint);
            sourceJoint = gripJoint = null;

            for (int i = segments.Count - 1; i >= 0; --i)
                if (segments[i].GameObject) ReleaseRuntimeObject(segments[i].GameObject);
            segments.Clear();
            if (sourceAnchor) ReleaseRuntimeObject(sourceAnchor.gameObject);
            if (handAnchor) ReleaseRuntimeObject(handAnchor.gameObject);
            sourceAnchor = handAnchor = null;
            if (hoseContact) ReleaseRuntimeObject(hoseContact);
            hoseContact = null;
            ownerCollider = null;
            sourceColliders = emptyColliders;
        }

        void OnDestroy() => Dispose();

        void CreateAnchors()
        {
            sourceAnchor = CreateAnchor("PhysX hose source anchor", sourceTarget);
            handAnchor = CreateAnchor("PhysX hose hand anchor", handTarget);
        }

        Rigidbody CreateAnchor(string name, Vector3 position)
        {
            GameObject go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, true);
            go.transform.position = position;
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            return body;
        }

        Segment CreateSegment(Vector3 proximal, Vector3 direction, float length, float radius)
        {
            if (segments.Count >= MaximumSegments || length < .0001f) return null;
            direction = SafeDirection(direction, lastTangent);
            GameObject go = new GameObject("PhysX hose segment " + segments.Count) { hideFlags = HideFlags.DontSave, layer=2 };
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(proximal, Quaternion.FromToRotation(Vector3.up, direction));
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.mass = SegmentMass(length);
            body.linearDamping = 1.1f;
            body.angularDamping = 2.8f;
            body.maxAngularVelocity = 22f;
            body.maxDepenetrationVelocity = 2f;
            body.maxLinearVelocity = 12f;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 128;
            body.solverVelocityIterations = 16;
            CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
            capsule.sharedMaterial = hoseContact;
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.center = Vector3.up*(length*.5f);
            capsule.height = Mathf.Max(radius * 2f + .0001f, length + radius * 2f);

            Segment segment = new Segment { GameObject = go, Body = body, Capsule = capsule, Length = length };
            IgnoreExternalCollisions(capsule);
            // The source fitting occupies the first short rod. Every later rod
            // collides with the source, and the fitting regains contact on release.
            if (segments.Count==0)
            {
                for(int i=0;i<sourceColliders.Length;i++) Ignore(capsule,sourceColliders[i]);
                segment.SourceIgnored=true;
            }
            if (segments.Count > 0)
            {
                Segment previous = segments[segments.Count - 1];
                segment.Link = CreateFlexibleLink(segment, previous);
                IgnoreNearSelfCollision(capsule, segments.Count - 1);
                IgnoreNearSelfCollision(capsule, segments.Count - 2);
            }
            segments.Add(segment);
            return segment;
        }

        ConfigurableJoint CreateFlexibleLink(Segment current, Segment previous)
        {
            ConfigurableJoint joint = current.GameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = previous.Body;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.up * previous.Length;
            ConfigureLockedLinear(joint);
            ConfigureBend(joint);
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.None;
            return joint;
        }

        void BuildSourceJoint()
        {
            if (segments.Count == 0 || !sourceAnchor) return;
            Segment first = segments[0];
            sourceJoint = first.GameObject.AddComponent<ConfigurableJoint>();
            sourceJoint.autoConfigureConnectedAnchor = false;
            sourceJoint.connectedBody = sourceAnchor;
            sourceJoint.anchor = Vector3.zero;
            sourceJoint.connectedAnchor = Vector3.zero;
            ConfigureLockedLinear(sourceJoint);
            sourceJoint.angularXMotion = ConfigurableJointMotion.Free;
            sourceJoint.angularYMotion = ConfigurableJointMotion.Free;
            sourceJoint.angularZMotion = ConfigurableJointMotion.Free;
            sourceJoint.enablePreprocessing = false;
            sourceJoint.projectionMode = JointProjectionMode.None;
        }

        void BuildGripJoint()
        {
            if (segments.Count == 0 || !handAnchor) return;
            Segment last = segments[segments.Count - 1];
            if (!gripJoint)
            {
                gripJoint = handAnchor.gameObject.AddComponent<ConfigurableJoint>();
                gripJoint.swapBodies=true;
            }
            gripJoint.autoConfigureConnectedAnchor = false;
            gripJoint.connectedBody = last.Body;
            gripJoint.anchor = Vector3.zero;
            gripJoint.connectedAnchor = Vector3.up * last.Length;
            ConfigureGripDrive(gripJoint, reeling, reelDrive);
            gripJoint.enablePreprocessing = false;
            gripJoint.projectionMode = JointProjectionMode.None;
        }

        static void ConfigureLockedLinear(ConfigurableJoint joint)
        {
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
        }

        static void ConfigureBend(ConfigurableJoint joint)
        {
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -80f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 80f };
            joint.angularYLimit = new SoftJointLimit { limit = 80f };
            joint.angularZLimit = new SoftJointLimit { limit = 80f };
            JointDrive drive = new JointDrive { positionSpring = .025f, positionDamper = .006f, maximumForce = .15f };
            joint.angularXDrive = drive;
            joint.angularYZDrive = drive;
        }

        static void ConfigureGripDrive(ConfigurableJoint joint, bool reeling, ReelDrive reelDrive)
        {
            // A finite implicit PD drive lets the user pull the end and avoids the
            // unstable high-stiffness AddForce spring which used to explode on impact.
            joint.xMotion = ConfigurableJointMotion.Free;
            joint.yMotion = ConfigurableJointMotion.Free;
            joint.zMotion = ConfigurableJointMotion.Free;
            // Linear axes are deliberately free: this is a finite-force coupling
            // to a kinematic CharacterController, not an unbounded hard motor.
            bool soft = reelDrive == ReelDrive.Soft;
            JointDrive drive = new JointDrive
            {
                positionSpring = reeling ? reelDrive switch
                {
                    ReelDrive.Fast => 5000f,
                    ReelDrive.Standard => 3300f,
                    _ => 550f
                } : 1800f,
                positionDamper = reeling ? (soft ? 18f : 36f) : 32f,
                maximumForce = reeling ? (soft ? 18f : 55f) : 120f
            };
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;
            joint.targetPosition = Vector3.zero;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
        }

        void UpdateGripJoint(bool reelMode)
        {
            if (!gripJoint) BuildGripJoint();
            if (!gripJoint || segments.Count == 0) return;
            Segment last = segments[segments.Count - 1];
            // During retrieval the driven storage point travels from the distal
            // tip to the proximal tip. This is a joint-anchor motion, not a
            // transform LERP: PhysX must actually bring the selected material
            // point to the hand before the segment can be removed.
            float localY = reelMode
                ? last.Length*(1f-reelCapture)
                : last.Length;
            Vector3 anchor = Vector3.up * localY;
            if (gripJoint.connectedAnchor != anchor) gripJoint.connectedAnchor = anchor;
        }

        void Payout(float budget)
        {
            Segment last = segments[segments.Count - 1];
            float wanted = Mathf.Min(ReserveLength, budget);
            if (last.Length < SegmentLength - .0001f)
            {
                float added = Mathf.Min(wanted, SegmentLength - last.Length);
                float needed=Mathf.Max(0f,Vector3.Distance(handTarget,ProximalPoint(last))-last.Length);
                added=Mathf.Min(added,needed);
                if(added<.0001f) return;
                ResizeLastSegment(last.Length + added);
                return;
            }

            Vector3 proximal = DistalPoint(last);
            Vector3 direction = SafeDirection(handTarget-proximal,GetLastDirection());
            float firstLength = Mathf.Min(Mathf.Min(wanted, ReserveLength), MinimumSegmentLength);
            Segment addedSegment = CreateSegment(proximal, direction, firstLength, last.Capsule.radius);
            if (addedSegment == null) return;
            addedSegment.Body.linearVelocity = last.Body.linearVelocity;
            addedSegment.Body.angularVelocity = last.Body.angularVelocity;
            BuildGripJoint();
            MaterialLength = SumMaterial();
        }

        void ResizeLastSegment(float length)
        {
            if (segments.Count == 0) return;
            Segment last = segments[segments.Count - 1];
            last.Length = Mathf.Clamp(length, MinimumSegmentLength, SegmentLength);
            last.Capsule.height = Mathf.Max(last.Capsule.radius * 2f + .0001f, last.Length + last.Capsule.radius * 2f);
            last.Capsule.center = Vector3.up*(last.Length*.5f);
            last.Body.mass = SegmentMass(last.Length);
            if (last.Link)
            {
                last.Link.anchor = Vector3.zero;
                Segment previous = segments[segments.Count - 2];
                last.Link.connectedAnchor = Vector3.up * previous.Length;
            }
            else if (sourceJoint)
            {
                // A one-segment hose grows from its hand side too; keep its
                // proximal material point locked to the source while it grows.
                sourceJoint.anchor = Vector3.zero;
            }
            UpdateGripJoint(reeling);
            MaterialLength = SumMaterial();
        }

        bool ShouldPayout()
        {
            Vector3 end = DistalPoint(segments[segments.Count - 1]);
            float error = Vector3.Distance(end, handTarget);
            float force = gripJoint ? gripJoint.currentForce.magnitude : 0f;
            float needed=Vector3.Distance(ProximalPoint(segments[segments.Count-1]),handTarget)-segments[segments.Count-1].Length;
            return needed>.025f && (error > .035f || force > 12f);
        }

        void Reel(float dt, float inletTravel)
        {
            reelElapsed += dt;
            if (segments.Count == 0) { FinishReel(); return; }
            // The soft coupling takes over when the inlet moves or the rope snags.
            // Keep it engaged across several fixed steps to avoid drive chatter.
            bool movingInlet = inletTravel > .014f;
            reelObstacleClock -= dt;
            if (!movingInlet && reelObstacleClock <= 0f)
            {
                nearReelObstacle = HasNearbyRoomObstacle();
                reelObstacleClock = ObstacleProbeInterval;
            }
            float gripLimit = reelDrive == ReelDrive.Fast ? FastReelGripLimit : .085f;
            bool loaded = movingInlet || MaximumJointGap > .035f || GripError > gripLimit;
            reelCooldown = loaded ? ReelMotionCooldown : Mathf.Max(0f,reelCooldown-dt);
            ReelDrive nextDrive = reelCooldown > 0f ? ReelDrive.Soft :
                nearReelObstacle ? ReelDrive.Standard : ReelDrive.Fast;
            if (nextDrive != reelDrive)
            {
                reelDrive = nextDrive;
                if (gripJoint) ConfigureGripDrive(gripJoint, true, reelDrive);
            }
            if (reelElapsed < SourceReleaseDelay) return;
            bool fastReel = reelDrive == ReelDrive.Fast;
            Segment last = segments[segments.Count - 1];
            Vector3 proximal = ProximalPoint(last);
            // The previous physics step must have moved the last rod into
            // the inlet. Continue feeding immediately after it arrives.
            if (fastReel && reelCapture >= .999f && Vector3.Distance(proximal, handTarget) <= ReelApproachDistance)
            {
                RemoveLastAtHand();
                if (!reeling) return;
                last = segments[segments.Count - 1];
                proximal = ProximalPoint(last);
            }
            Vector3 axis = last.Body.rotation * Vector3.up;
            float along = Mathf.Clamp(Vector3.Dot(handTarget-proximal,axis),0f,last.Length);
            Vector3 closest = proximal+axis*along;
            // Credit only material at the inlet. Keep a short lead on the native
            // drive so feeding is continuous but a snag cannot skip a rod.
            if ((closest-handTarget).sqrMagnitude<ReelApproachDistance*ReelApproachDistance)
                acceptedCapture=Mathf.Max(acceptedCapture,Mathf.Min(reelCapture,1f-along/last.Length));
            MaterialLength=SumMaterial();
            float speedFactor = reelDrive switch
            {
                ReelDrive.Fast => FastReelSpeedMultiplier,
                ReelDrive.Standard => ReelSpeedMultiplier,
                _ => 1f
            };
            float ramp = Mathf.SmoothStep(.3f, ReelMaximumSpeed,
                Mathf.Clamp01((reelElapsed-SourceReleaseDelay)*speedFactor/ReelAccelerationTime));
            float brake = Mathf.Sqrt(2f*ReelBrakeAcceleration*Mathf.Max(.01f,MaterialLength));
            float amount = Mathf.Min(ramp,brake)*speedFactor*dt;
            float lead = reelDrive switch
            {
                ReelDrive.Fast => FastReelLead,
                ReelDrive.Standard => .075f,
                _ => .055f
            };
            float allowed = Mathf.Clamp01(acceptedCapture+lead/last.Length);
            reelCapture=Mathf.Min(allowed,reelCapture+amount/last.Length);
            UpdateGripJoint(true);
            if (!fastReel && reelCapture >= .999f && Vector3.Distance(proximal, handTarget) <= ReelApproachDistance)
                RemoveLastAtHand();
        }

        bool HasNearbyRoomObstacle()
        {
            // Probe the actual chain against room collision before using the fast
            // drive. Floor support is handled by the rigidbody contacts.
            foreach (var segment in segments)
                if (Physics.CheckCapsule(ProximalPoint(segment), DistalPoint(segment), segment.Capsule.radius + .018f,
                    FixedObstacleMask, QueryTriggerInteraction.Ignore)) return true;
            return false;
        }

        void RemoveLastAtHand()
        {
            if (segments.Count == 0) { FinishReel(); return; }
            Segment last = segments[segments.Count - 1];
            segments.RemoveAt(segments.Count - 1);
            ReleaseRuntimeObject(last.GameObject);
            reelCapture = 0f;
            acceptedCapture = 0f;
            MaterialLength = SumMaterial();
            if (segments.Count == 0) { FinishReel(); return; }
            BuildGripJoint();
        }

        void FinishReel()
        {
            reeling = false;
            initialized = false;
            MaterialLength = 0f;
            if (gripJoint) DestroyJoint(gripJoint);
            gripJoint = null;
        }

        void IgnoreExternalCollisions(Collider capsule)
        {
            Ignore(capsule, ownerCollider);
            for(int i=0;i<ownerColliders.Length;i++) Ignore(capsule,ownerColliders[i]);
        }

        void RestoreReleasedSourceContacts()
        {
            foreach(var segment in segments)
            {
                if(!segment.SourceIgnored) continue;
                bool clear=true;
                foreach(var source in sourceColliders)
                    if(source && source.enabled && Physics.ComputePenetration(segment.Capsule,
                        segment.Capsule.transform.position,segment.Capsule.transform.rotation,
                        source,source.transform.position,source.transform.rotation,out _,out _)) {clear=false;break;}
                if(!clear) continue;
                foreach(var source in sourceColliders) if(source) Physics.IgnoreCollision(segment.Capsule,source,false);
                segment.SourceIgnored=false;
            }
        }

        void IgnoreNearSelfCollision(Collider current, int index)
        {
            if (index >= 0 && index < segments.Count) Ignore(current, segments[index].Capsule);
        }

        static void Ignore(Collider a, Collider b)
        {
            if (a && b && a != b) Physics.IgnoreCollision(a, b, true);
        }

        static void MoveAnchor(Rigidbody anchor, Vector3 target)
        {
            if (!anchor || !Finite(target)) return;
            if ((anchor.position-target).sqrMagnitude<.00000001f) return;
            anchor.MovePosition(target);
        }

        float MeasureMaximumGap()
        {
            if (segments.Count == 0) return 0f;
            float max = sourceJoint ? Vector3.Distance(sourceTarget, ProximalPoint(segments[0])) : 0f;
            for (int i = 1; i < segments.Count; i++)
                max = Mathf.Max(max, Vector3.Distance(DistalPoint(segments[i - 1]), ProximalPoint(segments[i])));
            return max;
        }

        Vector3 GetLastDirection()
        {
            if (segments.Count == 0) return lastTangent;
            Segment last = segments[segments.Count - 1];
            Vector3 end=DistalPoint(last);
            Vector3 before=ProximalPoint(segments[Mathf.Max(0,segments.Count-6)]);
            return SafeDirection(end-before,last.Body.rotation*Vector3.up);
        }

        static Vector3 ProximalPoint(Segment segment) => segment.Body.position;
        static Vector3 DistalPoint(Segment segment) => segment.Body.position + segment.Body.rotation * Vector3.up * segment.Length;
        static float SegmentMass(float length) => Mathf.Max(.012f, .018f * length / SegmentLength);

        float SumMaterial()
        {
            float total = 0f;
            for (int i = 0; i < segments.Count; i++) total += segments[i].Length;
            if (reeling && segments.Count>0) total-=segments[segments.Count-1].Length*acceptedCapture;
            return total;
        }

        static float MeasureSeed(Vector3[] seed)
        {
            float total = 0f;
            for (int i = 1; i < seed.Length; i++)
                if (Finite(seed[i - 1]) && Finite(seed[i])) total += Vector3.Distance(seed[i - 1], seed[i]);
            return total;
        }

        static Vector3 SeedPoint(Vector3[] seed, float distance)
        {
            for (int leg=0;leg<seed.Length-1;leg++)
            {
                Vector3 from = seed[leg];
                Vector3 to = seed[leg + 1];
                Vector3 delta = to - from;
                float length = delta.magnitude;
                if (!Finite(delta) || length < .0001f) continue;
                if (distance<=length) return Vector3.Lerp(from,to,distance/length);
                distance-=length;
            }
            return seed[seed.Length-1];
        }

        static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
        {
            return Finite(value) && value.sqrMagnitude > .000001f ? value.normalized :
                (Finite(fallback) && fallback.sqrMagnitude > .000001f ? fallback.normalized : Vector3.forward);
        }

        static float ValidStep(float value) => Finite(value) ? Mathf.Clamp(value, 0f, .1f) : 0f;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        static void DestroyJoint(ConfigurableJoint joint)
        {
            if (!joint) return;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Free;
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            joint.xDrive=joint.yDrive=joint.zDrive=new JointDrive();
            joint.angularXDrive=joint.angularYZDrive=new JointDrive();
            ReleaseRuntimeObject(joint);
        }

        static void ReleaseRuntimeObject(UnityEngine.Object value)
        {
            if (!value) return;
            if (value is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
