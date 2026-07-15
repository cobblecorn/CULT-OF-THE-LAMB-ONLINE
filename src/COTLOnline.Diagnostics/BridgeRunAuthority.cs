using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MMBiomeGeneration;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRunAuthority
    {
        private static readonly object Sync = new object();
        private static BridgeRunAuthoritySnapshot _snapshot = BridgeRunAuthoritySnapshot.Empty;
        private static bool _enabled;
        private static bool _forceDungeonSeed;
        private static bool _forceRewards;
        private static bool _waitForServerSeedBeforeBiomeGenerate;
        private static float _seedWaitTimeoutSeconds;
        private static int _rewardCursor;
        private static string _rewardSignature = "";
        private static string _lastSeedSignature = "";
        private static string _lastBiomeSeedSignature = "";
        private static int _lastDataManagerAuthoritySeed;
        private static string _overlayLine = "run authority: disabled";

        public static void Configure(bool enabled, bool forceDungeonSeed, bool forceRewards, bool waitForServerSeedBeforeBiomeGenerate, float seedWaitTimeoutSeconds)
        {
            _enabled = enabled;
            _forceDungeonSeed = forceDungeonSeed;
            _forceRewards = forceRewards;
            _waitForServerSeedBeforeBiomeGenerate = waitForServerSeedBeforeBiomeGenerate;
            _seedWaitTimeoutSeconds = Mathf.Clamp(seedWaitTimeoutSeconds, 0f, 30f);
            Reset();
            _overlayLine = enabled ? "run authority: waiting" : "run authority: disabled";
            WorldTrace.Record(
                "phase10.run_authority.config",
                "enabled=" + enabled
                + " forceDungeonSeed=" + forceDungeonSeed
                + " forceRewards=" + forceRewards
                + " waitForServerSeedBeforeBiomeGenerate=" + waitForServerSeedBeforeBiomeGenerate
                + " seedWaitTimeoutSeconds=" + WorldTrace.FormatFloat(_seedWaitTimeoutSeconds));
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeRunAuthoritySnapshot.Empty;
                _rewardCursor = 0;
                _rewardSignature = "";
                _lastSeedSignature = "";
                _lastBiomeSeedSignature = "";
                _lastDataManagerAuthoritySeed = 0;
            }
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeRunReward[] rewards = ParseRewards(message);
            BridgeRunAuthoritySnapshot next = new BridgeRunAuthoritySnapshot(
                DateTimeOffset.UtcNow,
                source ?? "unknown",
                ReadToken(message, "serverTime") ?? "unknown",
                ReadToken(message, "target") ?? "unknown",
                ReadToken(message, "worldId") ?? "none",
                ReadToken(message, "host") ?? "unknown",
                ReadToken(message, "seed") ?? "unknown",
                ReadToken(message, "seedSource") ?? "unknown",
                ReadToken(message, "seedAgeMs") ?? "unknown",
                ReadToken(message, "rewardCount") ?? rewards.Length.ToString(CultureInfo.InvariantCulture),
                rewards);

            lock (Sync)
            {
                _snapshot = next;
                string signature = next.RewardSignature;
                if (!string.Equals(signature, _rewardSignature, StringComparison.Ordinal))
                {
                    _rewardSignature = signature;
                    _rewardCursor = 0;
                }
            }
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "run authority: disabled";
                return;
            }

            BridgeRunAuthoritySnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                _overlayLine = "run authority: waiting";
                return;
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            _overlayLine = "run authority: seed=" + snapshot.Seed
                + " source=" + snapshot.SeedSourceClientId
                + " rewards=" + snapshot.Rewards.Length
                + " cursor=" + _rewardCursor
                + " age=" + ageSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        public static bool TryOverrideDungeonSeed(ref int seed, string source)
        {
            int authoritySeed;
            BridgeRunAuthoritySnapshot snapshot;
            if (!TryGetAuthoritySeed(out authoritySeed, out snapshot))
            {
                return false;
            }

            int original = seed;
            seed = authoritySeed;
            try
            {
                if (BiomeGenerator.Instance != null)
                {
                    BiomeGenerator.Instance.Seed = authoritySeed;
                }
            }
            catch
            {
            }

            string signature = source + "|" + original + "|" + authoritySeed + "|" + snapshot.SeedSourceClientId;
            if (!string.Equals(signature, _lastSeedSignature, StringComparison.Ordinal))
            {
                _lastSeedSignature = signature;
                WorldTrace.Record(
                    "phase10.seed.forced",
                    "source=" + Clean(source)
                    + " original=" + original
                    + " authority=" + authoritySeed
                    + " seedSource=" + Clean(snapshot.SeedSourceClientId)
                    + " target=" + Clean(snapshot.TargetClientId));
            }

            return original != authoritySeed;
        }

        public static bool TryApplyAuthoritySeed(BiomeGenerator generator, string source)
        {
            if (generator == null)
            {
                return false;
            }

            int authoritySeed;
            BridgeRunAuthoritySnapshot snapshot;
            if (!TryGetAuthoritySeed(out authoritySeed, out snapshot))
            {
                return false;
            }

            int original = generator.Seed;
            generator.Seed = authoritySeed;
            try
            {
                if (BiomeGenerator.Instance != null)
                {
                    BiomeGenerator.Instance.Seed = authoritySeed;
                }
            }
            catch
            {
            }

            TryReplaceLastDungeonSeed(authoritySeed, source);

            string signature = source + "|" + original + "|" + authoritySeed + "|" + snapshot.SeedSourceClientId;
            if (!string.Equals(signature, _lastBiomeSeedSignature, StringComparison.Ordinal))
            {
                _lastBiomeSeedSignature = signature;
                WorldTrace.Record(
                    "phase10.seed.biome_forced",
                    "source=" + Clean(source)
                    + " original=" + original
                    + " authority=" + authoritySeed
                    + " seedSource=" + Clean(snapshot.SeedSourceClientId)
                    + " target=" + Clean(snapshot.TargetClientId)
                    + " generator=" + Clean(Phase2Trace.DescribeBiomeGenerator(generator)));
            }

            return original != authoritySeed;
        }

        public static bool TryWrapBiomeGenerateRoutine(BiomeGenerator generator, IEnumerator original, out IEnumerator wrapped)
        {
            wrapped = null;
            if (!_enabled || !_forceDungeonSeed || !_waitForServerSeedBeforeBiomeGenerate || original == null || generator == null)
            {
                return false;
            }

            if (!ShouldWaitForAuthoritySeed())
            {
                return false;
            }

            int authoritySeed;
            BridgeRunAuthoritySnapshot snapshot;
            if (TryGetAuthoritySeed(out authoritySeed, out snapshot))
            {
                TryApplyAuthoritySeed(generator, "BiomeGenerator.GenerateRoutine.wrap_immediate");
                return false;
            }

            wrapped = WaitForAuthoritySeedThenRun(generator, original);
            return true;
        }

        private static IEnumerator WaitForAuthoritySeedThenRun(BiomeGenerator generator, IEnumerator original)
        {
            float started = Time.realtimeSinceStartup;
            WorldTrace.Record(
                "phase10.seed.wait_start",
                "timeoutSeconds=" + WorldTrace.FormatFloat(_seedWaitTimeoutSeconds)
                + " generator=" + Clean(Phase2Trace.DescribeBiomeGenerator(generator)));

            bool ready = false;
            while (Time.realtimeSinceStartup - started < _seedWaitTimeoutSeconds)
            {
                if (TryApplyAuthoritySeed(generator, "BiomeGenerator.GenerateRoutine.wait"))
                {
                    ready = true;
                    break;
                }

                yield return null;
            }

            if (ready)
            {
                WorldTrace.Record(
                    "phase10.seed.wait_ready",
                    "waitedSeconds=" + WorldTrace.FormatFloat(Time.realtimeSinceStartup - started)
                    + " generator=" + Clean(Phase2Trace.DescribeBiomeGenerator(generator)));
            }
            else
            {
                WorldTrace.Record(
                    "phase10.seed.wait_timeout",
                    "waitedSeconds=" + WorldTrace.FormatFloat(Time.realtimeSinceStartup - started)
                    + " generator=" + Clean(Phase2Trace.DescribeBiomeGenerator(generator)));
            }

            while (original.MoveNext())
            {
                yield return original.Current;
            }
        }

        public static bool TryApplyNextReward(Interaction_WeaponSelectionPodium podium, string expectedType, ref int forceLevel)
        {
            if (!_enabled || !_forceRewards || podium == null || string.IsNullOrEmpty(expectedType))
            {
                return false;
            }

            BridgeRunAuthoritySnapshot snapshot = Snapshot();
            if (!ShouldUseRewardAuthority(snapshot))
            {
                return false;
            }

            BridgeRunReward reward = null;
            int consumedIndex = -1;
            lock (Sync)
            {
                for (int i = _rewardCursor; i < _snapshot.Rewards.Length; i++)
                {
                    BridgeRunReward candidate = _snapshot.Rewards[i];
                    if (candidate != null && string.Equals(candidate.Type, expectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        reward = candidate.Copy();
                        consumedIndex = i;
                        _rewardCursor = i + 1;
                        break;
                    }
                }
            }

            if (reward == null)
            {
                return false;
            }

            EquipmentType equipment;
            if (!TryParseEquipment(reward.Equipment, out equipment))
            {
                return false;
            }

            podium.ForceEquipmentType = equipment;
            int level;
            if (int.TryParse(reward.Level, NumberStyles.Integer, CultureInfo.InvariantCulture, out level) && level > 0)
            {
                forceLevel = level;
            }

            WorldTrace.Record(
                "phase10.reward.forced",
                "index=" + consumedIndex
                + " type=" + Clean(reward.Type)
                + " equipment=" + Clean(reward.Equipment)
                + " level=" + Clean(reward.Level)
                + " forceLevel=" + forceLevel
                + " podium=" + Clean(WorldTrace.DescribeGameObject(podium.gameObject)));
            return true;
        }

        private static bool ShouldUseAuthority(BridgeRunAuthoritySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasPacket)
            {
                return false;
            }

            if (!string.Equals(snapshot.TargetClientId, "unknown", StringComparison.Ordinal)
                && !string.Equals(snapshot.TargetClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
            {
                return false;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self != null)
            {
                if (string.Equals(self.Role, "host-lamb", StringComparison.Ordinal))
                {
                    return false;
                }

                if (string.Equals(self.ClientId, snapshot.HostClientId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return !string.Equals(snapshot.SeedSourceClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal);
        }

        private static bool ShouldUseRewardAuthority(BridgeRunAuthoritySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasPacket || snapshot.Rewards.Length == 0)
            {
                return false;
            }

            if (!string.Equals(snapshot.TargetClientId, "unknown", StringComparison.Ordinal)
                && !string.Equals(snapshot.TargetClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetAuthoritySeed(out int authoritySeed, out BridgeRunAuthoritySnapshot snapshot)
        {
            authoritySeed = 0;
            snapshot = null;
            if (!_enabled || !_forceDungeonSeed)
            {
                return false;
            }

            snapshot = Snapshot();
            if (!ShouldUseAuthority(snapshot))
            {
                return false;
            }

            return int.TryParse(snapshot.Seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out authoritySeed)
                && authoritySeed != 0;
        }

        private static bool ShouldWaitForAuthoritySeed()
        {
            if (_seedWaitTimeoutSeconds <= 0f)
            {
                return false;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            if (roster != null
                && !string.IsNullOrEmpty(roster.WorldHost)
                && !string.Equals(roster.WorldHost, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(roster.WorldHost, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
            {
                return true;
            }

            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self == null)
            {
                return false;
            }

            return !string.Equals(self.Role, "host-lamb", StringComparison.Ordinal)
                && !string.Equals(self.ClientId, roster.WorldHost, StringComparison.Ordinal);
        }

        private static void TryReplaceLastDungeonSeed(int authoritySeed, string source)
        {
            if (authoritySeed == 0 || _lastDataManagerAuthoritySeed == authoritySeed)
            {
                return;
            }

            try
            {
                if (DataManager.Instance == null || DataManager.Instance.LastDungeonSeeds == null)
                {
                    return;
                }

                int count = DataManager.Instance.LastDungeonSeeds.Count;
                if (count > 0)
                {
                    DataManager.Instance.LastDungeonSeeds[count - 1] = authoritySeed;
                }
                else
                {
                    DataManager.Instance.LastDungeonSeeds.Add(authoritySeed);
                }

                _lastDataManagerAuthoritySeed = authoritySeed;
                WorldTrace.Record(
                    "phase10.seed.last_seed_replaced",
                    "source=" + Clean(source)
                    + " authority=" + authoritySeed
                    + " count=" + count);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase10.seed.last_seed_replace_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static BridgeRunAuthoritySnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
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

        private static BridgeRunReward[] ParseRewards(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeRunReward>();
            }

            int index = message.IndexOf("rewards=", StringComparison.Ordinal);
            if (index < 0)
            {
                return Array.Empty<BridgeRunReward>();
            }

            string payload = message.Substring(index + "rewards=".Length).Trim();
            if (payload.Length == 0 || payload == "none")
            {
                return Array.Empty<BridgeRunReward>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeRunReward> rewards = new List<BridgeRunReward>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeRunReward reward = ParseReward(entry.Trim());
                if (!string.IsNullOrEmpty(reward.Index))
                {
                    rewards.Add(reward);
                }
            }

            return rewards.ToArray();
        }

        private static BridgeRunReward ParseReward(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeRunReward reward = new BridgeRunReward
            {
                Index = parts.Length > 0 ? parts[0] : "unknown",
                SourceClientId = "unknown",
                SourceRole = "unknown",
                Run = "unknown",
                Type = "unknown",
                Equipment = "unknown",
                Level = "unknown",
                CoopPodium = "unknown",
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
                    case "source":
                        reward.SourceClientId = value;
                        break;
                    case "role":
                        reward.SourceRole = value;
                        break;
                    case "run":
                        reward.Run = value;
                        break;
                    case "type":
                        reward.Type = value;
                        break;
                    case "equipment":
                        reward.Equipment = value;
                        break;
                    case "level":
                        reward.Level = value;
                        break;
                    case "coopPodium":
                        reward.CoopPodium = value;
                        break;
                    case "ageMs":
                        reward.AgeMs = value;
                        break;
                }
            }

            return reward;
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

        private static bool TryParseEquipment(string value, out EquipmentType equipment)
        {
            equipment = EquipmentType.None;
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(value, out equipment)
                && equipment != EquipmentType.None;
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

    internal sealed class BridgeRunAuthoritySnapshot
    {
        public static readonly BridgeRunAuthoritySnapshot Empty = new BridgeRunAuthoritySnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "unknown",
            "none",
            "unknown",
            "unknown",
            "unknown",
            "unknown",
            "0",
            Array.Empty<BridgeRunReward>());

        public BridgeRunAuthoritySnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string targetClientId, string worldId, string hostClientId, string seed, string seedSourceClientId, string seedAgeMs, string rewardCount, BridgeRunReward[] rewards)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            TargetClientId = targetClientId;
            WorldId = worldId;
            HostClientId = hostClientId;
            Seed = seed;
            SeedSourceClientId = seedSourceClientId;
            SeedAgeMs = seedAgeMs;
            RewardCount = rewardCount;
            Rewards = rewards ?? Array.Empty<BridgeRunReward>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string TargetClientId { get; }
        public string WorldId { get; }
        public string HostClientId { get; }
        public string Seed { get; }
        public string SeedSourceClientId { get; }
        public string SeedAgeMs { get; }
        public string RewardCount { get; }
        public BridgeRunReward[] Rewards { get; }
        public bool HasPacket => ReceivedUtc != DateTimeOffset.MinValue;
        public string RewardSignature
        {
            get
            {
                string signature = Seed + "|" + SeedSourceClientId + "|" + Rewards.Length;
                for (int i = 0; i < Rewards.Length; i++)
                {
                    signature += "|" + Rewards[i].Index + ":run=" + Rewards[i].Run + ":" + Rewards[i].Type + ":" + Rewards[i].Equipment + "@" + Rewards[i].Level;
                }

                return signature;
            }
        }

        public BridgeRunAuthoritySnapshot Copy()
        {
            BridgeRunReward[] rewards = new BridgeRunReward[Rewards.Length];
            for (int i = 0; i < Rewards.Length; i++)
            {
                rewards[i] = Rewards[i].Copy();
            }

            return new BridgeRunAuthoritySnapshot(ReceivedUtc, Source, ServerTime, TargetClientId, WorldId, HostClientId, Seed, SeedSourceClientId, SeedAgeMs, RewardCount, rewards);
        }
    }

    internal sealed class BridgeRunReward
    {
        public string Index { get; set; }
        public string SourceClientId { get; set; }
        public string SourceRole { get; set; }
        public string Run { get; set; }
        public string Type { get; set; }
        public string Equipment { get; set; }
        public string Level { get; set; }
        public string CoopPodium { get; set; }
        public string AgeMs { get; set; }

        public BridgeRunReward Copy()
        {
            return new BridgeRunReward
            {
                Index = Index,
                SourceClientId = SourceClientId,
                SourceRole = SourceRole,
                Run = Run,
                Type = Type,
                Equipment = Equipment,
                Level = Level,
                CoopPodium = CoopPodium,
                AgeMs = AgeMs
            };
        }
    }
}
