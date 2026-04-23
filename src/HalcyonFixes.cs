using BepInEx;
using EntityStates;
using EntityStates.Halcyonite;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace HalcyonFixes;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]

public class HalcyonFixes : BaseUnityPlugin
{
    public const string PluginGUID = PluginAuthor + "." + PluginName;
    public const string PluginAuthor = "Onyx";
    public const string PluginName = "HalcyonFixes";
    public const string PluginVersion = "1.2.1";

    public void Awake()
    {
        Log.Init(Logger);

        RoR2.Skills.SkillDef LongrangeHalcSkill = Addressables.LoadAssetAsync<RoR2.Skills.SkillDef>("RoR2/DLC2/Halcyonite/HalcyoniteMonsterTriLaser.asset").WaitForCompletion();
        LongrangeHalcSkill.interruptPriority = InterruptPriority.Any;
		RoR2.Skills.SkillDef whirlwindRush = Addressables.LoadAssetAsync<RoR2.Skills.SkillDef>("RoR2/DLC2/Halcyonite/HalcyoniteMonsterWhirlwindRush.asset").WaitForCompletion();
		whirlwindRush.interruptPriority = InterruptPriority.Any;

		On.EntityStates.Halcyonite.SpawnState.OnEnter += SpawnState_OnEnter;
		On.EntityStates.Halcyonite.WhirlwindWarmUp.OnExit += WhirlwindWarmUp_OnExit;
		On.EntityStates.Halcyonite.WhirlWindPersuitCycle.OnEnter += WhirlwindWarmUp_OnEnter;
		On.EntityStates.Halcyonite.WhirlWindPersuitCycle.UpdateDecelerate += UpdateDecelerate;
		IL.EntityStates.Halcyonite.WhirlWindPersuitCycle.CheckIfArrived += CheckIfArrived;
		On.EntityStates.Halcyonite.WhirlWindPersuitCycle.GetMinimumInterruptPriority += GetMinimumInterruptPriority_PrioritySkill;
		On.EntityStates.EntityState.GetMinimumInterruptPriority += GetMinimumInterruptPriority_PrioritySkill;
		IL.EntityStates.Halcyonite.TriLaser.FireTriLaser += FireTriLaser;
	}

	void FireTriLaser(ILContext il)
	{
		ILCursor c = new ILCursor(il);
		int laserVectorLoc = 0;

		if (c.TryGotoNext(MoveType.After,
				x => x.MatchCallOrCallvirt(typeof(RaycastHit), "get_point"),
				x => x.MatchStloc(out laserVectorLoc)
			))
		{
			c.Emit(OpCodes.Ldarg_0);
			c.Emit(OpCodes.Ldloc, laserVectorLoc);
			c.EmitDelegate<Func<TriLaser, Vector3, Vector3>>(checkWallDistance);
			c.Emit(OpCodes.Stloc, laserVectorLoc);
		}
		else
		{
			Log.Error(il.Method.Name + " IL Hook failed!");
		}

		Vector3 checkWallDistance(TriLaser self, Vector3 laserVector)
		{
			Ray aimray = new(self.modifiedAimRay.origin + self.modifiedAimRay.direction.normalized * (-1), self.modifiedAimRay.direction);
			if (Physics.Raycast(aimray, out var hitInfo, 2, LayerIndex.world.mask))
			{
				return hitInfo.point;
			}
			return laserVector;
		}
	}

	private InterruptPriority GetMinimumInterruptPriority_PrioritySkill(On.EntityStates.EntityState.orig_GetMinimumInterruptPriority orig, EntityState self)
	{
		InterruptPriority result = orig(self);
		if(result < InterruptPriority.PrioritySkill)
		{
			if (self is WhirlwindWarmUp)
			{
				return InterruptPriority.PrioritySkill;
			}
			if (self is GoldenSlash)
			{
				return InterruptPriority.PrioritySkill;
			}
			if (self is GoldenSwipe)
			{
				return InterruptPriority.PrioritySkill;
			}
		}
		
		return result;
	}

	private InterruptPriority GetMinimumInterruptPriority_PrioritySkill(On.EntityStates.Halcyonite.WhirlWindPersuitCycle.orig_GetMinimumInterruptPriority orig, WhirlWindPersuitCycle self)
	{
		InterruptPriority result = orig(self);
		if (result < InterruptPriority.PrioritySkill)
		{
			return InterruptPriority.PrioritySkill;
		}
		return result;
	}

	private void SpawnState_OnEnter(On.EntityStates.Halcyonite.SpawnState.orig_OnEnter orig, SpawnState self)
	{
		orig(self);
		self.outer.nextStateModifier = fixInterruptinWhirlwind;

		void fixInterruptinWhirlwind(EntityStateMachine entityStateMachine, ref EntityState newNextState)
		{
			if (newNextState is not EntityStates.Halcyonite.WhirlWindPersuitCycle)
			{
				self.PlayCrossfade("FullBody Override", "Empty", "WhirlwindRush.playbackRate", 0.1f, 0.1f);
			}
		}
	}

	private void CheckIfArrived(ILContext il)
	{
		ILCursor c = new ILCursor(il);

		if (c.TryGotoNext(MoveType.After,
				x => x.MatchLdfld(typeof(WhirlWindPersuitCycle), nameof(WhirlWindPersuitCycle.targetPos))
			))
		{
			c.Emit(OpCodes.Pop);
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<WhirlWindPersuitCycle, Vector3>>(targetToCurrentPos);
		}
		else
		{
			Log.Error(il.Method.Name + " IL Hook failed!");
		}

		Vector3 targetToCurrentPos(WhirlWindPersuitCycle self)
		{
			return self.targetBody.footPosition;
		}
	}

	private void UpdateDecelerate(On.EntityStates.Halcyonite.WhirlWindPersuitCycle.orig_UpdateDecelerate orig, EntityStates.Halcyonite.WhirlWindPersuitCycle self)
	{
		orig(self);
		float speedCoeff = Mathf.Lerp(WhirlWindPersuitCycle.dashSpeedCoefficient, 0f, (self.fixedAge - self.startDecelerateTimeStamp) / WhirlWindPersuitCycle.decelerateDuration);
		self.characterMotor.velocity = self.targetMoveDirt.normalized * speedCoeff;
		self.characterDirection.moveVector = self.targetMoveDirt.normalized;
	}

	private void WhirlwindWarmUp_OnEnter(On.EntityStates.Halcyonite.WhirlWindPersuitCycle.orig_OnEnter orig, EntityStates.Halcyonite.WhirlWindPersuitCycle self)
	{
		orig(self);
		CharacterGravityParameters gravityParameters = self.characterMotor.gravityParameters;
		gravityParameters.channeledAntiGravityGranterCount++;
		self.characterMotor.gravityParameters = gravityParameters;
		CharacterFlightParameters flightParameters = self.characterMotor.flightParameters;
		flightParameters.channeledFlightGranterCount++;
		self.characterMotor.flightParameters = flightParameters;
	}

	private void WhirlwindWarmUp_OnExit(On.EntityStates.Halcyonite.WhirlwindWarmUp.orig_OnExit orig, EntityStates.Halcyonite.WhirlwindWarmUp self)
	{
		orig(self);
		CharacterGravityParameters gravityParameters = self.characterMotor.gravityParameters;
		gravityParameters.channeledAntiGravityGranterCount = 0;
		self.characterMotor.gravityParameters = gravityParameters;
		CharacterFlightParameters flightParameters = self.characterMotor.flightParameters;
		flightParameters.channeledFlightGranterCount = 0;
		self.characterMotor.flightParameters = flightParameters;
	}
}
