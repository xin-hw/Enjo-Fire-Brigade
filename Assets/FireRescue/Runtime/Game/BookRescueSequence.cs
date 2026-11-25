using UnityEngine;

namespace FireRescue
{
    public sealed class BookRescueSequence : MonoBehaviour
    {
        public enum Stage { Seated, StandingUp, WalkingToFridge, TakingSoda, Returning, SittingDown, Drinking }
        public const string Joke = "我懷疑你的本質是一本書，不然怎麼讓我越看越想睡?";
        public const string BookRequest = "把書交給油膩男子";
        const float FridgeIgnitionInterval = 3.6f;

        [SerializeField] FireField field;
        [SerializeField] FirstReviewMission mission;
        [SerializeField] HoseConnection hose;
        [SerializeField] BookRescueManPose man;
        [SerializeField] ObjectFire sofaFire, manFire, fridgeFire, computerFire;
        [SerializeField] ComputerFireSpread computerSpread;
        [SerializeField] FluidSource soda;
        [SerializeField] Vector3[] fridgeRoute;
        [SerializeField] Vector3[] fridgeHeatOffsets = System.Array.Empty<Vector3>();
        Vector3 seatPosition, lastIgnition;
        Quaternion seatFacing;
        float stageAge, fridgeHeatAge;
        int waypoint, nextFridgeHeatPoint;
        bool usedWater, usedPowder;

        public Stage CurrentStage { get; private set; }
        public bool AutomaticTick { get; set; } = true;
        public ObjectFire SofaFire => sofaFire;
        public ObjectFire ManFire => manFire;
        public ObjectFire FridgeFire => fridgeFire;
        public ObjectFire ComputerFire => computerFire;
        internal ComputerFireSpread ComputerSpread => computerSpread;
        public FluidSource Soda => soda;
        public Transform Man => man.transform;
        internal Vector3 DialogueAnchor => man.HeadPosition + Vector3.up * .39f;
        bool ReadyToOfferBottle => CurrentStage == Stage.Drinking && stageAge > .65f && !manFire.IsBurning;
        public bool CanUseSoda => ReadyToOfferBottle && man.BottleOffered;
        bool StudyFireActive => computerSpread ? computerSpread.IsBurning : computerFire.IsBurning;
        public bool CanTakeBook => CanUseSoda && !StudyFireActive;
        public int TrailIgnitions { get; private set; }
        internal int FridgeHeatIgnitions { get; private set; }

        public string CurrentObjective
        {
            get
            {
                if (!usedWater) return "繞過沙發，去浴室接馬桶水";
                if (!usedPowder) return "用水清出路，去倉庫接蘇打粉";
                if (manFire.IsBurning) return "用蘇打粉幫男子滅火";
                if (!CanUseSoda) return "等男子帶著汽水回到沙發";
                if (StudyFireActive) return "接汽水，在書房門外用二氧化碳熄滅藍火";
                return "從書櫃拿書，" + BookRequest;
            }
        }

        public void Initialize(FireField floor, FirstReviewMission state, HoseConnection connection, BookRescueManPose actor,
            ObjectFire sofa, ObjectFire resident, ObjectFire fridge, ObjectFire computer, FluidSource bottle, Vector3[] route)
        {
            field = floor;
            mission = state;
            hose = connection;
            man = actor;
            sofaFire = sofa;
            manFire = resident;
            fridgeFire = fridge;
            computerFire = computer;
            soda = bottle;
            fridgeRoute = route;
        }

        void Awake()
        {
            seatPosition = Man.position;
            seatFacing = Man.rotation;
            lastIgnition = seatPosition;
            soda.enabled = false;
            soda.gameObject.SetActive(false);
        }

        void Update()
        {
            if (AutomaticTick) Tick(Time.deltaTime);
        }

