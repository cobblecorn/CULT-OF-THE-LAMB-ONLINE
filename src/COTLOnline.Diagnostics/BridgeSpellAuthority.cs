using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MMRoomGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeSpellAuthority
    {
        private const float MaxCastAgeMs = 3500f;
        private const int MaxAppliedSignatures = 192;
        private static readonly object Sync = new object();
        private static readonly FieldInfo TargetPositionField = AccessTools.Field(typeof(PlayerSpells), "targetPosition");
        private static readonly HashSet<string> AppliedSignatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> AppliedOrder = new Queue<string>();
        private static BridgeSpellSnapshot _snapshot = BridgeSpellSnapshot.Empty;
        private static bool _enabled;
        private static bool _visualOnly;
        private static bool _isReplaying;
        private static GameObject _visualReplayOwner;
        private static float _visualReplayDamageSuppressUntil;
        private static int _localSequence;
        private static string _overlayLine = "spell relay: disabled";
        private static float _nextMissRecordAt;

        public static bool IsReplaying { get { return _isReplaying; } }

        public static bool ShouldSuppressRelayedSpellDamage(GameObject attacker)
        {
            if (!_enabled || !_visualOnly || attacker == null || Time.unscaledTime > _visualReplayDamageSuppressUntil)
            {
                return false;
            }

            GameObject owner = TryGetSpellOwner(attacker);
            if (_visualReplayOwner != null
                && (ReferenceEquals(attacker, _visualReplayOwner) || ReferenceEquals(owner, _visualReplayOwner)))
            {
                return true;
            }

            PlayerFarming ownerPlayer = owner != null ? owner.GetComponent<PlayerFarming>() : null;
            PlayerFarming attackerPlayer = attacker.GetComponent<PlayerFarming>();
            return IsBridgeOwnedBody(ownerPlayer) || IsBridgeOwnedBody(attackerPlayer);
        }

        public static void Configure(bool enabled, bool visualOnly)
        {
            _enabled = enabled;
            _visualOnly = visualOnly;
            _localSequence = 0;
            _nextMissRecordAt = 0f;
            _overlayLine = enabled ? "spell relay: waiting" : "spell relay: disabled";
            Reset();
            WorldTrace.Record("phase11.spell.relay.config", "enabled=" + enabled + " visualOnly=" + visualOnly + " maxCastAgeMs=" + MaxCastAgeMs);
        }

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeSpellSnapshot.Empty;
            }
            AppliedSignatures.Clear();
            AppliedOrder.Clear();
        }

        public static bool ShouldSuppressRelayedCurseInput(PlayerFarming player)
        {
            return _enabled && player != null
                && (BridgeRemoteP2Driver.ShouldBlockLocalInput(player) || BridgeRemoteHostMirror.ShouldBlockLocalInput(player));
        }

        public static void RecordLocalCast(
            PlayerSpells spells,
            EquipmentType curseType,
            bool autoAim,
            bool consumeAmmo,
            bool wasSpell,
            bool smallScale,
            GameObject shooter,
            float damageMultiplier,
            bool isFromFamiliar)
        {
            if (!_enabled || _isReplaying || spells == null || smallScale || isFromFamiliar)
            {
                return;
            }

            PlayerFarming player = spells.playerFarming;
            if (player == null || !ReferenceEquals(player, PlayerFarming.Instance))
            {
                return;
            }
            if (shooter != null && shooter != player.gameObject)
            {
                return;
            }

            Vector3 targetOffset = Vector3.zero;
            try
            {
                if (TargetPositionField != null)
                {
                    targetOffset = (Vector3)TargetPositionField.GetValue(spells);
                }
            }
            catch
            {
            }

            int sequence = ++_localSequence;
            string facing = player.state != null ? WorldTrace.FormatFloat(player.state.facingAngle) : "unknown";
            string look = player.state != null ? WorldTrace.FormatFloat(player.state.LookAngle) : "unknown";
            WorldTrace.Record(
                "sync.spell_cast",
                "clientId=" + Clean(DiagnosticsPlugin.ClientId)
                + " sessionId=" + Clean(DiagnosticsPlugin.SessionId)
                + " seq=" + sequence
                + " playerID=" + player.playerID
                + " curse=" + Clean(curseType.ToString())
                + " curseLevel=" + player.currentCurseLevel
                + " autoAim=" + autoAim
                + " consumeAmmo=" + consumeAmmo
                + " wasSpell=" + wasSpell
                + " damageMultiplier=" + WorldTrace.FormatFloat(damageMultiplier)
                + " facingAngle=" + facing
                + " lookAngle=" + look
                + " aimAngle=" + WorldTrace.FormatFloat(spells.AimAngle)
                + " targetOffset=" + WorldTrace.FormatVector(targetOffset)
                + " pos=" + WorldTrace.FormatVector(player.transform.position)
                + " scene=" + Clean(SceneManager.GetActiveScene().name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture))
                + " room=" + Clean(CurrentRoomToken()));
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeSpellCast[] casts = ParseCasts(message);
            lock (Sync)
            {
                _snapshot = new BridgeSpellSnapshot(DateTimeOffset.UtcNow, source ?? "unknown", casts);
            }
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "spell relay: disabled";
                return;
            }

            BridgeSpellSnapshot snapshot = Snapshot();
            if (!snapshot.HasPacket)
            {
                _overlayLine = "spell relay: waiting";
                return;
            }

            int pending = 0;
            int applied = 0;
            for (int i = 0; i < snapshot.Casts.Length; i++)
            {
                BridgeSpellCast cast = snapshot.Casts[i];
                if (cast == null || string.Equals(cast.SourceClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (AppliedSignatures.Contains(cast.Signature) || ParseFloat(cast.AgeMs, MaxCastAgeMs + 1f) > MaxCastAgeMs)
                {
                    continue;
                }

                pending++;
                if (TryApplyCast(cast))
                {
                    MarkApplied(cast.Signature);
                    applied++;
                }
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            _overlayLine = "spell relay: packet=" + snapshot.Casts.Length
                + " pending=" + pending
                + " applied=" + applied
                + " age=" + ageSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        private static bool TryApplyCast(BridgeSpellCast cast)
        {
            if (!SpaceMatches(cast))
            {
                RecordMiss(cast, "space_mismatch");
                return false;
            }

            PlayerFarming bridgeBody = BridgeCoopReservation.TryFindReservedP2();
            if (bridgeBody == null || bridgeBody.gameObject == null || !bridgeBody.gameObject.activeInHierarchy)
            {
                RecordMiss(cast, "bridge_body_unavailable");
                return false;
            }

            bool sourceIsHost = string.Equals(cast.SourceRole, "host-lamb", StringComparison.Ordinal);
            bool sourceIsRemote = string.Equals(cast.SourceRole, "remote-p2", StringComparison.Ordinal);
            if ((!sourceIsHost || !BridgeRemoteHostMirror.ShouldBlockLocalInput(bridgeBody))
                && (!sourceIsRemote || !BridgeRemoteP2Driver.ShouldBlockLocalInput(bridgeBody)))
            {
                RecordMiss(cast, "role_body_mismatch_" + Clean(cast.SourceRole));
                return false;
            }

            EquipmentType curse;
            int curseLevel;
            if (!Enum.TryParse(cast.Curse, true, out curse) || curse == EquipmentType.None
                || !int.TryParse(cast.CurseLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out curseLevel))
            {
                RecordMiss(cast, "bad_curse_" + Clean(cast.Curse));
                return false;
            }

            PlayerSpells spells = bridgeBody.playerSpells;
            if (spells == null)
            {
                RecordMiss(cast, "player_spells_missing");
                return false;
            }

            try
            {
                spells.Init();
                if (bridgeBody.currentCurse != curse || bridgeBody.currentCurseLevel != curseLevel)
                {
                    spells.SetSpell(curse, curseLevel);
                }

                float facing = ParseFloat(cast.FacingAngle, ParseFloat(cast.AimAngle, 0f));
                float aim = ParseFloat(cast.AimAngle, facing);
                if (bridgeBody.state != null)
                {
                    bridgeBody.state.facingAngle = facing;
                    bridgeBody.state.LookAngle = ParseFloat(cast.LookAngle, facing);
                }
                if (bridgeBody.playerController != null)
                {
                    bridgeBody.playerController.forceDir = facing;
                }
                spells.AimAngle = aim;

                Vector3 targetOffset;
                if (TargetPositionField != null && TryParseVector(cast.TargetOffset, out targetOffset))
                {
                    TargetPositionField.SetValue(spells, targetOffset);
                }

                bool autoAim = ParseBool(cast.AutoAim, false);
                bool wasSpell = ParseBool(cast.WasSpell, false);
                float replayDamageMultiplier = ParseFloat(cast.DamageMultiplier, 1f);
                if (_visualOnly && replayDamageMultiplier <= 0f)
                {
                    replayDamageMultiplier = 1f;
                }

                _isReplaying = true;
                if (_visualOnly)
                {
                    _visualReplayOwner = bridgeBody.gameObject;
                    _visualReplayDamageSuppressUntil = Time.unscaledTime + 8f;
                }

                spells.CastSpell(
                    curse,
                    autoAim,
                    false,
                    wasSpell,
                    false,
                    bridgeBody.gameObject,
                    replayDamageMultiplier,
                    false);

                WorldTrace.Record(
                    "phase11.spell.relay.applied",
                    "cast=" + Clean(cast.Signature)
                    + " role=" + Clean(cast.SourceRole)
                    + " curse=" + Clean(cast.Curse)
                    + " level=" + Clean(cast.CurseLevel)
                    + " aim=" + Clean(cast.AimAngle)
                    + " targetOffset=" + Clean(cast.TargetOffset)
                    + " visualOnly=" + _visualOnly
                    + " replayDamageMultiplier=" + WorldTrace.FormatFloat(replayDamageMultiplier)
                    + " body=" + Clean(WorldTrace.DescribePlayer(bridgeBody)));
                return true;
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase11.spell.relay.apply_error",
                    "cast=" + Clean(cast.Signature)
                    + " error=" + ex.GetType().Name + ":" + Clean(ex.Message));
                return false;
            }
            finally
            {
                _isReplaying = false;
            }
        }

        private static bool IsBridgeOwnedBody(PlayerFarming player)
        {
            return player != null
                && (BridgeRemoteP2Driver.ShouldBlockLocalInput(player) || BridgeRemoteHostMirror.ShouldBlockLocalInput(player));
        }

        private static GameObject TryGetSpellOwner(GameObject attacker)
        {
            try
            {
                ISpellOwning spellOwning = attacker.GetComponent<ISpellOwning>();
                if (spellOwning == null)
                {
                    spellOwning = attacker.GetComponentInParent<ISpellOwning>();
                }

                return spellOwning != null ? spellOwning.GetOwner() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool SpaceMatches(BridgeSpellCast cast)
        {
            string localScene = SceneManager.GetActiveScene().name;
            string localLocation = Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture) ?? "unknown";
            string localRoom = CurrentRoomToken();
            return TokenMatches(cast.Scene, localScene)
                && TokenMatches(cast.Location, localLocation)
                && TokenMatches(cast.Room, localRoom);
        }

        private static bool TokenMatches(string remote, string local)
        {
            return !IsKnownToken(remote)
                || !IsKnownToken(local)
                || string.Equals(Clean(remote), Clean(local), StringComparison.OrdinalIgnoreCase);
        }

        private static void MarkApplied(string signature)
        {
            if (!AppliedSignatures.Add(signature))
            {
                return;
            }
            AppliedOrder.Enqueue(signature);
            while (AppliedOrder.Count > MaxAppliedSignatures)
            {
                AppliedSignatures.Remove(AppliedOrder.Dequeue());
            }
        }

        private static void RecordMiss(BridgeSpellCast cast, string reason)
        {
            if (Time.unscaledTime < _nextMissRecordAt)
            {
                return;
            }
            _nextMissRecordAt = Time.unscaledTime + 1f;
            WorldTrace.Record("phase11.spell.relay.pending", "cast=" + Clean(cast.Signature) + " reason=" + Clean(reason));
        }

        private static BridgeSpellSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeSpellCast[] ParseCasts(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeSpellCast>();
            }
            int index = message.IndexOf("casts=", StringComparison.Ordinal);
            if (index < 0)
            {
                return Array.Empty<BridgeSpellCast>();
            }
            string payload = message.Substring(index + "casts=".Length).Trim();
            if (payload.Length == 0 || string.Equals(payload, "none", StringComparison.Ordinal))
            {
                return Array.Empty<BridgeSpellCast>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeSpellCast> casts = new List<BridgeSpellCast>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                BridgeSpellCast cast = ParseCast(entries[i]);
                if (IsKnownToken(cast.SourceClientId) && IsKnownToken(cast.Sequence))
                {
                    casts.Add(cast);
                }
            }
            return casts.ToArray();
        }

        private static BridgeSpellCast ParseCast(string entry)
        {
            BridgeSpellCast cast = new BridgeSpellCast();
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                int equals = parts[i].IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                string key = parts[i].Substring(0, equals);
                string value = parts[i].Substring(equals + 1);
                switch (key)
                {
                    case "source": cast.SourceClientId = value; break;
                    case "session": cast.SessionId = value; break;
                    case "role": cast.SourceRole = value; break;
                    case "seq": cast.Sequence = value; break;
                    case "playerID": cast.PlayerId = value; break;
                    case "curse": cast.Curse = value; break;
                    case "curseLevel": cast.CurseLevel = value; break;
                    case "autoAim": cast.AutoAim = value; break;
                    case "consumeAmmo": cast.ConsumeAmmo = value; break;
                    case "wasSpell": cast.WasSpell = value; break;
                    case "damageMultiplier": cast.DamageMultiplier = value; break;
                    case "facingAngle": cast.FacingAngle = value; break;
                    case "lookAngle": cast.LookAngle = value; break;
                    case "aimAngle": cast.AimAngle = value; break;
                    case "targetOffset": cast.TargetOffset = value; break;
                    case "pos": cast.Position = value; break;
                    case "scene": cast.Scene = value; break;
                    case "location": cast.Location = value; break;
                    case "room": cast.Room = value; break;
                    case "ageMs": cast.AgeMs = value; break;
                }
            }
            return cast;
        }

        private static bool TryParseVector(string value, out Vector3 vector)
        {
            vector = Vector3.zero;
            if (!IsKnownToken(value))
            {
                return false;
            }
            string trimmed = value.Trim('(', ')');
            string[] parts = trimmed.Split(',');
            float x;
            float y;
            float z = 0f;
            if (parts.Length < 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
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

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static string CurrentRoomToken()
        {
            try
            {
                return BridgeCombatAuthority.CurrentRoomKey();
            }
            catch
            {
                return "unknown";
            }
        }

        private static bool IsKnownToken(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
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

    internal sealed class BridgeSpellSnapshot
    {
        public static readonly BridgeSpellSnapshot Empty = new BridgeSpellSnapshot(DateTimeOffset.MinValue, "none", Array.Empty<BridgeSpellCast>());

        public BridgeSpellSnapshot(DateTimeOffset receivedUtc, string source, BridgeSpellCast[] casts)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            Casts = casts ?? Array.Empty<BridgeSpellCast>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public BridgeSpellCast[] Casts { get; }
        public bool HasPacket { get { return ReceivedUtc != DateTimeOffset.MinValue; } }

        public BridgeSpellSnapshot Copy()
        {
            BridgeSpellCast[] casts = new BridgeSpellCast[Casts.Length];
            for (int i = 0; i < Casts.Length; i++)
            {
                casts[i] = Casts[i].Copy();
            }
            return new BridgeSpellSnapshot(ReceivedUtc, Source, casts);
        }
    }

    internal sealed class BridgeSpellCast
    {
        public string SourceClientId { get; set; } = "unknown";
        public string SessionId { get; set; } = "unknown";
        public string SourceRole { get; set; } = "unknown";
        public string Sequence { get; set; } = "unknown";
        public string PlayerId { get; set; } = "unknown";
        public string Curse { get; set; } = "unknown";
        public string CurseLevel { get; set; } = "unknown";
        public string AutoAim { get; set; } = "unknown";
        public string ConsumeAmmo { get; set; } = "unknown";
        public string WasSpell { get; set; } = "unknown";
        public string DamageMultiplier { get; set; } = "1";
        public string FacingAngle { get; set; } = "unknown";
        public string LookAngle { get; set; } = "unknown";
        public string AimAngle { get; set; } = "unknown";
        public string TargetOffset { get; set; } = "unknown";
        public string Position { get; set; } = "unknown";
        public string Scene { get; set; } = "unknown";
        public string Location { get; set; } = "unknown";
        public string Room { get; set; } = "unknown";
        public string AgeMs { get; set; } = "unknown";

        public string Signature { get { return SourceClientId + "|" + SessionId + "|" + Sequence; } }

        public BridgeSpellCast Copy()
        {
            return (BridgeSpellCast)MemberwiseClone();
        }
    }
}
