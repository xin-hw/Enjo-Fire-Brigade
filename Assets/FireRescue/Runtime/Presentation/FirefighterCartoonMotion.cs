using System;
using UnityEngine;

namespace FireRescue
{
    /// <summary>Distance-driven toy gait and two-bone grip posing. Never moves the actor/colliders/nozzle gameplay anchors.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class FirefighterCartoonMotion : MonoBehaviour
    {
        [Serializable] public sealed class RestBone
        {public Transform bone;public Vector3 position;public Quaternion rotation;}
        [SerializeField] Transform actor, hips, chest, head;
        Transform spine;
        Vector3 spineRest;
        [SerializeField] Transform leftThigh,leftShin,leftFoot,rightThigh,rightShin,rightFoot;
        [SerializeField] Transform leftUpper,leftLower,leftHand,rightUpper,rightLower,rightHand;
        [SerializeField] Transform nozzleMount,axePivot;
        [SerializeField] FirefighterTools tools;
        [SerializeField] PlayerCarryInteraction carry;
        [SerializeField] RestBone[] rest;
        [SerializeField] Vector3 leftFootRest,rightFootRest;
        [SerializeField] Quaternion leftFootRotation,rightFootRotation;
        [SerializeField] float stride=.92f;
        [Header("Visual grip calibration (mount-local wrist positions)")]
        [SerializeField] Vector3 hoseLeftWrist=new Vector3(-.10f,.005f,-.045f);
        [SerializeField] Vector3 hoseRightWrist=new Vector3(.07f,-.015f,-.07f);
        [SerializeField] Vector3 axeRightWrist=new Vector3(.04f,.075f,0);
        Vector3 lastPosition;
        Vector3 gaitDirection=Vector3.forward;
        float phase,amount,turnLean,lastYaw;
        float poseClock,hoseBlend,axeBlend,carryBlend,sprayBlend;
        SprayEmitter emitter;
        Vector3 leftPalmAxis,rightPalmAxis,nozzleRestPosition,axeRestPosition;
        Quaternion leftHandRest,rightHandRest,nozzleRestRotation,axeRestRotation;
        bool captured;
        public float TravelledDistance {get;private set;}
        public float GaitPhase => phase;
        public float MovementBlend => amount;
        public float LeftGripError {get;private set;}
        public float RightGripError {get;private set;}
        public float HosePoseBlend=>hoseBlend;
        public float AxePoseBlend=>axeBlend;
        public float SprayBraceBlend=>sprayBlend;
        public bool AutomaticTick {get;set;}=true;
        public Vector3 VisualMuzzlePosition=>nozzleMount?nozzleMount.TransformPoint(new Vector3(0,.090184f,.270324f)):transform.position;
        public void Initialize(Transform root,Transform rig,FirefighterTools toolOwner,PlayerCarryInteraction itemOwner,Transform nozzle,Transform axe)
        {
            RestoreRig();
            actor=root;tools=toolOwner;carry=itemOwner;nozzleMount=nozzle;axePivot=axe;
            var all=rig.GetComponentsInChildren<Transform>(true);
            Transform Find(string n)=>Array.Find(all,t=>t.name==n);
            hips=Find("Hips");chest=Find("Chest");head=Find("Head");
            leftThigh=Find("L_Thigh");leftShin=Find("L_Shin");leftFoot=Find("L_Foot");
            rightThigh=Find("R_Thigh");rightShin=Find("R_Shin");rightFoot=Find("R_Foot");
            leftUpper=Find("L_UpperArm");leftLower=Find("L_LowerArm");leftHand=Find("L_Hand");
            rightUpper=Find("R_UpperArm");rightLower=Find("R_LowerArm");rightHand=Find("R_Hand");
            NormalizeArmSides();
            var bones=new[]{hips,chest,head,leftThigh,leftShin,leftFoot,rightThigh,rightShin,rightFoot,leftUpper,leftLower,leftHand,rightUpper,rightLower,rightHand};
            rest=Array.ConvertAll(bones,b=>new RestBone{bone=b,position=b.localPosition,rotation=b.localRotation});
            leftFootRest=actor.InverseTransformPoint(leftFoot.position);rightFootRest=actor.InverseTransformPoint(rightFoot.position);
            leftFootRotation=Quaternion.Inverse(actor.rotation)*leftFoot.rotation;rightFootRotation=Quaternion.Inverse(actor.rotation)*rightFoot.rotation;
            spine=chest?chest.parent:null;if(spine)spineRest=spine.localPosition;
            emitter=GetComponent<SprayEmitter>();captured=false;CapturePoseBasis();
            lastPosition=actor.position;lastYaw=actor.eulerAngles.y;
        }
        void Awake(){NormalizeArmSides();spine=chest?chest.parent:null;if(spine)spineRest=spine.localPosition;emitter=GetComponent<SprayEmitter>();CapturePoseBasis();}
        void NormalizeArmSides()
        {
            // FBX face correction rotates this user's rig 180 degrees; L_/R_ names are
            // opposite to actor-space X. Use actual shoulder positions, never cross arms.
            if(!actor||!leftUpper||actor.InverseTransformPoint(leftUpper.position).x<0)return;
            (leftUpper,rightUpper)=(rightUpper,leftUpper);(leftLower,rightLower)=(rightLower,leftLower);(leftHand,rightHand)=(rightHand,leftHand);
        }
        void OnEnable(){lastPosition=actor?actor.position:transform.position;lastYaw=actor?actor.eulerAngles.y:0;phase=amount=turnLean=0;poseClock=hoseBlend=axeBlend=carryBlend=sprayBlend=0;}
        void OnDisable()
        {
            RestoreRig();
            if(captured&&nozzleMount){nozzleMount.localPosition=nozzleRestPosition;nozzleMount.localRotation=nozzleRestRotation;}
            if(captured&&axePivot){axePivot.localPosition=axeRestPosition;axePivot.localRotation=axeRestRotation;}
        }
        void RestoreRig()
        {
            if(rest!=null)foreach(var b in rest)if(b!=null&&b.bone){b.bone.localPosition=b.position;b.bone.localRotation=b.rotation;}
            if(spine)spine.localPosition=spineRest;
        }
        void CapturePoseBasis()
        {
            if(captured||!actor||!leftHand||!rightHand||!leftLower||!rightLower||rest==null)return;
            RestoreRig();
            leftPalmAxis=actor.InverseTransformDirection(leftHand.position-leftLower.position).normalized;
            rightPalmAxis=actor.InverseTransformDirection(rightHand.position-rightLower.position).normalized;
            leftHandRest=Quaternion.Inverse(actor.rotation)*leftHand.rotation;rightHandRest=Quaternion.Inverse(actor.rotation)*rightHand.rotation;
            if(nozzleMount){nozzleRestPosition=nozzleMount.localPosition;nozzleRestRotation=nozzleMount.localRotation;}
            if(axePivot){axeRestPosition=axePivot.localPosition;axeRestRotation=axePivot.localRotation;}
            captured=true;
        }
        void LateUpdate(){if(AutomaticTick)TickPresentation(Time.deltaTime);}
        public void TickPresentation(float dt)
        {
            if(!isActiveAndEnabled||!actor||rest==null||dt<=0||!float.IsFinite(dt))return;
            CapturePoseBasis();if(!captured)return;
            poseClock+=Mathf.Min(dt,.1f);
            Vector3 delta=actor.position-lastPosition;delta.y=0;lastPosition=actor.position;
            float distance=delta.magnitude;if(distance/dt>16)distance=0;
            if(distance>.0001f)gaitDirection=actor.InverseTransformDirection(delta.normalized);
            float speed=distance/dt;float blend=1-Mathf.Exp(-18*dt);
            amount=Mathf.Lerp(amount,Mathf.Clamp01(speed/1.2f),blend);
            phase=Mathf.Repeat(phase+distance/stride,1);TravelledDistance+=distance;
            float yaw=actor.eulerAngles.y;float rate=Mathf.Clamp(Mathf.DeltaAngle(lastYaw,yaw)/Mathf.Max(dt,.001f)/600,-1,1);lastYaw=yaw;
            turnLean=Mathf.Lerp(turnLean,rate,1-Mathf.Exp(-12*dt));
            RestoreRig();
            bool hose=tools&&tools.isActiveAndEnabled&&tools.SelectedTool==FirefighterTools.Tool.Hose;
            bool axe=tools&&tools.isActiveAndEnabled&&tools.SelectedTool==FirefighterTools.Tool.Axe;
            bool carrying=carry&&carry.IsCarrying;
            float poseRate=1-Mathf.Exp(-22*dt);
            hoseBlend=Mathf.Lerp(hoseBlend,hose&&!carrying?1:0,poseRate);
            axeBlend=Mathf.Lerp(axeBlend,axe&&!carrying?1:0,poseRate);
            carryBlend=Mathf.Lerp(carryBlend,carrying?1:0,poseRate);
            sprayBlend=Mathf.Lerp(sprayBlend,hose&&emitter&&emitter.IsSpraying?1:0,1-Mathf.Exp(-16*dt));
            float wave=Mathf.Sin(phase*Mathf.PI*2),bounce=(1-Mathf.Cos(phase*Mathf.PI*4))*.018f*amount;
            hips.position+=Vector3.up*(-.09f*amount+bounce+Mathf.Sin(poseClock*3)*.003f*(1-amount)-sprayBlend*.014f);
            // Flex the short legs without bringing the oversized head down onto the nozzle.
            if(spine)spine.position+=Vector3.up*(.09f*amount);
            // Straighten the toy's upper body for a clear chest-level two-hand hold;
            // the feet, actor, nozzle and inlet gameplay anchors do not move.
            if(spine)spine.position+=Vector3.up*(.06f*hoseBlend);
            hips.rotation=Quaternion.AngleAxis(wave*3*amount-turnLean*5,actor.forward)*hips.rotation;
            Vector3 localMove=actor.InverseTransformDirection(delta/Mathf.Max(dt,.001f));
            chest.rotation=Quaternion.AngleAxis(Mathf.Clamp(localMove.z*1.4f,-6,6),actor.right)*chest.rotation;
            head.rotation=Quaternion.AngleAxis(-wave*1.8f*amount,actor.forward)*head.rotation;
            float stroke=axe&&tools.IsSwinging?tools.SwingProgress:0;
            float twist=0,lean=0;
            if(axe&&tools.IsSwinging)
            {
                twist=Stroke(stroke,0,13,-11,-14,0)*axeBlend;
                lean=Stroke(stroke,0,-4,7,9,0)*axeBlend;
            }
            chest.rotation=Quaternion.AngleAxis(twist,actor.up)*Quaternion.AngleAxis(lean+sprayBlend*4,actor.right)*chest.rotation;
            head.rotation=Quaternion.AngleAxis(-twist*.65f,actor.up)*Quaternion.AngleAxis(-lean*.4f,actor.right)*head.rotation;
            PoseLeg(leftThigh,leftShin,leftFoot,leftFootRest,leftFootRotation,phase);
            PoseLeg(rightThigh,rightShin,rightFoot,rightFootRest,rightFootRotation,Mathf.Repeat(phase+.5f,1));
            Vector3 leftIdle=actor.TransformPoint(new Vector3(-.35f,.48f,wave*.13f*amount));
            Vector3 rightIdle=actor.TransformPoint(new Vector3(.35f,.48f,-wave*.13f*amount));
            Vector3 leftTarget=leftIdle,rightTarget=rightIdle;
            if(nozzleMount)
            {
                // Gameplay muzzle/inlet remain independent. Recoil is visual-only and <1cm.
                float recoil=sprayBlend*(.006f+Mathf.Sin(poseClock*24)*.002f);
                nozzleMount.localPosition=new Vector3(.02f,.76f+bounce*.12f,.23f-recoil)+new Vector3(0,-.09f,-.035f)*(1-hoseBlend);
                nozzleMount.localRotation=Quaternion.Euler(0,0,-turnLean*.7f*hoseBlend);
                leftTarget=Vector3.Lerp(leftTarget,nozzleMount.TransformPoint(hoseLeftWrist),hoseBlend);
                rightTarget=Vector3.Lerp(rightTarget,nozzleMount.TransformPoint(hoseRightWrist),hoseBlend);
            }
            if(axePivot)
            {
                // One-handed toy axe keeps the free arm as counterbalance. The old
                // two-hand targets crossed the torso and exceeded this rig's short arms.
                Vector3 position=new Vector3(.29f,.63f,.13f);float pitch=24,roll=-38;
                if(axe&&tools.IsSwinging)
                {
                    position=Stroke(stroke,position,new Vector3(.31f,.68f,.06f),new Vector3(.25f,.57f,.20f),new Vector3(.27f,.56f,.18f),position);
                    pitch=Stroke(stroke,24,-32,72,105,24);roll=Stroke(stroke,-38,-52,-25,-22,-38);
                }
                axePivot.localPosition=position+new Vector3(0,bounce*.25f,0)+new Vector3(.02f,-.08f,-.025f)*(1-axeBlend);
                axePivot.localRotation=Quaternion.Euler(pitch,0,roll+wave*1.4f*amount);
                rightTarget=Vector3.Lerp(rightTarget,axePivot.TransformPoint(axeRightWrist),axeBlend);
                Vector3 counter=actor.TransformPoint(new Vector3(-.33f,.50f+Mathf.Abs(twist)*.001f,.025f-twist*.003f+wave*.07f*amount));
                leftTarget=Vector3.Lerp(leftTarget,counter,axeBlend);
            }
            leftTarget=Vector3.Lerp(leftTarget,actor.TransformPoint(new Vector3(-.10f,.76f,.23f)),carryBlend);
            rightTarget=Vector3.Lerp(rightTarget,actor.TransformPoint(new Vector3(.10f,.76f,.23f)),carryBlend);
            Solve(leftUpper,leftLower,leftHand,leftTarget,-actor.right-actor.forward*.25f);
            Solve(rightUpper,rightLower,rightHand,rightTarget,actor.right-actor.forward*.25f);
            if(nozzleMount)
            {
                OrientHand(leftHand,leftPalmAxis,leftHandRest,nozzleMount.TransformDirection(new Vector3(1,.05f,.45f)),hoseBlend);
                OrientHand(rightHand,rightPalmAxis,rightHandRest,nozzleMount.TransformDirection(new Vector3(-1,.1f,.7f)),hoseBlend);
            }
            if(axePivot)OrientHand(rightHand,rightPalmAxis,rightHandRest,axePivot.TransformDirection(-Vector3.right),axeBlend);
            LeftGripError=Vector3.Distance(leftHand.position,leftTarget);RightGripError=Vector3.Distance(rightHand.position,rightTarget);
        }
        void OrientHand(Transform hand,Vector3 restAxis,Quaternion restRotation,Vector3 palmDirection,float weight)
        {
            Quaternion grip=actor.rotation*Quaternion.FromToRotation(restAxis,actor.InverseTransformDirection(palmDirection))*restRotation;
            hand.rotation=Quaternion.Slerp(hand.rotation,grip,weight);
        }
        // The downstroke reaches its contact pose at the existing gameplay hit fraction .45.
        // Curves never write tool state, duration, damage or animation events.
        static float Stroke(float t,float ready,float wind,float contact,float follow,float recovered)
        {if(t<.23f)return Mathf.Lerp(ready,wind,Ease(t/.23f));if(t<.45f)return Mathf.Lerp(wind,contact,Ease((t-.23f)/.22f));if(t<.62f)return Mathf.Lerp(contact,follow,Ease((t-.45f)/.17f));return Mathf.Lerp(follow,recovered,Ease((t-.62f)/.38f));}
        static Vector3 Stroke(float t,Vector3 ready,Vector3 wind,Vector3 contact,Vector3 follow,Vector3 recovered)
        {return new Vector3(Stroke(t,ready.x,wind.x,contact.x,follow.x,recovered.x),Stroke(t,ready.y,wind.y,contact.y,follow.y,recovered.y),Stroke(t,ready.z,wind.z,contact.z,follow.z,recovered.z));}
        static float Ease(float t)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
        void PoseLeg(Transform a,Transform b,Transform foot,Vector3 restFoot,Quaternion rotation,float cycle)
        {
            // The stance half travels backwards at exactly the actor's distance rate.
            float z=cycle<.5f?stride*(.25f-cycle):Mathf.Lerp(-stride*.25f,stride*.25f,Mathf.SmoothStep(0,1,(cycle-.5f)*2));
            float lift=cycle<.5f?0:Mathf.Sin((cycle-.5f)*Mathf.PI*2)*.055f;
            Vector3 target=actor.TransformPoint(restFoot+(gaitDirection*z+Vector3.up*lift)*amount);
            Solve(a,b,foot,target,actor.forward);
            foot.rotation=actor.rotation*rotation;
        }
        // Exact limb lengths, with a stable local pole. No transform scale/mesh stretching.
        static void Solve(Transform a,Transform b,Transform c,Vector3 target,Vector3 pole)
        {
            float l1=Vector3.Distance(a.position,b.position),l2=Vector3.Distance(b.position,c.position);
            Vector3 d=target-a.position;float distance=Mathf.Clamp(d.magnitude,.005f,(l1+l2)*.998f);
            Vector3 axis=d.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(pole,axis).normalized;
            if(bend.sqrMagnitude<.001f)bend=Vector3.ProjectOnPlane(Vector3.forward,axis).normalized;
            float along=(distance*distance+l1*l1-l2*l2)/(2*distance);
            Vector3 elbow=a.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            a.rotation=Quaternion.FromToRotation(b.position-a.position,elbow-a.position)*a.rotation;
            Vector3 reachable=a.position+axis*distance;
            b.rotation=Quaternion.FromToRotation(c.position-b.position,reachable-b.position)*b.rotation;
        }
    }
}