        internal void Tick(float dt)
        {
            if (!mission.AllowsInput || !float.IsFinite(dt) || dt <= 0f) return;
            var source = hose.Source;
            if (source)
            {
                usedWater |= source.Element == ElementKind.Water;
                usedPowder |= source.Element == ElementKind.DryPowder;
            }

            stageAge += dt;
            float travelled = 0f;
            switch (CurrentStage)
            {
                case Stage.Seated when stageAge >= .82f:
                    manFire.Ignite(3f);
                    Enter(Stage.StandingUp);
                    break;
                case Stage.StandingUp when stageAge >= .42f:
                    waypoint = 0;
                    Enter(Stage.WalkingToFridge);
                    break;
                case Stage.WalkingToFridge:
                    if (WalkTo(fridgeRoute[waypoint], dt, out travelled) && ++waypoint == fridgeRoute.Length)
                    {
                        if (manFire.IsBurning) fridgeFire.Ignite(2.4f);
                        Enter(Stage.TakingSoda);
                    }
                    break;
                case Stage.TakingSoda when stageAge >= .66f:
                    soda.gameObject.SetActive(true);
                    waypoint = fridgeRoute.Length - 2;
                    Enter(Stage.Returning);
                    break;
                case Stage.Returning:
                    Vector3 target = waypoint >= 0 ? fridgeRoute[waypoint] : seatPosition;
                    if (WalkTo(target, dt, out travelled) && waypoint-- < 0) Enter(Stage.SittingDown);
                    break;
                case Stage.SittingDown:
                    Man.rotation = Quaternion.RotateTowards(Man.rotation, seatFacing, dt * 540f);
                    if (stageAge >= .45f)
                    {
                        if (computerSpread) computerSpread.Ignite();
                        else computerFire.Ignite(3.1f);
                        Enter(Stage.Drinking);
                    }
                    break;
            }

            if (travelled > 0f && manFire.IsBurning && (Man.position-lastIgnition).sqrMagnitude >= .64f * .64f)
            {
                lastIgnition = Man.position;
                field.Ignite(lastIgnition, .44f);
                TrailIgnitions++;
            }
            SpreadFridgeHeat(dt);
            if (computerSpread) computerSpread.Tick(dt);

            bool seated = CurrentStage is Stage.Seated or Stage.SittingDown or Stage.Drinking;
            bool bottleVisible = soda.gameObject.activeSelf;
            bool raiseBottle = CurrentStage == Stage.Drinking && stageAge < .62f;
            man.Pose(dt, travelled, seated, bottleVisible || CurrentStage == Stage.TakingSoda, raiseBottle, ReadyToOfferBottle,
                manFire.IsBurning);
            if (bottleVisible) man.GripBottle(soda.transform);
            if (soda.enabled != CanUseSoda) soda.enabled = CanUseSoda;
        }

        void SpreadFridgeHeat(float dt)
        {
            if (!fridgeFire.IsBurning)
            {
                fridgeHeatAge = 0f;
                return;
            }
            fridgeHeatAge += dt;
            if (fridgeHeatAge < FridgeIgnitionInterval) return;
            fridgeHeatAge -= FridgeIgnitionInterval;
            Transform fridge = fridgeFire.transform;
            Vector3 front = fridge.TransformPoint(Vector3.right * .20f);
            for (int i = 0; i < fridgeHeatOffsets.Length; i++)
            {
                Vector3 candidate = fridge.TransformPoint(fridgeHeatOffsets[nextFridgeHeatPoint]);
                nextFridgeHeatPoint = (nextFridgeHeatPoint + 1) % fridgeHeatOffsets.Length;
                if (!field.TryGetCell(candidate, out var cell) || field.GetCell(cell.x, cell.y).IsBurning) continue;
                Vector3 ground = field.CellCenter(cell.x, cell.y);
                if (!field.CanTransferHeat(front, ground)) continue;
                field.Ignite(ground, .48f);
                if (!field.GetCell(cell.x, cell.y).IsBurning) continue;
                FridgeHeatIgnitions++;
                break;
            }
        }

        bool WalkTo(Vector3 target, float dt, out float distance)
        {
            Vector3 previous = Man.position;
            Vector3 step = Vector3.MoveTowards(previous, target, 1.85f * dt) - previous;
            Man.position = previous + step;
            distance = step.magnitude;
            if (step.sqrMagnitude > .000001f)
                Man.rotation = Quaternion.RotateTowards(Man.rotation, Quaternion.LookRotation(step), dt * 620f);
            return (Man.position - target).sqrMagnitude < .000001f;
        }

        void Enter(Stage next)
        {
            CurrentStage = next;
            stageAge = 0f;
        }
    }
}
