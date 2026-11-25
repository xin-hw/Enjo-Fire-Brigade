using System;
using UnityEngine;

namespace FireRescue
{
    // The sole element-application boundary. A future server can invoke this after validating a spray command.
    public sealed class ElementReaction : MonoBehaviour
    {
        [SerializeField] FireField field;
        [SerializeField, Min(0f)] float waterPushImpulsePerUnit = 48f;
        [SerializeField] bool powderSurfaceSplash;
        bool waterHandledBySurface;
        const int FixedObstacles = 1 << 8;
        const float GroundClearance = .1f;
        Vector3 waterImpact;
        Predicate<Vector3> waterCanReach;
        Vector3 powderImpact;
        Predicate<Vector3> powderCanReach;
        public void Initialize(FireField fireField) => field = fireField;
        internal void UsePhysicalWaterDeposits() => waterHandledBySurface = true;
        public void EnablePowderSurfaceSplash(bool enabled)=>powderSurfaceSplash=enabled;

        public void Apply(ElementKind element, Vector3 point, Vector3 direction, float amount, Collider hit = null)
        {
            if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            // Physical targets own their fire class. Do not project their response onto
            // the floor beneath a counter, or require a grid/visual for a discrete fire.
            ElementReceiver receiver = hit ? hit.GetComponentInParent<ElementReceiver>() : null;
            if (receiver && receiver.isActiveAndEnabled) { GiveToReceiver(receiver, element, point, direction, amount); return; }
            if (!field) return;
            if (element == ElementKind.Oil) { field.ApplyOil(point, amount, .8f); return; }
            bool water = element == ElementKind.Water;
            if (!water && element != ElementKind.DryPowder) return;

            // Static solids stop the jet, rather than projecting a floor splash from inside
            // their footprint. Dynamic targets still receive their existing water impulse.
            bool fixedSolid = hit && hit.gameObject.layer == 8 &&
                (!hit.attachedRigidbody || hit.attachedRigidbody.isKinematic);
            if (!fixedSolid)
            {
                waterImpact = new Vector3(point.x, field.Origin.y + GroundClearance, point.z);
                if (waterCanReach == null) waterCanReach = WaterReachesCell;
                if (water) field.ApplyWater(point, amount, .95f, waterCanReach);
                else field.ApplyDryPowder(point, amount, .7f, waterCanReach);
            }
            if (!water || !hit) return;
            FirefighterMotor player = hit.GetComponentInParent<FirefighterMotor>();
            if (player) player.AddImpulse(direction * amount * 5);
            else if (hit.attachedRigidbody && !hit.attachedRigidbody.isKinematic)
                hit.attachedRigidbody.AddForce(direction * amount * waterPushImpulsePerUnit, ForceMode.Impulse);
        }

        /// <summary>
        /// Applies a directional spray impact using the caller's authored footprint and residue lifetime.
        /// Timed floor residues are blocked by layer-8 fixed solids and filtered by the existing low LOS test.
        /// </summary>
        public void ApplySpray(ElementKind element, Vector3 point, Vector3 direction, float amount, Collider hit,
            float radius, float residueSeconds)
            => ApplySpray(element, point, direction, amount, hit, radius, residueSeconds, SprayEffectSettings.Neutral);

        public void ApplySpray(ElementKind element, Vector3 point, Vector3 direction, float amount, Collider hit,
            float radius, float residueSeconds, SprayEffectSettings effects)
            => ApplySprayCore(element,point,direction,amount,hit,radius,residueSeconds,effects,false,1f);

        // Ballistic water carries momentum, not a cursor-directed fixed push. Keep the
        // legacy API's force contract for old levels; flow/extinguishing is unchanged.
        public void ApplyBallisticImpact(ElementKind element,Vector3 point,Vector3 velocity,float amount,Collider hit,
            float radius,float residueSeconds,SprayEffectSettings effects)
        {
            if(!IsFinite(velocity))return;
            float speed=velocity.magnitude;
            ApplySprayCore(element,point,speed>.0001f?velocity/speed:Vector3.zero,amount,hit,radius,residueSeconds,
                effects,true,Mathf.Clamp(speed/10f,0f,2f));
        }

        // A jet mostly slides along a grazing surface. Only its stopped normal momentum
        // (plus small tangential friction) is transferred, at the real contact point.
        // Dose and residue still use the incoming direction, not this force direction.
        public void ApplyBallisticSurfaceImpact(ElementKind element,Vector3 point,Vector3 velocity,Vector3 normal,
            float amount,Collider hit,float radius,float residueSeconds,SprayEffectSettings effects)
        {
            if(!IsFinite(velocity)||!IsFinite(normal))return;
            if(normal.sqrMagnitude<.0001f)
            { ApplyBallisticImpact(element,point,velocity,amount,hit,radius,residueSeconds,effects);return; }
            normal.Normalize();
            float incoming=Mathf.Max(0f,-Vector3.Dot(velocity,normal));
            Vector3 momentumVelocity=-normal*incoming+Vector3.ProjectOnPlane(velocity,normal)*.12f;
            float speed=velocity.magnitude;
            ApplySprayCore(element,point,speed>.0001f?velocity/speed:Vector3.zero,amount,hit,radius,residueSeconds,
                effects,true,Mathf.Clamp(momentumVelocity.magnitude/10f,0f,2f),momentumVelocity.normalized);
        }

