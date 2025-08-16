using BepInEx;
using EntityStates;
using EntityStates.Halcyonite;
using UnityEngine.AddressableAssets;

namespace HalcyonFixes;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]

public class HalcyonFixes : BaseUnityPlugin
{
    public const string PluginGUID = PluginAuthor + "." + PluginName;
    public const string PluginAuthor = "Onyx";
    public const string PluginName = "HalcyonFixes";
    public const string PluginVersion = "1.1.0";

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
                self.outer.SetNextState(new Whirlwind());
            }
        };

        On.EntityStates.Halcyonite.SpawnState.OnEnter += (orig, self) =>
        {
            SpawnState.duration = 2;
            orig(self);
            SpawnState.duration = 2.3f;
        };
    }
}
