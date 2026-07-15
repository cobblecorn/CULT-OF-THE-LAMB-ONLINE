using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class Phase2Trace
    {
        private static readonly FieldInfo WeaponLevelField = AccessTools.Field(typeof(Interaction_WeaponSelectionPodium), "WeaponLevel");
        private static readonly FieldInfo TarotDrawnCardField = AccessTools.Field(typeof(Interaction_TarotCard), "DrawnCard");
        private static readonly FieldInfo TarotCardTypeField = AccessTools.Field(typeof(TarotCards.TarotCard), "CardType");
        private static readonly FieldInfo RoomManagerPlayerField = AccessTools.Field(typeof(RoomManager), "player");
        private static readonly FieldInfo PickupCollectedField = AccessTools.Field(typeof(PickUp), "Collected");

        private static bool _coopEnabled;
        private static bool _roomEnabled;
        private static bool _rewardEnabled;
        private static string _lastPresenceSignature = "";
        private static float _lastRefreshLogTime;

        public static void Configure(bool coopEnabled, bool roomEnabled, bool rewardEnabled)
        {
            _coopEnabled = coopEnabled;
            _roomEnabled = roomEnabled;
            _rewardEnabled = rewardEnabled;

            WorldTrace.Record(
                "phase2.config",
                "coop=" + coopEnabled + " room=" + roomEnabled + " reward=" + rewardEnabled);
        }

        public static void RecordCoop(string category, string detail, bool deDuplicatePresence = false)
        {
            if (!_coopEnabled)
            {
                return;
            }

            SafeRecord(category, () =>
            {
                string presence = BuildPresence();
                if (deDuplicatePresence && !ShouldLogPresence(presence))
                {
                    return null;
                }

                return Append(detail, presence);
            });
        }

        public static void RecordRoom(string category, string detail)
        {
            if (!_roomEnabled)
            {
                return;
            }

            SafeRecord(category, () => Append(Append(detail, BuildRoomState()), BuildPresence()));
        }

        public static void RecordReward(string category, string detail)
        {
            if (!_rewardEnabled)
            {
                return;
            }

            SafeRecord(category, () => Append(Append(detail, BuildRewardContext()), BuildPresence()));
        }

        public static string DescribePodium(Interaction_WeaponSelectionPodium podium)
        {
            if (podium == null)
            {
                return "podium=null";
            }

            return "podium=" + WorldTrace.DescribeGameObject(podium.gameObject)
                + " podiumPos=" + WorldTrace.FormatVector(podium.transform.position)
                + " type=" + SafeValue(() => podium.Type.ToString())
                + " weapon=" + SafeValue(() => podium.TypeOfWeapon.ToString())
                + " relic=" + SafeValue(() => podium.TypeOfRelic.ToString())
                + " level=" + ReadWeaponLevel(podium)
                + " taken=" + SafeValue(() => podium.WeaponTaken.ToString())
                + " coopPodium=" + SafeValue(() => podium.isCoopPodium.ToString())
                + " destroyOtherInCoop=" + SafeValue(() => podium.DestroyOtherWeaponInCoop.ToString())
                + " forceEquipment=" + SafeValue(() => podium.ForceEquipmentType.ToString())
                + " forceRelic=" + SafeValue(() => podium.ForceRelicType.ToString());
        }

        public static string DescribeRelicPickup(RelicPickUp pickup)
        {
            if (pickup == null)
            {
                return "relicPickup=null";
            }

            return "relicPickup=" + WorldTrace.DescribeGameObject(pickup.gameObject)
                + " relic=" + SafeValue(() => pickup.RelicData != null ? pickup.RelicData.RelicType.ToString() : "null");
        }

        public static string DescribeTarot(Interaction_TarotCard tarot)
        {
            if (tarot == null)
            {
                return "tarot=null";
            }

            object drawn = SafeObject(() => TarotDrawnCardField != null ? TarotDrawnCardField.GetValue(tarot) : null);
            string drawnType = "null";
            if (drawn != null)
            {
                drawnType = SafeValue(() => TarotCardTypeField != null ? TarotCardTypeField.GetValue(drawn).ToString() : drawn.ToString());
            }

            return "tarot=" + WorldTrace.DescribeGameObject(tarot.gameObject)
                + " override=" + SafeValue(() => tarot.CardOverride.ToString())
                + " forceAllow=" + SafeValue(() => tarot.ForceAllow.ToString())
                + " activated=" + SafeValue(() => tarot.Activated.ToString())
                + " drawn=" + drawnType;
        }

        public static string DescribePickup(PickUp pickup)
        {
            if (pickup == null)
            {
                return "pickup=null";
            }

            return "pickup=" + WorldTrace.DescribeGameObject(pickup.gameObject)
                + " item=" + SafeValue(() => pickup.type.ToString())
                + " quantity=" + SafeValue(() => pickup.Quantity.ToString())
                + " collected=" + SafeValue(() => PickupCollectedField != null ? PickupCollectedField.GetValue(pickup).ToString() : "unknown")
                + " addToInventory=" + SafeValue(() => pickup.AddToInventory.ToString())
                + " player=" + WorldTrace.DescribeGameObject(SafeObject(() => pickup.Player) as GameObject);
        }

        public static string DescribeRoomManagerPlayer(RoomManager roomManager)
        {
            if (roomManager == null)
            {
                return "null";
            }

            return WorldTrace.DescribeGameObject(SafeObject(() => RoomManagerPlayerField != null ? RoomManagerPlayerField.GetValue(roomManager) : null) as GameObject);
        }

        public static string DescribeBiomeGenerator(BiomeGenerator biomeGenerator)
        {
            if (biomeGenerator == null)
            {
                return "biome=null";
            }

            return "biome=" + WorldTrace.DescribeObject(biomeGenerator)
                + " dungeon=" + SafeValue(() => biomeGenerator.DungeonLocation.ToString())
                + " seed=" + SafeValue(() => biomeGenerator.Seed.ToString())
                + " rooms=" + SafeValue(() => biomeGenerator.Rooms != null ? biomeGenerator.Rooms.Count.ToString() : "null")
                + " current=(" + SafeValue(() => biomeGenerator.CurrentX.ToString()) + "," + SafeValue(() => biomeGenerator.CurrentY.ToString()) + ")"
                + " currentRoom=" + DescribeBiomeRoom(SafeObject(() => biomeGenerator.CurrentRoom) as BiomeRoom)
                + " entrance=" + DescribeBiomeRoom(SafeObject(() => biomeGenerator.RoomEntrance) as BiomeRoom)
                + " exit=" + DescribeBiomeRoom(SafeObject(() => biomeGenerator.RoomExit) as BiomeRoom)
                + " player=" + WorldTrace.DescribeGameObject(SafeObject(() => biomeGenerator.Player) as GameObject)
                + " layer2=" + SafeValue(() => GameManager.Layer2.ToString())
                + " currentDungeonLayer=" + SafeValue(() => GameManager.CurrentDungeonLayer.ToString())
                + " floor=" + SafeValue(() => GameManager.CurrentDungeonFloor.ToString());
        }

        public static string DescribeBiomeRoom(BiomeRoom room)
        {
            if (room == null)
            {
                return "null";
            }

            return "room=(" + SafeValue(() => room.x.ToString()) + "," + SafeValue(() => room.y.ToString()) + ")"
                + " seed=" + SafeValue(() => room.Seed.ToString())
                + " custom=" + SafeValue(() => room.IsCustom.ToString())
                + " boss=" + SafeValue(() => room.IsBoss.ToString())
                + " hasWeapon=" + SafeValue(() => room.HasWeapon.ToString())
                + " generated=" + SafeValue(() => room.Generated.ToString())
                + " active=" + SafeValue(() => room.Active.ToString())
                + " visited=" + SafeValue(() => room.Visited.ToString())
                + " completed=" + SafeValue(() => room.Completed.ToString())
                + " hidden=" + SafeValue(() => room.Hidden.ToString())
                + " path=" + SafeValue(() => room.GameObjectPath)
                + " connections=" + SafeValue(() => DescribeConnections(room));
        }

        public static string DescribeGenerateRoom(GenerateRoom room)
        {
            if (room == null)
            {
                return "generateRoom=null";
            }

            return "generateRoom=" + WorldTrace.DescribeGameObject(room.gameObject)
                + " seed=" + SafeValue(() => room.Seed.ToString())
                + " generated=" + SafeValue(() => room.GeneratedDecorations.ToString())
                + " pathing=" + SafeValue(() => room.GeneratedPathing.ToString())
                + " connections=N:" + SafeValue(() => room.North.ToString())
                + ",E:" + SafeValue(() => room.East.ToString())
                + ",S:" + SafeValue(() => room.South.ToString())
                + ",W:" + SafeValue(() => room.West.ToString())
                + " roomMusic=" + SafeValue(() => room.roomMusicID.ToString());
        }

        public static string DescribeStatePlayer(StateMachine state)
        {
            return WorldTrace.DescribePlayer(SafeObject(() => state != null ? state.GetComponent<PlayerFarming>() : null) as PlayerFarming);
        }

        private static bool ShouldLogPresence(string presence)
        {
            float now = Time.realtimeSinceStartup;
            if (presence == _lastPresenceSignature && now - _lastRefreshLogTime < 1.0f)
            {
                return false;
            }

            _lastPresenceSignature = presence;
            _lastRefreshLogTime = now;
            return true;
        }

        private static string BuildPresence()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("scene=").Append(SafeValue(() => SceneManager.GetActiveScene().name));
            sb.Append(" coopActive=").Append(SafeValue(() => CoopManager.CoopActive.ToString()));
            sb.Append(" playersCount=").Append(SafeValue(() => PlayerFarming.playersCount.ToString()));
            sb.Append(" playersListCount=").Append(SafeValue(() => PlayerFarming.players != null ? PlayerFarming.players.Count.ToString() : "null"));
            sb.Append(" location=").Append(SafeValue(() => PlayerFarming.Location.ToString()));
            sb.Append(" lmLocation=").Append(SafeValue(() => LocationManager._Instance != null ? LocationManager._Instance.Location.ToString() : "null"));
            sb.Append(" isDungeon=").Append(SafeValue(() => GameManager.IsDungeon(PlayerFarming.Location).ToString()));
            sb.Append(" preventWeaponSpawn=").Append(SafeValue(() => CoopManager.PreventWeaponSpawn.ToString()));
            sb.Append(" preventRecycleRoom=").Append(SafeValue(() => CoopManager.PreventRecycleInCurrentRoom.ToString()));
            sb.Append(" players=[").Append(DescribePlayers()).Append("]");
            return sb.ToString();
        }

        private static string BuildRoomState()
        {
            return "roomXY=(" + SafeValue(() => RoomManager.CurrentX.ToString()) + "," + SafeValue(() => RoomManager.CurrentY.ToString()) + ")"
                + " prevXY=(" + SafeValue(() => RoomManager.PrevCurrentX.ToString()) + "," + SafeValue(() => RoomManager.PrevCurrentY.ToString()) + ")"
                + " room=" + SafeValue(() => RoomManager.r != null ? RoomManager.r.x + "," + RoomManager.r.y + ":" + RoomManager.r.PrefabDir : "null")
                + " legacyWorldGen=" + SafeValue(() => WorldGen.Instance != null ? "available" : "unavailable")
                + " worldRooms=" + SafeValue(() => WorldGen.Instance != null && WorldGen.rooms != null ? WorldGen.rooms.Count.ToString() : "null");
        }

        private static string BuildRewardContext()
        {
            return "runWeaponLevel=" + SafeValue(() => DataManager.Instance != null ? DataManager.Instance.CurrentRunWeaponLevel.ToString() : "null")
                + " runCurseLevel=" + SafeValue(() => DataManager.Instance != null ? DataManager.Instance.CurrentRunCurseLevel.ToString() : "null")
                + " spawnedRelics=" + SafeValue(() => DataManager.Instance != null && DataManager.Instance.SpawnedRelicsThisRun != null ? DataManager.Instance.SpawnedRelicsThisRun.Count.ToString() : "null");
        }

        private static string DescribePlayers()
        {
            try
            {
                if (PlayerFarming.players == null)
                {
                    return "null";
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("; ");
                    }

                    PlayerFarming player = PlayerFarming.players[i];
                    if (player == null)
                    {
                        sb.Append(i).Append(":null");
                        continue;
                    }

                    sb.Append(i).Append(":");
                    sb.Append("id=").Append(SafeValue(() => player.playerID.ToString()));
                    sb.Append(",lamb=").Append(SafeValue(() => player.isLamb.ToString()));
                    sb.Append(",state=").Append(SafeValue(() => player.state != null ? player.state.CURRENT_STATE.ToString() : "null"));
                    sb.Append(",pos=").Append(WorldTrace.FormatVector(player.transform.position));
                    sb.Append(",weapon=").Append(SafeValue(() => player.currentWeapon + "@" + player.currentWeaponLevel));
                    sb.Append(",curse=").Append(SafeValue(() => player.currentCurse + "@" + player.currentCurseLevel));
                    sb.Append(",relic=").Append(SafeValue(() => player.currentRelicType.ToString()));
                    sb.Append(",hp=").Append(SafeValue(() => player.health != null ? WorldTrace.FormatFloat(player.health.HP) + "/" + WorldTrace.FormatFloat(player.health.totalHP) : "null"));
                    sb.Append(",active=").Append(SafeValue(() => player.gameObject.activeSelf.ToString()));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "describe_players_failed:" + ex.GetType().Name;
            }
        }

        private static string DescribeConnections(BiomeRoom room)
        {
            return "N:" + DescribeConnection(room.N_Room)
                + ",E:" + DescribeConnection(room.E_Room)
                + ",S:" + DescribeConnection(room.S_Room)
                + ",W:" + DescribeConnection(room.W_Room);
        }

        private static string DescribeConnection(RoomConnection connection)
        {
            if (connection == null)
            {
                return "null";
            }

            return connection.ConnectionType + "->" + (connection.Room != null ? connection.Room.x + "," + connection.Room.y : "null");
        }

        private static string ReadWeaponLevel(Interaction_WeaponSelectionPodium podium)
        {
            return SafeValue(() => WeaponLevelField != null ? WeaponLevelField.GetValue(podium).ToString() : "unknown");
        }

        private static void SafeRecord(string category, Func<string> buildMessage)
        {
            try
            {
                string message = buildMessage();
                if (!string.IsNullOrEmpty(message))
                {
                    WorldTrace.Record(category, message);
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(category + ".error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string Append(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right ?? "";
            }

            if (string.IsNullOrEmpty(right))
            {
                return left;
            }

            return left + " " + right;
        }

        private static string SafeValue(Func<string> read)
        {
            try
            {
                return read() ?? "null";
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }
        }

        private static object SafeObject(Func<object> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }
    }
}
