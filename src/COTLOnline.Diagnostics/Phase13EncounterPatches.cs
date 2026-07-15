using System.Reflection;
using HarmonyLib;
using MMRoomGeneration;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch]
    internal static class GenerateRoomGetRandomEncounterIslandTracePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GenerateRoom), "GetRandomEncounterIsland");
        }

        private static void Postfix(object __instance, object __result)
        {
            BridgeCombatAuthority.RecordEncounterIsland("postfix", __instance, __result);
        }
    }

    [HarmonyPatch]
    internal static class EnemyEncounterChanceEventsTracePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(EnemyEncounterChanceEvents), "AssignShieldsAndGroups", new System.Type[0]);
        }

        private static void Prefix(object __instance)
        {
            BridgeCombatAuthority.RecordEncounterChance("prefix", __instance);
        }

        private static void Postfix(object __instance)
        {
            BridgeCombatAuthority.RecordEncounterChance("postfix", __instance);
        }
    }
}
