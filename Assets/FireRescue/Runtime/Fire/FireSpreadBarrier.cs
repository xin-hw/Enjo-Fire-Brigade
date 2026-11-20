using System;
using UnityEngine;

namespace FireRescue
{
    // Scene composition for floor heat transfer. Cache only adjacent grid edges;
    // a broken door invalidates the cache, not the authored fuel layout.
    public sealed class FireSpreadBarrier : MonoBehaviour
    {
        [SerializeField] FireField field;
        [SerializeField] SprayWallOcclusion geometry;
        [SerializeField] BreakableDoor[] doors=Array.Empty<BreakableDoor>();
        byte[] edges=Array.Empty<byte>();
        bool subscribed;
        public void Initialize(FireField floor,SprayWallOcclusion walls,BreakableDoor[] breakableDoors)
        {Unsubscribe();field=floor;geometry=walls;doors=breakableDoors??Array.Empty<BreakableDoor>();Invalidate();Subscribe();if(field)field.SetSpreadBarrier(this);}
        void OnEnable(){Subscribe();Invalidate();if(field)field.SetSpreadBarrier(this);}
        void OnDisable(){Unsubscribe();Invalidate();}
        void Subscribe(){if(subscribed)return;foreach(var door in doors)if(door)door.StateChanged+=DoorChanged;subscribed=true;}
        void Unsubscribe(){if(!subscribed)return;foreach(var door in doors)if(door)door.StateChanged-=DoorChanged;subscribed=false;}
        void DoorChanged(BreakableDoor _)=>Invalidate();
        public void Invalidate(){if(edges.Length>0)Array.Clear(edges,0,edges.Length);}
        public bool Allows(Vector3 from,Vector3 to)
        {
            if(!geometry)return true;
            int key=-1;
            if(field&&field.TryGetCell(from,out var a)&&field.TryGetCell(to,out var b))
            {
                int dx=b.x-a.x,dz=b.y-a.y;
                if(Mathf.Abs(dx)+Mathf.Abs(dz)==1)
                {
                    int size=field.Width*field.Height*4;if(edges.Length!=size)edges=new byte[size];
                    key=(a.y*field.Width+a.x)*4+(dx<0?0:dx>0?1:dz<0?2:3);
                    if(edges[key]!=0)return edges[key]==1;
                }
            }
            bool clear=!geometry.TryBlock(from,to,out _,out _);
            if(key>=0)edges[key]=(byte)(clear?1:2);
            return clear;
        }
    }
}
