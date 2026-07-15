using System;
using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteBodyGuard
    {
        private static float _nextAbortRecordAt;

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
