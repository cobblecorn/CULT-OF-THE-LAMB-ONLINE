using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeLoadoutAuthority
    {
        private static readonly object Sync = new object();
        private static BridgeLoadoutSnapshot _snapshot = BridgeLoadoutSnapshot.Empty;
        private static bool _enabled;
        private static bool _overrideRemoteBodies;
        private static string _lastSignature = "";
        private static float _nextRecordAt;
        private static float _lastAppliedAt;
        private static string _overlayLine = "loadout relay: disabled";

        public static bool IsApplying { get; private set; }

        public static void Configure(bool enabled, bool overrideRemoteBodies)
        {
            _enabled = enabled;
            _overrideRemoteBodies = overrideRemoteBodies;
            _lastSignature = "";
            _nextRecordAt = 0f;
            _lastAppliedAt = -9999f;
            _overlayLine = enabled ? "loadout relay: waiting" : "loadout relay: disabled";
            Reset();
            WorldTrace.Record(
                "phase9.loadout.config",
                "enabled=" + enabled + " overrideRemoteBodies=" + overrideRemoteBodies);
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeLoadoutSnapshot.Empty;
            }
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeLoadoutEntry[] loadouts = ParseLoadouts(message);
            lock (Sync)
            {
                _snapshot = new BridgeLoadoutSnapshot(
                    DateTimeOffset.UtcNow,
                    source ?? "unknown",
                    ReadToken(message, "serverTime") ?? "unknown",
                    ReadToken(message, "target") ?? "unknown",
                    ReadToken(message, "worldId") ?? "none",
                    ReadToken(message, "loadoutCount") ?? loadouts.Length.ToString(CultureInfo.InvariantCulture),
                    loadouts);
            }
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "loadout relay: disabled";
                return;
            }

            BridgeLoadoutSnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                _overlayLine = "loadout relay: waiting";
                return;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self == null)
            {
                _overlayLine = "loadout relay: waiting for roster";
                return;
            }

            int applied = 0;
            int considered = 0;
            for (int i = 0; i < snapshot.Loadouts.Length; i++)
            {
                BridgeLoadoutEntry loadout = snapshot.Loadouts[i];
                if (loadout == null || string.Equals(loadout.ClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ShouldAcceptLoadoutForLocalRole(self, loadout))
                {
                    continue;
                }

                PlayerFarming target = FindLocalTarget(self, loadout);
                if (target == null)
                {
                    continue;
                }

                considered++;
                if (ApplyLoadout(target, loadout))
                {
                    applied++;
                }
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            _overlayLine = "loadout relay: loadouts=" + snapshot.Loadouts.Length
                + " targets=" + considered
                + " applied=" + applied
                + " age=" + ageSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        public static bool RecentlyApplied(float seconds)
        {
            return _enabled
                && _lastAppliedAt > 0f
                && Time.unscaledTime - _lastAppliedAt <= Mathf.Max(0f, seconds);
        }

        private static bool ApplyLoadout(PlayerFarming target, BridgeLoadoutEntry loadout)
        {
            if (target == null)
            {
                return false;
            }

            bool changed = false;
            string signature = loadout.ClientId
                + "|" + loadout.HostSlot
                + "|" + loadout.PlayerId
                + "|" + target.playerID
                + "|" + loadout.Weapon
                + "|" + loadout.Curse
                + "|" + target.currentWeapon
                + "@" + target.currentWeaponLevel
                + "|" + target.currentCurse
                + "@" + target.currentCurseLevel;

            try
            {
                IsApplying = true;
                EquipmentType weapon;
                int weaponLevel;
                if (TryParseEquipment(loadout.Weapon, out weapon, out weaponLevel)
                    && ShouldApply(target.currentWeapon, target.currentWeaponLevel, weapon, weaponLevel))
                {
                    target.playerWeapon.SetWeapon(weapon, weaponLevel);
                    changed = true;
                }

                EquipmentType curse;
                int curseLevel;
                if (TryParseEquipment(loadout.Curse, out curse, out curseLevel)
                    && ShouldApply(target.currentCurse, target.currentCurseLevel, curse, curseLevel))
                {
                    target.playerSpells.SetSpell(curse, curseLevel);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase9.loadout.apply_error",
                    "sourceClient=" + Clean(loadout.ClientId)
                    + " target=" + Clean(WorldTrace.DescribePlayer(target))
                    + " error=" + ex.GetType().Name + ":" + Clean(ex.Message));
            }
            finally
            {
                IsApplying = false;
            }

            if (changed || (!string.Equals(signature, _lastSignature, StringComparison.Ordinal) && Time.unscaledTime >= _nextRecordAt))
            {
                if (changed)
                {
                    _lastAppliedAt = Time.unscaledTime;
                }

                _lastSignature = signature;
                _nextRecordAt = Time.unscaledTime + 1f;
                WorldTrace.Record(
                    changed ? "phase9.loadout.applied" : "phase9.loadout.checked",
                    "sourceClient=" + Clean(loadout.ClientId)
                    + " role=" + Clean(loadout.Role)
                    + " hostSlot=" + Clean(loadout.HostSlot)
                    + " weapon=" + Clean(loadout.Weapon)
                    + " curse=" + Clean(loadout.Curse)
                    + " target=" + Clean(WorldTrace.DescribePlayer(target))
                    + " changed=" + changed);
            }

            return changed;
        }

        private static bool ShouldApply(EquipmentType current, int currentLevel, EquipmentType incoming, int incomingLevel)
        {
            if (incoming == EquipmentType.None)
            {
                return false;
            }

            if (current == incoming && currentLevel == incomingLevel)
            {
                return false;
            }

            return _overrideRemoteBodies || current == EquipmentType.None || currentLevel <= 0;
        }

        private static bool ShouldAcceptLoadoutForLocalRole(BridgeRosterClient self, BridgeLoadoutEntry loadout)
        {
            if (self == null || loadout == null)
            {
                return false;
            }

            if (string.Equals(self.Role, "host-lamb", StringComparison.Ordinal)
                && string.Equals(loadout.Role, "remote-p2", StringComparison.Ordinal))
            {
                return string.Equals(loadout.PlayerId, "0", StringComparison.Ordinal);
            }

            if (string.Equals(self.Role, "remote-p2", StringComparison.Ordinal)
                && string.Equals(loadout.Role, "host-lamb", StringComparison.Ordinal))
            {
                return string.Equals(loadout.PlayerId, "0", StringComparison.Ordinal);
            }

            return false;
        }

        private static PlayerFarming FindLocalTarget(BridgeRosterClient self, BridgeLoadoutEntry loadout)
        {
            if (self == null || loadout == null)
            {
                return null;
            }

            if (string.Equals(self.Role, "host-lamb", StringComparison.Ordinal)
                && string.Equals(loadout.Role, "remote-p2", StringComparison.Ordinal))
            {
                int slot = ParseInt(loadout.HostSlot, 1);
                return FindPlayerById(slot);
            }

            if (string.Equals(self.Role, "remote-p2", StringComparison.Ordinal)
                && string.Equals(loadout.Role, "host-lamb", StringComparison.Ordinal))
            {
                return FindRemoteVisualBody();
            }

            int fallbackSlot = ParseInt(loadout.HostSlot, -1);
            return fallbackSlot > 0 ? FindPlayerById(fallbackSlot) : null;
        }

        private static PlayerFarming FindRemoteVisualBody()
        {
            PlayerFarming byId = FindPlayerById(1);
            if (byId != null && !SafeBool(() => byId.isLamb))
            {
                return byId;
            }

            try
            {
                if (PlayerFarming.players != null && PlayerFarming.players.Count > 1)
                {
                    PlayerFarming fallback = PlayerFarming.players[1];
                    if (fallback != null && !fallback.isLamb)
                    {
                        return fallback;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static PlayerFarming FindPlayerById(int playerId)
        {
            try
            {
                if (PlayerFarming.players == null)
                {
                    return null;
                }

                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    PlayerFarming player = PlayerFarming.players[i];
                    if (player != null && player.playerID == playerId)
                    {
                        return player;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TryParseEquipment(string value, out EquipmentType equipment, out int level)
        {
            equipment = EquipmentType.None;
            level = 0;
            if (string.IsNullOrEmpty(value)
                || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "None@0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string name = value;
            int at = value.LastIndexOf('@');
            if (at >= 0)
            {
                name = value.Substring(0, at);
                int.TryParse(value.Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out level);
            }

            if (level <= 0)
            {
                level = 1;
            }

            return Enum.TryParse(name, out equipment) && equipment != EquipmentType.None;
        }

        private static BridgeLoadoutSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeLoadoutEntry[] ParseLoadouts(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeLoadoutEntry>();
            }

            int index = message.IndexOf("loadouts=", StringComparison.Ordinal);
            if (index < 0)
            {
                return Array.Empty<BridgeLoadoutEntry>();
            }

            string payload = message.Substring(index + "loadouts=".Length).Trim();
            if (payload.Length == 0 || payload == "none")
            {
                return Array.Empty<BridgeLoadoutEntry>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeLoadoutEntry> loadouts = new List<BridgeLoadoutEntry>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeLoadoutEntry loadout = ParseLoadout(entry.Trim());
                if (!string.IsNullOrEmpty(loadout.ClientId))
                {
                    loadouts.Add(loadout);
                }
            }

            return loadouts.ToArray();
        }

        private static BridgeLoadoutEntry ParseLoadout(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeLoadoutEntry loadout = new BridgeLoadoutEntry
            {
                ClientId = parts.Length > 0 ? parts[0] : "unknown",
                SessionId = "unknown",
                PlayerId = "unknown",
                Role = "unknown",
                HostSlot = "unknown",
                Weapon = "unknown",
                Curse = "unknown",
                Decision = "unknown",
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
                        loadout.SessionId = value;
                        break;
                    case "playerID":
                        loadout.PlayerId = value;
                        break;
                    case "role":
                        loadout.Role = value;
                        break;
                    case "hostSlot":
                        loadout.HostSlot = value;
                        break;
                    case "weapon":
                        loadout.Weapon = value;
                        break;
                    case "curse":
                        loadout.Curse = value;
                        break;
                    case "decision":
                        loadout.Decision = value;
                        break;
                    case "ageMs":
                        loadout.AgeMs = value;
                        break;
                }
            }

            return loadout;
        }

        private static BridgeRosterClient FindClient(BridgeRosterSnapshot roster, string clientId)
        {
            if (roster == null || roster.Clients == null)
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

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
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

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_");
        }
    }

    internal sealed class BridgeLoadoutSnapshot
    {
        public static readonly BridgeLoadoutSnapshot Empty = new BridgeLoadoutSnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "unknown",
            "none",
            "0",
            Array.Empty<BridgeLoadoutEntry>());

        public BridgeLoadoutSnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string targetClientId, string worldId, string loadoutCount, BridgeLoadoutEntry[] loadouts)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            TargetClientId = targetClientId;
            WorldId = worldId;
            LoadoutCount = loadoutCount;
            Loadouts = loadouts ?? Array.Empty<BridgeLoadoutEntry>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string TargetClientId { get; }
        public string WorldId { get; }
        public string LoadoutCount { get; }
        public BridgeLoadoutEntry[] Loadouts { get; }
        public bool HasPacket => ReceivedUtc != DateTimeOffset.MinValue;

        public BridgeLoadoutSnapshot Copy()
        {
            BridgeLoadoutEntry[] loadouts = new BridgeLoadoutEntry[Loadouts.Length];
            for (int i = 0; i < Loadouts.Length; i++)
            {
                loadouts[i] = Loadouts[i].Copy();
            }

            return new BridgeLoadoutSnapshot(ReceivedUtc, Source, ServerTime, TargetClientId, WorldId, LoadoutCount, loadouts);
        }
    }

    internal sealed class BridgeLoadoutEntry
    {
        public string ClientId { get; set; }
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string Role { get; set; }
        public string HostSlot { get; set; }
        public string Weapon { get; set; }
        public string Curse { get; set; }
        public string Decision { get; set; }
        public string AgeMs { get; set; }

        public BridgeLoadoutEntry Copy()
        {
            return new BridgeLoadoutEntry
            {
                ClientId = ClientId,
                SessionId = SessionId,
                PlayerId = PlayerId,
                Role = Role,
                HostSlot = HostSlot,
                Weapon = Weapon,
                Curse = Curse,
                Decision = Decision,
                AgeMs = AgeMs
            };
        }
    }
}
