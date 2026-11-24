using UnityEngine;

namespace FireRescue
{
    /// <summary>How the first-rescue water packets are presented. This never owns fluid simulation.</summary>
    public enum FirstRescueEffectMode
    {
        BillboardParticles,
        DropletMeshes,
        Hybrid
    }

    /// <summary>
    /// Bounded presentation for the toilet-water rescue segment. It observes SprayEmitter and
    /// FireField only: changing, disabling, or removing this component cannot change gameplay.
    /// </summary>
    public sealed class FirstRescueEffects : MonoBehaviour
    {
        const int MaxBillboardCapacity = 500;
        const int DefaultBillboardCapacity = 320;
        const int DefaultDropletCapacity = 64;
        const int DefaultSplashCapacity = 32;
        const int DefaultFireCapacity = 48;
        const int FireAtmosphereCapacity = 40;

        [Header("Sources (optional until Initialize is called)")]
        [SerializeField] SprayEmitter emitter;
        [SerializeField] FireField field;

        [Header("Presentation")]
        [SerializeField] FirstRescueEffectMode mode = FirstRescueEffectMode.Hybrid;
        [SerializeField, Range(1, MaxBillboardCapacity)] int billboardCapacity = DefaultBillboardCapacity;
        [SerializeField, Range(1, 64)] int dropletCapacity = DefaultDropletCapacity;
        [SerializeField, Range(1, 32)] int splashCapacity = DefaultSplashCapacity;
        [SerializeField, Range(1, 512)] int fireCapacity = DefaultFireCapacity;
        [SerializeField] bool showFire = true;
        [SerializeField] bool groundHeatOnly;
        [SerializeField] bool spaciousFire;
        [SerializeField] bool useSurfaceWater;
        [SerializeField, Range(1f,3f)] float waterVisualScale=1f;
        [SerializeField, Min(.1f)] float wetMarkLifetime = 2.4f;

        ParticleSystem waterParticles;
        ParticleSystem fireAtmosphereParticles;
        ParticleSystem.Particle[] particleBuffer;
        ParticleSystem.Particle[] fireAtmosphereBuffer;
        Transform[] droplets;
        Renderer[] dropletRenderers;
        ImpactVisual[] impacts;
        FireVisual[] fires;
        Material waterMaterial;
        Material wetMaterial;
        Material fireMaterial;
        MaterialPropertyBlock fireProperties;
        float fireClock;
        Material billboardMaterial;
        Texture2D billboardTexture;
        Material fireAtmosphereMaterial;
        Texture2D fireAtmosphereTexture;
        Mesh flameMesh;
        Mesh puddleMesh;
        WaterVolumeMesh waterVolumes;
        static readonly int FlowTime=Shader.PropertyToID("_PresentationTime");
        static readonly int WetFade=Shader.PropertyToID("_WetFade");
        static readonly int ImpactAge=Shader.PropertyToID("_ImpactAge");
        Material heatMaterial;
        Mesh dangerMesh;
        Renderer dangerRenderer;
        Texture2D heatTexture;
        Color32[] heatPixels;
        Vector4 heatWorld;
        static readonly int HeatMap=Shader.PropertyToID("_HeatMap");
        static readonly int HeatMapWorld=Shader.PropertyToID("_HeatMapWorld");
        bool subscribed;
        int nextImpact;

        public FirstRescueEffectMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public int ActiveBillboardCount { get; private set; }
        public int ActiveDropletCount { get; private set; }
        public int ActiveImpactMistCount {get;private set;}
        public int ActiveInFlightSprayCount {get;private set;}
        public int ActiveSplashCount { get; private set; }
        public int ActiveFireVisualCount { get; private set; }
        public int ActiveFlameCount { get; private set; }
        public int ActiveHeatMarkCount { get; private set; }
        public int ActiveFireAtmosphereCount { get; private set; }
        public float WaterVisualScale=>waterVisualScale;
        public bool IsSubscribed => subscribed;
        public WaterVolumeMesh WaterVolumes=>waterVolumes;
        public void UseSurfaceWater() => useSurfaceWater = true;

