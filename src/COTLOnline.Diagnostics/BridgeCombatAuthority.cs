using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MMBiomeGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeCombatAuthority
    {
        private static bool _enabled;
        private static float _rosterInterval;
        private static float _nextRosterAt;
        private static int _rosterSequence;
        private static int _spawnSequence;
        private static int _eventSequence;
        private static int _encounterSequence;
        private static string _lastOverlayLine = "combat authority: disabled";

        public static void Configure(bool enabled, float rosterInterval)
        {
            _enabled = enabled;
            _rosterInterval = Mathf.Max(0.25f, rosterInterval);
            _nextRosterAt = 0f;
            _rosterSequence = 0;
            _spawnSequence = 0;
            _eventSequence = 0;
            _encounterSequence = 0;
            _lastOverlayLine = enabled ? "combat authority: waiting" : "combat authority: disabled";

            WorldTrace.Record(
                "phase13.config",
                "combatAuthorityDiagnostics=" + enabled
                + " rosterInterval=" + WorldTrace.FormatFloat(_rosterInterval));
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _lastOverlayLine = "combat authority: disabled";
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextRosterAt)
            {
                return;
            }

            _nextRosterAt = now + _rosterInterval;
            if (!IsDungeonLocation())
            {
                _lastOverlayLine = "combat authority: idle outside dungeon";
                return;
            }

            RecordCombatRoster(now);
        }

        public static string OverlayLine()
        {
            return _enabled ? _lastOverlayLine : "";
        }

        public static string CurrentRoomKey()
        {
            try
            {
                return RoomKey();
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }
        }

        public static void RecordEnemySpawn(string source, Vector3 position, Transform parent, GameObject prefab, GameObject result)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                _spawnSequence++;
                Vector3 resultPosition = result != null ? result.transform.position : position;
                WorldTrace.Record(
                    "sync.combat_spawn",
                    Identity()
                    + " seq=" + _spawnSequence
                    + " scene=" + Clean(SceneManager.GetActiveScene().name)
                    + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                    + " room=" + Clean(RoomKey())
                    + " source=" + Clean(source)
                    + " prefab=" + Clean(DescribeGameObject(prefab))
                    + " result=" + Clean(DescribeGameObject(result))
                    + " resultId=" + RuntimeId(result)
                    + " parent=" + Clean(parent != null ? parent.name : "null")
                    + " pos=" + WorldTrace.FormatVector(resultPosition)
                    + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.combat_spawn.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        public static void RecordEncounterIsland(string phase, object generator, object selected)
        {
            if (!_enabled || !IsDungeonLocation())
            {
                return;
            }

            try
            {
                _encounterSequence++;
                string piece = DescribeIslandPiece(selected);
                string encounters = DescribeEncounterList(selected, 10);
                string choices = DescribeStartPieces(generator, 8);
                string signature = piece + "|" + encounters;

                WorldTrace.Record(
                    "sync.encounter_island",
                    Identity()
                    + " seq=" + _encounterSequence
                    + " scene=" + Clean(SceneManager.GetActiveScene().name)
                    + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                    + " room=" + Clean(RoomKey())
                    + " phase=" + Clean(phase)
                    + " pieceHash=" + Clean(ShortHash(signature))
                    + " piece=" + Clean(piece)
                    + " choices=" + Clean(choices)
                    + " encounters=" + Clean(encounters)
                    + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.encounter_island.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        public static void RecordEncounterChance(string phase, object encounter)
        {
            if (!_enabled || !IsDungeonLocation())
            {
                return;
            }

            try
            {
                _encounterSequence++;
                GameObject root = GetGameObject(encounter);
                int unitCount;
                string unitPreview = DescribeUnits(root, 10, out unitCount);
                string unitSignature = BuildUnitSignature(root);

                WorldTrace.Record(
                    "sync.encounter_chance",
                    Identity()
                    + " seq=" + _encounterSequence
                    + " scene=" + Clean(SceneManager.GetActiveScene().name)
                    + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                    + " room=" + Clean(RoomKey())
                    + " phase=" + Clean(phase)
                    + " source=" + Clean(DescribeGameObject(root))
                    + " sourceId=" + RuntimeId(root)
                    + " units=" + unitCount
                    + " unitHash=" + Clean(ShortHash(unitSignature))
                    + " preview=" + Clean(unitPreview)
                    + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.encounter_chance.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        public static void RecordManipulation(string phase, WorldManipulatorManager.Manipulations manipulation, float delay, bool twitch)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                _eventSequence++;
                WorldTrace.Record(
                    "sync.world_manipulation",
                    Identity()
                    + " seq=" + _eventSequence
                    + " scene=" + Clean(SceneManager.GetActiveScene().name)
                    + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                    + " room=" + Clean(RoomKey())
                    + " phase=" + Clean(phase)
                    + " manipulation=" + Clean(Convert.ToString(manipulation, CultureInfo.InvariantCulture))
                    + " delay=" + WorldTrace.FormatFloat(delay)
                    + " twitch=" + twitch
                    + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.world_manipulation.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void RecordCombatRoster(float now)
        {
            try
            {
                List<CombatRow> rows = CollectCombatRows();
                rows.Sort((left, right) => string.CompareOrdinal(left.Signature, right.Signature));
                string signature = BuildSignature(rows);
                string hash = ShortHash(signature);
                string preview = BuildPreview(rows, 8);
                EnemyRoundsBase rounds = SafeObj(() => EnemyRoundsBase.Instance);
                string roundsText = rounds != null
                    ? SafeInt(() => rounds.CurrentRound) + "/" + SafeInt(() => rounds.TotalRounds) + "/" + SafeBool(() => rounds.Completed)
                    : "none";

                _rosterSequence++;
                _lastOverlayLine = "combat authority: enemies=" + rows.Count
                    + " hash=" + hash
                    + " room=" + RoomKey();

                WorldTrace.Record(
                    "sync.combat_roster",
                    Identity()
                    + " seq=" + _rosterSequence
                    + " scene=" + Clean(SceneManager.GetActiveScene().name)
                    + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                    + " room=" + Clean(RoomKey())
                    + " count=" + rows.Count
                    + " hash=" + Clean(hash)
                    + " rounds=" + Clean(roundsText)
                    + " roomActive=" + SafeBool(() => GameManager.RoomActive)
                    + " doorsOpen=" + SafeBool(() => RoomLockController.DoorsOpen)
                    + " preview=" + Clean(preview)
                    + " unscaledTime=" + WorldTrace.FormatFloat(now));
            }
            catch (Exception ex)
            {
                _lastOverlayLine = "combat authority: error " + ex.GetType().Name;
                WorldTrace.Record("sync.combat_roster.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static List<CombatRow> CollectCombatRows()
        {
            List<CombatRow> rows = new List<CombatRow>();
            if (Health.allUnits == null)
            {
                return rows;
            }

            foreach (Health health in Health.allUnits)
            {
                if (!IsCombatRelevant(health))
                {
                    continue;
                }

                UnitObject unit = health.GetComponent<UnitObject>();
                GameObject obj = health.gameObject;
                Vector3 pos = obj != null ? obj.transform.position : Vector3.zero;
                string type = unit != null ? Convert.ToString(unit.EnemyType, CultureInfo.InvariantCulture) : "unit";
                string row = RuntimeId(obj)
                    + ":" + Clean(type)
                    + ":" + Clean(obj != null ? obj.name : "null")
                    + ":team=" + health.team
                    + ":hp=" + Round(health.HP) + "/" + Round(health.totalHP)
                    + ":pos=" + Round(pos.x) + "," + Round(pos.y)
                    + ":active=" + (obj != null && obj.activeInHierarchy ? "1" : "0")
                    + ":charmed=" + (SafeBool(() => health.IsCharmedEnemy) ? "1" : "0");

                rows.Add(new CombatRow
                {
                    Id = RuntimeId(obj),
                    Name = obj != null ? obj.name : "null",
                    Type = type,
                    Team = Convert.ToString(health.team, CultureInfo.InvariantCulture),
                    HitPoints = Round(health.HP) + "/" + Round(health.totalHP),
                    Position = WorldTrace.FormatVector(pos),
                    Signature = row
                });
            }

            return rows;
        }

        private static bool IsCombatRelevant(Health health)
        {
            if (health == null || health.gameObject == null)
            {
                return false;
            }

            if (!health.gameObject.activeInHierarchy || health.HP <= 0f)
            {
                return false;
            }

            return health.team == Health.Team.Team2
                || health.team == Health.Team.KillAll
                || health.team == Health.Team.DangerousAnimals
                || SafeBool(() => health.IsCharmedEnemy);
        }

        private static string BuildSignature(List<CombatRow> rows)
        {
            StringBuilder sb = new StringBuilder(rows.Count * 64);
            foreach (CombatRow row in rows)
            {
                sb.Append(row.Signature).Append(";");
            }

            return sb.ToString();
        }

        private static string BuildPreview(List<CombatRow> rows, int limit)
        {
            StringBuilder sb = new StringBuilder(256);
            int count = Math.Min(limit, rows.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }

                CombatRow row = rows[i];
                sb.Append(row.Id)
                    .Append(":")
                    .Append(row.Type)
                    .Append("@")
                    .Append(row.Position)
                    .Append("/")
                    .Append(row.HitPoints);
            }

            if (rows.Count > count)
            {
                sb.Append(",+").Append(rows.Count - count);
            }

            return sb.ToString();
        }

        private static string RoomKey()
        {
            string roomManager = RoomManager.CurrentX + "," + RoomManager.CurrentY;
            string navigate = NavigateRooms.CurrentX + "," + NavigateRooms.CurrentY;
            string biome = "none";
            try
            {
                if (BiomeGenerator.Instance != null && BiomeGenerator.Instance.CurrentRoom != null)
                {
                    biome = BiomeGenerator.Instance.CurrentRoom.x
                        + "," + BiomeGenerator.Instance.CurrentRoom.y
                        + "#" + BiomeGenerator.Instance.CurrentRoom.Seed;
                }
            }
            catch
            {
                biome = "err";
            }

            return "rm=" + roomManager + ":nav=" + navigate + ":bio=" + biome;
        }

        private static string DescribeIslandPiece(object piece)
        {
            if (piece == null)
            {
                return "null";
            }

            string type = piece.GetType().Name;
            string name = DescribeObjectName(piece);
            string path = ReadStringMember(piece, "GameObjectPath");
            string collider = DescribeCollider(ReadMember(piece, "Collider"));
            int encounterCount = CountEnumerable(ReadMember(ReadMember(piece, "Encounters"), "ObjectList"));
            return type
                + ":" + name
                + ":path=" + path
                + ":encounters=" + encounterCount
                + ":collider=" + collider;
        }

        private static string DescribeStartPieces(object generator, int limit)
        {
            object list = ReadMember(generator, "StartPieces");
            if (list == null)
            {
                return "count=0,hash=none,preview=none";
            }

            StringBuilder signature = new StringBuilder(512);
            StringBuilder preview = new StringBuilder(256);
            int count = 0;
            foreach (object item in Enumerate(list))
            {
                string piece = DescribeIslandPiece(item);
                signature.Append(piece).Append(";");
                if (count < limit)
                {
                    if (preview.Length > 0)
                    {
                        preview.Append(",");
                    }

                    preview.Append(ShortPiece(piece));
                }

                count++;
            }

            if (count > limit)
            {
                preview.Append(",+").Append(count - limit);
            }

            return "count=" + count + ",hash=" + ShortHash(signature.ToString()) + ",preview=" + preview;
        }

        private static string DescribeEncounterList(object piece, int limit)
        {
            object list = ReadMember(ReadMember(piece, "Encounters"), "ObjectList");
            if (list == null)
            {
                return "count=0,hash=none,preview=none";
            }

            StringBuilder signature = new StringBuilder(512);
            StringBuilder preview = new StringBuilder(384);
            int count = 0;
            foreach (object item in Enumerate(list))
            {
                string path = ReadStringMember(item, "GameObjectPath");
                string layers = LayerFlags(item);
                string prob = ReadStringMember(item, "Probability");
                string entry = count + ":" + path + ":layers=" + layers + ":prob=" + prob;
                signature.Append(entry).Append(";");
                if (count < limit)
                {
                    if (preview.Length > 0)
                    {
                        preview.Append(",");
                    }

                    preview.Append(entry);
                }

                count++;
            }

            if (count > limit)
            {
                preview.Append(",+").Append(count - limit);
            }

            return "count=" + count + ",hash=" + ShortHash(signature.ToString()) + ",preview=" + preview;
        }

        private static string BuildUnitSignature(GameObject root)
        {
            if (root == null)
            {
                return "null";
            }

            StringBuilder sb = new StringBuilder(512);
            UnitObject[] units = root.GetComponentsInChildren<UnitObject>(true);
            for (int i = 0; i < units.Length; i++)
            {
                UnitObject unit = units[i];
                if (unit == null || unit.gameObject == null)
                {
                    continue;
                }

                Health health = unit.GetComponent<Health>();
                Vector3 local = unit.transform.localPosition;
                sb.Append(Convert.ToString(unit.EnemyType, CultureInfo.InvariantCulture))
                    .Append(":")
                    .Append(unit.gameObject.name)
                    .Append(":team=")
                    .Append(health != null ? Convert.ToString(health.team, CultureInfo.InvariantCulture) : "none")
                    .Append(":hp=")
                    .Append(health != null ? Round(health.HP) + "/" + Round(health.totalHP) : "none")
                    .Append(":local=")
                    .Append(Round(local.x))
                    .Append(",")
                    .Append(Round(local.y))
                    .Append(":active=")
                    .Append(unit.gameObject.activeSelf ? "1" : "0")
                    .Append(";");
            }

            return sb.ToString();
        }

        private static string DescribeUnits(GameObject root, int limit, out int count)
        {
            count = 0;
            if (root == null)
            {
                return "none";
            }

            StringBuilder sb = new StringBuilder(384);
            UnitObject[] units = root.GetComponentsInChildren<UnitObject>(true);
            for (int i = 0; i < units.Length; i++)
            {
                UnitObject unit = units[i];
                if (unit == null || unit.gameObject == null)
                {
                    continue;
                }

                Health health = unit.GetComponent<Health>();
                if (count < limit)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(",");
                    }

                    Vector3 pos = unit.transform.position;
                    sb.Append(count)
                        .Append(":")
                        .Append(Convert.ToString(unit.EnemyType, CultureInfo.InvariantCulture))
                        .Append(":")
                        .Append(unit.gameObject.name)
                        .Append("@")
                        .Append(WorldTrace.FormatVector(pos))
                        .Append("/")
                        .Append(health != null ? Round(health.HP) + "/" + Round(health.totalHP) : "none")
                        .Append("/")
                        .Append(unit.gameObject.activeSelf ? "active" : "inactive");
                }

                count++;
            }

            if (count > limit)
            {
                sb.Append(",+").Append(count - limit);
            }

            return sb.Length > 0 ? sb.ToString() : "none";
        }

        private static string ShortPiece(string piece)
        {
            if (string.IsNullOrEmpty(piece))
            {
                return "unknown";
            }

            return piece.Length <= 80 ? piece : piece.Substring(0, 80);
        }

        private static string LayerFlags(object item)
        {
            return (ReadBoolMember(item, "LayerOne") ? "1" : "-")
                + (ReadBoolMember(item, "LayerTwo") ? "2" : "-")
                + (ReadBoolMember(item, "LayerThree") ? "3" : "-")
                + (ReadBoolMember(item, "LayerFour") ? "4" : "-");
        }

        private static string DescribeCollider(object collider)
        {
            PolygonCollider2D poly = collider as PolygonCollider2D;
            if (poly == null)
            {
                return collider != null ? collider.GetType().Name : "none";
            }

            Bounds bounds = poly.bounds;
            return Round(bounds.size.x) + "x" + Round(bounds.size.y);
        }

        private static GameObject GetGameObject(object source)
        {
            Component component = source as Component;
            if (component != null)
            {
                return component.gameObject;
            }

            GameObject gameObject = source as GameObject;
            if (gameObject != null)
            {
                return gameObject;
            }

            return ReadMember(source, "gameObject") as GameObject;
        }

        private static string DescribeObjectName(object value)
        {
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            if (unityObject != null)
            {
                return unityObject.name;
            }

            string name = ReadStringMember(value, "name");
            return !string.IsNullOrEmpty(name) ? name : value.GetType().Name;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                Type type = target.GetType();
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(target, null);
                }

                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ReadStringMember(object target, string name)
        {
            object value = ReadMember(target, name);
            if (value == null)
            {
                return "none";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "none";
        }

        private static bool ReadBoolMember(object target, string name)
        {
            object value = ReadMember(target, name);
            return value is bool flag && flag;
        }

        private static int CountEnumerable(object value)
        {
            int count = 0;
            foreach (object _ in Enumerate(value))
            {
                count++;
            }

            return count;
        }

        private static IEnumerable<object> Enumerate(object value)
        {
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (object item in enumerable)
            {
                yield return item;
            }
        }

        private static bool IsDungeonLocation()
        {
            try
            {
                return GameManager.IsDungeon(PlayerFarming.Location);
            }
            catch
            {
                return false;
            }
        }

        private static string Identity()
        {
            return "clientId=" + Clean(DiagnosticsPlugin.ClientId) + " sessionId=" + Clean(DiagnosticsPlugin.SessionId);
        }

        private static string DescribeGameObject(GameObject obj)
        {
            return obj != null ? obj.name : "null";
        }

        private static int RuntimeId(GameObject obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
        }

        private static string ShortHash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8 && i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private static string Round(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static bool SafeBool(Func<bool> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInt(Func<int> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return -1;
            }
        }

        private static T SafeObj<T>(Func<T> read) where T : class
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

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            return value
                .Replace(" ", "_")
                .Replace("\t", "_")
                .Replace("\r", "_")
                .Replace("\n", "_")
                .Replace("|", "/")
                .Replace(";", ",");
        }

        private sealed class CombatRow
        {
            public int Id;
            public string Name;
            public string Type;
            public string Team;
            public string HitPoints;
            public string Position;
            public string Signature;
        }
    }
}
