using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeWorldAuthority
    {
        private static bool _enabled;
        private static float _snapshotInterval;
        private static float _nextSnapshotAt;
        private static int _sequence;
        private static string _lastOverlayLine = "world authority: disabled";

        public static void Configure(bool enabled, float snapshotInterval)
        {
            _enabled = enabled;
            _snapshotInterval = Mathf.Max(0.5f, snapshotInterval);
            _nextSnapshotAt = 0f;
            _sequence = 0;
            _lastOverlayLine = enabled ? "world authority: waiting" : "world authority: disabled";

            WorldTrace.Record(
                "phase8.config",
                "worldAuthorityDiagnostics=" + enabled
                + " cultSnapshotInterval=" + WorldTrace.FormatFloat(_snapshotInterval));
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _lastOverlayLine = "world authority: disabled";
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextSnapshotAt)
            {
                return;
            }

            _nextSnapshotAt = now + _snapshotInterval;
            if (IsDungeonLocation())
            {
                _lastOverlayLine = "world authority: paused in dungeon";
                return;
            }

            RecordCultSnapshot(now);
        }

        public static string OverlayLine()
        {
            return _enabled ? _lastOverlayLine : "";
        }

        private static void RecordCultSnapshot(float now)
        {
            try
            {
                BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
                BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
                string role = self != null ? self.Role : "unknown";
                string worldMatch = self != null ? self.WorldMatch : "unknown";
                string saveSlot = self != null ? self.SaveSlot : "unknown";

                List<FollowerSnapshotRow> rows = CollectFollowerRows();
                string followerSignature = BuildFollowerSignature(rows);
                string followerHash = ShortHash(followerSignature);
                string preview = BuildFollowerPreview(rows, 8);
                string scene = SceneManager.GetActiveScene().name;
                string location = Convert.ToString(PlayerFarming.Location);
                float cultFaith = SafeFloat(() => CultFaithManager.CurrentFaith, -1f);
                float staticFaith = SafeFloat(() => CultFaithManager.StaticFaith, -1f);
                int currentDay = SafeInt(() => TimeManager.CurrentDay, -1);
                float elapsed = SafeFloat(() => TimeManager.TotalElapsedGameTime, -1f);
                int structures = SafeInt(() => DataManager.Instance != null && DataManager.Instance.BaseStructures != null ? DataManager.Instance.BaseStructures.Count : -1, -1);
                int dataFollowers = SafeInt(() => DataManager.Instance != null && DataManager.Instance.Followers != null ? DataManager.Instance.Followers.Count : -1, -1);
                int activeFollowers = CountActiveFollowers(rows);

                _sequence++;
                _lastOverlayLine = "world authority: role=" + role
                    + " cultHash=" + followerHash
                    + " followers=" + rows.Count
                    + " active=" + activeFollowers
                    + " match=" + worldMatch;

                WorldTrace.Record(
                    "sync.cult_snapshot",
                    "clientId=" + Clean(DiagnosticsPlugin.ClientId)
                    + " sessionId=" + Clean(DiagnosticsPlugin.SessionId)
                    + " seq=" + _sequence
                    + " scene=" + Clean(scene)
                    + " location=" + Clean(location)
                    + " role=" + Clean(role)
                    + " worldId=" + Clean(roster.WorldId)
                    + " worldHost=" + Clean(roster.WorldHost)
                    + " worldMatch=" + Clean(worldMatch)
                    + " saveSlot=" + Clean(saveSlot)
                    + " hash=" + Clean(followerHash)
                    + " followers=" + rows.Count
                    + " dataFollowers=" + dataFollowers
                    + " activeFollowers=" + activeFollowers
                    + " structures=" + structures
                    + " cultFaith=" + WorldTrace.FormatFloat(cultFaith)
                    + " staticFaith=" + WorldTrace.FormatFloat(staticFaith)
                    + " day=" + currentDay
                    + " elapsed=" + WorldTrace.FormatFloat(elapsed)
                    + " preview=" + Clean(preview)
                    + " unscaledTime=" + WorldTrace.FormatFloat(now));
            }
            catch (Exception ex)
            {
                _lastOverlayLine = "world authority: error " + ex.GetType().Name;
                WorldTrace.Record("sync.cult_snapshot.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static List<FollowerSnapshotRow> CollectFollowerRows()
        {
            List<FollowerSnapshotRow> rows = new List<FollowerSnapshotRow>();
            HashSet<int> seen = new HashSet<int>();

            if (FollowerBrain.AllBrains != null)
            {
                foreach (FollowerBrain brain in FollowerBrain.AllBrains)
                {
                    AddBrainRow(rows, seen, brain);
                }
            }

            if (DataManager.Instance != null && DataManager.Instance.Followers != null)
            {
                foreach (FollowerInfo info in DataManager.Instance.Followers)
                {
                    if (info == null || seen.Contains(info.ID))
                    {
                        continue;
                    }

                    AddInfoRow(rows, seen, info);
                }
            }

            rows.Sort((left, right) => left.Id.CompareTo(right.Id));
            return rows;
        }

        private static void AddBrainRow(List<FollowerSnapshotRow> rows, HashSet<int> seen, FollowerBrain brain)
        {
            if (brain == null || brain.Info == null)
            {
                return;
            }

            FollowerInfo info = brain._directInfoAccess;
            if (info == null)
            {
                return;
            }

            int id = info.ID;
            if (!seen.Add(id))
            {
                return;
            }

            Follower follower = SafeObj(() => FollowerManager.FindFollowerByID(id));
            Vector3 pos = follower != null ? follower.transform.position : brain.LastPosition;
            rows.Add(new FollowerSnapshotRow
            {
                Id = id,
                Name = CleanName(info.Name),
                Location = Convert.ToString(brain.Location),
                HomeLocation = Convert.ToString(brain.HomeLocation),
                Task = Convert.ToString(brain.CurrentTaskType),
                State = brain.CurrentTask != null ? Convert.ToString(brain.CurrentTask.State) : "none",
                Position = WorldTrace.FormatVector(pos),
                Faith = info.Faith,
                Happiness = info.Happiness,
                Satiation = info.Satiation,
                Age = info.Age,
                CursedState = Convert.ToString(info.CursedState),
                Active = follower != null && follower.gameObject != null && follower.gameObject.activeInHierarchy
            });
        }

        private static void AddInfoRow(List<FollowerSnapshotRow> rows, HashSet<int> seen, FollowerInfo info)
        {
            if (!seen.Add(info.ID))
            {
                return;
            }

            rows.Add(new FollowerSnapshotRow
            {
                Id = info.ID,
                Name = CleanName(info.Name),
                Location = Convert.ToString(info.Location),
                HomeLocation = Convert.ToString(info.HomeLocation),
                Task = Convert.ToString(info.SavedFollowerTaskType),
                State = "data",
                Position = WorldTrace.FormatVector(info.LastPosition),
                Faith = info.Faith,
                Happiness = info.Happiness,
                Satiation = info.Satiation,
                Age = info.Age,
                CursedState = Convert.ToString(info.CursedState),
                Active = false
            });
        }

        private static string BuildFollowerSignature(List<FollowerSnapshotRow> rows)
        {
            StringBuilder sb = new StringBuilder(rows.Count * 80);
            foreach (FollowerSnapshotRow row in rows)
            {
                sb.Append(row.Id)
                    .Append(":loc=").Append(row.Location)
                    .Append(":home=").Append(row.HomeLocation)
                    .Append(":task=").Append(row.Task)
                    .Append(":state=").Append(row.State)
                    .Append(":pos=").Append(row.Position)
                    .Append(":faith=").Append(Round(row.Faith))
                    .Append(":happy=").Append(Round(row.Happiness))
                    .Append(":sat=").Append(Round(row.Satiation))
                    .Append(":age=").Append(row.Age)
                    .Append(":curse=").Append(row.CursedState)
                    .Append(":active=").Append(row.Active ? "1" : "0")
                    .Append(";");
            }

            return sb.ToString();
        }

        private static string BuildFollowerPreview(List<FollowerSnapshotRow> rows, int limit)
        {
            StringBuilder sb = new StringBuilder(256);
            int count = Math.Min(limit, rows.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }

                FollowerSnapshotRow row = rows[i];
                sb.Append(row.Id)
                    .Append(":")
                    .Append(row.Task)
                    .Append("@")
                    .Append(row.Position)
                    .Append("/")
                    .Append(row.CursedState);
            }

            if (rows.Count > count)
            {
                sb.Append(",+").Append(rows.Count - count);
            }

            return sb.ToString();
        }

        private static int CountActiveFollowers(List<FollowerSnapshotRow> rows)
        {
            int count = 0;
            foreach (FollowerSnapshotRow row in rows)
            {
                if (row.Active)
                {
                    count++;
                }
            }

            return count;
        }

        private static BridgeRosterClient FindClient(BridgeRosterSnapshot roster, string clientId)
        {
            if (roster == null || roster.Clients == null || string.IsNullOrEmpty(clientId))
            {
                return null;
            }

            foreach (BridgeRosterClient client in roster.Clients)
            {
                if (string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
                {
                    return client;
                }
            }

            return null;
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

        private static string Round(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : Clean(value);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_").Replace("|", "_").Replace(",", "_");
        }

        private static int SafeInt(Func<int> read, int fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }

        private static float SafeFloat(Func<float> read, float fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
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

        private sealed class FollowerSnapshotRow
        {
            public int Id;
            public string Name;
            public string Location;
            public string HomeLocation;
            public string Task;
            public string State;
            public string Position;
            public float Faith;
            public float Happiness;
            public float Satiation;
            public int Age;
            public string CursedState;
            public bool Active;
        }
    }
}