        /// <summary>Idempotent explicit wiring for scene setup code and replaceable level assets.</summary>
        public void Initialize(SprayEmitter source, FireField fireField)
        {
            Unsubscribe();
            emitter = source;
            field = fireField;
            Subscribe();
        }

        void Awake()
        {
            EnsurePresentation();
        }

        void OnEnable()
        {
            EnsurePresentation();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            ClearVisuals();
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (waterMaterial) Destroy(waterMaterial);
            if (wetMaterial) Destroy(wetMaterial);
            if (fireMaterial) Destroy(fireMaterial);
            if (billboardMaterial) Destroy(billboardMaterial);
            if (billboardTexture) Destroy(billboardTexture);
            if (fireAtmosphereMaterial) Destroy(fireAtmosphereMaterial);
            if (fireAtmosphereTexture) Destroy(fireAtmosphereTexture);
            if (flameMesh) Destroy(flameMesh);
            if (dangerMesh) Destroy(dangerMesh);
            if (heatMaterial) Destroy(heatMaterial);
            if (heatTexture) Destroy(heatTexture);
            if (puddleMesh) Destroy(puddleMesh);
            waterVolumes?.Dispose();
        }

        void LateUpdate()=>TickPresentation(Time.deltaTime);
        public void TickPresentation(float dt)
        {
            if (!isActiveAndEnabled||float.IsNaN(dt)||float.IsInfinity(dt)||dt<0) return;
            EnsurePresentation();
            if (!useSurfaceWater)
            {
                waterVolumes.Update(emitter,dt,waterVisualScale,mode==FirstRescueEffectMode.Hybrid);
                UpdateWater();
                if(wetMaterial.HasProperty(FlowTime))wetMaterial.SetFloat(FlowTime,waterVolumes.PresentationTime);
                UpdateImpacts(dt);
            }
            fireClock+=Mathf.Min(dt,.1f);
            if(fireMaterial.HasProperty(FireSurface.Clock))fireMaterial.SetFloat(FireSurface.Clock,fireClock);
            UpdateFire();
        }

        void Subscribe()
        {
            if (useSurfaceWater || subscribed || !emitter) return;
            emitter.ParticleImpacted += OnParticleImpacted;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            if (emitter) emitter.ParticleImpacted -= OnParticleImpacted;
            subscribed = false;
        }

        // This event is notification-only. FireField and ElementReaction remain the gameplay writers.
        void OnParticleImpacted(SprayEmitter.ParticleImpact impact)
        {
            if (!isActiveAndEnabled || impact.Element != ElementKind.Water || impacts == null || impacts.Length == 0) return;
            waterVolumes.Impact(impact);
            ImpactVisual visual = null;
            for(int i=0;i<impacts.Length;i++)
                if(impacts[i].Active&&Vector3.Dot(impacts[i].Normal,impact.Normal)>.9f&&
                    (impacts[i].Position-impact.Position).sqrMagnitude<.0625f){visual=impacts[i];break;}
            bool merge=visual!=null;
            if(!merge){visual=impacts[nextImpact];nextImpact=(nextImpact+1)%impacts.Length;visual.WetAge=0;}
            visual.Age = 0f;
            visual.Lifetime = Mathf.Max(.15f, wetMarkLifetime);
            if (field && impact.Position.y <= field.Origin.y + .1f && impact.Normal.y > .7f &&
                field.TryGetCell(impact.Position, out var cell))
                visual.Lifetime = Mathf.Max(.15f, field.GetCell(cell.x, cell.y).WaterRemaining);
            visual.Position = impact.Position + impact.Normal * .008f;
            visual.Normal = impact.Normal.sqrMagnitude > .001f ? impact.Normal.normalized : Vector3.up;
            var tangent=Vector3.ProjectOnPlane(impact.Velocity,visual.Normal);
            float speed=impact.Velocity.magnitude;
            visual.Tangent=tangent.sqrMagnitude>.0001f?tangent.normalized:Vector3.Cross(visual.Normal,Vector3.right).normalized;
            if(visual.Tangent.sqrMagnitude<.01f)visual.Tangent=Vector3.forward;
            visual.Stretch=1f+(speed>.001f?tangent.magnitude/speed:0f)*.8f;
            visual.Kick=Mathf.Clamp(Mathf.Abs(Vector3.Dot(impact.Velocity,visual.Normal))*.018f,.015f,.16f);
            visual.Amount = Mathf.Clamp((merge?visual.Amount:0)+impact.Amount*4f,.35f,1.8f);
            visual.Active = true;
        }

