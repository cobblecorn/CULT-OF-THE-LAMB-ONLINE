using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(EnterBuilding), "OnTriggerEnter2D")]
    internal static class Phase6EnterBuildingPatch
    {
        private static bool Prefix(Collider2D collision)
        {
            PlayerFarming player = Phase6TransitionPatchHelpers.GetPlayer(collision);
            return !BridgeRemoteP2Driver.ShouldBlockRemoteP2Transition(player, "EnterBuilding")
                && !BridgeRemoteHostMirror.ShouldBlockRemoteHostMirrorTransition(player, "EnterBuilding");
        }
    }

    [HarmonyPatch(typeof(Door), "OnTriggerEnter2D")]
    internal static class Phase6DoorPatch
    {
        private static bool Prefix(Collider2D collision)
        {
            PlayerFarming player = Phase6TransitionPatchHelpers.GetPlayer(collision);
            return !BridgeRemoteP2Driver.ShouldBlockRemoteP2Transition(player, "Door")
                && !BridgeRemoteHostMirror.ShouldBlockRemoteHostMirrorTransition(player, "Door");
        }
    }

    [HarmonyPatch(typeof(Interaction_BaseDungeonDoor), "OnTriggerEnter2D")]
    internal static class Phase6DungeonDoorPatch
    {
        private static bool Prefix(Collider2D collision)
        {
            PlayerFarming player = Phase6TransitionPatchHelpers.GetPlayer(collision);
            return !BridgeRemoteP2Driver.ShouldBlockRemoteP2Transition(player, "Interaction_BaseDungeonDoor")
                && !BridgeRemoteHostMirror.ShouldBlockRemoteHostMirrorTransition(player, "Interaction_BaseDungeonDoor");
        }
    }

    internal static class Phase6TransitionPatchHelpers
    {
        public static PlayerFarming GetPlayer(Collider2D collision)
        {
            if (collision == null || collision.gameObject == null)
            {
                return null;
            }

            PlayerFarming player = collision.gameObject.GetComponent<PlayerFarming>();
            if (player != null)
            {
                return player;
            }

            return collision.gameObject.GetComponentInParent<PlayerFarming>();
        }
    }
}
