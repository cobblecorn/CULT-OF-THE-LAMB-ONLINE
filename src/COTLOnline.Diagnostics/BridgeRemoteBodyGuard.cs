using System;
using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteBodyGuard
    {
        private static float _nextAbortRecordAt;
        private static float _nextBlockRecordAt;

        public static bool IsBridgeOwned(PlayerFarming player)
        {
            return player != null
                && (BridgeRemoteP2Driver.ShouldBlockLocalInput(player)
                    || BridgeRemoteHostMirror.ShouldBlockLocalInput(player));
        }

        public static bool CancelGoToIfBridgeOwned(PlayerFarming player, string source)
        {
            if (!IsBridgeOwned(player))
            {
                return false;
            }

            return CancelGoToForBridgeBody(player, source);
        }

        public static bool ShouldBlockMainPlayerSwitch(StateMachine state, string source)
        {
            return ShouldBlockMainPlayerSwitch(GetPlayer(state), source);
        }

        public static bool ShouldBlockMainPlayerSwitch(PlayerFarming player, string source)
        {
            if (!IsBridgeOwned(player))
            {
                return false;
            }

            CancelGoToForBridgeBody(player, source);
            RecordBlocked(player, source);
            return true;
        }

        public static bool ShouldBlockMainPlayerSwitch(Collider2D collider, string source)
        {
            return ShouldBlockMainPlayerSwitch(GetPlayer(collider), source);
        }

        public static bool ShouldBlockInteraction(StateMachine state, string source)
        {
            PlayerFarming player = GetPlayer(state);
            if (!IsBridgeOwned(player))
            {
                return false;
            }

            CancelGoToForBridgeBody(player, source);
            RecordBlocked(player, source);
            return true;
        }

        public static bool ShouldBlockHidePlayer(PlayerFarming player, string source)
        {
            if (!IsBridgeOwned(player))
            {
                return false;
            }

            RecordBlocked(player, source);
            return true;
        }

        public static bool ShouldBlockCameraTarget(GameObject target, string source)
        {
            PlayerFarming player = GetPlayer(target);
            if (!IsBridgeOwned(player))
            {
                return false;
            }

            RecordBlocked(player, source);
            return true;
        }

        public static void StripBridgeOwnedCameraTargets(CameraFollowTarget camera, string source)
        {
            if (camera == null || camera.targets == null || camera.targets.Count == 0)
            {
                return;
            }

            PlayerFarming removedPlayer = null;
            int removed = 0;
            for (int i = camera.targets.Count - 1; i >= 0; i--)
            {
                GameObject target = camera.targets[i] != null ? camera.targets[i].gameObject : null;
                PlayerFarming player = GetPlayer(target);
                if (!IsBridgeOwned(player))
                {
                    continue;
                }

                camera.targets.RemoveAt(i);
                removed++;
                removedPlayer = removedPlayer ?? player;
            }

            if (removed > 0)
            {
                RecordBlocked(removedPlayer, source + "_removed_" + removed);
            }
        }

        public static bool CancelGoToForBridgeBody(PlayerFarming player, string source)
        {
            if (player == null)
            {
                return false;
            }

            bool goToAndStopping;
            try
            {
                goToAndStopping = player.GoToAndStopping;
            }
            catch
            {
                return false;
            }

            if (!goToAndStopping)
            {
                return false;
            }

            try
            {
                player.AbortGoTo(false);
                if (player.state != null)
                {
                    player.state.CURRENT_STATE = StateMachine.State.Idle;
                }

                RecordAbort(player, source);
                return true;
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase14.remote_body_guard.abort_error",
                    "source=" + Clean(source)
                    + " " + ex.GetType().Name + ": " + Clean(ex.Message)
                    + " player=" + Clean(WorldTrace.DescribePlayer(player)));
                return false;
            }
        }

        private static PlayerFarming GetPlayer(StateMachine state)
        {
            try
            {
                return state != null ? state.GetComponent<PlayerFarming>() : null;
            }
            catch
            {
                return null;
            }
        }

        private static PlayerFarming GetPlayer(Collider2D collider)
        {
            try
            {
                if (collider == null)
                {
                    return null;
                }

                PlayerFarming player = collider.GetComponent<PlayerFarming>();
                return player != null ? player : collider.GetComponentInParent<PlayerFarming>();
            }
            catch
            {
                return null;
            }
        }

        private static PlayerFarming GetPlayer(GameObject target)
        {
            try
            {
                return target != null ? target.GetComponentInParent<PlayerFarming>() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void RecordBlocked(PlayerFarming player, string source)
        {
            float now = Time.unscaledTime;
            if (now < _nextBlockRecordAt)
            {
                return;
            }

            _nextBlockRecordAt = now + 0.75f;
            WorldTrace.Record(
                "phase14.remote_body_guard.blocked",
                "source=" + Clean(source)
                + " player=" + Clean(WorldTrace.DescribePlayer(player)));
        }

        private static void RecordAbort(PlayerFarming player, string source)
        {
            float now = Time.unscaledTime;
            if (now < _nextAbortRecordAt)
            {
                return;
            }

            _nextAbortRecordAt = now + 0.75f;
            WorldTrace.Record(
                "phase14.remote_body_guard.goto_aborted",
                "source=" + Clean(source)
                + " player=" + Clean(WorldTrace.DescribePlayer(player)));
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            return value.Replace(' ', '_').Replace('\r', '_').Replace('\n', '_');
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.SetMainPlayer), new Type[] { typeof(StateMachine) })]
    internal static class Phase14SetMainPlayerStateGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(StateMachine __0)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockMainPlayerSwitch(__0, "PlayerFarming.SetMainPlayer.StateMachine");
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.SetMainPlayer), new Type[] { typeof(PlayerFarming) })]
    internal static class Phase14SetMainPlayerPlayerGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerFarming __0)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockMainPlayerSwitch(__0, "PlayerFarming.SetMainPlayer.PlayerFarming");
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.SetMainPlayer), new Type[] { typeof(Collider2D) })]
    internal static class Phase14SetMainPlayerColliderGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Collider2D __0)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockMainPlayerSwitch(__0, "PlayerFarming.SetMainPlayer.Collider2D");
        }
    }

    [HarmonyPatch(typeof(Interaction), nameof(Interaction.OnInteract), new Type[] { typeof(StateMachine) })]
    internal static class Phase14BridgeOwnedInteractionGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(StateMachine __0)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockInteraction(__0, "Interaction.OnInteract");
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.HidePlayer), new Type[] { typeof(PlayerFarming), typeof(bool) })]
    internal static class Phase14BridgeOwnedHidePlayerGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerFarming playerFarming)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockHidePlayer(playerFarming, "PlayerFarming.HidePlayer");
        }
    }

    [HarmonyPatch(typeof(CameraFollowTarget), nameof(CameraFollowTarget.AddTarget), new Type[] { typeof(GameObject), typeof(float) })]
    internal static class Phase14BridgeOwnedCameraAddTargetGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameObject __0)
        {
            return !BridgeRemoteBodyGuard.ShouldBlockCameraTarget(__0, "CameraFollowTarget.AddTarget");
        }
    }

    [HarmonyPatch(typeof(CameraFollowTarget), "LateUpdate")]
    internal static class Phase14BridgeOwnedCameraLateUpdateGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CameraFollowTarget __instance)
        {
            BridgeRemoteBodyGuard.StripBridgeOwnedCameraTargets(__instance, "CameraFollowTarget.LateUpdate");
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), "Update")]
    internal static class Phase14RemoteBodyPlayerUpdateGuardPatch
    {
        private static void Prefix(PlayerFarming __instance)
        {
            BridgeRemoteBodyGuard.CancelGoToIfBridgeOwned(__instance, "PlayerFarming.Update.prefix");
        }

        private static void Postfix(PlayerFarming __instance)
        {
            BridgeRemoteBodyGuard.CancelGoToIfBridgeOwned(__instance, "PlayerFarming.Update.postfix");
        }
    }
}
