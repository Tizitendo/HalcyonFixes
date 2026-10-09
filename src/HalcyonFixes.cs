using BepInEx;
using EntityStates;
using EntityStates.Halcyonite;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using RoR2BepInExPack.GameAssetPathsBetter;
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
    public const string PluginVersion = "1.3.0";

    public void Awake()
    {
        Log.Init(Logger);

		AssetReferenceT<GameObject> prefab = new AssetReferenceT<GameObject>(RoR2_DLC2.ShrineHalcyonite_prefab);
		AssetAsyncReferenceManager<GameObject>.LoadAsset(prefab).Completed += (x) =>
		{
			GameObject brokenPing = x.Result.transform.Find("RangeIndicator/RangeFX/CrystalLines/Particle SystemCollider Kill Trigger").gameObject;
			brokenPing.layer = (int)LayerIndex.collideWithCharacterHullOnly;
			x.Result.transform.Find("Camp 1 - Flavor Props (Inner Radius)").GetComponent<CombatDirector>().enabled = false;
			x.Result.transform.Find("Camp 2 - Flavor Props (Outer Radius)").GetComponent<CombatDirector>().enabled = false;
		};

		AssetReferenceT<EntityStateConfiguration> entityState = new(RoR2_DLC2_Halcyonite.EntityStates_HalcyoniteMonster_GoldenSwipe_asset);
		AssetAsyncReferenceManager<EntityStateConfiguration>.LoadAsset(entityState).Completed += (x) =>
		{
			x.Result.targetType = (HG.SerializableSystemType)typeof(FixedSwipe);
		};
		entityState = new(RoR2_DLC2_Halcyonite.EntityStates_HalcyoniteMonster_GoldenSlash_asset);
		AssetAsyncReferenceManager<EntityStateConfiguration>.LoadAsset(entityState).Completed += (x) =>
		{
			x.Result.targetType = (HG.SerializableSystemType)typeof(FixedSlash);
		};

		AssetReferenceT<SkillDef> skillDef = new(RoR2_DLC2_Halcyonite.HalcyoniteMonsterGoldenSwipe_asset);
		AssetAsyncReferenceManager<SkillDef>.LoadAsset(skillDef).Completed += (x) =>
		{
			x.Result.activationState = new SerializableEntityStateType(typeof(FixedSwipe));
		};
		skillDef = new(RoR2_DLC2_Halcyonite.HalcyoniteMonsterGoldenSlash_asset);
		AssetAsyncReferenceManager<SkillDef>.LoadAsset(skillDef).Completed += (x) =>
		{
			x.Result.activationState = new SerializableEntityStateType(typeof(FixedSlash));
		};
		R2API.ContentAddition.AddEntityState<FixedSlash>(out _);
		R2API.ContentAddition.AddEntityState<FixedSwipe>(out _);

		On.EntityStates.Halcyonite.WhirlWindPersuitCycle.UpdateDecelerate += UpdateDecelerate;
		IL.EntityStates.Halcyonite.WhirlWindPersuitCycle.CheckIfArrived += CheckIfArrived;
		IL.EntityStates.Halcyonite.TriLaser.FireTriLaser += FireTriLaser;
		IL.EntityStates.Halcyonite.WhirlwindWarmUp.OnEnter += WhirlwindWarmUp_OnEnter;
	}

    private void WhirlwindWarmUp_OnEnter(ILContext il)
    {
        ILCursor c = new(il);
		if (c.TryGotoNext(
			x => x.MatchCallOrCallvirt(typeof(EntityStates.EntityState), nameof(EntityStates.EntityState.PlayCrossfade))
		) && c.TryGotoPrev(MoveType.After,
			x => x.MatchLdfld(typeof(EntityStates.Halcyonite.WhirlwindWarmUp), nameof(WhirlwindWarmUp.duration))
		))
		{
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<float, WhirlwindWarmUp, float>>(fuck);
			float fuck(float f, WhirlwindWarmUp self)
			{
				Log.Info("oooooooooooooooooooooooooooooooooooo");
				return f * self.attackSpeedStat * 2;
			}
		} else
		{
			Log.Error(il.Method.Name + " IL Hook failed!");
		}
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

	private void CheckIfArrived(ILContext il)
	{
		ILCursor c = new ILCursor(il);

		if (c.TryGotoNext(MoveType.After,
				x => x.MatchLdfld(typeof(WhirlWindPersuitCycle), nameof(WhirlWindPersuitCycle.targetPos))
			))
		{
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<Vector3, WhirlWindPersuitCycle, Vector3>>(targetToCurrentPos);
		}
		else
		{
			Log.Error(il.Method.Name + " IL Hook failed!");
		}

		Vector3 targetToCurrentPos(Vector3 target, WhirlWindPersuitCycle self)
		{
			if (self.targetBody && self.targetBody.footPosition != null)
			{
				return self.targetBody.footPosition;
			}
			return target;
		}
	}

	private void UpdateDecelerate(On.EntityStates.Halcyonite.WhirlWindPersuitCycle.orig_UpdateDecelerate orig, EntityStates.Halcyonite.WhirlWindPersuitCycle self)
	{
		orig(self);
		float speedCoeff = Mathf.Lerp(WhirlWindPersuitCycle.dashSpeedCoefficient, 0f, (self.fixedAge - self.startDecelerateTimeStamp) / WhirlWindPersuitCycle.decelerateDuration);
		self.characterMotor.velocity = self.targetMoveDirt.normalized * speedCoeff;
		self.characterDirection.moveVector = self.targetMoveDirt.normalized;
	}
}

public class FixedSwipe : GoldenSwipe
{
	public override bool allowExitFire => false;
	public override void OnEnter()
	{
		pushAwayForce = 0;
		forceVector = new Vector3(base.inputBank.aimDirection.x, 0.5f, base.inputBank.aimDirection.z) * 5000;
		base.OnEnter();
	}

	public override void FixedUpdate()
	{
		base.FixedUpdate();
	}
}

public class FixedSlash : GoldenSlash
{
	public override bool allowExitFire => false;
	public override void OnEnter()
	{
		pushAwayForce = 0;
		forceVector = new Vector3(base.inputBank.aimDirection.x, 0.5f, base.inputBank.aimDirection.z) * 5000;
		base.OnEnter();
	}

	public override void FixedUpdate()
	{
		base.FixedUpdate();
	}
}
