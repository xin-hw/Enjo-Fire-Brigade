using UnityEngine;

namespace FireRescue
{
    // Scene-specific requirement, not a new calming mechanic. Other NPCs need no component.
    public sealed class NpcItemRequest : MonoBehaviour
    {
        [SerializeField] string wantedItemId = "pudding";
        [SerializeField] Transform receivingHand;
        [SerializeField] BreakableDoor bedroomDoor;
        [SerializeField] ObjectFire cookingFire;
        [SerializeField] FirstReviewMission mission;
        [SerializeField] BookRescueSequence bookRescue;
        public bool IsFulfilled { get; private set; }
        public bool IsPoisoned {get;private set;}
        public bool IsDead {get;private set;}
        public bool AutomaticTick {get;set;}=true;
        float poisonAge;
        public bool CanLeave => IsFulfilled && !IsPoisoned && !IsDead && bedroomDoor && bedroomDoor.IsBroken && cookingFire && !cookingFire.IsBurning;
        public Transform ReceivingHand => receivingHand;
        public void Initialize(Transform hand, BreakableDoor door, ObjectFire fire, FirstReviewMission owner)
        { receivingHand = hand; bedroomDoor = door; cookingFire = fire; mission = owner; }
        public void InitializeBook(Transform hand, BookRescueSequence sequence, FirstReviewMission owner)
        {
            wantedItemId = "book";
            receivingHand = hand;
            mission = owner;
            bookRescue = sequence;
        }
        internal bool CanAcceptHeldItem(CarryItem item) => !bookRescue || (item && item.ItemId == wantedItemId && bookRescue.CanTakeBook);
        public bool TryAccept(CarryItem item)
        {
            if (!isActiveAndEnabled || IsFulfilled || !item || item.ItemId != wantedItemId ||
                !mission || !mission.CanDeliver || !CanAcceptHeldItem(item)) return false;
            if (!bookRescue && (!bedroomDoor || !bedroomDoor.IsBroken)) return false;
            if (!item.Deliver(receivingHand)) return false;
            IsFulfilled = true;
            if (bookRescue)
            {
                mission.CompleteBookRescue();
                return true;
            }
            if(item.IsBurnt){IsPoisoned=true;poisonAge=0;mission.NotifyPoisoned();}
            else mission.NotifyDelivered();
            return true;
        }
        void Update(){if(AutomaticTick)Tick(Time.deltaTime);}
        public void Tick(float dt)
        {
            if(!IsPoisoned||IsDead||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            poisonAge+=dt;if(poisonAge<1.4f)return;
            IsDead=true;if(mission)mission.Fail(FirstReviewMission.Failure.ResidentPoisoned);
        }
    }
}
