using UnityEngine;

namespace FireRescue
{
    public sealed class SprayEmitter : MonoBehaviour
    {
        [SerializeField] HoseConnection hose;
        [SerializeField] ElementReaction reactions;
        [SerializeField] Transform nozzleAnchor;
        [SerializeField] Transform hoseInletAnchor;
        [SerializeField] float range = 6.5f;
        // Opt-in migration: historical scenes keep their serialized point-aim contract.
        [SerializeField] bool directionalSpray;
        [SerializeField] bool automaticSimulation = true;
        [SerializeField] float groundHeight;
        [SerializeField] SprayWallOcclusion wallOcclusion;
        public const int ParticleCapacity = 256;
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];
        readonly Flight[] flights = new Flight[ParticleCapacity];
        SphereCollider overlapProbe;
        int flightCount;
        uint sequence;
        uint waterBurst;
        float emissionCredit;
        float pendingAmount;
        FluidSource emissionSource;
        EmissionSnapshot pendingSnapshot;
        struct Flight
        {
            public Vector3 Origin, Direction, Position, Previous, InitialVelocity;
            public SpraySettings Settings;
            public SprayEffectSettings Effects;
            public ElementKind Element;
            public float Amount, Reach, Lifetime, Age, Floor;
            public float TrailSeconds,VisualScale;
            public uint Seed;
            public uint Burst;
            public bool CoreWater;
            public Collider SpawnBlock;
        }
        struct EmissionSnapshot
        {
            public Vector3 Origin, Direction;
            public Vector3 AimedVelocity;
            public SpraySettings Settings;
            public SprayEffectSettings Effects;
            public ElementKind Element;
            public float Floor, LaunchAngle;
            public Collider SpawnBlock;
            public bool Valid;
            public bool DropletBundle;
            public uint Burst;
        }
        public readonly struct ParticleSample
        {
            public readonly Vector3 Position, Velocity;
            public readonly Vector3 TrailStart, TrailMiddle;
            public readonly float Radius, NormalizedAge;
            public readonly float VisualScale;
            public readonly uint Seed;
            public readonly uint Burst;
            public readonly float AgeSeconds;
            public readonly bool CoreWater;
            public readonly ElementKind Element;
            public ParticleSample(Vector3 position, Vector3 velocity, float radius, float age, ElementKind element)
                : this(position, velocity, radius, age, element, position, position) { }
            public ParticleSample(Vector3 position, Vector3 velocity, float radius, float age, ElementKind element,
                Vector3 trailStart, Vector3 trailMiddle,uint seed=0,float visualScale=1,bool coreWater=false,uint burst=0,float ageSeconds=0)
            { Position = position; Velocity = velocity; Radius = radius; NormalizedAge = age; Element = element;
                TrailStart = trailStart; TrailMiddle = trailMiddle; Seed=seed;VisualScale=visualScale;
                CoreWater=coreWater;Burst=burst;AgeSeconds=ageSeconds; }
        }
        // Notification only. Reactions are applied exactly once by this emitter, never by VFX.
        public readonly struct ParticleImpact
        {
            public readonly Vector3 Position, Normal, Velocity;
            public readonly ElementKind Element;
            public readonly float Amount;
            public readonly Collider Collider;
            public ParticleImpact(Vector3 position, Vector3 normal, Vector3 velocity, ElementKind element, float amount, Collider collider = null)
            { Position = position; Normal = normal; Velocity = velocity; Element = element; Amount = amount; Collider = collider; }
        }
        public event System.Action<ParticleImpact> ParticleImpacted;
        public bool UsesDirectionalSpray => directionalSpray;
        public bool UsesBallisticWater => directionalSpray && hose && hose.Source is FluidSource source &&
            source.Element == ElementKind.Water && source.SpraySettings.PhysicalBallistics;
        public bool AutomaticSimulation { get => automaticSimulation; set => automaticSimulation = value; }
        public int ActiveParticleCount => flightCount;
        public uint CurrentWaterBurst=>waterBurst;
        public int TotalEmitted { get; private set; }
        public float TotalEmittedAmount { get; private set; }
        public float DroppedAmount { get; private set; }
        internal bool UseDropletBundle { get; set; }
        public bool UsesSurfaceAim => UsesBallisticWater || (UseDropletBundle && directionalSpray && hose &&
            hose.Source && hose.Source.Element == ElementKind.DryPowder && hose.Source.SpraySettings.PhysicalBallistics);
        internal double WaterDiscarded { get; private set; }
        internal double AirborneWaterAmount
        {
            get
            {
                double amount = pendingSnapshot.Element == ElementKind.Water ? pendingAmount : 0d;
                for (int i = 0; i < flightCount; i++)
                    if (flights[i].Element == ElementKind.Water) amount += flights[i].Amount;
                return amount;
            }
        }
        public int TotalImpacts { get; private set; }
        public bool IsSpraying { get; private set; }
        public bool HasImpact { get; private set; }
        public Vector3 ImpactPoint { get; private set; }
        // Authored on the gameplay root, never on a replaceable mesh or animation bone.
        public Vector3 NozzlePosition => nozzleAnchor ? nozzleAnchor.position : transform.position + Vector3.up * .9f + transform.forward * .45f;
        public Vector3 HoseInletPosition => hoseInletAnchor ? hoseInletAnchor.position : NozzlePosition;
        public HoseConnection Connection => hose;
        public void Initialize(HoseConnection connection, ElementReaction reaction) { hose = connection; reactions = reaction; }
        public void SetNozzleAnchor(Transform anchor) => nozzleAnchor = anchor;
        public void SetHoseInletAnchor(Transform anchor) => hoseInletAnchor = anchor;
        public void SetGroundHeight(float height) => groundHeight = height;
        public void SetWallOcclusion(SprayWallOcclusion value) => wallOcclusion = value;
        public void EnableDirectionalSpray(bool enabled)
        {
            Stop(); ClearFlights(); sequence = 0;waterBurst=0;
            directionalSpray = enabled;
        }

