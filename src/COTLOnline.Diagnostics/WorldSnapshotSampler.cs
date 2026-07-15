using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class WorldSnapshotSampler
    {
        private static readonly string[] PersistentKeywords =
        {
            "inventory",
            "follower",
            "structure",
            "resource",
            "coin",
            "gold",
            "money",
            "devotion",
            "faith",
            "hunger",
            "illness",
            "quest",
            "objective",
            "unlock",
            "fleece",
            "weapon",
            "curse",
            "relic",
            "tarot",
            "dungeon",
            "location",
            "season",
            "weather",
            "time",
            "day",
            "boss",
            "doctrine",
            "ritual",
            "save"
        };

        private static bool _enabled;
        private static bool _persistentPreview;
        private static bool _deepPersistentWorldHash;
        private static float _runtimeInterval = 1f;
        private static float _persistentInterval = 10f;
        private static float _saveFileHashInterval;
        private static float _nextRuntimeSnapshot;
        private static float _nextPersistentSnapshot;
        private static int _cachedSaveHashSlot = int.MinValue;
        private static string _cachedSaveHashMetadata = string.Empty;
        private static string _cachedSaveFileHash = "unknown";
        private static bool _cachedSaveFileHashReady;
        private static bool _cachedSaveFileHashDirty;
        private static float _nextSaveFileHashRefreshAt;

        public static void Configure(bool enabled, float runtimeInterval, float persistentInterval, bool persistentPreview, float saveFileHashInterval, bool deepPersistentWorldHash)
        {
            _enabled = enabled;
            _persistentPreview = persistentPreview;
            _deepPersistentWorldHash = deepPersistentWorldHash;
            _runtimeInterval = Mathf.Max(0.25f, runtimeInterval);
            _persistentInterval = Mathf.Max(30f, persistentInterval);
            _saveFileHashInterval = Mathf.Max(0f, saveFileHashInterval);
            _nextRuntimeSnapshot = 0f;
            _nextPersistentSnapshot = 0f;
            ResetSaveFileHashCache();

            WorldTrace.Record(
                "snapshot.config",
                "enabled=" + enabled
                + " runtimeInterval=" + WorldTrace.FormatFloat(_runtimeInterval)
                + " persistentInterval=" + WorldTrace.FormatFloat(_persistentInterval)
                + " persistentPreview=" + persistentPreview
                + " saveFileHashInterval=" + WorldTrace.FormatFloat(_saveFileHashInterval)
                + " deepPersistentWorldHash=" + deepPersistentWorldHash);
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= _nextRuntimeSnapshot)
            {
                _nextRuntimeSnapshot = now + _runtimeInterval;
                CaptureRuntimeSnapshot();
            }

            if (now >= _nextPersistentSnapshot)
            {
                _nextPersistentSnapshot = now + _persistentInterval;
                if (IsDungeonLocation())
                {
                    return;
                }

                CapturePersistentSnapshot();
            }
        }

        private static void CaptureRuntimeSnapshot()
        {
            try
            {
                StringBuilder sb = new StringBuilder(2048);
                Scene scene = SceneManager.GetActiveScene();

                sb.Append("scene=").Append(scene.name);
                sb.Append(" timeScale=").Append(WorldTrace.FormatFloat(Time.timeScale));
                sb.Append(" unscaledTime=").Append(WorldTrace.FormatFloat(Time.unscaledTime));
                sb.Append(" coopActive=").Append(CoopManager.CoopActive);
                sb.Append(" playerLocation=").Append(PlayerFarming.Location);
                sb.Append(" playersCount=").Append(PlayerFarming.playersCount);

                if (PlayerFarming.players != null)
                {
                    for (int i = 0; i < PlayerFarming.players.Count; i++)
                    {
                        sb.Append(" player[").Append(i).Append("]=").Append(WorldTrace.DescribePlayer(PlayerFarming.players[i]));
                    }
                }

                sb.Append(" healthAll=").Append(CountList(Health.allUnits));
                sb.Append(" healthPlayers=").Append(CountList(Health.playerTeam));
                sb.Append(" healthEnemies=").Append(CountList(Health.team2));
                sb.Append(" healthNeutral=").Append(CountList(Health.neutralTeam));
                sb.Append(" healthDormant=").Append(CountList(Health.dormant));
                sb.Append(" healthDangerousAnimals=").Append(CountList(Health.dangerousAnimals));

                WorldTrace.Record("snapshot.runtime", sb.ToString());
                SyncEventRecorder.RecordPlayerState(scene);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("snapshot.runtime.error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void CapturePersistentSnapshot()
        {
            try
            {
                DataManager data = DataManager.Instance;
                if (data == null)
                {
                    WorldTrace.Record("snapshot.persistent", "DataManager.Instance=null");
                    return;
                }

                int saveSlot = SafeInt(() => SaveAndLoad.SAVE_SLOT);
                string saveFileHash = ComputeSaveSlotFileHash(saveSlot);
                int run = SafeInt(() => data.dungeonRun);
                int followersCount = SafeInt(() => data.Followers != null ? data.Followers.Count : -1);
                int structuresCount = SafeInt(() => data.BaseStructures != null ? data.BaseStructures.Count : -1);
                string cultName = SafeString(() => data.CultName);
                string lastDungeonSeeds = SafeString(() => data.LastDungeonSeeds != null ? string.Join(",", data.LastDungeonSeeds.ToArray()) : "null");
                string summary;
                string hash;
                string preview;
                string mode;
                int selectedFields;

                if (_deepPersistentWorldHash)
                {
                    summary = BuildDataManagerSummary(data);
                    hash = ComputeHash(summary);
                    preview = summary.Length > 1800 ? summary.Substring(0, 1800) + "..." : summary;
                    selectedFields = CountSelectedFields(data);
                    mode = "deep";
                }
                else
                {
                    summary = BuildLightweightWorldSummary(saveSlot, saveFileHash, run, followersCount, structuresCount, cultName, lastDungeonSeeds);
                    hash = ComputeHash(summary);
                    preview = summary;
                    selectedFields = 6;
                    mode = "light";
                }

                WorldTrace.Record(
                    "snapshot.persistent",
                    "mode=" + mode
                    + " hash=" + hash
                    + " saveHash=" + saveFileHash
                    + " selectedFields=" + selectedFields
                    + (_persistentPreview ? " preview=" + preview : string.Empty));
                SyncEventRecorder.RecordWorldHash(hash, selectedFields);
                SyncEventRecorder.RecordWorldIdentity(
                    hash,
                    selectedFields,
                    saveSlot,
                    run,
                    followersCount,
                    structuresCount,
                    cultName,
                    lastDungeonSeeds,
                    saveFileHash);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("snapshot.persistent.error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string BuildLightweightWorldSummary(int saveSlot, string saveFileHash, int run, int followersCount, int structuresCount, string cultName, string lastDungeonSeeds)
        {
            return "slot=" + saveSlot
                + ";saveHash=" + (saveFileHash ?? "unknown")
                + ";run=" + run
                + ";followers=" + followersCount
                + ";structures=" + structuresCount
                + ";cultName=" + (cultName ?? "null")
                + ";lastDungeonSeeds=" + (lastDungeonSeeds ?? "null");
        }

        private static int CountList(ICollection list)
        {
            return list != null ? list.Count : -1;
        }

        private static string BuildDataManagerSummary(DataManager data)
        {
            StringBuilder sb = new StringBuilder(8192);
            FieldInfo[] fields = typeof(DataManager).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (FieldInfo field in fields)
            {
                if (!IsUsefulPersistentField(field))
                {
                    continue;
                }

                object value = null;
                try
                {
                    value = field.GetValue(data);
                }
                catch
                {
                    sb.Append(field.Name).Append("=<read_failed>;");
                    continue;
                }

                sb.Append(field.Name).Append("=").Append(SummarizeValue(value)).Append(";");
            }

            return sb.ToString();
        }

        private static int CountSelectedFields(DataManager data)
        {
            int count = 0;
            foreach (FieldInfo field in typeof(DataManager).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (IsUsefulPersistentField(field))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsUsefulPersistentField(FieldInfo field)
        {
            string name = field.Name.ToLowerInvariant();
            foreach (string keyword in PersistentKeywords)
            {
                if (name.Contains(keyword))
                {
                    return true;
                }
            }

            Type type = field.FieldType;
            return type.IsPrimitive || type.IsEnum || type == typeof(string);
        }

        private static string SummarizeValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is string text)
            {
                return Quote(Trim(text, 96));
            }

            if (value is IList list)
            {
                return "list(count=" + list.Count + ",sample=" + ListSample(list) + ")";
            }

            if (value is IDictionary dictionary)
            {
                return "dict(count=" + dictionary.Count + ")";
            }

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return type.Name;
        }

        private static string ListSample(IList list)
        {
            int max = Math.Min(3, list.Count);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < max; i++)
            {
                if (i > 0)
                {
                    sb.Append("|");
                }

                object item = list[i];
                sb.Append(item != null ? Trim(item.ToString(), 80) : "null");
            }

            return Quote(sb.ToString());
        }

        private static string ComputeHash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private static string ComputeSaveSlotFileHash(int saveSlot)
        {
            try
            {
                if (saveSlot < 0)
                {
                    return "none";
                }

                bool hasFiles;
                string metadata = BuildSaveSlotFileMetadata(saveSlot, out hasFiles);
                if (!hasFiles)
                {
                    ResetSaveFileHashCache();
                    return "none";
                }

                float now = Time.unscaledTime;
                bool slotChanged = saveSlot != _cachedSaveHashSlot;
                bool metadataChanged = !string.Equals(metadata, _cachedSaveHashMetadata, StringComparison.Ordinal);
                bool shouldHashNow = !_cachedSaveFileHashReady
                    || slotChanged
                    || (_saveFileHashInterval > 0f && now >= _nextSaveFileHashRefreshAt);

                if (shouldHashNow)
                {
                    _cachedSaveFileHash = ComputeFullSaveSlotFileHash(saveSlot);
                    _cachedSaveHashSlot = saveSlot;
                    _cachedSaveHashMetadata = metadata;
                    _cachedSaveFileHashReady = true;
                    _cachedSaveFileHashDirty = false;
                    _nextSaveFileHashRefreshAt = _saveFileHashInterval > 0f
                        ? now + _saveFileHashInterval
                        : float.PositiveInfinity;
                    return _cachedSaveFileHash;
                }

                if (metadataChanged)
                {
                    _cachedSaveHashSlot = saveSlot;
                    _cachedSaveHashMetadata = metadata;
                    _cachedSaveFileHashDirty = true;
                }

                return _cachedSaveFileHashDirty ? _cachedSaveFileHash + "~dirty" : _cachedSaveFileHash;
            }
            catch (Exception ex)
            {
                return "err_" + ex.GetType().Name;
            }
        }

        private static void ResetSaveFileHashCache()
        {
            _cachedSaveHashSlot = int.MinValue;
            _cachedSaveHashMetadata = string.Empty;
            _cachedSaveFileHash = "unknown";
            _cachedSaveFileHashReady = false;
            _cachedSaveFileHashDirty = false;
            _nextSaveFileHashRefreshAt = 0f;
        }

        private static string BuildSaveSlotFileMetadata(int saveSlot, out bool hasFiles)
        {
            hasFiles = false;
            string savesDirectory = Path.Combine(Application.persistentDataPath, "saves");
            StringBuilder summary = new StringBuilder();
            foreach (string fileName in SaveSlotFileNames(saveSlot))
            {
                string path = Path.Combine(savesDirectory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                FileInfo info = new FileInfo(path);
                hasFiles = true;
                summary.Append(fileName)
                    .Append(":")
                    .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(":")
                    .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                    .Append(";");
            }

            return summary.ToString();
        }

        private static string ComputeFullSaveSlotFileHash(int saveSlot)
        {
            string savesDirectory = Path.Combine(Application.persistentDataPath, "saves");
            StringBuilder summary = new StringBuilder();
            foreach (string fileName in SaveSlotFileNames(saveSlot))
            {
                string path = Path.Combine(savesDirectory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                FileInfo info = new FileInfo(path);
                summary.Append(fileName)
                    .Append(":")
                    .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(":")
                    .Append(ComputeFileHash(path))
                    .Append(";");
            }

            return summary.Length > 0 ? ComputeHash(summary.ToString()) : "none";
        }

        private static string[] SaveSlotFileNames(int saveSlot)
        {
            return new[]
            {
                "slot_" + saveSlot + ".mp",
                "meta_" + saveSlot + ".mp",
                "slot_" + saveSlot + ".json",
                "meta_" + saveSlot + ".json"
            };
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

        private static string ComputeFileHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] hash = sha.ComputeHash(stream);
                return ShortHash(hash);
            }
        }

        private static string ShortHash(byte[] hash)
        {
            StringBuilder sb = new StringBuilder(16);
            for (int i = 0; i < 8 && i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "'") + "\"";
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
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

        private static string SafeString(Func<string> read)
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
    }
}
