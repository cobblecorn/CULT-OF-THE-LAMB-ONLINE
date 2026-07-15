using System;
using HarmonyLib;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemovePlayerFromMenu), new Type[0])]
    internal static class Phase5CoopRemoveFromMenuPatch
    {
        private static bool Prefix(ref bool __state)
        {
            __state = BridgeRemoteHostMirror.ShouldBlockCoopRemoval(null, "RemovePlayerFromMenu");
            if (__state)
            {
                return false;
            }

            BridgeCoopReservation.RecordRemoveMenu("prefix");
            return true;
        }

        private static void Postfix(bool __state)
        {
            if (__state)
            {
                return;
            }

            BridgeCoopReservation.RecordRemoveMenu("postfix");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RefreshCoopPlayerRewired), new Type[0])]
    internal static class Phase5RefreshCoopPlayerRewiredPatch
    {
        private static void Prefix()
        {
            BridgeCoopReservation.RecordRewiredRefresh("prefix");
        }

        private static void Postfix()
        {
            BridgeCoopReservation.RecordRewiredRefresh("postfix");
        }
    }
}