        void Update()
        {
            if (directionalSpray && automaticSimulation) Advance(Time.deltaTime);
        }

        public void Stop()
        {
            FlushPending();
            IsSpraying = false; emissionCredit = 0; emissionSource = null;
            pendingSnapshot = default;
            if (!directionalSpray) { HasImpact = false; ImpactPoint = NozzlePosition; }
        }
        void OnDisable()
        {
            // Disable remains a hard lifecycle boundary: do not launch a late packet into a disabled emitter.
            DropPending(); IsSpraying = false; emissionCredit = 0f; emissionSource = null; pendingSnapshot = default;
            ClearFlights(); HasImpact = false;
        }
        void ClearFlights()
        {
            for (int i = 0; i < flightCount; i++) DiscardFlight(flights[i]);
            flightCount = 0;
        }
        void OnDestroy()
        {
            if (overlapProbe) Destroy(overlapProbe.gameObject);
        }

        public void Tick(Vector3 aimPoint, bool held, float dt, bool aimAtSurface = false)
        {
            if (directionalSpray) { EmitDirectional(aimPoint, held, dt, aimAtSurface); return; }
            IsSpraying = held && hose && hose.IsConnected && dt > 0;
            HasImpact = false;
            Vector3 direction = (aimPoint - NozzlePosition).normalized;
            ImpactPoint = NozzlePosition + direction * range;
            if (!IsSpraying || direction.sqrMagnitude < .5f) return;
            int count = Physics.SphereCastNonAlloc(NozzlePosition, .08f, direction, hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.MaxValue;
            RaycastHit hit = default;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance >= closest) continue;
                hit = hits[i]; closest = hit.distance; HasImpact = true;
            }
            if (!HasImpact) return;
            ImpactPoint = hit.point;
            reactions.Apply(hose.Source.Element, hit.point, direction, hose.Source.FlowRate * dt, hit.collider);
        }

        public static Vector3 PlanarAimDirection(Vector3 playerPosition, Vector3 aimPoint, Vector3 fallback)
        {
            Vector3 direction = aimPoint - playerPosition; direction.y = 0;
            if (!Finite(direction) || direction.sqrMagnitude < .0001f)
            { direction = fallback; direction.y = 0; }
            return direction.sqrMagnitude > .0001f && Finite(direction) ? direction.normalized : Vector3.forward;
        }

        void EmitDirectional(Vector3 aimPoint, bool held, float dt, bool aimAtSurface)
        {
            if (!isActiveAndEnabled || !held || !hose || !hose.isActiveAndEnabled || !hose.Source || !hose.Source.isActiveAndEnabled ||
                !reactions || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) { Stop(); return; }
            FluidSource source = hose.Source;
            if (!Finite(source.FlowRate) || source.FlowRate <= 0f) { Stop(); return; }
            SpraySettings settings = source.SpraySettings;
            SprayEffectSettings effects = source.SprayEffects;
            if (!IsSpraying || emissionSource != source)
            {
                // Snapshot ownership is value-based: a destroyed Unity source still has to flush its old material.
                if (pendingAmount > 0f) FlushPending();
                waterBurst++;
                emissionCredit = 1f; // Preserve the immediate visual/physical packet, funded by this tick's actual flow.
            }
            IsSpraying = true; emissionSource = source;
            float addedAmount = source.FlowRate * dt;
            if (!Finite(addedAmount)) { Stop(); return; }
            if (source.Element == ElementKind.Water)
            {
                addedAmount = source.DrawWater(addedAmount);
                if (addedAmount <= 0f) { Stop(); return; }
            }
            float priorAmount = pendingAmount;
            pendingAmount += addedAmount;
            if (!Finite(pendingAmount))
            {
                DiscardPending(priorAmount, source.Element);
                DiscardPending(addedAmount, source.Element);
                pendingAmount = 0f; pendingSnapshot = default; Stop(); return;
            }
            emissionCredit += dt * settings.PacketsPerSecond;
            if (!Finite(emissionCredit))
            {
                DiscardPending(pendingAmount, source.Element);
                pendingAmount = 0f; pendingSnapshot = default; Stop(); return;
            }
            Vector3 direction = PlanarAimDirection(transform.position, aimPoint, transform.forward);
            bool aimWater = aimAtSurface && UsesSurfaceAim && settings.PhysicalBallistics;
            Vector3 toSurface = aimPoint - NozzlePosition;
            Vector3 aimedVelocity = aimWater && UseDropletBundle ? SolveSurfaceLaunch(toSurface, settings) : Vector3.zero;
            float angle = aimWater && aimedVelocity == Vector3.zero
                ? WaterLaunchAngle(toSurface, direction, settings) : settings.LaunchAngle;
            pendingSnapshot = CaptureSnapshot(direction, settings, effects, source.Element, angle, aimedVelocity);
            float wholeCredit = Mathf.Floor(emissionCredit);
            if (wholeCredit < 1f) return;
            emissionCredit -= wholeCredit;
            int slots = source.Element == ElementKind.Water && settings.PhysicalBallistics
                ? (pendingSnapshot.DropletBundle ? 4 : 2) : 1;
            int available = (ParticleCapacity - flightCount)/slots;
            if (available <= 0)
            {
                DropPending(); return;
            }
            // Clamp while still a float; casting a huge finite credit directly to int is undefined for this contract.
            int emitted = (int)Mathf.Min(wholeCredit, (float)available);
            float amountPerFlight = pendingAmount / emitted;
            pendingAmount = 0f;
            for (int i = 0; i < emitted; i++) EmitFlight(pendingSnapshot, amountPerFlight);
            pendingSnapshot = default;
        }

        static float WaterLaunchAngle(Vector3 toSurface, Vector3 direction, SpraySettings settings)
        {
            float distance = Vector3.Dot(toSurface, direction);
            if (!Finite(toSurface) || distance < .15f) return settings.LaunchAngle;

            // Low arc at the main stream speed. This is an approximate sighting angle:
            // drag, scatter, obstructions and the pitch limits still determine the hit.
            float gravityOverSpeedSquared = settings.Gravity / (settings.MuzzleSpeed * settings.MuzzleSpeed);
            float drop = gravityOverSpeedSquared * distance;
            // The sweep hits when the sphere's surface reaches the target. Give its
            // center room to clear the front lip before it descends onto the counter.
            float height = toSurface.y + settings.CollisionRadius;
            float root = Mathf.Sqrt(Mathf.Max(0f, 1f - drop * drop - 2f * gravityOverSpeedSquared * height));
            float angle = Mathf.Atan2(drop + 2f * height / distance, 1f + root) * Mathf.Rad2Deg;
            return Mathf.Clamp(angle, -30f, 42f);
        }

        static Vector3 SolveSurfaceLaunch(Vector3 offset, SpraySettings settings)
        {
            if (!Finite(offset)) return Vector3.zero;
            float distance = new Vector2(offset.x, offset.z).magnitude;
            if (distance < .10f || distance > settings.MaximumRange) return Vector3.zero;

            // Choose a flight time by distance, then invert the same gravity and drag
            // used by PositionAt. Save only the initial velocity in the launched drop.
            offset.y += settings.CollisionRadius;
            float earliest = Mathf.Max(.14f, distance / 8f);
            float latest = Mathf.Min(.75f, settings.AirLifetime);
            for (float time = earliest; time <= latest; time += 1f / 60f)
            {
                BallisticTerms(time, settings.AirDrag, out float response, out float fall);
                Vector3 velocity = (offset + Vector3.up * (settings.Gravity * fall)) / response;
                if (velocity.sqrMagnitude <= settings.MuzzleSpeed * settings.MuzzleSpeed) return velocity;
            }
            return Vector3.zero;
        }

        static void BallisticTerms(float age, float drag, out float response, out float fall)
        {
            response = age;
            fall = .5f * age * age;
            if (drag < .001f) return;
            response = (1f - Mathf.Exp(-drag * age)) / drag;
            fall = (age - response) / drag;
        }

        EmissionSnapshot CaptureSnapshot(Vector3 direction, SpraySettings settings, SprayEffectSettings effects,
            ElementKind element, float launchAngle, Vector3 aimedVelocity)
        {
            Vector3 origin = NozzlePosition;
            Collider spawnBlock = null;
            if (wallOcclusion && wallOcclusion.TryBlock(new Vector3(transform.position.x, origin.y, transform.position.z),
                origin, out spawnBlock, out var blockedPoint, settings.CollisionRadius)) origin = blockedPoint;
            return new EmissionSnapshot { Origin = origin, Direction = direction, AimedVelocity = aimedVelocity, Settings = settings, Effects = effects,
                Element = element, Floor = groundHeight, LaunchAngle = launchAngle, SpawnBlock = spawnBlock,
                Valid = true, Burst = waterBurst, DropletBundle = UseDropletBundle };
        }

        void EmitFlight(EmissionSnapshot snapshot, float amount)
        {
            if (!snapshot.Valid || amount <= 0f || !Finite(amount)) return;
            if(snapshot.Element==ElementKind.Water&&snapshot.Settings.PhysicalBallistics&&flightCount+2<=ParticleCapacity)
            {
                if (snapshot.DropletBundle)
                {
                    int count = Mathf.Min(4, ParticleCapacity - flightCount);
                    float share = amount / count;
                    for (int i = 0; i < count; i++)
                        SpawnFlight(snapshot, i == count - 1 ? amount - share * i : share, false, i == 0);
                }
                else
                {
                    SpawnFlight(snapshot, amount * .28f, true);
                    SpawnFlight(snapshot, amount * .72f, false);
                }
            }
            else if(flightCount<ParticleCapacity)SpawnFlight(snapshot,amount,false);
        }

        void SpawnFlight(EmissionSnapshot snapshot,float amount,bool core,bool centerDrop = false)
        {
            uint id=sequence++;
            bool irregularWater=snapshot.Element==ElementKind.Water&&snapshot.Settings.PhysicalBallistics;
            float tier = (id * 3 % 5) / 4f;
            float angle = ((id * 5 % 7) / 3f - 1f) * snapshot.Settings.SpreadAngle * .5f;
            float speed=tier>=1?1:Mathf.Lerp(snapshot.Settings.MinimumSpeedFraction,snapshot.Settings.MainStreamSpeedFraction,tier);
            float elevationJitter=0;
            if(irregularWater)
            {
                // Continuous launch samples, not five repeating speed/angle lanes.
                // Preserve near/mid coverage plus a smaller far-reaching fraction.
                float u=SampleVariation(id,17);
                speed=u<.82f?Mathf.Lerp(snapshot.Settings.MinimumSpeedFraction,snapshot.Settings.MainStreamSpeedFraction,u/.82f):
                    Mathf.Lerp(snapshot.Settings.MainStreamSpeedFraction,1,(u-.82f)/.18f);
                angle=(SampleVariation(id,31)*2-1)*snapshot.Settings.SpreadAngle*.5f;
                elevationJitter=(SampleVariation(id,47)*2-1)*3f;
            }
            if(core){speed=1;angle=0;elevationJitter=0;}
            if (irregularWater && snapshot.DropletBundle)
            {
                // Bias the cone toward its axis. Independent radii and bearings avoid rows.
                float radius = Mathf.Pow(SampleVariation(id, 31), .9f) * snapshot.Settings.SpreadAngle * .5f;
                float bearing = SampleVariation(id, 47) * Mathf.PI * 2f;
                angle = Mathf.Cos(bearing) * radius;
                elevationJitter = Mathf.Sin(bearing) * radius * .68f;
                speed = Mathf.Lerp(.78f, 1f, SampleVariation(id, 17));
            }
            float reach = Mathf.Lerp(snapshot.Settings.MinimumRange, snapshot.Settings.MaximumRange, tier);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * snapshot.Direction;
            float elevation = (snapshot.LaunchAngle+elevationJitter) * Mathf.Deg2Rad;
            Vector3 velocity = (direction * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) *
                snapshot.Settings.MuzzleSpeed * speed;
            if (snapshot.AimedVelocity != Vector3.zero)
            {
                velocity = snapshot.AimedVelocity;
                if (!centerDrop)
                {
                    Vector3 across = Vector3.Cross(Vector3.up, velocity).normalized;
                    velocity = Quaternion.AngleAxis(angle * .65f, Vector3.up) *
                        (Quaternion.AngleAxis(elevationJitter * .65f, across) * velocity);
                    velocity *= Mathf.Lerp(.91f, 1f, SampleVariation(id, 17));
                }
            }
            flights[flightCount++] = new Flight
            {
                Origin = snapshot.Origin, Position = snapshot.Origin, Previous = snapshot.Origin,
                Direction = direction, InitialVelocity = velocity,
                Reach = reach, Lifetime = snapshot.Settings.AirLifetime * (snapshot.Settings.PhysicalBallistics ? 1f : Mathf.Lerp(.55f, 1f, reach / snapshot.Settings.MaximumRange)),
                Settings = snapshot.Settings, Effects = snapshot.Effects, Element = snapshot.Element, Amount = amount,
                Floor = snapshot.Floor, SpawnBlock = snapshot.SpawnBlock,Seed=id,CoreWater=core,Burst=snapshot.Burst,
                TrailSeconds=(irregularWater?Mathf.Lerp(.65f,1.85f,SampleVariation(id,61)):5f)/snapshot.Settings.PacketsPerSecond,
                VisualScale=irregularWater?Mathf.Lerp(.68f,1.3f,SampleVariation(id,79)):1f
            };
            TotalEmitted++; TotalEmittedAmount += amount;
        }

        void FlushPending()
        {
            if (pendingAmount <= 0f) { pendingAmount = 0f; pendingSnapshot = default; return; }
            if (!pendingSnapshot.Valid) { DropPending(); return; }
            if (flightCount < ParticleCapacity) EmitFlight(pendingSnapshot, pendingAmount);
            else DiscardPending(pendingAmount, pendingSnapshot.Element);
            pendingAmount = 0f; pendingSnapshot = default;
        }

        void DropPending()
        {
            if (pendingAmount > 0f) DiscardPending(pendingAmount, pendingSnapshot.Element);
            pendingAmount = 0f; pendingSnapshot = default;
        }

        void DiscardPending(float amount, ElementKind element)
        {
            DroppedAmount += amount;
            if (element == ElementKind.Water) WaterDiscarded += amount;
        }

        void DiscardFlight(Flight flight)
        {
            if (flight.Element == ElementKind.Water) WaterDiscarded += flight.Amount;
        }

        // Separate from input: already emitted material keeps moving after release, tool switch or disconnect.
        // A later server can set AutomaticSimulation=false and call Advance with its own simulation clock.
        public void Advance(float dt)
        {
            if (!directionalSpray || !isActiveAndEnabled || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            HasImpact = false;
            for (int i = flightCount - 1; i >= 0; i--)
            {
                Flight flight = flights[i];
                if (flight.SpawnBlock)
                {
                    HasImpact = true; ImpactPoint = flight.Origin; TotalImpacts++;
                    DiscardFlight(flight);
                    flights[i] = flights[--flightCount];
                    continue; // Nozzle protrudes across a solid wall: no material can be created on its far side.
                }
                float remaining = Mathf.Min(dt, flight.Lifetime - flight.Age);
                bool alive = true;
                while (remaining > .000001f && alive)
                {
                    float step = Mathf.Min(remaining, 1f / 60f);
                    Vector3 previous = flight.Position;
                    flight.Previous = previous;
                    flight.Age = Mathf.Min(flight.Lifetime, flight.Age + step);
                    Vector3 next = PositionAt(flight, flight.Age);
                    if (FindCollision(previous, next, flight.Settings.CollisionRadius, out Collider collider, out Vector3 point, out Vector3 normal))
                    {
                        HasImpact = true; ImpactPoint = point; TotalImpacts++;
                        // An initial overlap without a surface contact still blocks the
                        // packet. It cannot place residue or effects at a fabricated point.
                        if (normal != Vector3.zero)
                        {
                            Vector3 velocity = VelocityAt(flight, flight.Age);
                            if (reactions) reactions.ApplyBallisticSurfaceImpact(flight.Element, point, velocity, normal,
                                flight.Amount, collider, flight.Settings.ImpactRadius, flight.Settings.GroundLifetime, flight.Effects);
                            ParticleImpacted?.Invoke(new ParticleImpact(point, normal, velocity, flight.Element, flight.Amount, collider));
                        }
                        else DiscardFlight(flight);
                        alive = false;
                    }
                    flight.Position = next;
                    if (alive && flight.Age >= flight.Lifetime)
                    {
                        DiscardFlight(flight);
                        alive = false;
                    }
                    remaining -= step;
                }
                if (alive) flights[i] = flight;
                else flights[i] = flights[--flightCount];
            }
        }

        static Vector3 PositionAt(Flight flight, float age)
        {
            if (flight.Settings.PhysicalBallistics)
            {
                BallisticTerms(age, flight.Settings.AirDrag, out float response, out float fall);
                return flight.Origin + flight.InitialVelocity * response + Vector3.down * (flight.Settings.Gravity * fall);
            }
            float t = Mathf.Clamp01(age / flight.Lifetime);
            // Range and flight shape belong to the source profile, never the cursor's distance/height.
            float vertical = 4f * flight.Settings.ArcHeight * t * (1f - t) -
                Mathf.Max(0f, flight.Origin.y - flight.Floor) * t * t;
            return flight.Origin + flight.Direction * (flight.Reach * t) + Vector3.up * vertical;
        }

        static Vector3 VelocityAt(Flight f, float age)
        {
            if (f.Settings.PhysicalBallistics)
            {
                float drag = f.Settings.AirDrag;
                if (drag < .001f) return f.InitialVelocity + Vector3.down * (f.Settings.Gravity * age);
                float decay = Mathf.Exp(-drag * age);
                return f.InitialVelocity * decay + Vector3.down * (f.Settings.Gravity * (1f - decay) / drag);
            }
            float t = age / f.Lifetime;
            float verticalSpeed = (4f * f.Settings.ArcHeight * (1f - 2f * t) -
                2f * Mathf.Max(0, f.Origin.y - f.Floor) * t) / f.Lifetime;
            return f.Direction * (f.Reach / f.Lifetime) + Vector3.up * verticalSpeed;
        }

        public ParticleSample GetParticle(int index)
        {
            if (index < 0 || index >= flightCount) return default;
            Flight f = flights[index];
            float t = f.Age / f.Lifetime;
            // Show this packet's own recent trajectory, never array neighbors. Variation
            // is fixed at emission: refreshing the view must not make drops jitter in place.
            float tailAge = Mathf.Max(0, f.Age - f.TrailSeconds);
            return new ParticleSample(f.Position, VelocityAt(f, f.Age),
                f.Settings.CollisionRadius, t, f.Element, PositionAt(f, tailAge), PositionAt(f, (tailAge + f.Age) * .5f),f.Seed,f.VisualScale,f.CoreWater,f.Burst,f.Age);
        }

        // Read-only span clipping for the continuous water surface. Uses the same owner
        // filtering/cutaway-wall query as flight collision; never applies water a second time.
        public bool TryClipWaterSpan(Vector3 from,Vector3 to,float radius,out Vector3 contact)
        {
            bool blocked=FindCollision(from,to,radius,out _,out contact,out var normal);
            if(blocked)contact+=normal*radius;return blocked;
        }

        // Local deterministic noise: no global Random.state, frame-dependent jitter or allocations.
        internal static float SampleVariation(uint id,uint channel)
        {
            unchecked
            {
                uint x=id+channel*0x9e3779b9u;x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;
                return (x&0x00ffffffu)/16777216f;
            }
        }

        internal bool FindCollision(Vector3 from, Vector3 to, float radius, out Collider collider, out Vector3 point, out Vector3 normal)
        {
            collider = null; point = to; normal = Vector3.up;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Vector3 direction = length > .000001f ? delta / length : Vector3.zero;
            int overlapCount = Physics.OverlapSphereNonAlloc(from, radius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // NonAlloc ordering is unspecified when full. Rare dense clusters use the complete query.
            var overlapBuffer = overlapCount == overlaps.Length
                ? Physics.OverlapSphere(from, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : overlaps;
            if (overlapBuffer != overlaps) overlapCount = overlapBuffer.Length;
            float closest = float.MaxValue;
            for (int i = 0; i < overlapCount; i++)
            {
                Collider candidate = overlapBuffer[i];
                if (!candidate || candidate.transform.IsChildOf(transform)) continue;
                OverlapContact(candidate, from, direction, radius, out var contact, out var contactNormal);
                float d = (contact - from).sqrMagnitude;
                if (contactNormal != Vector3.zero && d < closest)
                { closest = d; collider = candidate; point = contact; normal = contactNormal; }
                else if (!collider)
                { collider = candidate; point = contact; normal = contactNormal; }
            }
            if (collider) return true; // Starting inside a wall cannot launch material through it.
            if (length < .000001f) return false;
            closest = float.MaxValue;
            if (wallOcclusion && wallOcclusion.TryBlock(from, to, out collider, out point, out normal, radius))
            { closest = Vector3.Distance(from, point); }
            int count = Physics.SphereCastNonAlloc(from, radius, direction, hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var hitBuffer = count == hits.Length
                ? Physics.SphereCastAll(from, radius, direction, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : hits;
            if (hitBuffer != hits) count = hitBuffer.Length;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = hitBuffer[i].collider;
                // The authoring wall proxy may report this same contact first. Prefer
                // the actual collider normal on ties, otherwise glancing jets fan backwards.
                if (!candidate || candidate.transform.IsChildOf(transform) || hitBuffer[i].distance > closest+.001f) continue;
                closest = hitBuffer[i].distance; collider = candidate; point = hitBuffer[i].point; normal = hitBuffer[i].normal;
                if (closest == 0f && point == Vector3.zero)
                    OverlapContact(candidate, from, direction, radius, out point, out normal);
            }
            return collider;
        }

        void OverlapContact(Collider collider, Vector3 center, Vector3 direction, float radius,
            out Vector3 point, out Vector3 normal)
        {
            if (collider is not MeshCollider { convex: false })
            {
                point = collider.ClosestPoint(center);
                normal = (center - point).normalized;
                if (normal != Vector3.zero) return;
            }

            // A swept sphere can report zero distance when it begins in contact.
            // ClosestPoint cannot recover a surface on the authored concave meshes.
            if (!overlapProbe)
            {
                var probe = new GameObject("Spray overlap probe") { hideFlags = HideFlags.HideAndDontSave };
                overlapProbe = probe.AddComponent<SphereCollider>();
                overlapProbe.enabled = false;
            }
            overlapProbe.radius = radius;
            if (Physics.ComputePenetration(overlapProbe, center, Quaternion.identity, collider,
                collider.transform.position, collider.transform.rotation, out normal, out float penetration))
            {
                point = center + normal * (penetration - radius);
                return;
            }

            if (collider.Raycast(new Ray(center - direction * radius, direction), out var hit, radius * 2f))
            {
                point = hit.point;
                normal = hit.normal;
                return;
            }

            point = center;
            normal = Vector3.zero;
        }

        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
