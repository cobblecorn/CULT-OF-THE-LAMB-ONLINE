using HarmonyLib;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetHorizontalAxis), new[] { typeof(PlayerFarming) })]
    internal static class Phase6HorizontalAxisPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref float __result)
        {
            float horizontal;
            float vertical;
            if (BridgeRemoteP2Driver.TryGetAxis(playerFarming, out horizontal, out vertical))
            {
                __result = horizontal;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetAxis(playerFarming, out horizontal, out vertical))
            {
                __result = horizontal;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetVerticalAxis), new[] { typeof(PlayerFarming) })]
    internal static class Phase6VerticalAxisPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref float __result)
        {
            float horizontal;
            float vertical;
            if (BridgeRemoteP2Driver.TryGetAxis(playerFarming, out horizontal, out vertical))
            {
                __result = vertical;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetAxis(playerFarming, out horizontal, out vertical))
            {
                __result = vertical;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetDodgeButtonDown), new[] { typeof(PlayerFarming) })]
    internal static class Phase6DodgeButtonDownPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetDodgeButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetDodgeButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetDodgeButtonHeld), new[] { typeof(PlayerFarming) })]
    internal static class Phase6DodgeButtonHeldPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetDodgeButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetDodgeButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetAttackButtonDown), new[] { typeof(PlayerFarming) })]
    internal static class Phase6AttackButtonDownPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetAttackButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetAttackButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetAttackButtonHeld), new[] { typeof(PlayerFarming) })]
    internal static class Phase6AttackButtonHeldPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetAttackButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetAttackButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetCurseButtonDown), new[] { typeof(PlayerFarming) })]
    internal static class Phase6CurseButtonDownPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeSpellAuthority.ShouldSuppressRelayedCurseInput(playerFarming))
            {
                __result = false;
                return false;
            }

            if (BridgeRemoteP2Driver.TryGetCurseButton(playerFarming, held: false, up: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetCurseButton(playerFarming, held: false, up: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetCurseButtonHeld), new[] { typeof(PlayerFarming) })]
    internal static class Phase6CurseButtonHeldPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeSpellAuthority.ShouldSuppressRelayedCurseInput(playerFarming))
            {
                __result = false;
                return false;
            }

            if (BridgeRemoteP2Driver.TryGetCurseButton(playerFarming, held: true, up: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetCurseButton(playerFarming, held: true, up: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetCurseButtonUp), new[] { typeof(PlayerFarming) })]
    internal static class Phase6CurseButtonUpPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeSpellAuthority.ShouldSuppressRelayedCurseInput(playerFarming))
            {
                __result = false;
                return false;
            }

            if (BridgeRemoteP2Driver.TryGetCurseButton(playerFarming, held: false, up: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetCurseButton(playerFarming, held: false, up: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetHeavyAttackButtonDown), new[] { typeof(PlayerFarming) })]
    internal static class Phase6HeavyAttackButtonDownPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetHeavyAttackButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetHeavyAttackButton(playerFarming, held: false))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RewiredGameplayInputSource), nameof(RewiredGameplayInputSource.GetHeavyAttackButtonHeld), new[] { typeof(PlayerFarming) })]
    internal static class Phase6HeavyAttackButtonHeldPatch
    {
        private static bool Prefix(PlayerFarming playerFarming, ref bool __result)
        {
            if (BridgeRemoteP2Driver.TryGetHeavyAttackButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteHostMirror.TryGetHeavyAttackButton(playerFarming, held: true))
            {
                __result = true;
                return false;
            }

            if (BridgeRemoteP2Driver.ShouldBlockLocalInput(playerFarming) || BridgeRemoteHostMirror.ShouldBlockLocalInput(playerFarming))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
