using System;
using HarmonyLib;
using Lamb.UI.DeathScreen;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save), new Type[0])]
    internal static class SavePatch
    {
        private static void Prefix()
        {
            WorldTrace.Record("save.request", "Save()");
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save), new[] { typeof(string) })]
    internal static class SaveFilenamePatch
    {
        private static void Prefix(string filename)
        {
            WorldTrace.Record("save.request", "Save(filename=" + filename + ")");
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Load), new[] { typeof(int) })]
    internal static class LoadSlotPatch
    {
        private static void Prefix(int saveSlot)
        {
            WorldTrace.Record("save.load", "Load(saveSlot=" + saveSlot + ")");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.SpawnCoopPlayer))]
    internal static class CoopSpawnPatch
    {
        private static void Prefix(int slot, bool playEffects, float startingHealth)
        {
            WorldTrace.Record(
                "coop.spawn.request",
                "slot=" + slot + " playEffects=" + playEffects + " startingHealth=" + WorldTrace.FormatFloat(startingHealth));
        }
    }

    [HarmonyPatch(typeof(Health), nameof(Health.DealDamage))]
    internal static class HealthDealDamagePatch
    {
        private static bool Prefix(
            Health __instance,
            float Damage,
            GameObject Attacker,
            Vector3 AttackLocation,
            bool BreakBlocking,
            Health.AttackTypes AttackType,
            bool dealDamageImmediately,
            Health.AttackFlags AttackFlags,
            ref bool __result,
            out DamageEventState __state)
        {
            __state = SyncEventRecorder.CaptureDamage(
                __instance,
                Damage,
                Attacker,
                AttackLocation,
                AttackType,
                AttackFlags,
                dealDamageImmediately);

            if (BridgeSpellAuthority.ShouldSuppressRelayedSpellDamage(Attacker))
            {
                __result = false;
                WorldTrace.Record(
                    "phase11.spell.relay.damage_suppressed",
                    "victim=" + WorldTrace.DescribeHealth(__instance)
                    + " attacker=" + WorldTrace.DescribeGameObject(Attacker)
                    + " damage=" + WorldTrace.FormatFloat(Damage)
                    + " type=" + AttackType
                    + " flags=" + AttackFlags);
                return false;
            }

            if (DiagnosticsPlugin.TraceRawHookEvents)
            {
                WorldTrace.Record(
                    "health.damage.before",
                    "victim=" + WorldTrace.DescribeHealth(__instance)
                    + " attacker=" + WorldTrace.DescribeGameObject(Attacker)
                    + " damage=" + WorldTrace.FormatFloat(Damage)
                    + " loc=" + WorldTrace.FormatVector(AttackLocation)
                    + " breakBlocking=" + BreakBlocking
                    + " type=" + AttackType
                    + " immediate=" + dealDamageImmediately
                    + " flags=" + AttackFlags);
            }

            return true;
        }

        private static void Postfix(Health __instance, bool __result, DamageEventState __state)
        {
            if (DiagnosticsPlugin.TraceRawHookEvents)
            {
                WorldTrace.Record(
                    "health.damage.after",
                    "applied=" + __result + " victim=" + WorldTrace.DescribeHealth(__instance));
            }

            SyncEventRecorder.RecordDamage(__state, __instance, __result);
        }
    }

    [HarmonyPatch(typeof(UnitObject), nameof(UnitObject.OnDie))]
    internal static class UnitObjectDiePatch
    {
        private static void Prefix(UnitObject __instance, GameObject Attacker, Vector3 AttackLocation, Health Victim, Health.AttackTypes AttackType, Health.AttackFlags AttackFlags)
        {
            if (DiagnosticsPlugin.TraceRawHookEvents)
            {
                WorldTrace.Record(
                    "unit.die",
                    "unit=" + WorldTrace.DescribeGameObject(__instance != null ? __instance.gameObject : null)
                    + " victim=" + WorldTrace.DescribeHealth(Victim)
                    + " attacker=" + WorldTrace.DescribeGameObject(Attacker)
                    + " loc=" + WorldTrace.FormatVector(AttackLocation)
                    + " type=" + AttackType
                    + " flags=" + AttackFlags);
            }

            SyncEventRecorder.RecordDeath(__instance, Attacker, AttackLocation, Victim, AttackType, AttackFlags);
        }
    }

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.Create))]
    internal static class EnemySpawnerCreatePatch
    {
        private static void Prefix(Vector3 Position, Transform Parent, GameObject Spawn)
        {
            BridgeCombatAuthority.RecordEnemySpawn("EnemySpawner.Create.prefix", Position, Parent, Spawn, null);
            WorldTrace.Record(
                "enemy.spawn.create",
                "prefab=" + WorldTrace.DescribeGameObject(Spawn)
                + " parent=" + WorldTrace.DescribeObject(Parent)
                + " pos=" + WorldTrace.FormatVector(Position));
        }

        private static void Postfix(GameObject __result)
        {
            BridgeCombatAuthority.RecordEnemySpawn(
                "EnemySpawner.Create.postfix",
                __result != null ? __result.transform.position : Vector3.zero,
                __result != null ? __result.transform.parent : null,
                null,
                __result);
            WorldTrace.Record("enemy.spawn.created", "result=" + WorldTrace.DescribeGameObject(__result));
        }
    }

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.CreateWithAndInitInstantiatedEnemy))]
    internal static class EnemySpawnerCreateExistingPatch
    {
        private static void Prefix(Vector3 Position, Transform Parent, GameObject Spawn)
        {
            BridgeCombatAuthority.RecordEnemySpawn("EnemySpawner.CreateWithAndInitInstantiatedEnemy.prefix", Position, Parent, Spawn, null);
            WorldTrace.Record(
                "enemy.spawn.init_existing",
                "enemy=" + WorldTrace.DescribeGameObject(Spawn)
                + " parent=" + WorldTrace.DescribeObject(Parent)
                + " pos=" + WorldTrace.FormatVector(Position));
        }
    }

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.InitAndInstantiate))]
    internal static class EnemySpawnerInitAndInstantiatePatch
    {
        private static void Prefix(EnemySpawner __instance, GameObject g)
        {
            BridgeCombatAuthority.RecordEnemySpawn(
                "EnemySpawner.InitAndInstantiate.prefix",
                g != null ? g.transform.position : Vector3.zero,
                __instance != null ? __instance.transform : null,
                g,
                null);
            WorldTrace.Record(
                "enemy.spawn.init_instantiate",
                "spawner=" + WorldTrace.DescribeGameObject(__instance != null ? __instance.gameObject : null)
                + " prefab=" + WorldTrace.DescribeGameObject(g));
        }

        private static void Postfix(GameObject __result)
        {
            BridgeCombatAuthority.RecordEnemySpawn(
                "EnemySpawner.InitAndInstantiate.postfix",
                __result != null ? __result.transform.position : Vector3.zero,
                __result != null ? __result.transform.parent : null,
                null,
                __result);
            WorldTrace.Record("enemy.spawn.init_instantiate.result", WorldTrace.DescribeGameObject(__result));
        }
    }

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.Init))]
    internal static class EnemySpawnerInitPatch
    {
        private static void Prefix(EnemySpawner __instance, GameObject g)
        {
            BridgeCombatAuthority.RecordEnemySpawn(
                "EnemySpawner.Init.prefix",
                g != null ? g.transform.position : Vector3.zero,
                __instance != null ? __instance.transform : null,
                g,
                g);
            WorldTrace.Record(
                "enemy.spawn.init",
                "spawner=" + WorldTrace.DescribeGameObject(__instance != null ? __instance.gameObject : null)
                + " enemy=" + WorldTrace.DescribeGameObject(g));
        }
    }

    [HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.Spawn), new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) })]
    internal static class ObjectPoolSpawnPatch
    {
        private static void Prefix(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, bool active, bool worldPositionStays)
        {
            if (!DiagnosticsPlugin.TraceObjectPoolEvents)
            {
                return;
            }

            WorldTrace.Record(
                "objectpool.spawn",
                "prefab=" + WorldTrace.DescribeGameObject(prefab)
                + " parent=" + WorldTrace.DescribeObject(parent)
                + " pos=" + WorldTrace.FormatVector(position)
                + " active=" + active
                + " worldPositionStays=" + worldPositionStays);
        }

        private static void Postfix(GameObject __result)
        {
            if (!DiagnosticsPlugin.TraceObjectPoolEvents)
            {
                return;
            }

            WorldTrace.Record("objectpool.spawn.result", WorldTrace.DescribeGameObject(__result));
        }
    }

    [HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.Recycle), new[] { typeof(GameObject) })]
    internal static class ObjectPoolRecyclePatch
    {
        private static void Prefix(GameObject obj)
        {
            if (!DiagnosticsPlugin.TraceObjectPoolEvents)
            {
                return;
            }

            WorldTrace.Record("objectpool.recycle", WorldTrace.DescribeGameObject(obj));
        }
    }

    [HarmonyPatch(typeof(WorldManipulatorManager), nameof(WorldManipulatorManager.TriggerManipulation))]
    internal static class WorldManipulatorTriggerPatch
    {
        private static void Prefix(WorldManipulatorManager.Manipulations manipulation, float delay, bool twitch)
        {
            BridgeCombatAuthority.RecordManipulation("prefix", manipulation, delay, twitch);
        }
    }

    [HarmonyPatch(typeof(UIDeathScreenOverlayController), nameof(UIDeathScreenOverlayController.Show), new[] { typeof(UIDeathScreenOverlayController.Results), typeof(bool) })]
    internal static class DeathScreenShowPatch
    {
        private static void Prefix(UIDeathScreenOverlayController.Results result, bool instant)
        {
            DeathScreenTraceHelper.RecordDeathScreen("show", result, -1, instant);
        }
    }

    [HarmonyPatch(typeof(UIDeathScreenOverlayController), nameof(UIDeathScreenOverlayController.Show), new[] { typeof(UIDeathScreenOverlayController.Results), typeof(int), typeof(bool) })]
    internal static class DeathScreenShowLevelsPatch
    {
        private static void Prefix(UIDeathScreenOverlayController.Results result, int levels, bool instant)
        {
            DeathScreenTraceHelper.RecordDeathScreen("show_levels", result, levels, instant);
        }
    }

    internal static class DeathScreenTraceHelper
    {
        public static void RecordDeathScreen(string source, UIDeathScreenOverlayController.Results result, int levels, bool instant)
        {
            try
            {
                WorldTrace.Record(
                    "sync.death_screen",
                    "clientId=" + DiagnosticsPlugin.ClientId
                    + " sessionId=" + DiagnosticsPlugin.SessionId
                    + " scene=" + SceneManager.GetActiveScene().name
                    + " location=" + PlayerFarming.Location
                    + " source=" + source
                    + " result=" + result
                    + " levels=" + levels
                    + " instant=" + instant
                    + " autoRespawn=" + PlayerFarming.AutoRespawn
                    + " timeScale=" + WorldTrace.FormatFloat(Time.timeScale)
                    + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.death_screen.error", ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

}
