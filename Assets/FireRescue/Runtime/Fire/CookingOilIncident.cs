using UnityEngine;

namespace FireRescue
{
    // Oil pot and ordinary floor fire have separate state. No renderer participates in reactions.
    public sealed class CookingOilIncident : MonoBehaviour
    {
        [SerializeField] ObjectFire pot;
        [SerializeField] FireField floor;
        [SerializeField] CarryItem pudding;
        [SerializeField] Vector3 spillStart=new Vector3(-5.05f,.1f,-3.46f);
        // Retained only so historical scene builders remain serialization-compatible.
        // Neither the opening schedule nor its spread multipliers run any more.
        [SerializeField,HideInInspector] float fastSeconds=5,fastSpread=.4f,slowSpread=.035f;
        float waterDose,seedDelay;
        bool seeded;
        bool subscribed;
        int[] heatCells;
        float[] coldSeconds;
        bool[] heated;
        [SerializeField] float potHeatRadius=1.55f,relightDelay=.28f;
        public int FirstWaveTargetCount=>0;
        public int FirstWaveProcessedCount=>0;
        public bool AutomaticTick {get;set;}=true;
        public int ExplosionCount {get;private set;}
        public float ExplosionAge {get;private set;}=100;
        public void Initialize(ObjectFire source,FireField field,CarryItem food)
        {Unsubscribe();pot=source;floor=field;pudding=food;heatCells=null;seeded=false;seedDelay=0;Subscribe();}
        void OnEnable()=>Subscribe();
        void OnDisable()=>Unsubscribe();
        void Subscribe(){if(!subscribed&&pot){pot.OilContactedByWater+=OnWater;subscribed=true;}}
        void Unsubscribe(){if(subscribed&&pot)pot.OilContactedByWater-=OnWater;subscribed=false;}
        void Update(){if(AutomaticTick)Tick(Time.deltaTime);}
        public void Tick(float dt)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            ExplosionAge+=dt;
            if(!floor)return;
            if(heatCells==null)BuildHeatFootprint();
            if(!pot||!pot.IsBurning)return;
            // Only the spill immediately beside the pot starts the incident. No timed
            // flood-fill or radial ignition of every kitchen tile.
            if(!seeded){seedDelay+=dt;if(seedDelay>=.25f){floor.Ignite(spillStart,.7f);seeded=true;}}
            for(int i=0;i<heatCells.Length;i++)
            {
                int cell=heatCells[i],x=cell%floor.Width,z=cell/floor.Width;var point=floor.CellCenter(x,z);
                if(!floor.CanTransferHeat(spillStart,point))continue;
                if(floor.GetCell(x,z).IsBurning){coldSeconds[i]=0;heated[i]=true;continue;}
                if(!heated[i])continue;
                coldSeconds[i]+=dt;
                if(coldSeconds[i]>=relightDelay){floor.IgniteFromHeat(point);coldSeconds[i]=0;}
            }
        }
        void BuildHeatFootprint()
        {
            var list=new System.Collections.Generic.List<int>();
            for(int z=0;z<floor.Height;z++)for(int x=0;x<floor.Width;x++)
            {
                var d=floor.CellCenter(x,z)-spillStart;d.y=0;
                if(d.sqrMagnitude<=potHeatRadius*potHeatRadius&&floor.GetCell(x,z).Fuel>0)list.Add(z*floor.Width+x);
            }
            heatCells=list.ToArray();coldSeconds=new float[heatCells.Length];heated=new bool[heatCells.Length];
        }
        void OnWater(float amount)
        {
            waterDose+=amount;if(ExplosionCount!=0||waterDose<.08f)return;
            ExplosionCount++;ExplosionAge=0;
            if(pudding)
            {
                Vector3 d=pudding.transform.position-transform.position;d.y=0;
                if(d.sqrMagnitude<1.5f*1.5f)pudding.Scorch();
            }
            // Burst ignites a local footprint, not the whole kitchen. Wet cleared cells resist ignition.
            if(floor)for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++)
                if(x*x+z*z<=4)floor.Ignite(spillStart+new Vector3(x*floor.CellSize,0,z*floor.CellSize),1);
        }
    }
}
