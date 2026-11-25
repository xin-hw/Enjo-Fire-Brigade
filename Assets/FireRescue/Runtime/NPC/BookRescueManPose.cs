using System;
using System.Linq;
using UnityEngine;

namespace FireRescue
{
    // Procedural poses for the artist's low-poly mesh and the derived generic skeleton.
    public sealed class BookRescueManPose : MonoBehaviour
    {
        [Serializable]
        struct Limb
        {
            public Transform Upper, Lower, End;
        }

        [SerializeField] Transform hips;
        [SerializeField] Limb leftArm, rightArm, leftLeg, rightLeg;
        [SerializeField] CapsuleCollider hitBody;
        Quaternion[] restRotations;
        Transform[] posedBones;
        Transform head;
        Vector3 hipOrigin;
        Quaternion hipsRest, headRest;
        float seated = 1f, stride, sip, offer;
        float alarm, alarmTime, reliefAge = 1f;
        bool wasOnFire;

        public Transform BottleHand => rightArm.End;
        public Transform BookHand => leftArm.End;
        public bool BottleOffered => offer >= .99f;
        internal Vector3 HeadPosition => head ? head.position : transform.TransformPoint(Vector3.up);

        public void Initialize(Transform model, CapsuleCollider body)
        {
            var bones = model.GetComponentsInChildren<Transform>(true);
            hips = Bone("Hips");
            leftArm = Chain("L_UpperArm", "L_Forearm", "L_Hand");
            rightArm = Chain("R_UpperArm", "R_Forearm", "R_Hand");
            leftLeg = Chain("L_Thigh", "L_Shin", "L_Foot");
            rightLeg = Chain("R_Thigh", "R_Shin", "R_Foot");
            hitBody = body;

            Transform Bone(string name) => bones.Single(bone => bone.name == name);
            Limb Chain(string upper, string lower, string end) =>
                new() { Upper = Bone(upper), Lower = Bone(lower), End = Bone(end) };
        }

        void Awake()
        {
            head = hips.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == "Head");
            posedBones = new[] { leftArm.Upper, leftArm.Lower, rightArm.Upper, rightArm.Lower,
                leftLeg.Upper, leftLeg.Lower, rightLeg.Upper, rightLeg.Lower };
            restRotations = posedBones.Select(bone => bone.localRotation).ToArray();
            hipOrigin = hips.localPosition;
            hipsRest = hips.localRotation;
            if (head) headRest = head.localRotation;
            Pose(0f, 0f, true, false, false, false, false);
        }

        internal void Pose(float dt, float distance, bool sitting, bool holdingBottle, bool drinking, bool offeringBottle, bool onFire)
        {
            if (wasOnFire && !onFire) reliefAge = 0f;
            wasOnFire = onFire;
            reliefAge = Mathf.Min(1f, reliefAge + dt);
            alarm = Mathf.MoveTowards(alarm, onFire ? 1f : 0f, (onFire ? 6f : 4.8f) * dt);
            alarmTime += dt;
            seated = Mathf.MoveTowards(seated, sitting ? 1f : 0f, 2.8f * dt);
            sip = Mathf.MoveTowards(sip, drinking ? 1f : 0f, 2.9f * dt);
            offer = Mathf.MoveTowards(offer, offeringBottle && reliefAge >= .38f ? 1f : 0f, 3.6f * dt);
            stride += distance * (Mathf.PI * 2f / Mathf.Lerp(1.12f, .80f, alarm));
            float walking = dt > 0 ? Mathf.Clamp01(distance / (1.3f * dt)) * (1f - seated) : 0f;
            float swing = Mathf.Sin(stride) * walking;
            float shake = Mathf.Sin(alarmTime * 14f);
            float exhale = Mathf.Sin(Mathf.PI * Mathf.Clamp01(reliefAge / .90f));
            for (int i = 0; i < posedBones.Length; i++) posedBones[i].localRotation = restRotations[i];
            hips.localRotation = hipsRest;
            hips.localPosition = hipOrigin + hips.parent.InverseTransformVector(Vector3.up *
                (-.235f * seated + Mathf.Abs(swing) * (.026f + .024f * alarm) +
                    seated * (.045f * alarm * Mathf.Abs(shake) - .07f * exhale)));
            hips.rotation = Quaternion.AngleAxis(alarm * Mathf.Lerp(17f, -15f, seated) + exhale * 18f, transform.right) *
                Quaternion.AngleAxis(8f * alarm * shake, transform.forward) * hips.rotation;
            if (head)
            {
                head.localRotation = headRest;
                head.rotation = Quaternion.AngleAxis(23f * alarm * shake, transform.up) *
                    Quaternion.AngleAxis(21f * exhale, transform.right) * head.rotation;
            }
            PlaceLeg(leftLeg, -.035f, swing, shake);
            PlaceLeg(rightLeg, .035f, -swing, -shake);
            PlaceArm(leftArm, -.25f, -swing, false, Mathf.Sin(alarmTime * 16f));
            PlaceArm(rightArm, .25f, swing, holdingBottle, Mathf.Sin(alarmTime * 16f + 2.6f));
            hitBody.center = new Vector3(0, Mathf.Lerp(.89f, .70f, seated), .22f * seated);
            hitBody.height = Mathf.Lerp(1.48f, 1.1f, seated);
        }