        void ApplySprayCore(ElementKind element,Vector3 point,Vector3 direction,float amount,Collider hit,
            float radius,float residueSeconds,SprayEffectSettings effects,bool atContact,float momentum,Vector3? forceDirection=null)
        {
            if (!IsFinite(point) || !IsFinite(direction) || !IsFinite(amount) || !IsFinite(radius) || !IsFinite(residueSeconds) ||
                amount <= 0f || radius < 0f || residueSeconds < 0f)
                return;

            effects = effects.Validated();
            bool water = element == ElementKind.Water;
            bool oil = element == ElementKind.Oil;
            bool powder = element == ElementKind.DryPowder;
            bool gas = element == ElementKind.CarbonDioxide;
            if (!water && !oil && !powder && !gas) return;
            ElementReceiver receiver = hit ? hit.GetComponentInParent<ElementReceiver>() : null;
            if (receiver && receiver.isActiveAndEnabled)
            {
                GiveToReceiver(receiver, element, point, direction, amount * (oil ? 1f : effects.ExtinguishMultiplier));
                if(powder&&powderSurfaceSplash)ApplyPowderSplash(point,direction,amount,radius,residueSeconds,effects);
                return;
            }
            if (gas) return;

            bool fixedSolid = hit && hit.gameObject.layer == 8 &&
                (!hit.attachedRigidbody || hit.attachedRigidbody.isKinematic);
            if(powder&&fixedSolid&&powderSurfaceSplash)
                ApplyPowderSplash(point,direction,amount,radius,residueSeconds,effects);
            // Elevated physical contacts can splash or push the object, but have no
            // direct contact with the floor grid beneath it.
            bool elevatedWaterContact = water && atContact && hit && field && point.y > field.Origin.y + GroundClearance;
            if (!fixedSolid && field && !elevatedWaterContact && (!water || !waterHandledBySurface))
            {
                waterImpact = new Vector3(point.x, field.Origin.y + GroundClearance, point.z);
                if (waterCanReach == null)
                    waterCanReach = WaterReachesCell;

                if (water)
                    field.ApplyTimedWater(point, amount, radius, residueSeconds,
                        effects.ExtinguishMultiplier, effects.ResidueMultiplier, waterCanReach);
                else if (oil)
                    field.ApplyTimedOil(point, amount * effects.ResidueMultiplier, radius, residueSeconds, waterCanReach);
                else
                    field.ApplyTimedDryPowder(point, amount, radius, residueSeconds,
                        effects.ExtinguishMultiplier, effects.ResidueMultiplier, waterCanReach);
            }

            if (!water || !hit)
                return;

            FirefighterMotor player = hit.GetComponentInParent<FirefighterMotor>();
            if (player)
                player.AddImpulse((forceDirection??direction) * amount * 5f * effects.PushMultiplier * momentum);
            else if (hit.attachedRigidbody && !hit.attachedRigidbody.isKinematic)
            {
                var impulse=(forceDirection??direction)*amount*waterPushImpulsePerUnit*effects.PushMultiplier*momentum;
                if(atContact)hit.attachedRigidbody.AddForceAtPosition(impulse,point,ForceMode.Impulse);
                else hit.attachedRigidbody.AddForce(impulse,ForceMode.Impulse);
            }
        }

        static void GiveToReceiver(ElementReceiver receiver, ElementKind element, Vector3 point, Vector3 direction, float amount)
        {
            if (receiver is ObjectFire fire) fire.ReceiveImpact(element, amount, point, direction);
            else receiver.Receive(element, amount);
        }

        bool WaterReachesCell(Vector3 center)
        {
            center.y = waterImpact.y;
            return !Physics.Linecast(waterImpact, center, FixedObstacles, QueryTriggerInteraction.Ignore);
        }

        void ApplyPowderSplash(Vector3 point,Vector3 direction,float amount,float radius,float duration,SprayEffectSettings effects)
        {
            if(!field)return;
            // A powder cloud can clear the counter edge then settle, unlike a water
            // splash projected through a solid. Both legs still respect authored walls.
            powderImpact=point-direction.normalized*.08f+Vector3.up*.08f;
            powderImpact.y=Mathf.Max(powderImpact.y,field.Origin.y+.15f);
            if(powderCanReach==null)powderCanReach=PowderReachesCell;
            field.ApplyTimedDryPowder(point,amount,radius,duration,effects.ExtinguishMultiplier,effects.ResidueMultiplier,powderCanReach);
        }
        bool PowderReachesCell(Vector3 center)
        {
            center.y=field.Origin.y+.12f;
            var above=new Vector3(center.x,powderImpact.y,center.z);
            return !Physics.Linecast(powderImpact,above,FixedObstacles,QueryTriggerInteraction.Ignore)&&
                !Physics.Linecast(above,center,FixedObstacles,QueryTriggerInteraction.Ignore);
        }

        static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
