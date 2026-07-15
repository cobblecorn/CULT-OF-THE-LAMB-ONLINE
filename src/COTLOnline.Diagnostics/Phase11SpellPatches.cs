using System;
using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(PlayerSpells), nameof(PlayerSpells.CastSpell))]
    internal static class Phase11PlayerSpellsCastSpellPatch
    {
        private static void Prefix(PlayerSpells __instance, EquipmentType curseType, bool autoAim, bool consumeAmmo, bool wasSpell, bool smallScale, GameObject shooter, float damageMultiplier, bool isFromFamiliar)
        {
            if (__instance == null || !IsBridgeOwned(__instance.playerFarming))
            {
                return;
            }

            try
            {
                __instance.Init();
                WorldTrace.Record(
                    "phase11.spell.cast.prefix",
                    DescribeCast(__instance, curseType, autoAim, consumeAmmo, wasSpell, smallScale, shooter, damageMultiplier, isFromFamiliar)
                    + " init=True");
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase11.spell.cast.init_error",
                    ex.GetType().Name + ": " + Clean(ex.Message)
                    + " " + DescribeCast(__instance, curseType, autoAim, consumeAmmo, wasSpell, smallScale, shooter, damageMultiplier, isFromFamiliar));
            }
        }

        private static Exception Finalizer(PlayerSpells __instance, EquipmentType curseType, bool autoAim, bool consumeAmmo, bool wasSpell, bool smallScale, GameObject shooter, float damageMultiplier, bool isFromFamiliar, Exception __exception)
        {
            if (__exception == null && __instance != null)
            {
                BridgeSpellAuthority.RecordLocalCast(__instance, curseType, autoAim, consumeAmmo, wasSpell, smallScale, shooter, damageMultiplier, isFromFamiliar);
            }

            if (__exception == null || __instance == null || !IsBridgeOwned(__instance.playerFarming))
            {
                return __exception;
            }

            WorldTrace.Record(
                "phase11.spell.cast.exception",
                __exception.GetType().Name + ": " + Clean(__exception.Message)
                + " " + DescribeCast(__instance, curseType, autoAim, consumeAmmo, wasSpell, smallScale, shooter, damageMultiplier, isFromFamiliar));

            RecoverBridgeCaster(__instance);
            return null;
        }

        private static void RecoverBridgeCaster(PlayerSpells spells)
        {
            try
            {
                spells.HideChargeBars();
                PlayerFarming player = spells.playerFarming;
                if (player != null && player.state != null)
                {
                    StateMachine.State state = player.state.CURRENT_STATE;
                    if (state == StateMachine.State.Aiming || state == StateMachine.State.Casting || state == StateMachine.State.CustomAnimation)
                    {
                        player.state.CURRENT_STATE = StateMachine.State.Idle;
                    }
                }

                Time.timeScale = 1f;
                WorldTrace.Record("phase11.spell.cast.recovered", DescribePlayer(player));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase11.spell.cast.recover_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static bool IsBridgeOwned(PlayerFarming player)
        {
            return player != null
                && (BridgeRemoteP2Driver.ShouldBlockLocalInput(player) || BridgeRemoteHostMirror.ShouldBlockLocalInput(player));
        }

        private static string DescribeCast(PlayerSpells spells, EquipmentType curseType, bool autoAim, bool consumeAmmo, bool wasSpell, bool smallScale, GameObject shooter, float damageMultiplier, bool isFromFamiliar)
        {
            PlayerFarming player = spells != null ? spells.playerFarming : null;
            EquipmentType currentCurse = player != null ? player.currentCurse : EquipmentType.None;
            CurseData data = SafeCurseData(currentCurse);
            return "player=" + DescribePlayer(player)
                + " curseType=" + curseType
                + " currentCurse=" + currentCurse
                + " curseLevel=" + (player != null ? player.currentCurseLevel.ToString() : "unknown")
                + " primary=" + (data != null ? data.PrimaryEquipmentType.ToString() : "null")
                + " prefab=" + (data != null && data.Prefab != null ? data.Prefab.name : "null")
                + " secondary=" + (data != null && data.SecondaryPrefab != null ? data.SecondaryPrefab.name : "null")
                + " faithAmmo=" + (spells != null && spells.faithAmmo != null ? "True" : "False")
                + " shooter=" + Clean(shooter != null ? shooter.name : "null")
                + " autoAim=" + autoAim
                + " consumeAmmo=" + consumeAmmo
                + " wasSpell=" + wasSpell
                + " smallScale=" + smallScale
                + " damageMultiplier=" + WorldTrace.FormatFloat(damageMultiplier)
                + " familiar=" + isFromFamiliar;
        }

        private static CurseData SafeCurseData(EquipmentType curse)
        {
            try
            {
                return EquipmentManager.GetCurseData(curse);
            }
            catch
            {
                return null;
            }
        }

        private static string DescribePlayer(PlayerFarming player)
        {
            if (player == null)
            {
                return "null";
            }

            return Clean(WorldTrace.DescribePlayer(player))
                + " weapon=" + player.currentWeapon + "@" + player.currentWeaponLevel
                + " curse=" + player.currentCurse + "@" + player.currentCurseLevel;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_");
        }
    }
}
