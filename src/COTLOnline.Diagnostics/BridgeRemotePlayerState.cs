using System;
using System.Collections.Generic;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemotePlayerState
    {
        private static readonly object Sync = new object();
        private static BridgeRemotePlayerSnapshot _snapshot = BridgeRemotePlayerSnapshot.Empty;

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeRemotePlayerSnapshot.Empty;
            }
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeRemotePlayer[] players = ParsePlayers(message);
            lock (Sync)
            {
                _snapshot = new BridgeRemotePlayerSnapshot(
                    DateTimeOffset.UtcNow,
                    source ?? "unknown",
                    ReadToken(message, "serverTime") ?? "unknown",
                    ReadToken(message, "target") ?? "unknown",
                    ReadToken(message, "worldId") ?? "none",
                    ReadToken(message, "remoteCount") ?? players.Length.ToString(),
                    players);
            }
        }

        public static BridgeRemotePlayerSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeRemotePlayer[] ParsePlayers(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeRemotePlayer>();
            }

            int playersIndex = message.IndexOf("players=", StringComparison.Ordinal);
            if (playersIndex < 0)
            {
                return Array.Empty<BridgeRemotePlayer>();
            }

            string payload = message.Substring(playersIndex + "players=".Length).Trim();
            if (payload.Length == 0 || payload == "none")
            {
                return Array.Empty<BridgeRemotePlayer>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeRemotePlayer> players = new List<BridgeRemotePlayer>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeRemotePlayer player = ParsePlayer(entry.Trim());
                if (!string.IsNullOrEmpty(player.ClientId))
                {
                    players.Add(player);
                }
            }

            return players.ToArray();
        }

        private static BridgeRemotePlayer ParsePlayer(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeRemotePlayer player = new BridgeRemotePlayer
            {
                ClientId = parts.Length > 0 ? parts[0] : "unknown",
                SessionId = "unknown",
                PlayerId = "unknown",
                Role = "unknown",
                HostSlot = "unknown",
                Sequence = "unknown",
                Name = "unknown",
                Position = "unknown",
                State = "unknown",
                HitPoints = "unknown",
                Scene = "unknown",
                Location = "unknown",
                Room = "unknown",
                AgeMs = "unknown"
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
                    case "session":
                        player.SessionId = value;
                        break;
                    case "playerID":
                        player.PlayerId = value;
                        break;
                    case "role":
                        player.Role = value;
                        break;
                    case "hostSlot":
                        player.HostSlot = value;
                        break;
                    case "seq":
                        player.Sequence = value;
                        break;
                    case "name":
                        player.Name = value;
                        break;
                    case "pos":
                        player.Position = value;
                        break;
                    case "state":
                        player.State = value;
                        break;
                    case "hp":
                        player.HitPoints = value;
                        break;
                    case "scene":
                        player.Scene = value;
                        break;
                    case "location":
                        player.Location = value;
                        break;
                    case "room":
                        player.Room = value;
                        break;
                    case "ageMs":
                        player.AgeMs = value;
                        break;
                }
            }

            return player;
        }

        private static string ReadToken(string message, string key)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = key + "=";
            int start = message.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += pattern.Length;
            int end = message.IndexOf(' ', start);
            if (end < 0)
            {
                end = message.Length;
            }

            return message.Substring(start, end - start);
        }
    }

    internal sealed class BridgeRemotePlayerSnapshot
    {
        public static readonly BridgeRemotePlayerSnapshot Empty = new BridgeRemotePlayerSnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "unknown",
            "none",
            "0",
            Array.Empty<BridgeRemotePlayer>());

        public BridgeRemotePlayerSnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string targetClientId, string worldId, string remoteCount, BridgeRemotePlayer[] players)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            TargetClientId = targetClientId;
            WorldId = worldId;
            RemoteCount = remoteCount;
            Players = players ?? Array.Empty<BridgeRemotePlayer>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string TargetClientId { get; }
        public string WorldId { get; }
        public string RemoteCount { get; }
        public BridgeRemotePlayer[] Players { get; }
        public bool HasPacket => ReceivedUtc != DateTimeOffset.MinValue;

        public BridgeRemotePlayerSnapshot Copy()
        {
            BridgeRemotePlayer[] players = new BridgeRemotePlayer[Players.Length];
            for (int i = 0; i < Players.Length; i++)
            {
                players[i] = Players[i].Copy();
            }

            return new BridgeRemotePlayerSnapshot(ReceivedUtc, Source, ServerTime, TargetClientId, WorldId, RemoteCount, players);
        }
    }

    internal sealed class BridgeRemotePlayer
    {
        public string ClientId { get; set; }
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string Role { get; set; }
        public string HostSlot { get; set; }
        public string Sequence { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }
        public string State { get; set; }
        public string HitPoints { get; set; }
        public string Scene { get; set; }
        public string Location { get; set; }
        public string Room { get; set; }
        public string AgeMs { get; set; }

        public BridgeRemotePlayer Copy()
        {
            return new BridgeRemotePlayer
            {
                ClientId = ClientId,
                SessionId = SessionId,
                PlayerId = PlayerId,
                Role = Role,
                HostSlot = HostSlot,
                Sequence = Sequence,
                Name = Name,
                Position = Position,
                State = State,
                HitPoints = HitPoints,
                Scene = Scene,
                Location = Location,
                Room = Room,
                AgeMs = AgeMs
            };
        }
    }
}
