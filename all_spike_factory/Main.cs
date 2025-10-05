using MelonLoader;
using HarmonyLib; // <- IMPORTANT: HarmonyLib, not Harmony

using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Utils;
using System;
using System.Text.RegularExpressions;
using Il2CppAssets.Scripts.Unity.Scenes;
using UnityEngine;
using System.Linq;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Attack;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Attack.Behaviors;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Models.Towers.Behaviors;
using Il2CppAssets.Scripts.Models.Bloons.Behaviors;
using Il2CppAssets.Scripts.Models.Towers.Projectiles.Behaviors;
using System.Collections.Generic;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Towers.Projectiles;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Emissions;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities;
using Il2CppAssets.Scripts.Simulation.Track;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities.Behaviors;
using Il2CppAssets.Scripts.Models.GenericBehaviors;

[assembly: MelonInfo(typeof(all_spike_factory.Main), all_spike_factory.ModHelperData.Name, all_spike_factory.ModHelperData.Version, all_spike_factory.ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace all_spike_factory
{
    // Using BloonsTD6Mod (Mod Helper) is optional but recommended
    public class Main : BloonsTD6Mod
    {
        static TowerModel baseSpac;

        public override void OnApplicationStart()
        {
            base.OnApplicationStart();
            ModHelper.Msg<Main>("all_spike_factory loaded");
        }

        [HarmonyPatch(typeof(TitleScreen), nameof(TitleScreen.Start))]
        internal static class TitleScreen_Start_Patch
        {
            [HarmonyPostfix]
            internal static void Postfix()
            {
                var model = Game.instance?.model;
                if (model == null) return;

                baseSpac = model.GetTowerFromId("SpikeFactory");
                var towers = model.towers;

                for (int i = 0; i < towers.Count; i++)
                {
                    var tower = towers[i];

                    // skip air units & some high-tier edge cases, as in your original
                    var name = tower.name.ToLowerInvariant();
                    if (name.Contains("helipilot") || name.Contains("monkeyace")) continue;
                    if (Regex.IsMatch(tower.name, "DartlingGunner-4..") ||
                        Regex.IsMatch(tower.name, "DartlingGunner-5..") ||
                        Regex.IsMatch(tower.name, "BoomerangMonkey-5..")) continue;

                    try
                    {
                        if (!tower.HasBehavior<AttackModel>()) continue;

                        var baseAttack = baseSpac.GetBehavior<AttackModel>().Duplicate();
                        var baseWeapon0 = baseSpac.GetBehavior<AttackModel>().weapons[0].Duplicate();

                        bool hasProjectiles = false;
                        foreach (var proj in tower.GetBehavior<AttackModel>().GetAllProjectiles())
                        {
                            if (proj.HasBehavior<TravelStraitModel>() || name.Contains("boomer"))
                            {
                                hasProjectiles = true;
                                break;
                            }
                        }
                        if (!hasProjectiles) continue;

                        var oldAttack = tower.GetBehavior<AttackModel>().Duplicate();
                        baseAttack.range = oldAttack.range;

                        int j = 0;
                        bool modified = false;

                        foreach (var wep in tower.GetBehavior<AttackModel>().weapons)
                        {
                            if (!(wep.projectile.HasBehavior<TravelStraitModel>() || name.Contains("boomer")))
                                continue;

                            if (modified)
                                baseAttack.AddWeapon(baseWeapon0.Duplicate());
                            modified = true;

                            baseAttack.weapons[j].Rate = wep.Rate;

                            // Try to infer “shot count” to preserve effective pierce
                            int pierceMultiplier = 1;
                            try { pierceMultiplier = wep.emission.Cast<RandomArcEmissionModel>().Count; } catch { }
                            try { pierceMultiplier = wep.emission.Cast<ArcEmissionModel>().Count; } catch { }
                            try { pierceMultiplier = wep.emission.Cast<RandomEmissionModel>().count; } catch { }
                            try { pierceMultiplier = wep.emission.Cast<AdoraEmissionModel>().count; } catch { }
                            try { pierceMultiplier = wep.emission.Cast<AlternatingArcEmissionModel>().count; } catch { }

                            // Clean projectile travel/visual behaviors so we can re-use spike projectile
                            wep.projectile.RemoveBehavior<TravelStraitModel>();
                            wep.projectile.RemoveBehavior<FollowPathModel>();
                            wep.projectile.RemoveBehavior<DisplayModel>();
                            wep.projectile.RemoveBehavior<TrackTargetWithinTimeModel>();
                            wep.projectile.RemoveBehavior<TrackTargetModel>();

                            // carry over collision passes
                            baseAttack.weapons[j].projectile.collisionPasses = wep.projectile.collisionPasses;

                            // copy damage or remove if none
                            if (wep.projectile.HasBehavior<DamageModel>())
                            {
                                var dmg = baseAttack.weapons[j].projectile.GetBehavior<DamageModel>();
                                dmg.damage = wep.projectile.GetBehavior<DamageModel>().damage;
                                dmg.immuneBloonProperties = wep.projectile.GetBehavior<DamageModel>().immuneBloonProperties;
                            }
                            else
                            {
                                baseAttack.weapons[j].projectile.RemoveBehavior<DamageModel>();
                            }

                            // copy remaining behaviors
                            foreach (var bev in wep.projectile.behaviors)
                                baseAttack.weapons[j].projectile.AddBehavior(bev.Duplicate());

                            // scale pierce
                            baseAttack.weapons[j].projectile.pierce = wep.projectile.pierce * pierceMultiplier;
                            baseAttack.weapons[j].projectile.maxPierce = wep.projectile.maxPierce * pierceMultiplier;

                            j++;
                            // Keep single weapon mapped to avoid weird ultra-fast fire (as in your original)
                            break;
                        }

                        tower.RemoveBehavior<AttackModel>();
                        tower.AddBehavior(baseAttack);
                        tower.TargetTypes = baseSpac.TargetTypes.Duplicate();
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning($"{tower.name} failed: {e.Message}");
                    }
                }
            }
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            bool inAGame = InGame.instance != null && InGame.instance.bridge != null;
            if (inAGame)
            {
                // no-op
            }
        }
    }
}
