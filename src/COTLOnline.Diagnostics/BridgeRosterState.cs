using System;
using System.Collections.Generic;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRosterState
    {
        private static readonly object Sync = new object();
        private static BridgeRosterSnapshot _snapshot = BridgeRosterSnapshot.Empty;

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeRosterSnapshot.Empty;
            }
        }

        public static void UpdateFromRoster(string source, string message)
        {
            BridgeRosterClient[] clients = ParseClients(message);
            string serverTime = ReadToken(message, "serverTime") ?? "unknown";

            lock (Sync)
            {
                _snapshot = new BridgeRosterSnapshot(
                    DateTimeOffset.UtcNow,
                    source ?? "unknown",
                    serverTime,
                    ReadToken(message, "worldId") ?? "none",
                    ReadToken(message, "worldHost") ?? "unknown",
                    ReadToken(message, "baselineHash") ?? "unknown",
                    ReadToken(message, "serverSaveSlot") ?? "unknown",
                    ReadToken(message, "runSeed") ?? "unknown",
                    clients);
            }
        }

        public static BridgeRosterSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeRosterClient[] ParseClients(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeRosterClient>();
            }

            int clientsIndex = message.IndexOf("clients=", StringComparison.Ordinal);
            if (clientsIndex < 0)
            {
                return Array.Empty<BridgeRosterClient>();
            }

            string payload = message.Substring(clientsIndex + "clients=".Length).Trim();
            if (payload.Length == 0)
            {
                return Array.Empty<BridgeRosterClient>();
            }

            string[] entries = payload.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeRosterClient> clients = new List<BridgeRosterClient>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeRosterClient client = ParseClient(entry.Trim());
                if (!string.IsNullOrEmpty(client.ClientId))
                {
                    clients.Add(client);
                }
            }

            return clients.ToArray();
        }

        private static BridgeRosterClient ParseClient(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeRosterClient client = new BridgeRosterClient
            {
                ClientId = parts.Length > 0 ? parts[0] : "unknown",
                SessionId = "unknown",
                Role = "unknown",
                HostSlot = "unknown",
                SaveSlot = "unknown",
                SaveSlotOk = "unknown",
                WorldHash = "unknown",
                WorldMatch = "unknown",
                PluginVersion = "unknown",
                Scene = "unknown",
                Location = "unknown",
                Players = "unknown",
                P2Wanted = "unknown",
                P2Active = "unknown",
                P2Hold = "unknown",
                P2NoController = "unknown",
                CultHash = "unknown",
                CultMatch = "unknown",
                CultFollowers = "unknown",
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
                        client.SessionId = value;
                        break;
                    case "role":
                        client.Role = value;
                        break;
                    case "hostSlot":
                        client.HostSlot = value;
                        break;
                    case "saveSlot":
                        client.SaveSlot = value;
                        break;
                    case "saveSlotOk":
                        client.SaveSlotOk = value;
                        break;
                    case "worldHash":
                        client.WorldHash = value;
                        break;
                    case "worldMatch":
                        client.WorldMatch = value;
                        break;
                    case "plugin":
                        client.PluginVersion = value;
                        break;
                    case "scene":
                        client.Scene = value;
                        break;
                    case "location":
                        client.Location = value;
                        break;
                    case "players":
                        client.Players = value;
                        break;
                    case "p2Wanted":
                        client.P2Wanted = value;
                        break;
                    case "p2Active":
                        client.P2Active = value;
                        break;
                    case "p2Hold":
                        client.P2Hold = value;
                        break;
                    case "p2NoController":
                        client.P2NoController = value;
                        break;
                    case "cultHash":
                        client.CultHash = value;
                        break;
                    case "cultMatch":
                        client.CultMatch = value;
                        break;
                    case "cultFollowers":
                        client.CultFollowers = value;
                        break;
                    case "ageMs":
                        client.AgeMs = value;
                        break;
                }
            }

            return client;
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

    internal sealed class BridgeRosterSnapshot
    {
        public static readonly BridgeRosterSnapshot Empty = new BridgeRosterSnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "none",
            "unknown",
            "unknown",
            "unknown",
            "unknown",
            Array.Empty<BridgeRosterClient>());

        public BridgeRosterSnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string worldId, string worldHost, string baselineHash, string serverSaveSlot, string runSeed, BridgeRosterClient[] clients)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            WorldId = worldId;
            WorldHost = worldHost;
            BaselineHash = baselineHash;
            ServerSaveSlot = serverSaveSlot;
            RunSeed = runSeed;
            Clients = clients ?? Array.Empty<BridgeRosterClient>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string WorldId { get; }
        public string WorldHost { get; }
        public string BaselineHash { get; }
        public string ServerSaveSlot { get; }
        public string RunSeed { get; }
        public BridgeRosterClient[] Clients { get; }
        public bool HasRoster => ReceivedUtc != DateTimeOffset.MinValue;

        public BridgeRosterSnapshot Copy()
        {
            BridgeRosterClient[] clients = new BridgeRosterClient[Clients.Length];
            for (int i = 0; i < Clients.Length; i++)
            {
                clients[i] = Clients[i].Copy();
            }

            return new BridgeRosterSnapshot(ReceivedUtc, Source, ServerTime, WorldId, WorldHost, BaselineHash, ServerSaveSlot, RunSeed, clients);
        }
    }

    internal sealed class BridgeRosterClient
    {
        public string ClientId { get; set; }
        public string SessionId { get; set; }
        public string Role { get; set; }
        public string HostSlot { get; set; }
        public string SaveSlot { get; set; }
        public string SaveSlotOk { get; set; }
        public string WorldHash { get; set; }
        public string WorldMatch { get; set; }
        public string PluginVersion { get; set; }
        public string Scene { get; set; }
        public string Location { get; set; }
        public string Players { get; set; }
        public string P2Wanted { get; set; }
        public string P2Active { get; set; }
        public string P2Hold { get; set; }
        public string P2NoController { get; set; }
        public string CultHash { get; set; }
        public string CultMatch { get; set; }
        public string CultFollowers { get; set; }
        public string AgeMs { get; set; }

        public BridgeRosterClient Copy()
        {
            return new BridgeRosterClient
            {
                ClientId = ClientId,
                SessionId = SessionId,
                Role = Role,
                HostSlot = HostSlot,
                SaveSlot = SaveSlot,
                SaveSlotOk = SaveSlotOk,
                WorldHash = WorldHash,
                WorldMatch = WorldMatch,
                PluginVersion = PluginVersion,
                Scene = Scene,
                Location = Location,
                Players = Players,
                P2Wanted = P2Wanted,
                P2Active = P2Active,
                P2Hold = P2Hold,
                P2NoController = P2NoController,
                CultHash = CultHash,
                CultMatch = CultMatch,
                CultFollowers = CultFollowers,
                AgeMs = AgeMs
            };
        }
    }
}
