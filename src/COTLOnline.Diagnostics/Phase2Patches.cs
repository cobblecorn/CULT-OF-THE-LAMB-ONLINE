using System;
using System.Collections;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.SpawnCoopPlayer), new[] { typeof(int), typeof(bool), typeof(float) })]
    internal static class Phase2CoopSpawnPatch
    {
        private static void Prefix(int slot, bool playEffects, float startingHealth)
        {
            Phase2Trace.RecordCoop(
                "phase2.coop.spawn.prefix",
                "slot=" + slot + " playEffects=" + playEffects + " startingHealth=" + WorldTrace.FormatFloat(startingHealth));
        }

        private static void Postfix(int slot)
        {
            Phase2Trace.RecordCoop("phase2.coop.spawn.postfix", "slot=" + slot);
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.AddPlayerFromMenu), new Type[0])]
    internal static class Phase2CoopAddFromMenuPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordCoop("phase2.coop.add_menu.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordCoop("phase2.coop.add_menu.postfix", "");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemovePlayerFromMenu), new Type[0])]
    internal static class Phase2CoopRemoveFromMenuPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordCoop("phase2.coop.remove_menu.prefix", "", true);
        }

        private static void Postfix()
        {
            Phase2Trace.RecordCoop("phase2.coop.remove_menu.postfix", "", true);
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.ClearCoopMode), new Type[0])]
    internal static class Phase2CoopClearPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordCoop("phase2.coop.clear.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordCoop("phase2.coop.clear.postfix", "");
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemoveCoopPlayer), new[] { typeof(PlayerFarming), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class Phase2CoopRemovePatch
    {
        private static void Prefix(PlayerFarming playerToRemove, bool instant, bool disengagePlayer, bool withDelay)
        {
            Phase2Trace.RecordCoop(
                "phase2.coop.remove.prefix",
                "player=" + WorldTrace.DescribePlayer(playerToRemove)
                + " instant=" + instant
                + " disengagePlayer=" + disengagePlayer
                + " withDelay=" + withDelay);
        }

        private static void Postfix(PlayerFarming playerToRemove)
        {
            Phase2Trace.RecordCoop("phase2.coop.remove.postfix", "player=" + WorldTrace.DescribePlayer(playerToRemove));
        }
    }

    [HarmonyPatch(typeof(CoopManager), nameof(CoopManager.RemoveCoopPlayerStatic), new[] { typeof(PlayerFarming), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class Phase2CoopRemoveStaticPatch
    {
        private static void Prefix(PlayerFarming playerToRemove, bool instant, bool disengagePlayer, bool withDelay)
        {
            Phase2Trace.RecordCoop(
                "phase2.coop.remove_static.prefix",
                "player=" + WorldTrace.DescribePlayer(playerToRemove)
                + " instant=" + instant
                + " disengagePlayer=" + disengagePlayer
                + " withDelay=" + withDelay);
        }

        private static void Postfix(PlayerFarming playerToRemove)
        {
            Phase2Trace.RecordCoop("phase2.coop.remove_static.postfix", "player=" + WorldTrace.DescribePlayer(playerToRemove));
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.RefreshPlayersCount), new[] { typeof(bool) })]
    internal static class Phase2RefreshPlayersCountPatch
    {
        private static void Prefix(bool initPlayers)
        {
            Phase2Trace.RecordCoop("phase2.player_count.prefix", "initPlayers=" + initPlayers, true);
        }

        private static void Postfix(bool initPlayers)
        {
            Phase2Trace.RecordCoop("phase2.player_count.postfix", "initPlayers=" + initPlayers, true);
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.HidePlayer), new[] { typeof(PlayerFarming), typeof(bool) })]
    internal static class Phase2HidePlayerPatch
    {
        private static void Prefix(PlayerFarming playerFarming, bool withDelay)
        {
            Phase2Trace.RecordCoop("phase2.player.hide.prefix", "player=" + WorldTrace.DescribePlayer(playerFarming) + " withDelay=" + withDelay);
        }

        private static void Postfix(PlayerFarming playerFarming)
        {
            Phase2Trace.RecordCoop("phase2.player.hide.postfix", "player=" + WorldTrace.DescribePlayer(playerFarming));
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), "OnDestroy")]
    internal static class Phase2PlayerDestroyPatch
    {
        private static void Prefix(PlayerFarming __instance)
        {
            Phase2Trace.RecordCoop("phase2.player.destroy.prefix", "player=" + WorldTrace.DescribePlayer(__instance));
        }

        private static void Postfix(PlayerFarming __instance)
        {
            Phase2Trace.RecordCoop("phase2.player.destroy.postfix", "player=" + WorldTrace.DescribePlayer(__instance));
        }
    }

    [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Init), new Type[0])]
    internal static class Phase2PlayerInitPatch
    {
        private static void Postfix(PlayerFarming __instance)
        {
            Phase2Trace.RecordCoop("phase2.player.init.postfix", "player=" + WorldTrace.DescribePlayer(__instance), true);
        }
    }

    [HarmonyPatch(typeof(RoomManager), "OnWorldGenerated")]
    internal static class Phase2RoomWorldGeneratedPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordRoom("phase2.room.world_generated.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordRoom("phase2.room.world_generated.postfix", "");
        }
    }

    [HarmonyPatch(typeof(RoomManager), "DoWorldGeneration")]
    internal static class Phase2RoomDoWorldGenerationPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordRoom("phase2.room.do_world_generation.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordRoom("phase2.room.do_world_generation.postfix", "");
        }
    }

    [HarmonyPatch(typeof(RoomManager), "CreateRooms")]
    internal static class Phase2RoomCreateRoomsPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordRoom("phase2.room.create_rooms.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordRoom("phase2.room.create_rooms.postfix", "");
        }
    }

    [HarmonyPatch(typeof(RoomManager), "ChangeRoom", new Type[0])]
    internal static class Phase2RoomChangeInternalPatch
    {
        private static void Prefix()
        {
            Phase2Trace.RecordRoom("phase2.room.change.prefix", "");
        }

        private static void Postfix()
        {
            Phase2Trace.RecordRoom("phase2.room.change.postfix", "");
        }
    }

    [HarmonyPatch(typeof(RoomManager), nameof(RoomManager.ChangeRoom), new[] { typeof(Vector3Int) })]
    internal static class Phase2RoomChangeDirectionPatch
    {
        private static void Prefix(Vector3Int Direction)
        {
            Phase2Trace.RecordRoom("phase2.room.change_direction.prefix", "direction=(" + Direction.x + "," + Direction.y + "," + Direction.z + ")");
        }

        private static void Postfix(Vector3Int Direction)
        {
            Phase2Trace.RecordRoom("phase2.room.change_direction.postfix", "direction=(" + Direction.x + "," + Direction.y + "," + Direction.z + ")");
        }
    }

    [HarmonyPatch(typeof(RoomManager), nameof(RoomManager.ChangeRoom), new[] { typeof(int), typeof(int) })]
    internal static class Phase2RoomChangeXYPatch
    {
        private static void Prefix(int X, int Y)
        {
            Phase2Trace.RecordRoom("phase2.room.change_xy.prefix", "target=(" + X + "," + Y + ")");
        }

        private static void Postfix(int X, int Y)
        {
            Phase2Trace.RecordRoom("phase2.room.change_xy.postfix", "target=(" + X + "," + Y + ")");
        }
    }

    [HarmonyPatch(typeof(RoomManager), nameof(RoomManager.PlaceAndPositionPlayer), new[] { typeof(bool) })]
    internal static class Phase2RoomPlacePlayerPatch
    {
        private static void Prefix(RoomManager __instance, bool ForceCentrePlayer)
        {
            Phase2Trace.RecordRoom(
                "phase2.room.place_player.prefix",
                "forceCentre=" + ForceCentrePlayer + " roomPlayer=" + Phase2Trace.DescribeRoomManagerPlayer(__instance));
        }

        private static void Postfix(RoomManager __instance, bool ForceCentrePlayer)
        {
            Phase2Trace.RecordRoom(
                "phase2.room.place_player.postfix",
                "forceCentre=" + ForceCentrePlayer + " roomPlayer=" + Phase2Trace.DescribeRoomManagerPlayer(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "GenerateRoutine")]
    internal static class Phase2BiomeGenerateRoutinePatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            BridgeRunAuthority.TryApplyAuthoritySeed(__instance, "BiomeGenerator.GenerateRoutine.prefix");
            Phase2Trace.RecordRoom("phase2.biome.generate_routine.prefix", Phase2Trace.DescribeBiomeGenerator(__instance));
        }

        private static void Postfix(BiomeGenerator __instance, ref IEnumerator __result)
        {
            IEnumerator wrapped;
            if (BridgeRunAuthority.TryWrapBiomeGenerateRoutine(__instance, __result, out wrapped))
            {
                __result = wrapped;
            }

            Phase2Trace.RecordRoom("phase2.biome.generate_routine.postfix", Phase2Trace.DescribeBiomeGenerator(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.Generate), new Type[0])]
    internal static class Phase2BiomeGeneratePatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            BridgeRunAuthority.TryApplyAuthoritySeed(__instance, "BiomeGenerator.Generate.prefix");
            Phase2Trace.RecordRoom("phase2.biome.generate.prefix", Phase2Trace.DescribeBiomeGenerator(__instance));
        }

        private static void Postfix(BiomeGenerator __instance)
        {
            Phase2Trace.RecordRoom("phase2.biome.generate.postfix", Phase2Trace.DescribeBiomeGenerator(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "SetRoom", new[] { typeof(int), typeof(int) })]
    internal static class Phase2BiomeSetRoomPatch
    {
        private static void Prefix(BiomeGenerator __instance, int x, int y)
        {
            Phase2Trace.RecordRoom("phase2.biome.set_room.prefix", "target=(" + x + "," + y + ") " + Phase2Trace.DescribeBiomeGenerator(__instance));
        }

        private static void Postfix(BiomeGenerator __instance, int x, int y)
        {
            Phase2Trace.RecordRoom("phase2.biome.set_room.postfix", "target=(" + x + "," + y + ") " + Phase2Trace.DescribeBiomeGenerator(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.ChangeRoom), new[] { typeof(int), typeof(int) })]
    internal static class Phase2BiomeChangeRoomXYPatch
    {
        private static void Prefix(int X, int Y)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_xy.prefix", "target=(" + X + "," + Y + ") " + Phase2Trace.DescribeBiomeGenerator(BiomeGenerator.Instance));
        }

        private static void Postfix(int X, int Y)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_xy.postfix", "target=(" + X + "," + Y + ") " + Phase2Trace.DescribeBiomeGenerator(BiomeGenerator.Instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.ChangeRoom), new[] { typeof(Vector2Int) })]
    internal static class Phase2BiomeChangeRoomDirectionPatch
    {
        private static void Prefix(Vector2Int Direction)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_direction.prefix", "direction=(" + Direction.x + "," + Direction.y + ") " + Phase2Trace.DescribeBiomeGenerator(BiomeGenerator.Instance));
        }

        private static void Postfix(Vector2Int Direction)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_direction.postfix", "direction=(" + Direction.x + "," + Direction.y + ") " + Phase2Trace.DescribeBiomeGenerator(BiomeGenerator.Instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), "ChangeRoomRoutine", new[] { typeof(BiomeRoom) })]
    internal static class Phase2BiomeChangeRoomRoutinePatch
    {
        private static void Prefix(BiomeGenerator __instance, BiomeRoom CurrentRoom)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_routine.prefix", "target=" + Phase2Trace.DescribeBiomeRoom(CurrentRoom) + " " + Phase2Trace.DescribeBiomeGenerator(__instance));
        }

        private static void Postfix(BiomeGenerator __instance, BiomeRoom CurrentRoom)
        {
            Phase2Trace.RecordRoom("phase2.biome.change_routine.postfix", "target=" + Phase2Trace.DescribeBiomeRoom(CurrentRoom) + " " + Phase2Trace.DescribeBiomeGenerator(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.PlacePlayer), new Type[0])]
    internal static class Phase2BiomePlacePlayerPatch
    {
        private static void Prefix(BiomeGenerator __instance)
        {
            Phase2Trace.RecordRoom("phase2.biome.place_player.prefix", Phase2Trace.DescribeBiomeGenerator(__instance));
        }

        private static void Postfix(BiomeGenerator __instance, GameObject __result)
        {
            Phase2Trace.RecordRoom("phase2.biome.place_player.postfix", "result=" + WorldTrace.DescribeGameObject(__result) + " " + Phase2Trace.DescribeBiomeGenerator(__instance));
        }
    }

    [HarmonyPatch(typeof(BiomeRoom), nameof(BiomeRoom.Activate), new[] { typeof(BiomeRoom), typeof(bool) })]
    internal static class Phase2BiomeRoomActivatePatch
    {
        private static void Prefix(BiomeRoom __instance, BiomeRoom PrevRoom, bool ReuseGeneratorRoom)
        {
            Phase2Trace.RecordRoom("phase2.biome_room.activate.prefix", "room=" + Phase2Trace.DescribeBiomeRoom(__instance) + " prev=" + Phase2Trace.DescribeBiomeRoom(PrevRoom) + " reuseGeneratorRoom=" + ReuseGeneratorRoom);
        }

        private static void Postfix(BiomeRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.biome_room.activate.postfix", "room=" + Phase2Trace.DescribeBiomeRoom(__instance));
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate), new[] { typeof(int), typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes) })]
    internal static class Phase2GenerateRoomGeneratePublicPatch
    {
        private static void Prefix(GenerateRoom __instance, int Seed, GenerateRoom.ConnectionTypes North, GenerateRoom.ConnectionTypes East, GenerateRoom.ConnectionTypes South, GenerateRoom.ConnectionTypes West)
        {
            Phase2Trace.RecordRoom(
                "phase2.generate_room.generate_public.prefix",
                "seed=" + Seed + " connections=N:" + North + ",E:" + East + ",S:" + South + ",W:" + West + " " + Phase2Trace.DescribeGenerateRoom(__instance));
        }

        private static void Postfix(GenerateRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.generate_room.generate_public.postfix", Phase2Trace.DescribeGenerateRoom(__instance));
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), "Generate", new Type[0])]
    internal static class Phase2GenerateRoomGenerateRoutinePatch
    {
        private static void Prefix(GenerateRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.generate_room.generate_routine.prefix", Phase2Trace.DescribeGenerateRoom(__instance));
        }

        private static void Postfix(GenerateRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.generate_room.generate_routine.postfix", Phase2Trace.DescribeGenerateRoom(__instance));
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), "OnEnable")]
    internal static class Phase2GenerateRoomOnEnablePatch
    {
        private static void Prefix(GenerateRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.generate_room.enable.prefix", Phase2Trace.DescribeGenerateRoom(__instance));
        }

        private static void Postfix(GenerateRoom __instance)
        {
            Phase2Trace.RecordRoom("phase2.generate_room.enable.postfix", Phase2Trace.DescribeGenerateRoom(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), nameof(Interaction_WeaponSelectionPodium.ResetRandom), new[] { typeof(bool), typeof(int) })]
    internal static class Phase2PodiumResetRandomPatch
    {
        private static void Prefix(Interaction_WeaponSelectionPodium __instance, bool ForceShowGoop, int ForceLevel)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_reset.prefix", "forceShowGoop=" + ForceShowGoop + " forceLevel=" + ForceLevel + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_reset.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), "SetWeapon", new[] { typeof(int) })]
    internal static class Phase2PodiumSetWeaponPatch
    {
        private static void Prefix(Interaction_WeaponSelectionPodium __instance, ref int ForceLevel)
        {
            BridgeRunAuthority.TryApplyNextReward(__instance, "Weapon", ref ForceLevel);
            Phase2Trace.RecordReward("phase2.reward.podium_set_weapon.prefix", "forceLevel=" + ForceLevel + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_set_weapon.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), "SetCurse", new[] { typeof(int) })]
    internal static class Phase2PodiumSetCursePatch
    {
        private static void Prefix(Interaction_WeaponSelectionPodium __instance, ref int ForceLevel)
        {
            BridgeRunAuthority.TryApplyNextReward(__instance, "Curse", ref ForceLevel);
            Phase2Trace.RecordReward("phase2.reward.podium_set_curse.prefix", "forceLevel=" + ForceLevel + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_set_curse.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), "SetRelic", new Type[0])]
    internal static class Phase2PodiumSetRelicPatch
    {
        private static void Prefix(Interaction_WeaponSelectionPodium __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_set_relic.prefix", Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_set_relic.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponChoiceChest), "SetWeapon", new[] { typeof(int) })]
    internal static class Phase2ChoiceChestSetWeaponPatch
    {
        private static void Prefix(Interaction_WeaponChoiceChest __instance, ref int ForceLevel)
        {
            BridgeRunAuthority.TryApplyNextReward(__instance, "Weapon", ref ForceLevel);
            Phase2Trace.RecordReward("phase2.reward.choice_set_weapon.prefix", "forceLevel=" + ForceLevel + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponChoiceChest __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_set_weapon.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponChoiceChest), "SetCurse", new[] { typeof(int) })]
    internal static class Phase2ChoiceChestSetCursePatch
    {
        private static void Prefix(Interaction_WeaponChoiceChest __instance, ref int ForceLevel)
        {
            BridgeRunAuthority.TryApplyNextReward(__instance, "Curse", ref ForceLevel);
            Phase2Trace.RecordReward("phase2.reward.choice_set_curse.prefix", "forceLevel=" + ForceLevel + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponChoiceChest __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_set_curse.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponChoiceChest), "SetRelic", new Type[0])]
    internal static class Phase2ChoiceChestSetRelicPatch
    {
        private static void Prefix(Interaction_WeaponChoiceChest __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_set_relic.prefix", Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponChoiceChest __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_set_relic.postfix", Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), nameof(Interaction_WeaponSelectionPodium.OnInteract), new[] { typeof(StateMachine) })]
    internal static class Phase2PodiumInteractPatch
    {
        private static void Prefix(Interaction_WeaponSelectionPodium __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_interact.prefix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.podium_interact.postfix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponChoiceChest), nameof(Interaction_WeaponChoiceChest.OnInteract), new[] { typeof(StateMachine) })]
    internal static class Phase2ChoiceChestInteractPatch
    {
        private static void Prefix(Interaction_WeaponChoiceChest __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_interact.prefix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribePodium(__instance));
        }

        private static void Postfix(Interaction_WeaponChoiceChest __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.choice_interact.postfix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribePodium(__instance));
        }
    }

    [HarmonyPatch(typeof(RelicPickUp), nameof(RelicPickUp.Configure), new[] { typeof(RelicData) })]
    internal static class Phase2RelicConfigurePatch
    {
        private static void Prefix(RelicPickUp __instance, RelicData relicData)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_configure.prefix", "newRelic=" + (relicData != null ? relicData.RelicType.ToString() : "null") + " " + Phase2Trace.DescribeRelicPickup(__instance));
        }

        private static void Postfix(RelicPickUp __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_configure.postfix", Phase2Trace.DescribeRelicPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(RelicPickUp), nameof(RelicPickUp.ConfigureRandomise), new Type[0])]
    internal static class Phase2RelicRandomisePatch
    {
        private static void Prefix(RelicPickUp __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_randomise.prefix", Phase2Trace.DescribeRelicPickup(__instance));
        }

        private static void Postfix(RelicPickUp __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_randomise.postfix", Phase2Trace.DescribeRelicPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(RelicPickUp), nameof(RelicPickUp.OnInteract), new[] { typeof(StateMachine) })]
    internal static class Phase2RelicInteractPatch
    {
        private static void Prefix(RelicPickUp __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_interact.prefix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribeRelicPickup(__instance));
        }

        private static void Postfix(RelicPickUp __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.relic_interact.postfix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribeRelicPickup(__instance));
        }
    }

    [HarmonyPatch(typeof(Interaction_TarotCard), nameof(Interaction_TarotCard.OnInteract), new[] { typeof(StateMachine) })]
    internal static class Phase2TarotInteractPatch
    {
        private static void Prefix(Interaction_TarotCard __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.tarot_interact.prefix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribeTarot(__instance));
        }

        private static void Postfix(Interaction_TarotCard __instance, StateMachine state)
        {
            Phase2Trace.RecordReward("phase2.reward.tarot_interact.postfix", "actor=" + Phase2Trace.DescribeStatePlayer(state) + " " + Phase2Trace.DescribeTarot(__instance));
        }
    }

    [HarmonyPatch(typeof(PickUp), nameof(PickUp.PickMeUp), new Type[0])]
    internal static class Phase2PickupCollectPatch
    {
        private static void Prefix(PickUp __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.pickup.prefix", Phase2Trace.DescribePickup(__instance));
        }

        private static void Postfix(PickUp __instance)
        {
            Phase2Trace.RecordReward("phase2.reward.pickup.postfix", Phase2Trace.DescribePickup(__instance));
        }
    }
}
