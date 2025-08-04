using BepInEx;
using EntityStates;
using EntityStates.Halcyonite;
using LootPoolLimiter;
using RoR2;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static EntityStates.Halcyonite.WhirlWindPersuitCycle;

namespace HalcyonFixes;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]

public class HalcyonFixes : BaseUnityPlugin
{
    public const string PluginGUID = PluginAuthor + "." + PluginName;
    public const string PluginAuthor = "Onyx";
    public const string PluginName = "HalcyonFixes";
    public const string PluginVersion = "1.0.0";

    public void Awake()
    {
        Log.Init(Logger);

        RoR2.Skills.SkillDef LongrangeHalcSkill = Addressables.LoadAssetAsync<RoR2.Skills.SkillDef>("RoR2/DLC2/Halcyonite/HalcyoniteMonsterTriLaser.asset").WaitForCompletion();
        LongrangeHalcSkill.interruptPriority = InterruptPriority.Any;
        LongrangeHalcSkill = Addressables.LoadAssetAsync<RoR2.Skills.SkillDef>("RoR2/DLC2/Halcyonite/HalcyoniteMonsterWhirlwindRush.asset").WaitForCompletion();
        LongrangeHalcSkill.interruptPriority = InterruptPriority.Any;

        On.EntityStates.Halcyonite.WhirlwindWarmUp.OnEnter += (orig, self) =>
        {
            
            if (self.isAuthority)
            {
                self.outer.SetNextState(new WhirlWindPersuitCycle());
            }
        };


        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.OnEnter += (orig, self) =>
        {
            //WhirlWindPersuitCycle.dashSpeedCoefficient = 30; //20
            //WhirlWindPersuitCycle.dashSafeExitDuration = 2f; //5
            WhirlWindPersuitCycle.decelerateDuration = 1f; //1

            Animator animator = self.GetModelAnimator();
            ChildLocator childLocator = animator.GetComponent<ChildLocator>();
            self.PlayCrossfade("FullBody Override", "WhirlwindRushEnter", "WhirlwindRush.playbackRate", 0.5f, 0.1f);
            Util.PlaySound("Play_halcyonite_skill3_start", base.gameObject);
            self.SmallHop(self.characterMotor, 9);
            CharacterGravityParameters gravityParameters = self.characterMotor.gravityParameters;
            gravityParameters.channeledAntiGravityGranterCount++;
            self.characterMotor.gravityParameters = gravityParameters;
            CharacterFlightParameters flightParameters = self.characterMotor.flightParameters;
            flightParameters.channeledFlightGranterCount++;
            self.characterMotor.flightParameters = flightParameters;
            self.characterMotor.walkSpeedPenaltyCoefficient = 0f;
            self.gameObject.AddComponent<origPersuit>();
            self.gameObject.GetComponent<origPersuit>().reset();
        };

        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.FixedUpdate += (orig, self) =>
        {
            origPersuit origPersuit = self.gameObject.GetComponent<origPersuit>();
            if (!(origPersuit.fixedAge < 0.5f))
            {
                origPersuit.OnEnter(self);
                orig(self);
            }
        };

        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.CheckIfArrived += (orig, self) =>
        {
            Ray ray = self.GetAimRay();
            if (Vector3.Dot(ray.direction, self.targetMoveDirt) < 0f)
            {
                self.state = PersuitState.Decelerate;
                self.startDecelerateTimeStamp = self.gameObject.GetComponent<origPersuit>().fixedAge;
                self.startedDash = false;
            }
        };

        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.UpdateDecelerate += (orig, self) =>
        {
            if (self.gameObject.GetComponent<origPersuit>().fixedAge > self.startDecelerateTimeStamp + decelerateDuration)
            {
                self.state = PersuitState.Land;
            }
            self.characterMotor.velocity = self.targetMoveDirt.normalized * dashSpeedCoefficient * Mathf.Lerp(1, 0, (self.gameObject.GetComponent<origPersuit>().fixedAge - self.startDecelerateTimeStamp) / decelerateDuration);
        };

        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.UpdateFindTarget += (orig, self) =>
        {
            Ray aimRay = self.GetAimRay();
            BullseyeSearch enemyFinder = new BullseyeSearch();
            enemyFinder.viewer = self.characterBody;
            enemyFinder.maxDistanceFilter = WhirlWindPersuitCycle.maxSearchDist;
            enemyFinder.searchOrigin = aimRay.origin;
            enemyFinder.searchDirection = aimRay.direction;
            enemyFinder.filterByLoS = false;
            enemyFinder.sortMode = BullseyeSearch.SortMode.Angle;
            enemyFinder.teamMaskFilter = TeamMask.allButNeutral;
            if ((bool)self.teamComponent)
            {
                enemyFinder.teamMaskFilter.RemoveTeam(self.teamComponent.teamIndex);
            }
            enemyFinder.RefreshCandidates();
            HurtBox hurtBox = enemyFinder.GetResults().FirstOrDefault();
            if (hurtBox != null)
            {
                self.targetBody = hurtBox.healthComponent.body;
                self.targetPos = self.targetBody.footPosition + (base.transform.position - self.targetBody.footPosition).normalized * 2f;
            } else
            {
                self.targetPos = aimRay.origin + aimRay.direction;
            }
            self.findTargetTimeStamp = self.fixedAge;
            self.startForwardDirt = self.characterDirection.forward;
            self.state = PersuitState.Dash;
        };

        On.EntityStates.Halcyonite.WhirlWindPersuitCycle.GetMinimumInterruptPriority += (orig, self) =>
        {
            return InterruptPriority.PrioritySkill;
        };
    }

    public class origPersuit : MonoBehaviour
    {
        bool ran = false;
        public float fixedAge = 0;

        public void reset()
        {
            this.fixedAge = 0;
            ran = false;
        }
        public void OnEnter(WhirlWindPersuitCycle self)
        {
            if (!ran)
            {
                ran = true;
                Util.PlaySound("Play_halcyonite_skill3_loop", base.gameObject);
                self.characterMotor.walkSpeedPenaltyCoefficient = 1f;
                self.state = PersuitState.Find_Target;
                ChildLocator modelChildLocator = self.GetModelChildLocator();
                if (whirlWindVortexVFXPrefab != null)
                {
                    Transform parent = modelChildLocator.FindChild("WhirlWindPoint");
                    self.whirlwindVortexInstance = Instantiate(whirlWindVortexVFXPrefab, parent);
                }
            }
        }

        void Update()
        {
            fixedAge += Time.deltaTime;
        }
    }
}
