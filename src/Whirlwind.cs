using BepInEx;
using EntityStates;
using RoR2;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace HalcyonFixes;

public class Whirlwind : BaseState
{
    public static GameObject whirlWindVortexVFXPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Halcyonite/WhirlWindHalcyoniteVortexVFX.prefab").WaitForCompletion();
    public const float maxSearchDist = 100;
    public const float dashSpeedCoefficient = 20;
    public const float attackInterval = 0.2f;
    public const float attackRadius = 10;
    public const float decelerateDuration = 1;
    public const float baseForce = 1500;
    public const float dashSafeExitDuration = 5;


    public enum PersuitState
    {
        None,
        Warmup,
        Dash,
        Decelerate,
        Land
    }

    private PersuitState state;
    private CharacterBody targetBody;
    private Vector3 targetPos;
    private float attackTimeStamp;
    private float startDecelerateTimeStamp;
    private Vector3 targetMoveDirt;
    private bool startedLand;
    private float dashSafeOutTime;
    private GameObject whirlwindVortexInstance;

    public override void OnEnter()
    {
        Animator animator = GetModelAnimator();
        ChildLocator childLocator = animator.GetComponent<ChildLocator>();
        PlayCrossfade("FullBody Override", "WhirlwindRushEnter", "WhirlwindRush.playbackRate", 0.5f, 0.1f);
        Util.PlaySound("Play_halcyonite_skill3_start", base.gameObject);
        SmallHop(base.characterMotor, 9);
        CharacterGravityParameters gravityParameters = base.characterMotor.gravityParameters;
        gravityParameters.channeledAntiGravityGranterCount++;
        base.characterMotor.gravityParameters = gravityParameters;
        CharacterFlightParameters flightParameters = base.characterMotor.flightParameters;
        flightParameters.channeledFlightGranterCount++;
        base.characterMotor.flightParameters = flightParameters;
        base.characterMotor.walkSpeedPenaltyCoefficient = 0f;
        state = PersuitState.Warmup;
    }

    public override void FixedUpdate()
    {
        base.FixedUpdate();
        if (NetworkServer.active)
        {
            switch (state)
            {
                case PersuitState.None:
                    return;
                case PersuitState.Dash:
                    UpdateDash();
                    UpdateAttack();
                    break;
                case PersuitState.Decelerate:
                    UpdateDecelerate();
                    UpdateAttack();
                    break;
                case PersuitState.Land:
                    UpdateLand();
                    UpdateAttack();
                    break;
                case PersuitState.Warmup:
                    update_warmup();
                    break;
            }
        }
    }

    public void update_warmup() {
        if (base.fixedAge > 0.5f)
        {
            Util.PlaySound("Play_halcyonite_skill3_loop", base.gameObject);
            characterMotor.walkSpeedPenaltyCoefficient = 1f;
            ChildLocator modelChildLocator = GetModelChildLocator();
            if (whirlWindVortexVFXPrefab != null)
            {
                Transform parent = modelChildLocator.FindChild("WhirlWindPoint");
                whirlwindVortexInstance = Object.Instantiate(whirlWindVortexVFXPrefab, parent);
            }

            FindTarget();
            targetMoveDirt = (targetPos - base.transform.position).normalized;
            base.characterDirection.moveVector = targetMoveDirt;
            attackTimeStamp = base.fixedAge;
        }
    }

    private void FindTarget()
    {
        Ray aimRay = GetAimRay();
        BullseyeSearch enemyFinder = new BullseyeSearch();
        enemyFinder.viewer = characterBody;
        enemyFinder.maxDistanceFilter = maxSearchDist;
        enemyFinder.searchOrigin = aimRay.origin;
        enemyFinder.searchDirection = aimRay.direction;
        enemyFinder.filterByLoS = false;
        enemyFinder.sortMode = BullseyeSearch.SortMode.Angle;
        enemyFinder.teamMaskFilter = TeamMask.allButNeutral;
        if ((bool)teamComponent)
        {
            enemyFinder.teamMaskFilter.RemoveTeam(teamComponent.teamIndex);
        }
        enemyFinder.RefreshCandidates();
        HurtBox hurtBox = enemyFinder.GetResults().FirstOrDefault();
        if (hurtBox != null)
        {
            targetBody = hurtBox.healthComponent.body;
            targetPos = targetBody.footPosition + (base.transform.position - targetBody.footPosition).normalized * 2f;
        }
        else
        {
            targetPos = aimRay.origin + aimRay.direction;
        }
        state = PersuitState.Dash;
    }

