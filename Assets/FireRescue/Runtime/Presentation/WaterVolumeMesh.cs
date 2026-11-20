using System;
using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// Bounded 3D water volumes sampled from authoritative spray trajectories. Secondary
    /// droplets have gravity/swept collision but are feedback only: they never reapply dose.
    /// No Rigidbody/renderer per droplet, and no scene/model dependency.
    /// </summary>
    public sealed class WaterVolumeMesh : IDisposable
    {
        const int SecondaryCapacity=96, Sides=12;
        const int VertexCapacity=SprayEmitter.ParticleCapacity*(Sides*2+4)+32;
        readonly Transform owner;
        Transform ignoredActor;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Material material;
        static readonly int FlowTime=Shader.PropertyToID("_PresentationTime");
        readonly Vector3[] vertices=new Vector3[VertexCapacity];
        readonly Vector3[] normals=new Vector3[VertexCapacity];
        readonly Vector4[] tangents=new Vector4[VertexCapacity];
        readonly Vector4[] coordinates=new Vector4[VertexCapacity];
        readonly int[] indices=new int[SprayEmitter.ParticleCapacity*(Sides*12+12)+Sides*6];
        readonly Drop[] drops=new Drop[SecondaryCapacity];
        readonly RaycastHit[] hits=new RaycastHit[16];
        readonly SprayEmitter.ParticleSample[] coreSamples=new SprayEmitter.ParticleSample[SprayEmitter.ParticleCapacity];
        int nextDrop, vertexCount, indexCount;
        uint impactSequence;
        float clock;
        public float PresentationTime {get;private set;}
        const float Step=1f/120;
        struct Drop { public Vector3 Position,Velocity; public float Age,Lifetime,Radius; public uint Seed; public int Bounces; public bool Active; }
        public int StreamBodyCount {get;private set;}
        public int SecondaryCount {get;private set;}
        public int SecondaryCollisions {get;private set;}
        public int CoreSampleCount {get;private set;}
        public int CoreSectionCount {get;private set;}
        public int CoreRingCount {get;private set;}
        public float CoreLength {get;private set;}
        public bool CoreAttachedToNozzle {get;private set;}
        public Vector3 CoreStart {get;private set;}
        public Vector3 CoreEnd {get;private set;}
        public Mesh DisplayMesh=>mesh;
        public Material DisplayMaterial=>material;
        public bool IsVisible=>renderer.enabled;
        public readonly struct MistSample
        {
            public readonly Vector3 Position,Velocity;
            public readonly float Radius,Age01;
            public MistSample(Vector3 position,Vector3 velocity,float radius,float age01)
            {Position=position;Velocity=velocity;Radius=radius;Age01=age01;}
        }
        public bool TryGetMist(int index,out MistSample sample)
        {
            sample=default;if(index<0||index>=drops.Length||!drops[index].Active)return false;
            var drop=drops[index];sample=new MistSample(drop.Position,drop.Velocity,drop.Radius,Mathf.Clamp01(drop.Age/drop.Lifetime));return true;
        }
        public int MistCapacity=>SecondaryCapacity;

        public WaterVolumeMesh(Transform parent,Material material)
        {
            owner=parent;this.material=material;
            var go=new GameObject("Physical water volumes");go.transform.SetParent(parent,false);
            mesh=new Mesh{name="Continuous physical water core"};mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;renderer.enabled=false;
            // Translucent water must not cast the opaque pebble/rod shadows of a solid prop.
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Impact(SprayEmitter.ParticleImpact hit)
        {
            if(hit.Element!=ElementKind.Water||!float.IsFinite(hit.Amount)||hit.Amount<=0||
                !Finite(hit.Position)||!Finite(hit.Velocity)||!Finite(hit.Normal))return;
            var normal=hit.Normal.sqrMagnitude>.001f?hit.Normal.normalized:Vector3.up;
            var tangent=Vector3.ProjectOnPlane(hit.Velocity,normal);
            var side=Vector3.Cross(normal,tangent.sqrMagnitude>.001f?tangent.normalized:Vector3.forward).normalized;
            if(side.sqrMagnitude<.1f)side=Vector3.right;
            var forward=Vector3.Cross(side,normal);
            float normalSpeed=Mathf.Max(0,-Vector3.Dot(hit.Velocity,normal));
            float kick=Mathf.Clamp(normalSpeed*.12f,.06f,1.35f);
            float dose=Mathf.Clamp01(hit.Amount/.035f);
            int count=Mathf.Clamp(Mathf.CeilToInt(1+normalSpeed*.16f+dose),1,5);
            // Grazing hits keep travelling along the surface; a head-on hit fans out.
            // These are short-lived feedback samples, never extra extinguishing packets.
            for(int i=0;i<count;i++)
            {
                uint seed=impactSequence++;float a=SprayEmitter.SampleVariation(seed,101)*Mathf.PI*2;
                drops[nextDrop]=new Drop{Active=true,Position=hit.Position+normal*.055f,
                    Velocity=tangent*.32f+normal*kick*Mathf.Lerp(.45f,1, SprayEmitter.SampleVariation(seed,103))+
                        (side*Mathf.Cos(a)+forward*Mathf.Sin(a))*kick*Mathf.Lerp(.35f,1.2f,SprayEmitter.SampleVariation(seed,107)),
                    Radius=Mathf.Lerp(.006f,.012f,SprayEmitter.SampleVariation(seed,109))*Mathf.Lerp(.65f,1,dose),
                    Lifetime=Mathf.Lerp(.18f,.34f,SprayEmitter.SampleVariation(seed,113)),Seed=seed};
                nextDrop=(nextDrop+1)%drops.Length;
            }
        }

        public void Update(SprayEmitter emitter,float dt,float scale,bool visible)
        {
            if(!float.IsFinite(dt)||dt<0)return;
            PresentationTime+=Mathf.Min(dt,.1f);if(material.HasProperty(FlowTime))material.SetFloat(FlowTime,PresentationTime);
            ignoredActor=emitter?emitter.transform:null;
            clock=Mathf.Min(clock+dt,.1f);
            while(clock+1e-6f>=Step){SimulateDrops(Step);clock-=Step;}
            clock=Mathf.Max(0,clock);
            vertexCount=indexCount=StreamBodyCount=SecondaryCount=0;
            CoreSampleCount=CoreSectionCount=CoreRingCount=0;CoreLength=0;CoreAttachedToNozzle=false;
            if(visible&&emitter)
                for(int i=0;i<emitter.ActiveParticleCount;i++)
                {
                    var sample=emitter.GetParticle(i);if(sample.Element!=ElementKind.Water)continue;
                    StreamBodyCount++;
                    if(sample.CoreWater)coreSamples[CoreSampleCount++]=sample;
                }
            if(visible&&emitter)BuildCore(emitter,scale);
            // Impact spray is rendered as short, fading velocity streaks by the particle
            // view. It is deliberately NOT another pool of glass-ball meshes.
            if(visible)for(int i=0;i<drops.Length;i++)if(drops[i].Active)SecondaryCount++;
            mesh.Clear(false);
            if(vertexCount>0)
            {
                mesh.SetVertices(vertices,0,vertexCount);mesh.SetNormals(normals,0,vertexCount);mesh.SetTangents(tangents,0,vertexCount);
                mesh.SetUVs(0,coordinates,0,vertexCount);
                mesh.SetTriangles(indices,0,indexCount,0);mesh.RecalculateBounds();
            }
            renderer.enabled=visible&&vertexCount>0;
        }

        void BuildCore(SprayEmitter emitter,float scale)
        {
            // Array storage is swap-removed on impact. Sort only the bounded core samples
            // by age; don't accidentally stitch unrelated array neighbors together.
            for(int i=1;i<CoreSampleCount;i++)
            {
                var value=coreSamples[i];int j=i-1;
                while(j>=0&&coreSamples[j].AgeSeconds>value.AgeSeconds){coreSamples[j+1]=coreSamples[j];j--;}
                coreSamples[j+1]=value;
            }
            int lastRing=-1;Vector3 last=default,tangent=Vector3.forward;float radius=0;SprayEmitter.ParticleSample previous=default;
            for(int i=0;i<CoreSampleCount;i++)
            {
                var sample=coreSamples[i];radius=Mathf.Clamp(sample.Radius*scale*.7f,.03f,.085f);
                float ageGap=i>0?sample.AgeSeconds-previous.AgeSeconds:0;
                bool separate=lastRing>=0&&(sample.Burst!=previous.Burst||ageGap>.075f||
                    Vector3.Dot(sample.Velocity.normalized,previous.Velocity.normalized)<.75f||
                    Vector3.Distance(last,sample.Position)>Mathf.Max(.22f,sample.Velocity.magnitude*ageGap*1.75f+.06f));
                if(separate){CapCore(lastRing,last,tangent,false);lastRing=-1;}
                if(lastRing<0)
                {
                    bool attach=emitter.IsSpraying&&sample.Burst==emitter.CurrentWaterBurst&&sample.AgeSeconds<.075f;
                    last=attach?emitter.NozzlePosition:sample.TrailStart;tangent=sample.Velocity.normalized;
                    lastRing=CoreRing(last,tangent,radius,-1);CapCore(lastRing,last,tangent,true);CoreSectionCount++;
                    if(CoreSectionCount==1){CoreStart=last;CoreAttachedToNozzle=attach;}
                }
                var next=sample.Position;bool clipped=emitter.TryClipWaterSpan(last,next,radius,out var contact);
                if(clipped)next=contact;
                if((next-last).sqrMagnitude>.000001f)
                {
                    CoreLength+=Vector3.Distance(last,next);tangent=sample.Velocity.normalized;
                    lastRing=CoreRing(next,tangent,radius*(1+Mathf.Sin(sample.AgeSeconds*45)*.045f),lastRing);last=next;
                }
                if(clipped){CapCore(lastRing,last,tangent,false);lastRing=-1;}
                previous=sample;
            }
            if(lastRing>=0)
            {
                // Close the last tiny distance to a surface, only if a real collision is there.
                // In open air don't project an instant full-length beam ahead of the water.
                if(emitter.TryClipWaterSpan(last,last+tangent*.23f,radius,out var contact)&&(contact-last).sqrMagnitude>.000001f)
                {CoreLength+=Vector3.Distance(last,contact);lastRing=CoreRing(contact,tangent,radius,lastRing);last=contact;}
                CapCore(lastRing,last,tangent,false);
            }
            CoreEnd=last;
        }
        int CoreRing(Vector3 center,Vector3 tangent,float radius,int previous)
        {
            if(tangent.sqrMagnitude<.01f)tangent=Vector3.forward;
            var side=Vector3.Cross(tangent,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=Vector3.right;
            var up=Vector3.Cross(tangent,side).normalized;int start=vertexCount;
            // Only the surface changes width, within the existing collision radius.
            float ripples=.74f+.26f*Mathf.PerlinNoise(CoreLength*3.7f-PresentationTime*6.2f,.37f)+
                Mathf.Sin(CoreLength*19-PresentationTime*31)*.035f;
            ripples=Mathf.Clamp(ripples,.65f,1);
            float width=radius*ripples*Mathf.Lerp(1,.76f,Mathf.Clamp01(CoreLength/5));
            // Duplicate the UV seam: interpolating .92 back to 0 across one face made
            // a broad frozen stripe on the old tube, especially during turns.
            for(int j=0;j<=Sides;j++)
            {
                float a=j*Mathf.PI*2/Sides;var n=side*Mathf.Cos(a)+up*Mathf.Sin(a);
                float lobe=1+.07f*Mathf.Sin(a*3+CoreLength*7-PresentationTime*15);
                coordinates[vertexCount]=new Vector4(j/(float)Sides,CoreLength,1,0);
                var flow=owner.InverseTransformDirection(tangent);tangents[vertexCount]=new Vector4(flow.x,flow.y,flow.z,1);
                vertices[vertexCount]=owner.InverseTransformPoint(center+n*width*lobe);normals[vertexCount++]=owner.InverseTransformDirection(n);
            }
            if(previous>=0)for(int j=0;j<Sides;j++)
            {int next=j+1;Triangle(previous+j,previous+next,start+j);Triangle(previous+next,start+next,start+j);}
            CoreRingCount++;return start;
        }
        void CapCore(int ring,Vector3 point,Vector3 tangent,bool first)
        {
            int center=vertexCount;coordinates[vertexCount]=new Vector4(.5f,CoreLength,1,-1);
            var flow=owner.InverseTransformDirection(tangent);tangents[vertexCount]=new Vector4(flow.x,flow.y,flow.z,1);
            vertices[vertexCount]=owner.InverseTransformPoint(point);normals[vertexCount++]=owner.InverseTransformDirection(first?-tangent:tangent);
            for(int j=0;j<Sides;j++){int next=(j+1)%Sides;if(first)Triangle(center,ring+next,ring+j);else Triangle(center,ring+j,ring+next);}
        }

        void SimulateDrops(float dt)
        {
            for(int i=0;i<drops.Length;i++)
            {
                var d=drops[i];if(!d.Active)continue;
                d.Age+=dt;if(d.Age>=d.Lifetime){d.Active=false;drops[i]=d;continue;}
                d.Velocity+=Vector3.down*(12f*dt);var delta=d.Velocity*dt;
                float length=delta.magnitude;
                int count=length>.00001f?Physics.SphereCastNonAlloc(d.Position,d.Radius,delta/length,hits,length,
                    (1<<0)|(1<<8)|(1<<10),QueryTriggerInteraction.Ignore):0;
                float closest=float.MaxValue;RaycastHit impact=default;
                for(int j=0;j<count;j++)if(hits[j].collider&&(!ignoredActor||!hits[j].transform.IsChildOf(ignoredActor))&&hits[j].distance<closest)
                {closest=hits[j].distance;impact=hits[j];}
                if(closest<float.MaxValue)
                {
                    d.Position=impact.point+impact.normal*(d.Radius+.002f);SecondaryCollisions++;
                    d.Velocity=Vector3.ProjectOnPlane(d.Velocity,impact.normal)*.35f+
                        impact.normal*Mathf.Abs(Vector3.Dot(d.Velocity,impact.normal))*.12f;
                    if(++d.Bounces>=2)d.Active=false;
                }
                else d.Position+=delta;
                drops[i]=d;
            }
        }

        void Triangle(int a,int b,int c){indices[indexCount++]=a;indices[indexCount++]=b;indices[indexCount++]=c;}
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        public void Clear(){Array.Clear(drops,0,drops.Length);clock=0;impactSequence=0;renderer.enabled=false;StreamBodyCount=SecondaryCount=0;
            CoreSampleCount=CoreSectionCount=CoreRingCount=0;CoreLength=0;CoreAttachedToNozzle=false;}
        public void Dispose(){if(mesh)UnityEngine.Object.Destroy(mesh);if(renderer)UnityEngine.Object.Destroy(renderer.gameObject);}
    }
}
