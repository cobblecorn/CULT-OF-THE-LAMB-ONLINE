using System;
using System.Globalization;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeEnemyAuthority
    {
        private static readonly object Sync = new object();
        private static BridgeEnemyAuthoritySnapshot _snapshot = BridgeEnemyAuthoritySnapshot.Empty;
        private static string _overlayLine = "enemy authority: waiting";

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeEnemyAuthoritySnapshot.Empty;
            }

            _overlayLine = "enemy authority: waiting";
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeEnemyAuthoritySnapshot snapshot = Parse(source, message);
            lock (Sync)
            {
                _snapshot = snapshot;
            }
        }

        public static string OverlayLine()
        {
            BridgeEnemyAuthoritySnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                return _overlayLine;
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            _overlayLine = "enemy authority: host="
                + snapshot.HostClientId
                + " room=" + snapshot.Room
                + " enemies=" + snapshot.Count
                + " match=" + snapshot.TargetMatch
                + " age=" + ageSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            return _overlayLine;
        }

        private static BridgeEnemyAuthoritySnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeEnemyAuthoritySnapshot Parse(string source, string message)
        {
            BridgeEnemyAuthoritySnapshot snapshot = new BridgeEnemyAuthoritySnapshot
            {
                ReceivedUtc = DateTimeOffset.UtcNow,
                Source = source ?? "unknown",
                TargetClientId = ReadToken(message, "target") ?? "unknown",
                HostClientId = ReadToken(message, "host") ?? "unknown",
                Room = ReadToken(message, "room") ?? "unknown",
                Hash = ReadToken(message, "hash") ?? "unknown",
                Count = ReadToken(message, "count") ?? "unknown",
                Rounds = ReadToken(message, "rounds") ?? "unknown",
                TargetMatch = ReadToken(message, "targetMatch") ?? "unknown",
                Mode = ReadToken(message, "mode") ?? "unknown",
                Preview = ReadToken(message, "preview") ?? "unknown"
            };
            return snapshot;
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
    }

    internal sealed class BridgeEnemyAuthoritySnapshot
    {
        public static readonly BridgeEnemyAuthoritySnapshot Empty = new BridgeEnemyAuthoritySnapshot
        {
            ReceivedUtc = DateTimeOffset.MinValue,
            Source = "none"
        };

        public DateTimeOffset ReceivedUtc { get; set; }
        public string Source { get; set; }
        public string TargetClientId { get; set; } = "unknown";
        public string HostClientId { get; set; } = "unknown";
        public string Room { get; set; } = "unknown";
        public string Hash { get; set; } = "unknown";
        public string Count { get; set; } = "unknown";
        public string Rounds { get; set; } = "unknown";
        public string TargetMatch { get; set; } = "unknown";
        public string Mode { get; set; } = "unknown";
        public string Preview { get; set; } = "unknown";
        public bool HasPacket { get { return ReceivedUtc != DateTimeOffset.MinValue; } }

        public BridgeEnemyAuthoritySnapshot Copy()
        {
            return (BridgeEnemyAuthoritySnapshot)MemberwiseClone();
        }
    }
}
