using UnityEngine;

namespace FireRescue
{
    /// <summary>
    /// Continuous tube following the physical contact path in the first level.
    /// Older scenes can still use the loose Verlet presentation.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public sealed class PhysicalHoseTube:MonoBehaviour
    {
        const int BaseSamples=128,Capacity=BaseSamples+HoseConnection.MaximumPathPoints;
        const int Sides=12;
        const int ContactSegments=24;
        internal const int HoseLayer=2;
        const float FixedStep=1f/120f;
        const float ReelDuration=1.30f;
        [SerializeField] SprayEmitter emitter;
        [SerializeField] float radius=.05f;
        [SerializeField] float floorHeight=.1f;
        readonly Vector3[] route=new Vector3[HoseConnection.MaximumPathPoints];
        readonly float[] routeDistance=new float[HoseConnection.MaximumPathPoints];
        readonly Vector3[] guide=new Vector3[Capacity],nodes=new Vector3[Capacity],previous=new Vector3[Capacity],scratch=new Vector3[Capacity];
        readonly float[] times=new float[Capacity];
        readonly bool[] pinned=new bool[Capacity];
        readonly Vector3[] vertices=new Vector3[Capacity*Sides],normals=new Vector3[Capacity*Sides];
        readonly Vector2[] uvs=new Vector2[Capacity*Sides];
        readonly int[] triangles=new int[(Capacity-1)*Sides*6];
        readonly Collider[] contacts=new Collider[16];
        Mesh mesh;MeshRenderer tubeRenderer;Material fabric;SphereCollider probe;
        Material couplingMaterial,gripMaterial;Texture2D fabricTexture;
        readonly Transform[] collars=new Transform[4];
        CapsuleCollider[] bodySegments;
        HoseConnection connection;
        readonly HoseWrapPath reelPath=new HoseWrapPath();
        FluidSource reelSource;
        Vector3 freeEnd, reelInlet, reelWindow;
        bool hasReelWindow;
        float reelTime, reelBudget;
        FluidSource lastSource;int count;bool initialized;
        float accumulatedTime;
        public int NodeCount=>count;
        public bool IsVisible=>tubeRenderer&&tubeRenderer.enabled;
        public Mesh DisplayMesh=>mesh;
        public float RenderRadius=>radius;
        public int ConnectedVisibleFrames {get;private set;}
        public int UnexpectedHiddenFrames {get;private set;}
        public float LongestSegment {get;private set;}
        public float MaximumNodeStep {get;private set;}
        public bool IsReeling=>connection && connection.Physical ? connection.Physical.IsReeling : reelSource;
        float ReelProgress=>Mathf.Clamp01(reelTime/ReelDuration);
        internal bool RenderedSpanIsBlocked(Vector3 start,Vector3 end,float thickness) =>
            connection.IsCableBlocked(start,end,thickness,IsReeling ? reelSource : connection.Source);
        public bool AutomaticSimulation {get;set;}=true;
        public Vector3 NodePosition(int index)=>nodes[Mathf.Clamp(index,0,Mathf.Max(0,count-1))];
        public void Initialize(SprayEmitter source){emitter=source;BindConnection();}
        void Awake()
        {
            var go=new GameObject("Continuous 3D hose mesh");go.transform.SetParent(transform,false);
            mesh=new Mesh{name="Procedural continuous hose"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;tubeRenderer=go.AddComponent<MeshRenderer>();
            fabric=new Material(Shader.Find("Standard")){name="White hose fabric",color=new Color(.88f,.9f,.87f)};
            fabric.SetFloat("_Metallic",0);fabric.SetFloat("_Glossiness",.38f);tubeRenderer.sharedMaterial=fabric;tubeRenderer.enabled=false;
            fabricTexture=new Texture2D(32,32,TextureFormat.RGB24,false){name="Subtle woven hose surface",wrapMode=TextureWrapMode.Repeat};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {float c=.93f+((x+y)%4<2?.055f:0);fabricTexture.SetPixel(x,y,new Color(c,c,c));}
            fabricTexture.Apply(false,true);fabric.mainTexture=fabricTexture;
            couplingMaterial=new Material(Shader.Find("Standard")){name="Hose coupling metal",color=new Color(.52f,.57f,.6f)};
            couplingMaterial.SetFloat("_Metallic",.65f);couplingMaterial.SetFloat("_Glossiness",.55f);
            gripMaterial=new Material(Shader.Find("Standard")){name="Hose coupling dark seals",color=new Color(.075f,.1f,.11f)};
            for(int i=0;i<collars.Length;i++)
            {
                var collar=GameObject.CreatePrimitive(PrimitiveType.Cylinder);collar.name="Hose end coupling "+i;collar.transform.SetParent(transform,false);
                var collider=collar.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
                collar.GetComponent<Renderer>().sharedMaterial=i%2==0?couplingMaterial:gripMaterial;collars[i]=collar.transform;collar.SetActive(false);
            }
            var proxy=new GameObject("Hose collision query shape"){hideFlags=HideFlags.HideAndDontSave};proxy.transform.SetParent(transform,false);
            probe=proxy.AddComponent<SphereCollider>();probe.radius=radius;probe.enabled=false;
        }
        void OnEnable()=>BindConnection();
        void BindConnection()
        {
            var next = emitter ? emitter.Connection : null;
            if (connection == next) return;
            if (connection) connection.Broke -= ReleaseEnd;
            connection = next;
            if (connection && isActiveAndEnabled) connection.Broke += ReleaseEnd;
        }

        void ReleaseEnd(HoseConnection hose)
        {
            if (hose.Physical) return;
            reelSource=hose.Source;
            freeEnd=reelSource.Port;
            reelInlet=hose.ResolvedInlet;
            hasReelWindow=hose.ActiveWindow;
            reelWindow=hasReelWindow ? hose.ActiveWindow.RoutePosition : default;
            hose.CopyContactPath(reelPath);
            reelTime=0f;
            int routeCount=GetReelRoute();
            reelBudget=MeasureRoute(routeCount);
            UpdateRouteTube(routeCount,0f,reelSource);
        }

        void LateUpdate(){if(AutomaticSimulation)TickPresentation(Time.deltaTime);}
        public void TickPresentation(float deltaTime)
        {
            if(!isActiveAndEnabled||float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)||deltaTime<0)return;
            if (!connection) BindConnection();
            if (connection && connection.Physical)
            {
                var physical=connection.Physical;
                if (!physical.IsActive || physical.Chain.NodeCount<2) { HideTube(); return; }
                radius=physical.CollisionRadius;
                count=Mathf.Min(Capacity,physical.Chain.NodeCount);
                float length=0f;
                for (int i=0;i<count;i++)
                {
                    nodes[i]=physical.Chain.GetPoint(i);
                    if (i>0) length+=Vector3.Distance(nodes[i-1],nodes[i]);
                    times[i]=length/connection.MaximumLength;
                }
                BuildMesh();
                UpdateCollars();
                tubeRenderer.enabled=true;
                DisableContacts(); // Native capsule rigidbodies already own all contacts.
                ConnectedVisibleFrames++;
                return;
            }
            if(!connection||!connection.IsConnected)
            {
                if (connection && IsReeling)
                {
                    TickReel(deltaTime);
                    return;
                }
                HideTube();
                return;
            }
            reelSource=null;
            if (connection.BreakUnderTension)
            {
                int routeCount=connection.PathPointCount;
                for (int i=0;i<routeCount-1;i++) route[i]=connection.GetPathPoint(i);
                route[routeCount-1]=connection.InletPosition;
                float slack=Mathf.Max(0f,connection.MaximumLength-connection.UsedLength);
                UpdateRouteTube(routeCount,slack,connection.Source);
                ConnectedVisibleFrames++;
                return;
            }
            if(initialized&&lastSource==connection.Source&&!tubeRenderer.enabled)UnexpectedHiddenFrames++;
            bool continuous=initialized&&lastSource==connection.Source;
            if(continuous)for(int i=0;i<count;i++)scratch[i]=nodes[i];
            BuildGuides(connection);
            if(count<2)return;
            if(!continuous)
            {for(int i=0;i<count;i++)nodes[i]=previous[i]=guide[i];initialized=true;lastSource=connection.Source;accumulatedTime=0;}
            AdvanceTube(connection, deltaTime);
            // At >120 FPS a frame may have no fixed step: ends still follow the source/nozzle.
            for(int i=0;i<count;i++)if(pinned[i])nodes[i]=previous[i]=guide[i];
            if(continuous)for(int i=2;i<count-2;i++)MaximumNodeStep=Mathf.Max(MaximumNodeStep,Vector3.Distance(nodes[i],scratch[i]));
            ShowTube();
            ConnectedVisibleFrames++;
        }

        void TickReel(float deltaTime)
        {
            reelTime+=deltaTime;
            Vector3 inlet=connection.InletPosition;
            if ((inlet-reelInlet).sqrMagnitude>.000001f &&
                connection.RouteReelingInlet(reelPath,reelInlet,hasReelWindow ? reelWindow : freeEnd,inlet,reelSource))
                reelInlet=inlet;
            int routeCount=GetReelRoute();
            float remaining=MeasureRoute(routeCount);
            float t=ReelProgress;
            float eased=t*t*t*(10f+t*(-15f+6f*t));
            float target=reelBudget*(1f-eased);
            AdvanceFreeEnd(Mathf.Max(0f,remaining-target));
            if (Vector3.Distance(freeEnd,inlet)<.009f && !hasReelWindow && reelPath.Count==0)
            {
                HideTube();
                return;
            }
            routeCount=GetReelRoute();
            LiftFreeEnd(t);
            UpdateRouteTube(routeCount,0f,reelSource);
        }

        int GetReelRoute()
        {
            int write=0;
            route[write++]=freeEnd;
            if (hasReelWindow) route[write++]=reelWindow;
            for (int i=0;i<reelPath.Count;i++) route[write++]=reelPath[i];
            route[write++]=reelInlet;
            return write;
        }

        void AdvanceFreeEnd(float distance)
        {
            while (distance>.0001f)
            {
                Vector3 next=hasReelWindow ? reelWindow : (reelPath.Count>0 ? reelPath[0] : reelInlet);
                float leg=Vector3.Distance(freeEnd,next);
                if (distance<leg || (!hasReelWindow && reelPath.Count==0))
                {
                    freeEnd=Vector3.MoveTowards(freeEnd,next,distance);
                    return;
                }
                freeEnd=next;
                distance-=leg;
                if (hasReelWindow) hasReelWindow=false;
                else reelPath.ConsumeFirstBend();
            }
        }

        float MeasureRoute(int routeCount)
        {
            float total=0f;
            for (int i=1;i<routeCount;i++) total+=Vector3.Distance(route[i-1],route[i]);
            return total;
        }

        void LiftFreeEnd(float progress)
        {
            // A small snap lifts the detached coupling before it is drawn in.
            // Keep the lift only when both the coupling and the first leg clear the room.
            float pulse=Mathf.Sin(Mathf.PI*Mathf.Clamp01(progress/.47f))*.11f;
            if (pulse<.003f) return;
            Vector3 lifted=route[0]+Vector3.up*pulse;
            if (connection.IsCableBlocked(route[0],lifted,radius,reelSource) ||
                connection.IsCableBlocked(lifted,route[1],radius,reelSource)) return;
            route[0]=lifted;
        }

        void UpdateRouteTube(int routeCount,float slack,FluidSource source)
        {
            float total=MeasureRoute(routeCount);
            if (total<.006f)
            {
                // The end coupling has reached the reel in this final fraction.
                tubeRenderer.enabled=false;
                ShowCollars(false);
                DisableContacts();
                return;
            }
            int written=0;
            float travelled=0f;
            nodes[written]=route[0];
            times[written++]=0f;
            for (int leg=1;leg<routeCount;leg++)
            {
                Vector3 start=route[leg-1], end=route[leg];
                float length=Vector3.Distance(start,end);
                if (length<.0001f) continue;
                // Every guide bend gets its own row, so the tube cannot bridge
                // a corner even if the two adjacent legs are short.
                int steps=Mathf.Max(1,Mathf.CeilToInt((BaseSamples-2)*length/total));
                Vector3 sideways=Vector3.Cross(Vector3.up,end-start).normalized;
                float curve=Mathf.Min(.14f,Mathf.Min(slack*.42f,length*.12f));
                int offset=written;
                for (int i=1;i<=steps;i++)
                {
                    float t=i/(float)steps;
                    Vector3 point=Vector3.Lerp(start,end,t);
                    if (i<steps && curve>0f)
                    {
                        float weight=Mathf.Sin(t*Mathf.PI);
                        point+=weight*(sideways*(Mathf.Sin(t*Mathf.PI*2f)*curve)+
                            Vector3.down*Mathf.Min(curve*.52f,Mathf.Max(0f,point.y-floorHeight-radius)));
                    }
                    nodes[written]=point;
                    times[written++]=(travelled+length*t)/connection.MaximumLength;
                }
                if (curve>0f)
                {
                    for (int i=offset;i<written;i++)
                    {
                        if (!connection.IsCableBlocked(nodes[i-1],nodes[i],radius,source)) continue;
                        for (int j=0;j<steps;j++) nodes[offset+j]=Vector3.Lerp(start,end,(j+1f)/steps);
                        break;
                    }
                }
                travelled+=length;
            }
            count=written;
            ShowTube();
        }

        void AdvanceTube(HoseConnection connection, float deltaTime)
        {
            // Catch up ordinary low-FPS frames, but bound a stall to twelve substeps.
            accumulatedTime=Mathf.Min(accumulatedTime+deltaTime,.1f);
            int steps=Mathf.Min(12,Mathf.FloorToInt((accumulatedTime+.000001f)/FixedStep));
            for(int step=0;step<steps;step++)Simulate(connection,FixedStep);
            accumulatedTime=Mathf.Max(0,accumulatedTime-steps*FixedStep);
        }

        void ShowTube()
        {
            BuildMesh();
            UpdateCollars();
            tubeRenderer.enabled = true;
            if (connection.BreakUnderTension && !IsReeling) UpdateContactBody();
            else DisableContacts();
        }

        void UpdateContactBody()
        {
            if (bodySegments == null)
            {
                var body = new GameObject("Physical hose contacts") { layer = HoseLayer };
                body.transform.SetParent(transform, false);
                var rigidbody = body.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                bodySegments = new CapsuleCollider[ContactSegments];
                for (int i = 0; i < bodySegments.Length; i++)
                {
                    var segment = new GameObject("Hose contact") { layer = HoseLayer };
                    segment.transform.SetParent(body.transform, false);
                    var capsule = segment.AddComponent<CapsuleCollider>();
                    capsule.excludeLayers = ~(1 << 10); // Dynamic pushable props only; walls use rope queries.
                    capsule.layerOverridePriority = 1;
                    capsule.radius = radius;
                    bodySegments[i] = capsule;
                }
            }
            for (int i = 0; i < bodySegments.Length; i++)
            {
                int start=i*(count-1)/bodySegments.Length;
                int end=(i+1)*(count-1)/bodySegments.Length;
                Vector3 a=nodes[start], b=nodes[end];
                Vector3 span = b - a;
                var capsule = bodySegments[i];
                if (span.sqrMagnitude<.00000001f || connection.IsCableBlocked(a,b,radius))
                {
                    // A coarse collision capsule must not take a shortcut across
                    // the corner which the fine visual mesh follows.
                    b=nodes[Mathf.Min(start+1,count-1)];
                    span=b-a;
                }
                capsule.transform.SetPositionAndRotation((a + b) * .5f, Quaternion.FromToRotation(Vector3.up, span));
                capsule.height = span.magnitude + radius * 2f;
                capsule.enabled = span.sqrMagnitude>.00000001f;
            }
        }

        void HideTube()
        {
            if (tubeRenderer) tubeRenderer.enabled = false;
            ShowCollars(false);
            DisableContacts();
            initialized = false;
            lastSource = reelSource = null;
            accumulatedTime = 0f;
            count=0;
        }

        void DisableContacts()
        {
            if (bodySegments==null) return;
            foreach (var capsule in bodySegments) capsule.enabled=false;
        }
        void BuildGuides(HoseConnection connection)
        {
            int routeCount=connection.PathPointCount;float total=0;
            for(int i=0;i<routeCount;i++){route[i]=connection.GetPathPoint(i);if(i==routeCount-1)route[i]=emitter.HoseInletPosition;if(i>0)total+=Vector3.Distance(route[i],route[i-1]);routeDistance[i]=total;}
            // Fixed material particles: wall contacts change a guide, never insert a
            // hard-pinned vertex into the visible hose or renumber downstream particles.
            int written=0,leg=0;
            for(int sample=0;sample<BaseSamples;sample++)
            {
                float distance=total*sample/(BaseSamples-1f);
                while(leg<routeCount-2&&routeDistance[leg+1]<distance)leg++;
                float length=routeDistance[leg+1]-routeDistance[leg];
                float t=length>.0001f?(distance-routeDistance[leg])/length:0;
                bool pin=sample==0||sample==BaseSamples-1;
                var point=Vector3.Lerp(route[leg],route[leg+1],t);
                if(!pin)
                {
                    // Long runs lie on the floor; only the first/last 65cm rise to a
                    // raised source/nozzle. A straight-height guide looked like a wire.
                    float floor=floorHeight+radius+.001f;
                    float liftA=Mathf.Max(0,route[0].y-floor)*(1-Mathf.SmoothStep(0,1,distance/.65f));
                    float liftB=Mathf.Max(0,route[routeCount-1].y-floor)*(1-Mathf.SmoothStep(0,1,(total-distance)/.65f));
                    point.y=floor+Mathf.Max(liftA,liftB);
                    // A little unused cable rests in a broad curve, rather than floating
                    // taut like a wire. Never curve across an authored solid wall.
                    float slack=Mathf.Clamp(connection.MaximumLength-connection.UsedLength,0,.5f);
                    var direction=route[leg+1]-route[leg];direction.y=0;
                    var sideways=Vector3.Cross(Vector3.up,direction.normalized);
                    float amplitude=Mathf.Min(.12f,slack*.24f);
                    var curved=point+sideways*Mathf.Sin(sample*Mathf.PI/(BaseSamples-1))*Mathf.Sin(sample*Mathf.PI*2/(BaseSamples-1))*amplitude;
                    if(!connection.IsCableBlocked(point,curved,radius))point=curved;
                }
                Append(point,sample/(BaseSamples-1f),pin,ref written);
            }
            count=written;
        }
        void Append(Vector3 point,float time,bool pin,ref int written)
        {
            if(written>0&&(guide[written-1]-point).sqrMagnitude<.00000001f){pinned[written-1]|=pin;return;}
            if(written>=Capacity)return;
            guide[written]=point;times[written]=time;pinned[written]=pin;written++;
        }
        void Simulate(HoseConnection connection,float dt)
        {
            float minY=floorHeight+radius;
            for(int i=0;i<count;i++)
            {
                if(pinned[i]){nodes[i]=previous[i]=guide[i];continue;}
                Vector3 p=nodes[i];Vector3 velocity=Vector3.ClampMagnitude((p-previous[i])*.88f,.08f);previous[i]=p;
                Vector3 fall=p+velocity+Vector3.down*(9.81f*dt*dt);
                nodes[i]=Vector3.Lerp(fall,guide[i],1-Mathf.Exp(-5*dt));
                nodes[i].y=Mathf.Max(minY,nodes[i].y);
            }
            float slack=Mathf.Clamp(connection.MaximumLength-connection.UsedLength,0,.5f);
            for(int iteration=0;iteration<7;iteration++)
            {
                for(int i=1;i<count;i++)
                {
                    Vector3 d=nodes[i]-nodes[i-1];float length=d.magnitude;if(length<.00001f)continue;
                    float rest=Vector3.Distance(guide[i],guide[i-1])+slack/(count-1);
                    // Rope is a maximum-distance constraint, not a rod that pushes every
                    // short span apart. Compression here created high-frequency zigzags.
                    if(length<=rest)continue;
                    Vector3 correction=d*((length-rest)/length);
                    if(pinned[i-1]&&!pinned[i])nodes[i]-=correction;
                    else if(!pinned[i-1]&&pinned[i])nodes[i-1]+=correction;
                    else if(!pinned[i-1]&&!pinned[i]){nodes[i-1]+=correction*.5f;nodes[i]-=correction*.5f;}
                }
                for(int i=1;i<count;i++)if(!pinned[i])
                {
                    if(i<count-1)nodes[i]=Vector3.Lerp(nodes[i],(nodes[i-1]+nodes[i+1])*.5f,.25f);
                    nodes[i].y=Mathf.Max(minY,nodes[i].y);ResolveCollision(i,connection);
                }
                for(int i=0;i<count;i++)if(pinned[i])nodes[i]=guide[i];
            }
            // Bends are included exactly. If a simulated section grazes a wall, use its
            // valid guide section for that section only; never blank the complete hose.
            for(int i=1;i<count;i++)
                if(connection.IsCableBlocked(nodes[i-1],nodes[i],radius))
                {nodes[i-1]=previous[i-1]=guide[i-1];nodes[i]=previous[i]=guide[i];}
        }
        void ResolveCollision(int i,HoseConnection connection)
        {
            int found=Physics.OverlapSphereNonAlloc(nodes[i],radius,contacts,(1<<8)|(1<<0),QueryTriggerInteraction.Ignore);
            for(int j=0;j<found;j++)
            {
                var obstacle=contacts[j];if(!obstacle||obstacle==probe||obstacle.transform.IsChildOf(emitter.transform))continue;
                FluidSource anchor=connection.Source;
                if(anchor&&obstacle.transform.IsChildOf(anchor.transform)&&i<5)continue;
                if(Physics.ComputePenetration(probe,nodes[i],Quaternion.identity,obstacle,obstacle.transform.position,obstacle.transform.rotation,out var normal,out float distance))
                    nodes[i]+=normal*Mathf.Min(distance+.001f,.12f);
            }
        }
        void BuildMesh()
        {
            Vector3 side=Vector3.right;LongestSegment=0;
            for(int i=0;i<count;i++)
            {
                Vector3 tangent=nodes[Mathf.Min(count-1,i+1)]-nodes[Mathf.Max(0,i-1)];if(tangent.sqrMagnitude<.000001f)tangent=Vector3.forward;tangent.Normalize();
                side=Vector3.ProjectOnPlane(side,tangent).normalized;if(side.sqrMagnitude<.01f)side=Vector3.Cross(tangent,Vector3.forward).normalized;if(side.sqrMagnitude<.01f)side=Vector3.right;
                Vector3 up=Vector3.Cross(tangent,side).normalized;
                if(i>0)LongestSegment=Mathf.Max(LongestSegment,Vector3.Distance(nodes[i],nodes[i-1]));
                for(int j=0;j<Sides;j++)
                {
                    float a=j*Mathf.PI*2/Sides;Vector3 normal=side*Mathf.Cos(a)+up*Mathf.Sin(a);int k=i*Sides+j;
                    vertices[k]=transform.InverseTransformPoint(nodes[i]+normal*radius);normals[k]=transform.InverseTransformDirection(normal);uvs[k]=new Vector2(times[i]*8,j/(float)Sides);
                }
            }
            int index=0;for(int i=0;i<count-1;i++)for(int j=0;j<Sides;j++)
            {int a=i*Sides+j,b=i*Sides+(j+1)%Sides,c=a+Sides,d=b+Sides;triangles[index++]=a;triangles[index++]=b;triangles[index++]=c;triangles[index++]=b;triangles[index++]=d;triangles[index++]=c;}
            if(mesh.vertexCount!=count*Sides)mesh.Clear(false);mesh.SetVertices(vertices,0,count*Sides);mesh.SetNormals(normals,0,count*Sides);mesh.SetUVs(0,uvs,0,count*Sides);mesh.SetTriangles(triangles,0,index,0);mesh.RecalculateBounds();
        }
        void UpdateCollars()
        {
            for(int end=0;end<2;end++)
            {
                int node=end==0?0:count-1;var direction=(nodes[end==0?1:count-2]-nodes[node]).normalized;
                if(direction.sqrMagnitude<.01f)direction=Vector3.up;
                for(int band=0;band<2;band++)
                {
                    var c=collars[end*2+band];c.gameObject.SetActive(true);c.position=nodes[node]+direction*(band==0?.035f:.09f);
                    c.rotation=Quaternion.FromToRotation(Vector3.up,direction);
                    c.localScale=new Vector3(radius*(band==0?2.7f:2.35f),band==0?.032f:.018f,radius*(band==0?2.7f:2.35f));
                }
            }
        }
        void ShowCollars(bool show){foreach(var c in collars)if(c)c.gameObject.SetActive(show);}
        void OnDisable(){if(connection)connection.Broke-=ReleaseEnd;connection=null;HideTube();}
        void OnDestroy(){if(mesh)Destroy(mesh);if(fabric)Destroy(fabric);if(fabricTexture)Destroy(fabricTexture);if(couplingMaterial)Destroy(couplingMaterial);if(gripMaterial)Destroy(gripMaterial);if(probe)Destroy(probe.gameObject);}
    }
}
