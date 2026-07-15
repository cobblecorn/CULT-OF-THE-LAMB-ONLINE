using System;
using HarmonyLib;
using MMBiomeGeneration;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.SetNewRun), new[] { typeof(FollowerLocation) })]
    internal static class Phase3SetNewRunPatch
    {
        private static void Prefix(FollowerLocation location)
        {
            Phase3Trace.Record("phase3.run.set_new.prefix", "location=" + location);
        }

        private static void Postfix(FollowerLocation location)
        {
            Phase3Trace.Record("phase3.run.set_new.postfix", "location=" + location);
        }
    }

    [HarmonyPatch(typeof(DataManager), nameof(DataManager.AddNewDungeonSeed), new[] { typeof(int) })]
    internal static class Phase3AddNewDungeonSeedPatch
    {
        private static void Prefix(DataManager __instance, ref int seed)
        {
            bool forced = BridgeRunAuthority.TryOverrideDungeonSeed(ref seed, "DataManager.AddNewDungeonSeed");
            Phase3Trace.Record("phase3.run.seed_added.prefix", "seed=" + seed + " forced=" + forced + " manager=" + (__instance != null ? __instance.GetType().Name : "null"));
        }

        private static void Postfix(DataManager __instance, int seed)
        {
            Phase3Trace.Record("phase3.run.seed_added.postfix", "seed=" + seed + " manager=" + (__instance != null ? __instance.GetType().Name : "null"));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "RandomiseSeed", new Type[0])]
    internal static class Phase3BiomeRandomiseSeedPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            bool forced = BridgeRunAuthority.TryApplyAuthoritySeed(__instance, "BiomeGenerator.RandomiseSeed.prefix");
            Phase3Trace.Record("phase3.biome.randomise_seed.prefix", "forced=" + forced + " " + Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            bool forced = BridgeRunAuthority.TryApplyAuthoritySeed(__instance, "BiomeGenerator.RandomiseSeed.postfix");
            Phase3Trace.Record("phase3.biome.randomise_seed.postfix", "forced=" + forced + " " + Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "CreateRandomWalk", new Type[0])]
    internal static class Phase3BiomeRandomWalkPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            bool forced = BridgeRunAuthority.TryApplyAuthoritySeed(__instance, "BiomeGenerator.CreateRandomWalk.prefix");
            Phase3Trace.Record("phase3.world.random_walk.prefix", "forced=" + forced + " " + Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.random_walk.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceEntranceAndExit", new Type[0])]
    internal static class Phase3BiomeEntranceExitPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.entrance_exit.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.entrance_exit.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceLockAndKey", new Type[0])]
    internal static class Phase3BiomeLockAndKeyPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.lock_key.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.lock_key.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceRespawnRoom", new Type[0])]
    internal static class Phase3BiomeRespawnRoomPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.respawn_room.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.respawn_room.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceDeathCatRoom", new Type[0])]
    internal static class Phase3BiomeDeathCatRoomPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.death_cat_room.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.death_cat_room.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceFixedCustomRooms", new Type[0])]
    internal static class Phase3BiomeFixedCustomRoomsPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.fixed_custom_rooms.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.fixed_custom_rooms.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceStoryRooms", new Type[0])]
    internal static class Phase3BiomeStoryRoomsPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.story_rooms.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.story_rooms.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "PlaceDynamicCustomRooms", new Type[0])]
    internal static class Phase3BiomeDynamicCustomRoomsPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.dynamic_custom_rooms.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.dynamic_custom_rooms.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "InstantiatePrefabs", new Type[0])]
    internal static class Phase3BiomeInstantiatePrefabsPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.instantiate_prefabs.prefix", Phase3Trace.DescribeBiomeGraph(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase3Trace.Record("phase3.world.instantiate_prefabs.postfix", Phase3Trace.DescribeBiomeGraph(__instance));
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.SpawnCoopWeapons), new[] { typeof(PlayerFarming), typeof(bool), typeof(bool) })]
    internal static class Phase3SpawnCoopWeaponsPatch
    {
        private static void Prefix(PlayerFarming playerFarming, bool forceWeapon, bool forceCurse)
        {
            Phase3Trace.Record(
                "phase3.coop.spawn_weapons.prefix",
                "forceWeapon=" + forceWeapon
                + " forceCurse=" + forceCurse
                + " target=" + Phase3Trace.DescribePlayerEquipment(playerFarming)
                + " " + Phase3Trace.DescribeStack(2));
        }

        private static void Postfix(PlayerFarming playerFarming, bool forceWeapon, bool forceCurse)
        {
            Phase3Trace.Record(
                "phase3.coop.spawn_weapons.postfix",
                "forceWeapon=" + forceWeapon
                + " forceCurse=" + forceCurse
                + " target=" + Phase3Trace.DescribePlayerEquipment(playerFarming));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponPickUp), nameof(Interaction_WeaponPickUp.SetWeapon), new[] { typeof(EquipmentType), typeof(int), typeof(Interaction_WeaponPickUp.Types) })]
    internal static class Phase3WeaponPickupSetWeaponPatch
    {
        private static void Prefix(Interaction_WeaponPickUp __instance, EquipmentType TypeOfWeapon, int WeaponLevel, Interaction_WeaponPickUp.Types Type)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_set.prefix",
                "new=" + TypeOfWeapon + "@" + WeaponLevel + " pickupType=" + Type
                + " " + Phase3Trace.DescribeWeaponPickup(__instance)
                + " " + Phase3Trace.DescribeStack(2));
        }

        private static void Postfix(Interaction_WeaponPickUp __instance, EquipmentType TypeOfWeapon, int WeaponLevel, Interaction_WeaponPickUp.Types Type)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_set.postfix",
                "new=" + TypeOfWeapon + "@" + WeaponLevel + " pickupType=" + Type
                + " " + Phase3Trace.DescribeWeaponPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponPickUp), nameof(Interaction_WeaponPickUp.OnInteract), new[] { typeof(StateMachine) })]
    internal static class Phase3WeaponPickupInteractPatch
    {
        private static void Prefix(Interaction_WeaponPickUp __instance, StateMachine state)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_interact.prefix",
                "actor=" + Phase3Trace.DescribeStatePlayer(state)
                + " " + Phase3Trace.DescribeWeaponPickup(__instance));
        }

        private static void Postfix(Interaction_WeaponPickUp __instance, StateMachine state)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_interact.postfix",
                "actor=" + Phase3Trace.DescribeStatePlayer(state)
                + " " + Phase3Trace.DescribeWeaponPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponPickUp), nameof(Interaction_WeaponPickUp.OnSecondaryInteract), new[] { typeof(StateMachine) })]
    internal static class Phase3WeaponPickupSecondaryInteractPatch
    {
        private static void Prefix(Interaction_WeaponPickUp __instance, StateMachine state)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_secondary.prefix",
                "actor=" + Phase3Trace.DescribeStatePlayer(state)
                + " " + Phase3Trace.DescribeWeaponPickup(__instance));
        }

        private static void Postfix(Interaction_WeaponPickUp __instance, StateMachine state)
        {
            Phase3Trace.Record(
                "phase3.equipment.pickup_secondary.postfix",
                "actor=" + Phase3Trace.DescribeStatePlayer(state)
                + " " + Phase3Trace.DescribeWeaponPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(PlayerWeapon), nameof(PlayerWeapon.SetWeapon), new[] { typeof(EquipmentType), typeof(int) })]
    internal static class Phase3PlayerWeaponSetPatch
    {
        private static void Prefix(PlayerWeapon __instance, EquipmentType weaponType, int WeaponLevel)
        {
            Phase3Trace.Record(
                "phase3.equipment.player_weapon_set.prefix",
                "new=" + weaponType + "@" + WeaponLevel
                + " bridgeApply=" + BridgeLoadoutAuthority.IsApplying
                + " before=" + Phase3Trace.DescribePlayerEquipment(__instance != null ? __instance.playerFarming : null)
                + " " + Phase3Trace.DescribeStack(2));
        }

        private static void Postfix(PlayerWeapon __instance, EquipmentType weaponType, int WeaponLevel)
        {
            Phase3Trace.Record(
                "phase3.equipment.player_weapon_set.postfix",
                "new=" + weaponType + "@" + WeaponLevel
                + " bridgeApply=" + BridgeLoadoutAuthority.IsApplying
                + " after=" + Phase3Trace.DescribePlayerEquipment(__instance != null ? __instance.playerFarming : null));
        }
    }

    [HarmonyPatch(typeof(PlayerSpells), nameof(PlayerSpells.SetSpell), new[] { typeof(EquipmentType), typeof(int) })]
    internal static class Phase3PlayerSpellSetPatch
    {
        private static void Prefix(PlayerSpells __instance, EquipmentType Spell, int CurseLevel)
        {
            Phase3Trace.Record(
                "phase3.equipment.player_spell_set.prefix",
                "new=" + Spell + "@" + CurseLevel
                + " bridgeApply=" + BridgeLoadoutAuthority.IsApplying
                + " before=" + Phase3Trace.DescribePlayerEquipment(__instance != null ? __instance.playerFarming : null)
                + " " + Phase3Trace.DescribeStack(2));
        }

        private static void Postfix(PlayerSpells __instance, EquipmentType Spell, int CurseLevel)
        {
            Phase3Trace.Record(
                "phase3.equipment.player_spell_set.postfix",
                "new=" + Spell + "@" + CurseLevel
                + " bridgeApply=" + BridgeLoadoutAuthority.IsApplying
                + " after=" + Phase3Trace.DescribePlayerEquipment(__instance != null ? __instance.playerFarming : null));
        }
    }
}
