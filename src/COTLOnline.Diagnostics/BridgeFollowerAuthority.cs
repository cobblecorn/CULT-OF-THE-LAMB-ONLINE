using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeFollowerAuthority
    {
        private static readonly object Sync = new object();
        private static BridgeFollowerAuthoritySnapshot _snapshot = BridgeFollowerAuthoritySnapshot.Empty;
        private static bool _enabled;
        private static bool _applyHostPositions;
        private static float _maxAgeMs;
        private static float _snapDistance;
        private static float _correctionSpeed;
        private static float _nextRecordAt;
        private static string _overlayLine = "follower authority: disabled";

        public static void Configure(bool enabled, bool applyHostPositions, float maxAgeMs, float snapDistance, float correctionSpeed)
        {
            _enabled = enabled;
            _applyHostPositions = applyHostPositions;
            _maxAgeMs = Mathf.Max(250f, maxAgeMs);
            _snapDistance = Mathf.Max(0.25f, snapDistance);
            _correctionSpeed = Mathf.Max(0f, correctionSpeed);
            _nextRecordAt = 0f;
            _overlayLine = enabled ? "follower authority: waiting" : "follower authority: disabled";
            Reset();
            WorldTrace.Record(
                "phase16.follower.config",
                "enabled=" + enabled
                + " applyHostPositions=" + applyHostPositions
                + " maxAgeMs=" + WorldTrace.FormatFloat(_maxAgeMs)
                + " snapDistance=" + WorldTrace.FormatFloat(_snapDistance)
                + " correctionSpeed=" + WorldTrace.FormatFloat(_correctionSpeed));
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeFollowerAuthoritySnapshot.Empty;
            }
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeFollowerAuthoritySnapshot snapshot = Parse(source, message);
            lock (Sync)
            {
                _snapshot = snapshot;
            }
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "follower authority: disabled";
                return;
            }

            BridgeFollowerAuthoritySnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                _overlayLine = "follower authority: waiting";
                return;
            }

            double packetAge = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalMilliseconds);
            if (packetAge > _maxAgeMs)
            {
                _overlayLine = "follower authority: stale ageMs=" + packetAge.ToString("0", CultureInfo.InvariantCulture)
                    + " count=" + snapshot.Followers.Length;
                return;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            bool remoteClient = self != null
                && (string.Equals(self.Role, "remote-p2", StringComparison.Ordinal)
                    || string.Equals(self.Role, "pending", StringComparison.Ordinal));

            int found = 0;
            int corrected = 0;
            float totalDistance = 0f;
            float maxDistance = 0f;

            if (_applyHostPositions && remoteClient && !IsDungeonLocation())
            {
                ApplyHostFollowerPositions(snapshot, out found, out corrected, out totalDistance, out maxDistance);
            }

            float avgDistance = found > 0 ? totalDistance / found : 0f;
            _overlayLine = "follower authority: host=" + snapshot.HostClientId
                + " count=" + snapshot.Followers.Length
                + " match=" + snapshot.TargetMatch
                + " found=" + found
                + " moved=" + corrected
                + " avg=" + WorldTrace.FormatFloat(avgDistance)
                + " max=" + WorldTrace.FormatFloat(maxDistance)
                + " age=" + (packetAge / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        private static void ApplyHostFollowerPositions(
            BridgeFollowerAuthoritySnapshot snapshot,
            out int found,
            out int corrected,
            out float totalDistance,
            out float maxDistance)
        {
            found = 0;
            corrected = 0;
            totalDistance = 0f;
            maxDistance = 0f;

            for (int i = 0; i < snapshot.Followers.Length; i++)
            {
                BridgeFollowerAuthorityEntry entry = snapshot.Followers[i];
                if (entry == null || !entry.Active || entry.Id < 0)
                {
                    continue;
                }

                Vector3 target;
                if (!TryParseVector(entry.Position, out target))
                {
                    continue;
                }

                Follower follower = SafeObj(() => FollowerManager.FindFollowerByID(entry.Id));
                if (follower == null || follower.gameObject == null || !follower.gameObject.activeInHierarchy)
                {
                    continue;
                }

                found++;
                Vector3 before = follower.transform.position;
                float distance = Vector3.Distance(before, target);
                totalDistance += distance;
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                }

                if (distance < 0.05f)
                {
                    continue;
                }

                Vector3 after = distance >= _snapDistance || _correctionSpeed <= 0f
                    ? target
                    : Vector3.MoveTowards(before, target, _correctionSpeed * Time.unscaledDeltaTime);
                follower.transform.position = after;
                corrected++;

                if (Time.unscaledTime >= _nextRecordAt && (distance >= _snapDistance || corrected <= 3))
                {
                    _nextRecordAt = Time.unscaledTime + 1.5f;
                    WorldTrace.Record(
                        "phase16.follower.position_apply",
                        "id=" + entry.Id
                        + " name=" + Clean(entry.Name)
                        + " task=" + Clean(entry.Task)
                        + " state=" + Clean(entry.State)
                        + " distance=" + WorldTrace.FormatFloat(distance)
                        + " before=" + Clean(WorldTrace.FormatVector(before))
                        + " after=" + Clean(WorldTrace.FormatVector(after))
                        + " target=" + Clean(entry.Position)
                        + " mode=" + (distance >= _snapDistance ? "snap" : "correct"));
                }
            }
        }

        private static BridgeFollowerAuthoritySnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeFollowerAuthoritySnapshot Parse(string source, string message)
        {
            BridgeFollowerAuthorityEntry[] followers = ParseFollowers(message);
            return new BridgeFollowerAuthoritySnapshot
            {
                ReceivedUtc = DateTimeOffset.UtcNow,
                Source = source ?? "unknown",
                ServerTime = ReadToken(message, "serverTime") ?? "unknown",
                TargetClientId = ReadToken(message, "target") ?? "unknown",
                WorldId = ReadToken(message, "worldId") ?? "none",
                HostClientId = ReadToken(message, "host") ?? "unknown",
                HostHash = ReadToken(message, "hash") ?? "unknown",
                TargetHash = ReadToken(message, "targetHash") ?? "unknown",
                TargetMatch = ReadToken(message, "targetMatch") ?? "unknown",
                Count = ReadToken(message, "count") ?? followers.Length.ToString(CultureInfo.InvariantCulture),
                Active = ReadToken(message, "active") ?? "unknown",
                AgeMs = ReadToken(message, "ageMs") ?? "unknown",
                Followers = followers
            };
        }

        private static BridgeFollowerAuthorityEntry[] ParseFollowers(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return new BridgeFollowerAuthorityEntry[0];
            }

            int index = message.IndexOf("followers=", StringComparison.Ordinal);
            if (index < 0)
            {
                return new BridgeFollowerAuthorityEntry[0];
            }

            string payload = message.Substring(index + "followers=".Length).Trim();
            if (payload.Length == 0 || string.Equals(payload, "none", StringComparison.OrdinalIgnoreCase))
            {
                return new BridgeFollowerAuthorityEntry[0];
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeFollowerAuthorityEntry> followers = new List<BridgeFollowerAuthorityEntry>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                BridgeFollowerAuthorityEntry follower = ParseFollower(entries[i]);
                if (follower.Id >= 0)
                {
                    followers.Add(follower);
                }
            }

            return followers.ToArray();
        }

        private static BridgeFollowerAuthorityEntry ParseFollower(string entry)
        {
            string[] parts = (entry ?? string.Empty).Split(new[] { '|' }, StringSplitOptions.None);
            BridgeFollowerAuthorityEntry follower = new BridgeFollowerAuthorityEntry
            {
                Id = ParseInt(parts.Length > 0 ? parts[0] : "", -1),
                Name = "unknown",
                Location = "unknown",
                HomeLocation = "unknown",
                Task = "unknown",
                State = "unknown",
                Position = "unknown",
                CursedState = "unknown",
                Active = false
            };

            for (int i = 1; i < parts.Length; i++)
            {
                int equals = parts[i].IndexOf('=');
                if (equals <= 0 || equals >= parts[i].Length - 1)
                {
                    continue;
                }

                string key = parts[i].Substring(0, equals);
                string value = parts[i].Substring(equals + 1);
                switch (key)
                {
                    case "name":
                        follower.Name = value;
                        break;
                    case "loc":
                        follower.Location = value;
                        break;
                    case "home":
                        follower.HomeLocation = value;
                        break;
                    case "task":
                        follower.Task = value;
                        break;
                    case "state":
                        follower.State = value;
                        break;
                    case "pos":
                        follower.Position = value;
                        break;
                    case "faith":
                        follower.Faith = value;
                        break;
                    case "happy":
                        follower.Happiness = value;
                        break;
                    case "sat":
                        follower.Satiation = value;
                        break;
                    case "age":
                        follower.Age = value;
                        break;
                    case "curse":
                        follower.CursedState = value;
                        break;
                    case "active":
                        follower.Active = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return follower;
        }

        private static bool TryParseVector(string value, out Vector3 vector)
        {
            vector = Vector3.zero;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string clean = value.Trim();
            if (clean.StartsWith("(", StringComparison.Ordinal) && clean.EndsWith(")", StringComparison.Ordinal))
            {
                clean = clean.Substring(1, clean.Length - 2);
            }

            string[] parts = clean.Split(',');
            if (parts.Length < 2)
            {
                return false;
            }

            float x;
            float y;
            float z = 0f;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                return false;
            }

            if (parts.Length > 2)
            {
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        private static BridgeRosterClient FindClient(BridgeRosterSnapshot roster, string clientId)
        {
            if (roster == null || roster.Clients == null || string.IsNullOrEmpty(clientId))
            {
                return null;
            }

            for (int i = 0; i < roster.Clients.Length; i++)
            {
                BridgeRosterClient client = roster.Clients[i];
                if (client != null && string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
                {
                    return client;
                }
            }

            return null;
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

        private static string ReadToken(string message, string key)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string prefix = key + "=";
            int start = message.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += prefix.Length;
            int end = message.IndexOf(' ', start);
            return end < 0 ? message.Substring(start) : message.Substring(start, end - start);
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
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

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_").Replace("|", "/").Replace(";", ",");
        }
    }

    internal sealed class BridgeFollowerAuthoritySnapshot
    {
        public static readonly BridgeFollowerAuthoritySnapshot Empty = new BridgeFollowerAuthoritySnapshot
        {
            ReceivedUtc = DateTimeOffset.MinValue,
            Source = "none",
            Followers = new BridgeFollowerAuthorityEntry[0]
        };

        public DateTimeOffset ReceivedUtc { get; set; }
        public string Source { get; set; }
        public string ServerTime { get; set; } = "unknown";
        public string TargetClientId { get; set; } = "unknown";
        public string WorldId { get; set; } = "none";
        public string HostClientId { get; set; } = "unknown";
        public string HostHash { get; set; } = "unknown";
        public string TargetHash { get; set; } = "unknown";
        public string TargetMatch { get; set; } = "unknown";
        public string Count { get; set; } = "unknown";
        public string Active { get; set; } = "unknown";
        public string AgeMs { get; set; } = "unknown";
        public BridgeFollowerAuthorityEntry[] Followers { get; set; } = new BridgeFollowerAuthorityEntry[0];
        public bool HasPacket { get { return ReceivedUtc != DateTimeOffset.MinValue; } }

        public BridgeFollowerAuthoritySnapshot Copy()
        {
            BridgeFollowerAuthorityEntry[] followers = new BridgeFollowerAuthorityEntry[Followers.Length];
            for (int i = 0; i < Followers.Length; i++)
            {
                followers[i] = Followers[i] != null ? Followers[i].Copy() : null;
            }

            return new BridgeFollowerAuthoritySnapshot
            {
                ReceivedUtc = ReceivedUtc,
                Source = Source,
                ServerTime = ServerTime,
                TargetClientId = TargetClientId,
                WorldId = WorldId,
                HostClientId = HostClientId,
                HostHash = HostHash,
                TargetHash = TargetHash,
                TargetMatch = TargetMatch,
                Count = Count,
                Active = Active,
                AgeMs = AgeMs,
                Followers = followers
            };
        }
    }

    internal sealed class BridgeFollowerAuthorityEntry
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public string HomeLocation { get; set; }
        public string Task { get; set; }
        public string State { get; set; }
        public string Position { get; set; }
        public string Faith { get; set; }
        public string Happiness { get; set; }
        public string Satiation { get; set; }
        public string Age { get; set; }
        public string CursedState { get; set; }
        public bool Active { get; set; }

        public BridgeFollowerAuthorityEntry Copy()
        {
            return new BridgeFollowerAuthorityEntry
            {
                Id = Id,
                Name = Name,
                Location = Location,
                HomeLocation = HomeLocation,
                Task = Task,
                State = State,
                Position = Position,
                Faith = Faith,
                Happiness = Happiness,
                Satiation = Satiation,
                Age = Age,
                CursedState = CursedState,
                Active = Active
            };
        }
    }
}
