using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRewardClaimAuthority
    {
        private static readonly object Sync = new object();
        private static readonly FieldInfo WeaponLevelField = AccessTools.Field(typeof(Interaction_WeaponSelectionPodium), "WeaponLevel");
        private static readonly HashSet<string> AppliedClaims = new HashSet<string>(StringComparer.Ordinal);
        private static BridgeRewardClaimSnapshot _snapshot = BridgeRewardClaimSnapshot.Empty;
        private static bool _enabled;
        private static string _overlayLine = "reward claim relay: disabled";
        private static float _nextRecordAt;

        public static void Configure(bool enabled)
        {
            _enabled = enabled;
            _overlayLine = enabled ? "reward claim relay: waiting" : "reward claim relay: disabled";
            _nextRecordAt = 0f;
            Reset();
            WorldTrace.Record("phase12.reward_claim.config", "enabled=" + enabled);
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeRewardClaimSnapshot.Empty;
            }

            AppliedClaims.Clear();
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeRewardClaim[] claims = ParseClaims(message);
            lock (Sync)
            {
                _snapshot = new BridgeRewardClaimSnapshot(
                    DateTimeOffset.UtcNow,
                    source ?? "unknown",
                    ReadToken(message, "serverTime") ?? "unknown",
                    ReadToken(message, "target") ?? "unknown",
                    ReadToken(message, "worldId") ?? "none",
                    ReadToken(message, "claimCount") ?? claims.Length.ToString(CultureInfo.InvariantCulture),
                    claims);
            }
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "reward claim relay: disabled";
                return;
            }

            BridgeRewardClaimSnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                _overlayLine = "reward claim relay: waiting";
                return;
            }

            int applied = 0;
            int considered = 0;
            for (int i = 0; i < snapshot.Claims.Length; i++)
            {
                BridgeRewardClaim claim = snapshot.Claims[i];
                if (claim == null || string.Equals(claim.SourceClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                {
                    continue;
                }

                considered++;
                if (TryApplyClaim(claim))
                {
                    applied++;
                }
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            _overlayLine = "reward claim relay: claims=" + snapshot.Claims.Length
                + " targets=" + considered
                + " applied=" + applied
                + " age=" + ageSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        private static bool TryApplyClaim(BridgeRewardClaim claim)
        {
            string signature = claim.Signature;
            if (AppliedClaims.Contains(signature))
            {
                return false;
            }

            Interaction_WeaponSelectionPodium podium = FindMatchingPodium(claim);
            if (podium == null)
            {
                RecordMissIfNeeded(claim);
                return false;
            }

            try
            {
                podium.SetWeaponInactive();
                AppliedClaims.Add(signature);
                WorldTrace.Record(
                    "phase12.reward_claim.applied",
                    "claim=" + Clean(signature)
                    + " source=" + Clean(claim.SourceClientId)
                    + " type=" + Clean(claim.Type)
                    + " equipment=" + Clean(claim.Equipment)
                    + " level=" + Clean(claim.Level)
                    + " podium=" + Clean(WorldTrace.DescribeGameObject(podium.gameObject)));
                return true;
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase12.reward_claim.apply_error",
                    "claim=" + Clean(signature)
                    + " error=" + ex.GetType().Name + ":" + Clean(ex.Message));
                return false;
            }
        }

        private static Interaction_WeaponSelectionPodium FindMatchingPodium(BridgeRewardClaim claim)
        {
            if (Interaction_WeaponSelectionPodium.Podiums == null)
            {
                return null;
            }

            Vector3 claimPosition;
            bool hasPosition = TryParsePosition(claim.PodiumPosition, out claimPosition);
            Interaction_WeaponSelectionPodium best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < Interaction_WeaponSelectionPodium.Podiums.Count; i++)
            {
                Interaction_WeaponSelectionPodium podium = Interaction_WeaponSelectionPodium.Podiums[i];
                if (podium == null || podium.gameObject == null || podium.WeaponTaken)
                {
                    continue;
                }

                if (!MatchesType(podium, claim.Type) || !MatchesEquipment(podium, claim.Equipment))
                {
                    continue;
                }

                float score = 100f;
                if (MatchesLevel(podium, claim.Level))
                {
                    score += 20f;
                }
                else if (IsKnownToken(claim.Level))
                {
                    score -= 25f;
                }

                if (MatchesCoopPodium(podium, claim.CoopPodium))
                {
                    score += 10f;
                }

                if (hasPosition)
                {
                    float distance = Vector3.Distance(podium.transform.position, claimPosition);
                    if (distance > 4.0f)
                    {
                        continue;
                    }

                    score += Mathf.Max(0f, 25f - distance * 10f);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = podium;
                }
            }

            return best;
        }

        private static bool MatchesType(Interaction_WeaponSelectionPodium podium, string type)
        {
            return !IsKnownToken(type)
                || string.Equals(podium.Type.ToString(), type, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesEquipment(Interaction_WeaponSelectionPodium podium, string equipment)
        {
            if (!IsKnownToken(equipment))
            {
                return false;
            }

            EquipmentType parsed;
            return Enum.TryParse(equipment, out parsed)
                && parsed != EquipmentType.None
                && podium.TypeOfWeapon == parsed;
        }

        private static bool MatchesLevel(Interaction_WeaponSelectionPodium podium, string level)
        {
            if (!IsKnownToken(level))
            {
                return false;
            }

            int expected;
            if (!int.TryParse(level, NumberStyles.Integer, CultureInfo.InvariantCulture, out expected))
            {
                return false;
            }

            int actual = SafeInt(() => WeaponLevelField != null ? Convert.ToInt32(WeaponLevelField.GetValue(podium), CultureInfo.InvariantCulture) : -1);
            return actual == expected;
        }

        private static bool MatchesCoopPodium(Interaction_WeaponSelectionPodium podium, string coopPodium)
        {
            bool expected;
            return !bool.TryParse(coopPodium, out expected) || podium.isCoopPodium == expected;
        }

        private static BridgeRewardClaimSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeRewardClaim[] ParseClaims(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeRewardClaim>();
            }

            int index = message.IndexOf("claims=", StringComparison.Ordinal);
            if (index < 0)
            {
                return Array.Empty<BridgeRewardClaim>();
            }

            string payload = message.Substring(index + "claims=".Length).Trim();
            if (payload.Length == 0 || payload == "none")
            {
                return Array.Empty<BridgeRewardClaim>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeRewardClaim> claims = new List<BridgeRewardClaim>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeRewardClaim claim = ParseClaim(entry.Trim());
                if (!string.IsNullOrEmpty(claim.Index))
                {
                    claims.Add(claim);
                }
            }

            return claims.ToArray();
        }

        private static BridgeRewardClaim ParseClaim(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeRewardClaim claim = new BridgeRewardClaim
            {
                Index = parts.Length > 0 ? parts[0] : "unknown",
                SourceClientId = "unknown",
                PlayerId = "unknown",
                RewardIndex = "unknown",
                Type = "unknown",
                Equipment = "unknown",
                Level = "unknown",
                CoopPodium = "unknown",
                PodiumPosition = "unknown",
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
                        claim.SourceClientId = value;
                        break;
                    case "playerID":
                        claim.PlayerId = value;
                        break;
                    case "rewardIndex":
                        claim.RewardIndex = value;
                        break;
                    case "type":
                        claim.Type = value;
                        break;
                    case "equipment":
                        claim.Equipment = value;
                        break;
                    case "level":
                        claim.Level = value;
                        break;
                    case "coopPodium":
                        claim.CoopPodium = value;
                        break;
                    case "podiumPos":
                        claim.PodiumPosition = value;
                        break;
                    case "ageMs":
                        claim.AgeMs = value;
                        break;
                }
            }

            return claim;
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

        private static bool TryParsePosition(string value, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(value) || !IsKnownToken(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            string[] parts = trimmed.Split(',');
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

            if (parts.Length >= 3)
            {
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            }

            position = new Vector3(x, y, z);
            return true;
        }

        private static void RecordMissIfNeeded(BridgeRewardClaim claim)
        {
            if (Time.unscaledTime < _nextRecordAt)
            {
                return;
            }

            _nextRecordAt = Time.unscaledTime + 1.5f;
            WorldTrace.Record(
                "phase12.reward_claim.no_match",
                "claim=" + Clean(claim.Signature)
                + " type=" + Clean(claim.Type)
                + " equipment=" + Clean(claim.Equipment)
                + " level=" + Clean(claim.Level)
                + " podiumPos=" + Clean(claim.PodiumPosition));
        }

        private static bool IsKnownToken(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
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

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_").Replace("|", "/").Replace(";", ",");
        }
    }

    internal sealed class BridgeRewardClaimSnapshot
    {
        public static readonly BridgeRewardClaimSnapshot Empty = new BridgeRewardClaimSnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "unknown",
            "none",
            "0",
            Array.Empty<BridgeRewardClaim>());

        public BridgeRewardClaimSnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string targetClientId, string worldId, string claimCount, BridgeRewardClaim[] claims)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            TargetClientId = targetClientId;
            WorldId = worldId;
            ClaimCount = claimCount;
            Claims = claims ?? Array.Empty<BridgeRewardClaim>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string TargetClientId { get; }
        public string WorldId { get; }
        public string ClaimCount { get; }
        public BridgeRewardClaim[] Claims { get; }
        public bool HasPacket => ReceivedUtc != DateTimeOffset.MinValue;

        public BridgeRewardClaimSnapshot Copy()
        {
            BridgeRewardClaim[] claims = new BridgeRewardClaim[Claims.Length];
            for (int i = 0; i < Claims.Length; i++)
            {
                claims[i] = Claims[i].Copy();
            }

            return new BridgeRewardClaimSnapshot(ReceivedUtc, Source, ServerTime, TargetClientId, WorldId, ClaimCount, claims);
        }
    }

    internal sealed class BridgeRewardClaim
    {
        public string Index { get; set; }
        public string SourceClientId { get; set; }
        public string PlayerId { get; set; }
        public string RewardIndex { get; set; }
        public string Type { get; set; }
        public string Equipment { get; set; }
        public string Level { get; set; }
        public string CoopPodium { get; set; }
        public string PodiumPosition { get; set; }
        public string AgeMs { get; set; }

        public string Signature => SourceClientId + "|" + PlayerId + "|" + RewardIndex + "|" + Type + "|" + Equipment + "|" + Level + "|" + PodiumPosition;

        public BridgeRewardClaim Copy()
        {
            return new BridgeRewardClaim
            {
                Index = Index,
                SourceClientId = SourceClientId,
                PlayerId = PlayerId,
                RewardIndex = RewardIndex,
                Type = Type,
                Equipment = Equipment,
                Level = Level,
                CoopPodium = CoopPodium,
                PodiumPosition = PodiumPosition,
                AgeMs = AgeMs
            };
        }
    }
}
