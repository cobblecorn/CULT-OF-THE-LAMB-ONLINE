using HarmonyLib;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemovePlayerFromMenu), new System.Type[0])]
    internal static class Phase7RemovePlayerFromMenuBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !BridgeRemoteHostMirror.ShouldBlockCoopRemoval(null, "RemovePlayerFromMenu");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemoveCoopPlayer), new[] { typeof(PlayerFarming), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class Phase7RemoveCoopPlayerBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerFarming playerToRemove)
        {
            return !BridgeRemoteHostMirror.ShouldBlockCoopRemoval(playerToRemove, "RemoveCoopPlayer");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemoveCoopPlayerStatic), new[] { typeof(PlayerFarming), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class Phase7RemoveCoopPlayerStaticBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerFarming playerToRemove)
        {
            return !BridgeRemoteHostMirror.ShouldBlockCoopRemoval(playerToRemove, "RemoveCoopPlayerStatic");
        }
    }
}
