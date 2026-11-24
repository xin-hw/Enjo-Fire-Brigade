using UnityEngine;
namespace FireRescue
{
    // Read-only tongues and brief radial flare. Never applies fire or damages food.
    public sealed class OilPanFireView:MonoBehaviour
    {
        [SerializeField] ObjectFire fire;
        [SerializeField] CookingOilArea area;
        [SerializeField] CookingOilIncident incident;
        [SerializeField] Light bounceLight;
        Transform[] flames;
        Transform[] patches;
        Vector3[] patchPositions;
        Material material;
        Mesh mesh;
        float clock;
        float bounceIntensity;
        public bool AutomaticTick {get;set;}=true;
        public int ActiveTongueCount {get;private set;}
        public float BurstAmount {get;private set;}
        public void Initialize(ObjectFire state)=>fire=state;
        public void SetArea(CookingOilArea value)=>area=value;
        public void SetIncident(CookingOilIncident value)=>incident=value;
        void Awake()
        {
            if (bounceLight)
            {
                bounceIntensity = bounceLight.intensity;
                bounceLight.enabled = false;
            }
            EnsurePresentation();
        }
        void EnsurePresentation()
        {
            if(flames!=null)return;
            material=FireSurface.CreateMaterial();mesh=FireSurface.CreateTongueMesh();
            var properties=new MaterialPropertyBlock();
            flames=new Transform[5];for(int i=0;i<flames.Length;i++)flames[i]=CreateTongue("Flame view "+i,i*1.73f);
            if(area)
            {
                patches=new Transform[72];patchPositions=new Vector3[72];
                for(int i=0;i<patches.Length;i++)
                {
                    int x=i%8,z=i/8;patchPositions[i]=new Vector3(Mathf.Lerp(area.Minimum.x+.12f,area.Maximum.x-.12f,x/7f),.12f,Mathf.Lerp(area.Minimum.y+.12f,area.Maximum.y-.12f,z/8f));
                    patchPositions[i]+=new Vector3(Mathf.Sin(i*17.13f)*.13f,0,Mathf.Cos(i*23.71f)*.13f);
                    if(Physics.Raycast(patchPositions[i]+Vector3.up*2,Vector3.down,out var surface,3,(1<<8)|(1<<0),QueryTriggerInteraction.Ignore))patchPositions[i].y=surface.point.y+.02f;
                    patches[i]=CreateTongue("Oil-spill flame "+i,i*1.91f);
                }
            }
            Transform CreateTongue(string name,float phase)
            {
                var go=new GameObject(name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                // A few hot cores make the pan source read as an active flame cluster,
                // while remaining within this fixed five-renderer presentation pool.
                FireSurface.SetPhase(renderer,properties,phase,Mathf.Repeat(phase*.19f,1f)>.52f?1f:0f);go.SetActive(false);return go.transform;
            }
        }
        void LateUpdate(){if(AutomaticTick)TickPresentation(Time.deltaTime);}
        public void TickPresentation(float dt)
        {
            if(!isActiveAndEnabled||!float.IsFinite(dt)||dt<0)return;
            EnsurePresentation();clock+=Mathf.Min(dt,.1f);
            if(material.HasProperty(FireSurface.Clock))material.SetFloat(FireSurface.Clock,clock);
            float intensity=fire?Mathf.Max(0,fire.Intensity):0;
            if (bounceLight)
            {
                bounceLight.intensity = bounceIntensity * Mathf.Sqrt(Mathf.Clamp01(intensity));
                bounceLight.enabled = intensity > 0f;
            }
            float age=incident?incident.ExplosionAge:100;
            BurstAmount=intensity>0&&age>=0&&age<.55f
                ?Mathf.SmoothStep(0,1,age/.05f)*(1-Mathf.SmoothStep(0,1,(age-.05f)/.5f)):0;
            ActiveTongueCount=0;
            for(int i=0;i<flames.Length;i++)
            {
                var f=flames[i];f.gameObject.SetActive(intensity>0);if(intensity<=0)continue;ActiveTongueCount++;
                bool inner=i==1||i==3;float s=Mathf.Sqrt(intensity),a=i*2.4f;
                // Fixed per-tongue proportions keep this bounded source cluster from
                // reading as five duplicated icons; phase-driven motion remains shader-only.
                float shape=.86f+Mathf.Repeat(i*.381966f,.28f);
                float radius=(inner?.035f:.10f)*s+BurstAmount*.14f;
                f.localPosition=new Vector3(Mathf.Cos(a)*radius,.07f,Mathf.Sin(a)*radius);
                float width=(inner?.11f:.20f)*s*(1+BurstAmount*.65f)*shape;
                f.localScale=new Vector3(width,(inner?.45f:.72f)*s*(1+BurstAmount*.35f)*(1.13f-shape*.13f),width);
                f.localRotation=Quaternion.Euler(0,i*37,0);
            }
            if(patches!=null)for(int i=0;i<patches.Length;i++)
            {
                var f=patches[i];bool hot=area&&area.IsHot(patchPositions[i]);f.gameObject.SetActive(hot);if(!hot)continue;
                ActiveTongueCount++;f.position=patchPositions[i];
                float shape=.84f+Mathf.Repeat(i*.618034f,.30f);
                f.localScale=new Vector3(.18f*shape,.4f*Mathf.Sqrt(Mathf.Max(1,intensity))*(1.14f-shape*.14f),.18f*shape);f.rotation=Quaternion.Euler(0,i*31,0);
            }
        }
        void OnDisable(){ActiveTongueCount=0;BurstAmount=0;clock=0;if(bounceLight)bounceLight.enabled=false;if(flames!=null)foreach(var f in flames)if(f)f.gameObject.SetActive(false);if(patches!=null)foreach(var f in patches)if(f)f.gameObject.SetActive(false);}
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