        internal void GripBottle(Transform bottle)
        {
            float scale = transform.lossyScale.y;
            Vector3 forearm = (rightArm.End.position - rightArm.Lower.position).normalized;
            Vector3 grip = rightArm.End.position + (forearm * .046f - transform.right * .043f) * scale;
            Vector3 mouth = HeadPosition + (transform.forward * .13f + Vector3.up * .09f) * scale;
            Vector3 axis = Vector3.Slerp(Vector3.up, (mouth - grip).normalized, sip);
            bottle.SetPositionAndRotation(grip - axis * (.074f * scale),
                Quaternion.FromToRotation(Vector3.up, axis) * transform.rotation);
        }

        void PlaceLeg(Limb leg, float outward, float swing, float shake)
        {
            Aim(leg.Upper, leg.Lower, Vector3.Lerp(new Vector3(outward,-1f,swing*.52f), new Vector3(outward,-.23f,1f), seated));
            Aim(leg.Lower, leg.End, Vector3.Lerp(new Vector3(outward,-1f,swing*.25f-.18f),
                new Vector3(outward,-1f,.055f + .35f * alarm * shake), seated));
        }

        void PlaceArm(Limb arm, float outward, float swing, bool bottle, float wave)
        {
            var shoulder = new Vector3(outward, -1f, .13f + swing * .41f + seated * .35f);
            var forearm = new Vector3(outward * .35f, -1f, .25f + swing * .29f + seated * .50f);
            if (bottle)
            {
                shoulder = Vector3.Lerp(new Vector3(outward,-.82f,.52f), new Vector3(outward,-.38f,.75f), sip);
                forearm = Vector3.Lerp(new Vector3(-outward*.5f,.69f,.63f), new Vector3(-.46f,.83f,.32f), sip);
                shoulder = Vector3.Lerp(shoulder, new Vector3(outward * .70f,-.48f,.89f), offer);
                forearm = Vector3.Lerp(forearm, new Vector3(-outward * .70f,.20f,.96f), offer);
            }
            else
            {
                float side = Mathf.Sign(outward);
                shoulder = Vector3.Lerp(shoulder, new Vector3(side * (.83f + .15f * wave), .25f + .56f * wave, .58f), alarm);
                forearm = Vector3.Lerp(forearm, new Vector3(side * (.39f - .28f * wave), .87f, .42f), alarm);
            }
            Aim(arm.Upper, arm.Lower, shoulder);
            Aim(arm.Lower, arm.End, forearm);
        }

        void Aim(Transform bone, Transform next, Vector3 actorDirection)
        {
            Vector3 current = next.position - bone.position;
            bone.rotation = Quaternion.FromToRotation(current, transform.TransformDirection(actorDirection)) * bone.rotation;
        }
    }
}