    private void UpdateAttack()
    {
        if (base.fixedAge > attackTimeStamp && NetworkServer.active)
        {
            attackTimeStamp = base.fixedAge + attackInterval;
            BlastAttack blastAttack = new BlastAttack();
            blastAttack.attacker = base.characterBody.gameObject;
            blastAttack.inflictor = base.characterBody.gameObject;
            blastAttack.teamIndex = base.characterBody.teamComponent.teamIndex;
            blastAttack.position = base.transform.position;
            blastAttack.procCoefficient = 0f;
            blastAttack.radius = attackRadius;
            blastAttack.baseForce = baseForce;
            blastAttack.baseDamage = base.characterBody.damage * attackInterval;
            blastAttack.bonusForce = Vector3.zero;
            blastAttack.crit = false;
            blastAttack.damageType = DamageType.Generic;
            blastAttack.falloffModel = BlastAttack.FalloffModel.None;
            blastAttack.attackerFiltering = AttackerFiltering.Default;
            blastAttack.Fire();
        }
    }

    private void UpdateLand()
    {
        if (!startedLand)
        {
            CharacterGravityParameters gravityParameters = base.characterMotor.gravityParameters;
            gravityParameters.channeledAntiGravityGranterCount--;
            base.characterMotor.gravityParameters = gravityParameters;
            CharacterFlightParameters flightParameters = base.characterMotor.flightParameters;
            flightParameters.channeledFlightGranterCount--;
            base.characterMotor.flightParameters = flightParameters;
            startedLand = true;
        }
        if (base.characterMotor.isGrounded)
        {
            outer.SetNextStateToMain();
        }
    }

    private void UpdateDecelerate()
    {
        if (base.fixedAge > startDecelerateTimeStamp + decelerateDuration)
        {
            state = PersuitState.Land;
        }
        characterMotor.velocity = targetMoveDirt.normalized * dashSpeedCoefficient * Mathf.Lerp(1, 0, (base.fixedAge - startDecelerateTimeStamp) / decelerateDuration);
    }

    private void UpdateDash()
    {
        dashSafeOutTime += Time.deltaTime;
        if (dashSafeOutTime > dashSafeExitDuration)
        {
            state = PersuitState.Decelerate;
            startDecelerateTimeStamp = base.fixedAge;
        }
        else
        {
            base.characterMotor.velocity = targetMoveDirt.normalized * dashSpeedCoefficient;
            CheckIfArrived();
        }
    }

    private void CheckIfArrived()
    {
        Ray ray = GetAimRay();
        if (Vector3.Dot(ray.direction, targetMoveDirt) < 0f)
        {
            state = PersuitState.Decelerate;
            startDecelerateTimeStamp = base.fixedAge;
        }
    }

    public override void OnExit()
    {
        DisableAntiGravity();
        base.characterMotor.walkSpeedPenaltyCoefficient = 1f;
        base.characterMotor.velocity = Vector3.zero;
        PlayCrossfade("FullBody Override", "WhirlwindRushExit", "WhirlwindRush.playbackRate", 1f, 0.1f);
        Util.PlaySound("Play_halcyonite_skill3_end", base.gameObject);
        Util.PlaySound("Stop_halcyonite_skill3_loop", base.gameObject);
        base.characterBody.RecalculateStats();
        if (whirlwindVortexInstance != null)
        {
            EntityState.Destroy(whirlwindVortexInstance);
        }
    }

    private void DisableAntiGravity()
    {
        CharacterGravityParameters gravityParameters = base.characterMotor.gravityParameters;
        gravityParameters.channeledAntiGravityGranterCount = 0;
        base.characterMotor.gravityParameters = gravityParameters;
        CharacterFlightParameters flightParameters = base.characterMotor.flightParameters;
        flightParameters.channeledFlightGranterCount = 0;
        base.characterMotor.flightParameters = flightParameters;
    }

    public override InterruptPriority GetMinimumInterruptPriority()
    {
        return InterruptPriority.PrioritySkill;
    }
}