        void UpdateWater()
        {
            int count = emitter ? Mathf.Min(emitter.ActiveParticleCount, particleBuffer.Length) : 0;
            bool billboards = mode == FirstRescueEffectMode.BillboardParticles || mode == FirstRescueEffectMode.Hybrid;
            bool meshDrops = mode == FirstRescueEffectMode.DropletMeshes;

            ActiveBillboardCount = ActiveImpactMistCount = ActiveInFlightSprayCount = 0;
            if (billboards && emitter)
            {
                for (int i = 0; i < count; i++)
                {
                    SprayEmitter.ParticleSample sample = emitter.GetParticle(i);
                    if (sample.Element != ElementKind.Water) continue;
                    bool hybrid=mode==FirstRescueEffectMode.Hybrid;
                    if(hybrid&&(sample.CoreWater||SprayEmitter.SampleVariation(sample.Seed,149)>.18f))continue;
                    particleBuffer[ActiveBillboardCount++] = new ParticleSystem.Particle
                    {
                        position = sample.Position,
                        // Mist is subordinate to the lit 3D body in Hybrid, not a second opaque jet.
                        startColor = new Color(.62f,.85f,1f,hybrid?.22f:.95f),
                        startSize = hybrid?.018f:Mathf.Max(.06f,sample.Radius*1.8f)*waterVisualScale,
                        remainingLifetime = 1f,
                        startLifetime = 1f,
                        velocity = sample.Velocity
                    };
                    ActiveInFlightSprayCount++;
                }
            }
            if(mode==FirstRescueEffectMode.Hybrid)
                for(int i=0;i<waterVolumes.MistCapacity&&ActiveBillboardCount<particleBuffer.Length;i++)
                {
                    if(!waterVolumes.TryGetMist(i,out var mist))continue;
                    float fade=1-mist.Age01;
                    particleBuffer[ActiveBillboardCount++]=new ParticleSystem.Particle
                    {
                        position=mist.Position,velocity=mist.Velocity,
                        startColor=new Color(.4f,.73f,.94f,.72f*fade*fade),
                        startSize=mist.Radius*8*Mathf.Lerp(1,.3f,mist.Age01),
                        startLifetime=1,remainingLifetime=1
                    };
                    ActiveImpactMistCount++;
                }
            waterParticles.SetParticles(particleBuffer, ActiveBillboardCount);

            ActiveDropletCount = 0;
            if (meshDrops && emitter)
            {
                int step = Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(1, count) / droplets.Length));
                for (int i = 0; i < count && ActiveDropletCount < droplets.Length; i += step)
                {
                    SprayEmitter.ParticleSample sample = emitter.GetParticle(i);
                    if (sample.Element != ElementKind.Water) continue;
                    Transform drop = droplets[ActiveDropletCount];
                    Vector3 velocity = sample.Velocity;
                    drop.position = sample.Position;
                    if (velocity.sqrMagnitude > .0001f) drop.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
                    float radius = Mathf.Max(.035f, sample.Radius * 1.15f)*waterVisualScale;
                    drop.localScale = new Vector3(radius, radius, Mathf.Max(radius * 1.8f, velocity.magnitude * .025f));
                    dropletRenderers[ActiveDropletCount].enabled = true;
                    ActiveDropletCount++;
                }
            }
            for (int i = ActiveDropletCount; i < dropletRenderers.Length; i++) dropletRenderers[i].enabled = false;
        }

        void UpdateImpacts(float dt)
        {
            ActiveSplashCount = 0;
            for (int i = 0; i < impacts.Length; i++)
            {
                ImpactVisual visual = impacts[i];
                if (!visual.Active) continue;
                visual.Age += dt;
                visual.WetAge += dt;
                if (visual.Age >= visual.Lifetime)
                {
                    visual.Active = false;
                    visual.SplashRenderer.enabled = false;
                    visual.WetRenderer.enabled = false;
                    continue;
                }

                ActiveSplashCount++;
                Quaternion rotation = Quaternion.LookRotation(visual.Tangent, visual.Normal);
                // Spread quickly on contact, then remain a thin fading film. Do not
                // grow a perfectly opaque dot for several seconds then pop it away.
                float wetScale = Mathf.Lerp(.12f, .52f, 1-Mathf.Exp(-visual.WetAge*5f)) * visual.Amount;
                visual.Wet.position = visual.Position;
                visual.Wet.rotation = rotation;
                visual.Wet.localScale = new Vector3(wetScale, .008f, wetScale*visual.Stretch);
                visual.WetRenderer.enabled = true;
                visual.Properties.SetFloat(WetFade,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,1f,visual.Age/visual.Lifetime)));
                visual.Properties.SetFloat(ImpactAge,visual.Age);
                visual.WetRenderer.SetPropertyBlock(visual.Properties);

                bool splashVisible = visual.Age < .24f;
                visual.SplashRenderer.enabled = splashVisible&&mode!=FirstRescueEffectMode.Hybrid;
                if (splashVisible)
                {
                    float splashScale = Mathf.Lerp(.06f, .24f, visual.Age / .24f) * visual.Amount;
                    float t=visual.Age/.24f;
                    visual.Splash.position = visual.Position + visual.Normal * (.018f+visual.Kick*4*t*(1-t));
                    visual.Splash.rotation = rotation;
                    visual.Splash.localScale = new Vector3(splashScale, .03f, splashScale*visual.Stretch);
                }
            }
        }

        void UpdateFire()
        {
            ActiveFireVisualCount = ActiveFlameCount = ActiveHeatMarkCount = 0;
            ActiveFireAtmosphereCount=0;
            if (!showFire || !field)
            {
                HideFires(0);
                dangerRenderer.enabled=false;
                UpdateFireAtmosphere(0);
                return;
            }

            // A single surface reads the live field. Flame placement and player
            // contact still use the unfiltered simulation cells.
            EnsureHeatMap();
            int burningCount=0, heatIndex=0;
            bool heatChanged=false;
            for (int z = 0; z < field.Height; z++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    FireCellState state = field.GetCell(x, z);
                    byte heat=state.IsBurning ? (byte)Mathf.RoundToInt(255f*Mathf.Sqrt(state.Intensity)) : (byte)0;
                    if (heatPixels[heatIndex].r!=heat)
                    {
                        heatPixels[heatIndex]=new Color32(heat,state.IsBurning ? (byte)255 : (byte)0,0,255);
                        heatChanged=true;
                    }
                    heatIndex++;
                    if (state.IsBurning) burningCount++;
                }
            }
            if (heatChanged && heatTexture)
            {
                heatTexture.SetPixels32(heatPixels);
                heatTexture.Apply(false,false);
            }
            if (heatMaterial.HasProperty(FireSurface.Clock)) heatMaterial.SetFloat(FireSurface.Clock,fireClock);
            ActiveFireVisualCount=burningCount;
            ActiveHeatMarkCount=burningCount;
            dangerRenderer.enabled=burningCount>0 && heatTexture;
            if (groundHeatOnly) return;

            int flameCount=0;
            int stride=Mathf.Max(1,Mathf.CeilToInt(burningCount/(float)Mathf.Max(1,fires.Length)));
            for (int z = 0; z < field.Height && flameCount < fires.Length; z++)
            {
                for (int x = 0; x < field.Width && flameCount < fires.Length; x++)
                {
                    FireCellState state = field.GetCell(x, z);
                    if (!state.IsBurning) continue;
                    bool hasNeighbor = Burning(x-1,z)||Burning(x+1,z)||Burning(x,z-1)||Burning(x,z+1);
                    int cellKey=z*field.Width+x;
                    // A stable hash distributes the fixed flame pool through the full fire,
                    // rather than rendering only the first rows of a large room.
                    bool withinBudget=stride==1||FireSurface.CellHash(cellKey)%stride==0;
                    // A broad fire reads as a few threatening columns, not a repeated
                    // checkerboard of identical icons. Isolated cells stay visible.
                    bool keepForSparse=!spaciousFire || FireSurface.CellHash(cellKey+941)%5==0 || !hasNeighbor;
                    if(!withinBudget||!keepForSparse)continue;
                    FireVisual visual = fires[flameCount++];
                    float strength = Mathf.Clamp01(state.Intensity);
                    // Stable cell phase survives pool compaction; only the GPU surface moves.
                    if(visual.CellKey!=cellKey)
                    {
                        float phase=x*1.618f+z*2.414f;
                        FireSurface.SetPhase(visual.OuterRenderer,fireProperties,phase);
                        FireSurface.SetPhase(visual.InnerRenderer,fireProperties,phase+3.7f,1f);
                        visual.CellKey=cellKey;
                    }
                    float response=Mathf.Sqrt(strength);
                    float variation=.90f+(FireSurface.CellHash(cellKey+187)%100)*.002f;
                    // Fewer, taller columns give a large fire a readable, urgent silhouette.
                    float height = (spaciousFire ? 1f : .86f)*response*variation;
                    Vector3 center = field.CellCenter(x, z);
                    var offset=new Vector3(Mathf.Sin(x*17.3f+z*11.1f),0,Mathf.Cos(z*13.7f+x*23.1f))*field.CellSize*.035f;
                    visual.Outer.position = center + offset + Vector3.up*.015f;
                    float width = field.CellSize * (spaciousFire ? .80f : .94f)*response;
                    visual.Outer.localScale = new Vector3(width, height, width);
                    // The inner core sits inside the same column instead of reading as a second icon.
                    visual.Inner.position = center + offset + new Vector3(width*.09f,.018f,-width*.025f);
                    visual.Inner.localScale = Vector3.Scale(visual.Outer.localScale,new Vector3(.53f,.64f,.53f));
                    bool flame = !spaciousFire || (x+z*2)%3==0 || !hasNeighbor;
                    visual.OuterRenderer.enabled = flame;
                    visual.InnerRenderer.enabled = flame;
                    if(flame)ActiveFlameCount++;
                }
            }
            HideFires(flameCount);
            UpdateFireAtmosphere(burningCount);
        }

        void UpdateFireAtmosphere(int burningCount)
        {
            if(!fireAtmosphereParticles||!field)return;
            if(burningCount<=0){ActiveFireAtmosphereCount=0;fireAtmosphereParticles.Clear(true);return;}
            int count=0;
            int stride=Mathf.Max(1,Mathf.CeilToInt(burningCount/(float)FireAtmosphereCapacity));
            for(int z=0;z<field.Height&&count<FireAtmosphereCapacity;z++)
            for(int x=0;x<field.Width&&count<FireAtmosphereCapacity;x++)
            {
                var state=field.GetCell(x,z);if(!state.IsBurning)continue;
                int key=z*field.Width+x;if(stride>1&&FireSurface.CellHash(key+491)%stride!=0)continue;
                float phase=Mathf.Repeat(key*.618034f,1f);
                float rise=Mathf.Repeat(fireClock*(.33f+phase*.16f)+phase,1f);
                float strength=Mathf.Sqrt(Mathf.Clamp01(state.Intensity));
                Vector3 root=field.CellCenter(x,z);
                float sway=Mathf.Sin(fireClock*2.4f+phase*17f)*field.CellSize*.12f*rise;
                bool ember=(key&3)==0;
                float riseHeight=ember ? .52f : 1f;
                fireAtmosphereBuffer[count++]=new ParticleSystem.Particle
                {
                    position=root+new Vector3(sway,.08f+rise*riseHeight*strength,Mathf.Cos(fireClock*1.9f+phase*13f)*field.CellSize*.07f*rise),
                    startSize=Mathf.Lerp(ember ? .025f : .12f,ember ? .05f : .30f,rise)*strength,
                    startColor=ember
                        ?new Color(1f,.26f,.025f,Mathf.Lerp(.72f,.08f,rise)*strength)
                        :new Color(.13f,.075f,.045f,Mathf.Lerp(.13f,.015f,rise)*strength),
                    remainingLifetime=1f,startLifetime=1f
                };
            }
            ActiveFireAtmosphereCount=count;
            fireAtmosphereParticles.SetParticles(fireAtmosphereBuffer,count);
        }

        void EnsureHeatMap()
        {
            if (!heatMaterial.HasProperty(HeatMap))
            {
                if (heatPixels==null || heatPixels.Length!=field.Width*field.Height)
                    heatPixels=new Color32[field.Width*field.Height];
                return;
            }
            if (!heatTexture || heatTexture.width!=field.Width || heatTexture.height!=field.Height)
            {
                if (heatTexture) Destroy(heatTexture);
                heatTexture=new Texture2D(field.Width,field.Height,TextureFormat.RGBA32,false,true)
                {
                    name="Ground ash coverage", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp
                };
                heatPixels=new Color32[field.Width*field.Height];
                heatMaterial.SetTexture(HeatMap,heatTexture);
            }
            float width=field.Width*field.CellSize, depth=field.Height*field.CellSize;
            var mapping=new Vector4(field.Origin.x,field.Origin.z,1f/width,1f/depth);
            // Floor finish is 4.5 mm above the field origin and surface water is 6 mm above it.
            float height = field.Origin.y + (useSurfaceWater ? .005f : .035f);
            if (mapping == heatWorld && dangerRenderer.transform.position.y == height) return;
            heatWorld=mapping;
            heatMaterial.SetVector(HeatMapWorld,mapping);
            dangerRenderer.transform.SetPositionAndRotation(
                new Vector3(field.Origin.x + width * .5f, height, field.Origin.z + depth * .5f), Quaternion.identity);
            dangerRenderer.transform.localScale=new Vector3(width,1f,depth);
        }

        bool Burning(int x,int z)=>x>=0&&z>=0&&x<field.Width&&z<field.Height&&field.GetCell(x,z).IsBurning;

        void HideFires(int from)
        {
            for (int i = from; i < fires.Length; i++)
            {
                fires[i].OuterRenderer.enabled = false;
                fires[i].InnerRenderer.enabled = false;
            }
        }

        void ClearVisuals()
        {
            fireClock=0;
            ActiveBillboardCount = ActiveDropletCount = ActiveSplashCount = ActiveFireVisualCount = ActiveFlameCount = ActiveHeatMarkCount = 0;
            ActiveImpactMistCount=ActiveInFlightSprayCount=ActiveFireAtmosphereCount=0;
            if (waterParticles) waterParticles.Clear(true);
            if (fireAtmosphereParticles) fireAtmosphereParticles.Clear(true);
            waterVolumes?.Clear();
            if (dropletRenderers != null) for (int i = 0; i < dropletRenderers.Length; i++) dropletRenderers[i].enabled = false;
            if (impacts != null) for (int i = 0; i < impacts.Length; i++)
            {
                impacts[i].Active = false;
                impacts[i].SplashRenderer.enabled = false;
                impacts[i].WetRenderer.enabled = false;
            }
            if (fires != null) HideFires(0);
            if(dangerRenderer)dangerRenderer.enabled=false;
        }

        void EnsurePresentation()
        {
            if (particleBuffer != null) return;
            fireProperties=new MaterialPropertyBlock();
            billboardCapacity = Mathf.Clamp(billboardCapacity, 1, MaxBillboardCapacity);
            dropletCapacity = Mathf.Clamp(dropletCapacity, 1, 64);
            splashCapacity = Mathf.Clamp(splashCapacity, 1, 32);
            fireCapacity = Mathf.Clamp(fireCapacity, 1, 512);
            CreateMaterials();
            waterVolumes=new WaterVolumeMesh(transform,waterMaterial);
            puddleMesh=CreatePuddleMesh();
            if (!groundHeatOnly) flameMesh = FireSurface.CreateTongueMesh();
            heatMaterial=FireSurface.CreateMaterial();
            heatMaterial.name="Cartoon ground ash";
            heatMaterial.renderQueue=2985;
            dangerMesh=FireSurface.CreateFootprintMesh();
            dangerMesh.name="Ground ash surface";
            var floorUv=new Vector4(0f,0f,-2f,0f);
            dangerMesh.SetUVs(0,new[]{floorUv,floorUv,floorUv,floorUv});
            var danger=new GameObject("Ground Ash");danger.transform.SetParent(transform,false);
            danger.AddComponent<MeshFilter>().sharedMesh=dangerMesh;dangerRenderer=danger.AddComponent<MeshRenderer>();
            dangerRenderer.sharedMaterial=heatMaterial;dangerRenderer.enabled=false;
            dangerRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;dangerRenderer.receiveShadows=false;
            particleBuffer = new ParticleSystem.Particle[billboardCapacity];
            waterParticles = CreateWaterParticles();
            if (!groundHeatOnly)
            {
                fireAtmosphereBuffer=new ParticleSystem.Particle[FireAtmosphereCapacity];
                fireAtmosphereParticles=CreateFireAtmosphereParticles();
            }
            droplets = new Transform[dropletCapacity];
            dropletRenderers = new Renderer[dropletCapacity];
            for (int i = 0; i < droplets.Length; i++) CreateDrop(i);
            impacts = new ImpactVisual[splashCapacity];
            for (int i = 0; i < impacts.Length; i++) impacts[i] = CreateImpact(i);
            fires = new FireVisual[groundHeatOnly ? 0 : fireCapacity];
            for (int i = 0; i < fires.Length; i++) fires[i] = CreateFire(i);
        }

        void CreateMaterials()
        {
            // Resources keeps this runtime-created material's shader in future player builds.
            var waterShader=Resources.Load<Shader>("FlowingWater");
            if(waterShader&&waterShader.isSupported)
            {
                waterMaterial=new Material(waterShader){name="Translucent flowing water",color=new Color(.18f,.65f,.86f,1)};
                waterMaterial.SetFloat("_Opacity",.64f);
                wetMaterial=new Material(waterShader){name="Thin wet surface",color=new Color(.1f,.48f,.7f,1)};
                wetMaterial.SetFloat("_Opacity",.38f);
            }
            else
            {
                waterMaterial=CreateStandardMaterial(new Color(.38f,.8f,.99f),false);
                wetMaterial=CreateStandardMaterial(new Color(.19f,.62f,.86f,.38f),false);
                wetMaterial.SetFloat("_Mode",2);wetMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                wetMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                wetMaterial.SetInt("_ZWrite",0);wetMaterial.EnableKeyword("_ALPHABLEND_ON");wetMaterial.renderQueue=3000;
            }
            fireMaterial=FireSurface.CreateMaterial();
        }

        static Material CreateStandardMaterial(Color color, bool emission)
        {
            Shader shader = Shader.Find("Standard");
            Material material = new Material(shader) { color = color };
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .7f);
            }
            return material;
        }

        ParticleSystem CreateWaterParticles()
        {
            GameObject root = new GameObject("Water Billboards");
            root.transform.SetParent(transform, false);
            ParticleSystem system = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = billboardCapacity;
            main.simulationSpeed = 0f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            var shape=system.shape;shape.enabled=false;var collision=system.collision;collision.enabled=false;
            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3.2f;
            renderer.velocityScale = .02f;
            renderer.cameraVelocityScale=0;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            billboardTexture = CreateRadialTexture();
            billboardMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = billboardTexture };
            renderer.sharedMaterial = billboardMaterial;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play();
            return system;
        }

        ParticleSystem CreateFireAtmosphereParticles()
        {
            GameObject root=new GameObject("Bounded rising embers and smoke");root.transform.SetParent(transform,false);
            ParticleSystem system=root.AddComponent<ParticleSystem>();
            var main=system.main;main.loop=false;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=FireAtmosphereCapacity;main.simulationSpeed=0f;
            var emission=system.emission;emission.enabled=false;var shape=system.shape;shape.enabled=false;var collision=system.collision;collision.enabled=false;
            var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            fireAtmosphereTexture=CreateFireAtmosphereTexture();
            fireAtmosphereMaterial=new Material(Shader.Find("Sprites/Default")){mainTexture=fireAtmosphereTexture,name="Soft fire embers and smoke"};
            renderer.sharedMaterial=fireAtmosphereMaterial;system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);system.Play();return system;
        }

        void CreateDrop(int index)
        {
            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            drop.name = "Water Droplet " + index;
            drop.transform.SetParent(transform, false);
            drop.GetComponent<Collider>().enabled = false;
            Object.Destroy(drop.GetComponent<Collider>());
            Renderer renderer = drop.GetComponent<Renderer>();
            renderer.sharedMaterial = waterMaterial;
            renderer.enabled = false;
            droplets[index] = drop.transform;
            dropletRenderers[index] = renderer;
        }

        ImpactVisual CreateImpact(int index)
        {
            GameObject root = new GameObject("Water Impact " + index);
            root.transform.SetParent(transform, false);
            Transform wet = CreateVisualPrimitive(root.transform, "Wet Mark", PrimitiveType.Sphere, wetMaterial, out Renderer wetRenderer);
            wet.GetComponent<MeshFilter>().sharedMesh=puddleMesh;
            wetRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            Transform splash = CreateVisualPrimitive(root.transform, "Splash", PrimitiveType.Sphere, waterMaterial, out Renderer splashRenderer);
            wetRenderer.enabled = false;
            splashRenderer.enabled = false;
            return new ImpactVisual { Wet = wet, Splash = splash, WetRenderer = wetRenderer, SplashRenderer = splashRenderer,
                Properties=new MaterialPropertyBlock() };
        }

        FireVisual CreateFire(int index)
        {
            GameObject root = new GameObject("Fire Cluster " + index);
            root.transform.SetParent(transform, false);
            Transform outer = CreateFlameVisual(root.transform, "Orange Flame", fireMaterial, out Renderer outerRenderer);
            Transform inner = CreateFlameVisual(root.transform, "Yellow Flame", fireMaterial, out Renderer innerRenderer);
            outerRenderer.enabled = false;
            innerRenderer.enabled = false;
            return new FireVisual { Outer = outer, Inner = inner, OuterRenderer = outerRenderer, InnerRenderer = innerRenderer };
        }

        Transform CreateFlameVisual(Transform parent, string name, Material material, out Renderer renderer)
        {
            GameObject visual = new GameObject(name);
            visual.transform.SetParent(parent, false);
            MeshFilter filter = visual.AddComponent<MeshFilter>();
            filter.sharedMesh = flameMesh;
            MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows=false;
            renderer = meshRenderer;
            return visual.transform;
        }

        static Transform CreateVisualPrimitive(Transform parent, string name, PrimitiveType type, Material material, out Renderer renderer)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.GetComponent<Collider>().enabled = false;
            Object.Destroy(visual.GetComponent<Collider>());
            renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return visual.transform;
        }

        static Texture2D CreateRadialTexture()
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FirstRescueWaterDrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f;
                float v = (y + .5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(u * u + v * v);
                float alpha = Mathf.Exp(-distance*distance*2)*Mathf.Clamp01(1-distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D CreateFireAtmosphereTexture()
        {
            const int size=24;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="SoftFireAtmosphere",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1;
                float falloff=Mathf.Exp(-(u*u+v*v)*3.7f)*Mathf.Clamp01(1-Mathf.Sqrt(u*u+v*v));
                texture.SetPixel(x,y,new Color(1,1,1,falloff));
            }
            texture.Apply(false,true);return texture;
        }

        static Mesh CreatePuddleMesh()
        {
            const int sides=24;var v=new Vector3[sides+1];var tri=new int[sides*3];var uv=new Vector4[sides+1];
            uv[0]=new Vector4(.5f,.5f,-1,0);
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,r=.5f*(1+Mathf.Sin(a*3)*.09f+Mathf.Cos(a*5)*.06f);
                v[i+1]=new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
                uv[i+1]=new Vector4(v[i+1].x+.5f,v[i+1].z+.5f,-1,0);
                tri[i*3]=0;tri[i*3+1]=(i+1)%sides+1;tri[i*3+2]=i+1;
            }
            var mesh=new Mesh{name="Irregular water surface"};mesh.vertices=v;mesh.triangles=tri;mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }

        sealed class ImpactVisual
        {
            public MaterialPropertyBlock Properties;
            public Transform Wet;
            public Transform Splash;
            public Renderer WetRenderer;
            public Renderer SplashRenderer;
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Tangent;
            public float Stretch;
            public float Kick;
            public float Amount;
            public float Age;
            public float WetAge;
            public float Lifetime;
            public bool Active;
        }

        sealed class FireVisual
        {
            public int CellKey=-1;
            public Transform Outer;
            public Transform Inner;
            public Renderer OuterRenderer;
            public Renderer InnerRenderer;
        }
    }
}
