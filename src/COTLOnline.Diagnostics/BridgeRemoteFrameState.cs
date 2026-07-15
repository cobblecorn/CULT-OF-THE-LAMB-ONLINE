using System;
using System.Globalization;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteFrameState
    {
        private static float _nextRecordAt;

        public static void Apply(PlayerFarming player, BridgeRemoteInput input, string source)
        {
            if (player == null || input == null)
            {
                return;
            }

            bool facingChanged = ApplyFacing(player, input);
            bool ammoChanged = ApplyFaithAmmo(player, input);
            if ((facingChanged || ammoChanged) && Time.unscaledTime >= _nextRecordAt)
            {
                _nextRecordAt = Time.unscaledTime + 1f;
                WorldTrace.Record(
                    "phase12.remote_frame.applied",
                    "source=" + Clean(source)
                    + " target=" + Clean(WorldTrace.DescribePlayer(player))
                    + " facing=" + Clean(input.FacingAngle)
                    + " look=" + Clean(input.LookAngle)
                    + " aim=" + Clean(input.AimAngle)
                    + " faith=" + Clean(input.FaithAmmo)
                    + "/" + Clean(input.FaithTotal)
                    + " changedFacing=" + facingChanged
                    + " changedAmmo=" + ammoChanged);
            }
        }

        private static bool ApplyFacing(PlayerFarming player, BridgeRemoteInput input)
        {
            float facing;
            float look;
            float aim;
            bool hasFacing = TryParseFloat(input.FacingAngle, out facing);
            bool hasLook = TryParseFloat(input.LookAngle, out look);
            bool hasAim = TryParseFloat(input.AimAngle, out aim);
            if (!hasFacing && !hasLook && !hasAim)
            {
                return false;
            }

            bool changed = false;
            StateMachine state = player.state;
            if (state != null)
            {
                float targetFacing = RepeatAngle(hasFacing ? facing : hasAim ? aim : look);
                float targetLook = RepeatAngle(hasLook ? look : targetFacing);
                if (!SameAngle(state.facingAngle, targetFacing))
                {
                    state.facingAngle = targetFacing;
                    changed = true;
                }

                if (!SameAngle(state.LookAngle, targetLook))
                {
                    state.LookAngle = targetLook;
                    changed = true;
                }

                if (player.playerController != null && !SameAngle(player.playerController.forceDir, targetFacing))
                {
                    player.playerController.forceDir = targetFacing;
                    changed = true;
                }
            }

            if (player.playerSpells != null && hasAim)
            {
                float targetAim = RepeatAngle(aim);
                if (!SameAngle(player.playerSpells.AimAngle, targetAim))
                {
                    player.playerSpells.AimAngle = targetAim;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ApplyFaithAmmo(PlayerFarming player, BridgeRemoteInput input)
        {
            float remoteAmmo;
            if (!TryParseFloat(input.FaithAmmo, out remoteAmmo))
            {
                return false;
            }

            PlayerSpells spells = player.playerSpells;
            if (spells == null)
            {
                return false;
            }

            if (spells.faithAmmo == null)
            {
                try
                {
                    spells.Init();
                }
                catch (Exception ex)
                {
                    WorldTrace.Record("phase12.remote_frame.faith_init_error", ex.GetType().Name + ": " + Clean(ex.Message));
                }
            }

            FaithAmmo faithAmmo = spells.faithAmmo;
            if (faithAmmo == null)
            {
                return false;
            }

            float next = Mathf.Clamp(remoteAmmo, 0f, faithAmmo.Total);
            if (Mathf.Abs(faithAmmo.Ammo - next) < 0.5f)
            {
                return false;
            }

            faithAmmo.Ammo = next;
            return true;
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                && !float.IsNaN(parsed)
                && !float.IsInfinity(parsed);
        }

        private static bool SameAngle(float current, float next)
        {
            return Mathf.Abs(Mathf.DeltaAngle(current, next)) < 0.5f;
        }

        private static float RepeatAngle(float value)
        {
            return Mathf.Repeat(value, 360f);
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
