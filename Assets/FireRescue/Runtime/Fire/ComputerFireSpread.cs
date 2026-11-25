using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    // The last patch spans the doorway. All patches are separate spray receivers.
    public sealed class ComputerFireSpread : MonoBehaviour
    {
        const float SpreadInterval = 2.8f;
        [SerializeField] ObjectFire computer;
        [SerializeField] CharacterController player;
        [SerializeField] WorldOutlineView outlines;
        [SerializeField] Vector3[] spreadPoints = Array.Empty<Vector3>();
        ObjectFire[] fires = Array.Empty<ObjectFire>();
        BoxCollider[] contacts = Array.Empty<BoxCollider>();
        int nextPoint;
        float spreadAge;
        bool retreatAllowed;

        internal IReadOnlyList<ObjectFire> Fires => fires;
        internal int IgnitedCount => nextPoint;
        internal bool DoorwayBurning => fires.Length != 0 && fires[fires.Length - 1].IsBurning;
        internal bool IsBurning
        {
            get
            {
                if (computer.IsBurning) return true;
                foreach (var fire in fires)
                    if (fire.IsBurning) return true;
                return false;
            }
        }

        void Start()
        {
            fires = new ObjectFire[spreadPoints.Length];
            contacts = new BoxCollider[spreadPoints.Length];
            for (int i = 0; i < spreadPoints.Length; i++)
            {
                bool doorway = i == spreadPoints.Length - 1;
                var patch = new GameObject(doorway ? "Study doorway fire" : "Study electrical fire");
                patch.SetActive(false);
                patch.transform.SetParent(transform, false);
                patch.transform.localPosition = spreadPoints[i];
                var contact = patch.AddComponent<BoxCollider>();
                contact.size = doorway ? new Vector3(1.05f, .86f, .29f) : new Vector3(.53f, .77f, .32f);
                contact.center = Vector3.up * (contact.size.y * .5f);
                contact.enabled = false;
                contacts[i] = contact;
                var fire = patch.AddComponent<ObjectFire>();
                fire.Configure(FireKind.Electrical, 0f);
                fires[i] = fire;

                var feet = new Vector3[doorway ? 5 : 3];
                for (int j = 0; j < feet.Length; j++)
                    feet[j] = new Vector3(Mathf.Lerp(-.38f, .38f, j / (float)(feet.Length - 1)) * contact.size.x,
                        .02f, j % 2 == 0 ? -.07f : .06f);
                patch.AddComponent<ClassifiedFireView>().Initialize(fire, feet, doorway ? .96f : .76f);
                patch.SetActive(true);
                if (outlines) outlines.AddEffect(patch.transform);
            }
        }

        internal void Ignite() => computer.Ignite(.65f);

        internal void Tick(float dt)
        {
            if (computer.IsBurning)
            {
                computer.GrowWhileBurning(.28f * dt, 2.8f);
                spreadAge += dt;
                if (nextPoint < fires.Length && spreadAge >= SpreadInterval)
                {
                    spreadAge -= SpreadInterval;
                    fires[nextPoint].Ignite(nextPoint == fires.Length - 1 ? 1.08f : .53f);
                    nextPoint++;
                }
            }

            for (int i = 0; i < nextPoint; i++)
            {
                bool doorway = i == fires.Length - 1;
                fires[i].GrowWhileBurning(.12f * dt, doorway ? 1.5f : 1.08f);
                if (doorway) UpdateDoorway(fires[i], contacts[i]);
                else if (contacts[i].enabled != fires[i].IsBurning)
                {
                    contacts[i].enabled = fires[i].IsBurning;
                    if (contacts[i].enabled && player) Physics.IgnoreCollision(player, contacts[i], true);
                }
            }
        }

        void UpdateDoorway(ObjectFire fire, BoxCollider contact)
        {
            if (contact.enabled != fire.IsBurning)
            {
                contact.enabled = fire.IsBurning;
                // A player already on the room side can retreat past a newly lit entrance.
                retreatAllowed = contact.enabled && player && player.transform.position.z >= contact.bounds.center.z;
                if (retreatAllowed) Physics.IgnoreCollision(player, contact, true);
            }
            if (!retreatAllowed || !player ||
                player.transform.position.z + player.radius + .02f >= contact.bounds.min.z) return;
            retreatAllowed = false;
            Physics.IgnoreCollision(player, contact, false);
        }
    }
}
