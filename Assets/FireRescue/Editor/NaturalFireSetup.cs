using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireRescue.Editor
{
    public static class NaturalFireSetup
    {
        public const string Evidence="Docs/Validation/2026-11-01-natural-fire";
        public static readonly string[] RoomNames={"Living floor","Kitchen floor","Bedroom floor","Bathroom floor","Storage floor"};
        public static void Set(UnityEngine.Object target,string name,float value)
        {var state=new SerializedObject(target);state.FindProperty(name).floatValue=value;state.ApplyModifiedPropertiesWithoutUndo();}
        public static void BatchApply()
        {
            try
            {
                Directory.CreateDirectory(Evidence);
                var scene=EditorSceneManager.OpenScene(FirstReview1027Setup.ScenePath);var root=GameObject.Find("First Review 1027");
                var field=root.GetComponentInChildren<FireField>();var incident=root.GetComponentInChildren<CookingOilIncident>();
                var protectedComponents=root.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c&&!(c is FireField)&&!(c is CookingOilIncident)&&!(c is FireSpreadBarrier)&&!(c is DoorFireExposure)&&!(c is DoorBurnView)).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
                var colliders=root.GetComponentsInChildren<Collider>(true).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
                var rooms=RoomNames.Select(n=>root.transform.Find(n).GetComponent<Renderer>().bounds).ToArray();
                var blocked=new List<Vector2Int>();var roomFuel=new int[rooms.Length];var hits=new Collider[64];
                Physics.SyncTransforms();
                for(int z=0;z<field.Height;z++)for(int x=0;x<field.Width;x++)
                {
                    var p=field.CellCenter(x,z);bool inside=false;
                    // Visual floor plates leave sub-cell seams at door thresholds. Cover
                    // their half-cell fringe; actual wall/door geometry still blocks heat.
                    float fringe=field.CellSize*.5f;
                    foreach(var b in rooms)if(p.x>b.min.x-fringe&&p.x<b.max.x+fringe&&p.z>b.min.z-fringe&&p.z<b.max.z+fringe){inside=true;break;}
                    bool solid=false;
                    int count=Physics.OverlapSphereNonAlloc(p+Vector3.up*.18f,.08f,hits,1<<8,QueryTriggerInteraction.Ignore);
                    if(count==hits.Length)solid=true;
                    for(int i=0;i<count&&!solid;i++)if(!hits[i].GetComponentInParent<BreakableDoor>())solid=true;
                    if(!inside||solid)blocked.Add(new Vector2Int(x,z));
                    else for(int i=0;i<rooms.Length;i++)if(rooms[i].Contains(new Vector3(p.x,rooms[i].center.y,p.z)))roomFuel[i]++;
                }
                Undo.RecordObject(field,"Enable natural indoor fire");
                field.SetIgnitionSeeds(Array.Empty<Vector2Int>());field.SetNonFlammableCells(blocked.ToArray());
                Set(field,"fuelBurnRate",.004f);Set(field,"naturalIntensityLoss",.01f);Set(field,"fireSpreadRate",.13f);Set(field,"fuelFlameGain",.18f);
                // A constant neighbour rate owns propagation for the whole round. The
                // historical incident fastSeconds/fastSpread fields no longer execute.
                var doors=root.GetComponentsInChildren<BreakableDoor>();
                foreach(var door in doors)
                {
                    var exposure=door.GetComponent<DoorFireExposure>();if(!exposure)exposure=Undo.AddComponent<DoorFireExposure>(door.gameObject);
                    exposure.Initialize(field,door);EditorUtility.SetDirty(exposure);
                    var burn=door.GetComponent<DoorBurnView>();if(!burn)burn=Undo.AddComponent<DoorBurnView>(door.gameObject);
                    burn.Initialize(exposure,door.transform.Find("Visual").GetComponentsInChildren<Renderer>(true));EditorUtility.SetDirty(burn);
                }
                var barrier=root.GetComponent<FireSpreadBarrier>();if(!barrier)barrier=Undo.AddComponent<FireSpreadBarrier>(root);
                barrier.Initialize(field,root.GetComponent<SprayWallOcclusion>(),doors);EditorUtility.SetDirty(barrier);EditorUtility.SetDirty(field);
                foreach(var pair in protectedComponents)if(EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidDataException("Unrelated component changed: "+pair.Key.GetType().Name);
                foreach(var pair in colliders)if(EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidDataException("Collider changed: "+pair.Key.name);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed");AssetDatabase.SaveAssets();
                File.WriteAllText(Evidence+"/apply.txt","NATURAL_FIRE_APPLY_PASS\nFuel="+(field.Width*field.Height-blocked.Count)+"; doors="+doors.Length+"; constant spread=.13; fuel growth=.18\n"+string.Join("\n",RoomNames.Select((n,i)=>n+" fuel="+roomFuel[i]))+"\nExisting colliders="+colliders.Count+" and protected components="+protectedComponents.Count+" unchanged. No model/camera/timer/hose rebuild.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
