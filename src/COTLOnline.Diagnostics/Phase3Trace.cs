using System;
using System.Diagnostics;
using System.Text;
using MMBiomeGeneration;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class Phase3Trace
    {
        private static bool _enabled;

        public static void Configure(bool enabled)
        {
            _enabled = enabled;
            WorldTrace.Record("phase3.config", "authority=" + enabled);
        }

        public static void Record(string category, string detail)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord(category, () => Limit(Append(Append(detail, BuildRunContext()), BuildPlayerContext()), 8000));
        }

        public static string DescribeDataManager()
        {
            DataManager dataManager = SafeObject(() => DataManager.Instance) as DataManager;
            if (dataManager == null)
            {
                return "dataManager=null";
            }

            return "run=" + SafeValue(() => dataManager.dungeonRun.ToString())
                + " currentRunWeaponLevel=" + SafeValue(() => dataManager.CurrentRunWeaponLevel.ToString())
                + " currentRunCurseLevel=" + SafeValue(() => dataManager.CurrentRunCurseLevel.ToString())
                + " useDataManagerSeed=" + SafeValue(() => DataManager.UseDataManagerSeed.ToString())
                + " lastDungeonSeeds=[" + SafeValue(() => dataManager.LastDungeonSeeds != null ? string.Join(",", dataManager.LastDungeonSeeds) : "null") + "]"
                + " weaponPoolCount=" + SafeValue(() => dataManager.WeaponPool != null ? dataManager.WeaponPool.Count.ToString() : "null")
                + " cursePoolCount=" + SafeValue(() => dataManager.CursePool != null ? dataManager.CursePool.Count.ToString() : "null")
                + " spawnedRelics=" + SafeValue(() => dataManager.SpawnedRelicsThisRun != null ? dataManager.SpawnedRelicsThisRun.Count.ToString() : "null");
        }

        public static string DescribeBiomeGraph(BiomeGenerator biomeGenerator)
        {
            if (biomeGenerator == null)
            {
                return "biome=null";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(Phase2Trace.DescribeBiomeGenerator(biomeGenerator));
            sb.Append(" graph=[");
            try
            {
                if (biomeGenerator.Rooms == null)
                {
                    sb.Append("null");
                }
                else
                {
                    for (int i = 0; i < biomeGenerator.Rooms.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(" | ");
                        }

                        BiomeRoom room = biomeGenerator.Rooms[i];
                        sb.Append(i).Append(":").Append(DescribeCompactRoom(room));
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append("graph_failed:").Append(ex.GetType().Name);
            }

            sb.Append("]");
            return Limit(sb.ToString(), 8000);
        }

        public static string DescribeWeaponPickup(Interaction_WeaponPickUp pickup)
        {
            if (pickup == null)
            {
                return "weaponPickup=null";
            }

            return "weaponPickup=" + WorldTrace.DescribeGameObject(pickup.gameObject)
                + " equipment=" + SafeValue(() => pickup.TypeOfWeapon.ToString())
                + " level=" + SafeValue(() => pickup.WeaponLevel.ToString())
                + " type=" + SafeValue(() => pickup.Type.ToString());
        }

        public static string DescribePlayerEquipment(PlayerFarming player)
        {
            if (player == null)
            {
                return "player=null";
            }

            return "player=" + WorldTrace.DescribePlayer(player)
                + " lamb=" + SafeValue(() => player.isLamb.ToString())
                + " weapon=" + SafeValue(() => player.currentWeapon + "@" + player.currentWeaponLevel)
                + " weaponComponentLevel=" + SafeValue(() => player.playerWeapon != null ? player.playerWeapon.CurrentWeaponLevel.ToString() : "null")
                + " curse=" + SafeValue(() => player.currentCurse + "@" + player.currentCurseLevel)
                + " relic=" + SafeValue(() => player.currentRelicType.ToString())
                + " hp=" + SafeValue(() => player.health != null ? WorldTrace.FormatFloat(player.health.HP) + "/" + WorldTrace.FormatFloat(player.health.totalHP) : "null");
        }

        public static string DescribeStatePlayer(StateMachine state)
        {
            return DescribePlayerEquipment(SafeObject(() => state != null ? state.GetComponent<PlayerFarming>() : null) as PlayerFarming);
        }

        public static string DescribeStack(int skipFrames = 2)
        {
            try
            {
                StackTrace stackTrace = new StackTrace(skipFrames, false);
                StringBuilder sb = new StringBuilder();
                int max = Math.Min(8, stackTrace.FrameCount);
                for (int i = 0; i < max; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(" <- ");
                    }

                    var method = stackTrace.GetFrame(i).GetMethod();
                    sb.Append(method.DeclaringType != null ? method.DeclaringType.Name : "unknown");
                    sb.Append(".").Append(method.Name);
                }

                return "stack=" + sb;
            }
            catch (Exception ex)
            {
                return "stack=err:" + ex.GetType().Name;
            }
        }

        private static string BuildRunContext()
        {
            return "scene=" + SafeValue(() => SceneManager.GetActiveScene().name)
                + " location=" + SafeValue(() => PlayerFarming.Location.ToString())
                + " layer2=" + SafeValue(() => GameManager.Layer2.ToString())
                + " dungeonLayer=" + SafeValue(() => GameManager.CurrentDungeonLayer.ToString())
                + " dungeonFloor=" + SafeValue(() => GameManager.CurrentDungeonFloor.ToString())
                + " coopActive=" + SafeValue(() => CoopManager.CoopActive.ToString())
                + " preventWeaponSpawn=" + SafeValue(() => CoopManager.PreventWeaponSpawn.ToString())
                + " " + DescribeDataManager();
        }

        private static string BuildPlayerContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("playersCount=").Append(SafeValue(() => PlayerFarming.playersCount.ToString()));
            sb.Append(" players=[");
            try
            {
                if (PlayerFarming.players == null)
                {
                    sb.Append("null");
                }
                else
                {
                    for (int i = 0; i < PlayerFarming.players.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append("; ");
                        }

                        PlayerFarming player = PlayerFarming.players[i];
                        sb.Append(i).Append(":").Append(DescribePlayerEquipment(player));
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append("players_failed:").Append(ex.GetType().Name);
            }

            sb.Append("]");
            return Limit(sb.ToString(), 4000);
        }

        private static string DescribeCompactRoom(BiomeRoom room)
        {
            if (room == null)
            {
                return "null";
            }

            return "(" + SafeValue(() => room.x.ToString()) + "," + SafeValue(() => room.y.ToString()) + ")"
                + " seed=" + SafeValue(() => room.Seed.ToString())
                + " custom=" + SafeValue(() => room.IsCustom.ToString())
                + " boss=" + SafeValue(() => room.IsBoss.ToString())
                + " weapon=" + SafeValue(() => room.HasWeapon.ToString())
                + " generated=" + SafeValue(() => room.Generated.ToString())
                + " active=" + SafeValue(() => room.Active.ToString())
                + " path=" + SafeValue(() => room.GameObjectPath)
                + " con=" + SafeValue(() => CompactConnections(room));
        }

        private static string CompactConnections(BiomeRoom room)
        {
            return "N:" + CompactConnection(room.N_Room)
                + ",E:" + CompactConnection(room.E_Room)
                + ",S:" + CompactConnection(room.S_Room)
                + ",W:" + CompactConnection(room.W_Room);
        }

        private static string CompactConnection(RoomConnection connection)
        {
            if (connection == null)
            {
                return "null";
            }

            return connection.ConnectionType + ">" + (connection.Room != null ? connection.Room.x + "," + connection.Room.y : "null");
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

        private static string Limit(string value, int max)
        {
            if (value == null || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max) + "...truncated";
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
