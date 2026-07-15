using System;
using System.Globalization;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeCameraSplit
    {
        private static bool _enabled;
        private static bool _splitActive;
        private static float _splitDistance;
        private static float _returnDistance;
        private static string _overlayLine = "camera split: disabled";
        private static float _nextRecordAt;

        public static void Configure(bool enabled, float splitDistance, float returnDistance)
        {
            _enabled = enabled;
            _splitActive = false;
            _splitDistance = Mathf.Max(2f, splitDistance);
            _returnDistance = Mathf.Clamp(returnDistance, 1f, _splitDistance);
            _overlayLine = enabled ? "camera split: waiting" : "camera split: disabled";
            _nextRecordAt = 0f;
            WorldTrace.Record(
                "phase9.camera_split.config",
                "enabled=" + enabled
                + " splitDistance=" + WorldTrace.FormatFloat(_splitDistance)
                + " returnDistance=" + WorldTrace.FormatFloat(_returnDistance));
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                Release("disabled");
                _overlayLine = "camera split: disabled";
                return;
            }

            CameraFollowTarget cam = SafeCam();
            PlayerFarming local = FindLocalFocusPlayer();
            if (cam == null || local == null || local.CameraBone == null)
            {
                Release("missing_camera_or_players");
                _overlayLine = "camera split: waiting";
                return;
            }

            if (cam.TargetCamera != null || LetterBox.IsPlaying)
            {
                Release("camera_event");
                _overlayLine = "camera split: paused camera event";
                return;
            }

            PlayerFarming remote = FindFarthestOtherPlayer(local);
            if (remote == null)
            {
                string reason;
                if (ShouldForceLocalFocusForRemoteAway(out reason))
                {
                    ApplyLocalFocus(cam, local, reason);
                    return;
                }

                float relayDistance;
                if (ShouldForceLocalFocusForRemoteRelay(local, out relayDistance, out reason))
                {
                    ApplyLocalFocus(cam, local, reason + " dist=" + WorldTrace.FormatFloat(relayDistance));
                    return;
                }

                Release("missing_remote_player");
                _overlayLine = "camera split: waiting";
                return;
            }

            float distance = Vector3.Distance(local.transform.position, remote.transform.position);
            if (!_splitActive && distance < _splitDistance)
            {
                _overlayLine = "camera split: vanilla dist=" + WorldTrace.FormatFloat(distance);
                return;
            }

            if (_splitActive && distance <= _returnDistance)
            {
                Release("distance_return_" + WorldTrace.FormatFloat(distance));
                _overlayLine = "camera split: vanilla dist=" + WorldTrace.FormatFloat(distance);
                return;
            }

            ApplyLocalFocus(cam, local, remote, distance);
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        private static void ApplyLocalFocus(CameraFollowTarget cam, PlayerFarming local, PlayerFarming remote, float distance)
        {
            bool first = !_splitActive;
            _splitActive = true;

            try
            {
                cam.ClearAllTargets();
                cam.AddTarget(local.CameraBone, 1f);
                _overlayLine = "camera split: local p" + local.playerID + " dist=" + WorldTrace.FormatFloat(distance);

                float now = Time.unscaledTime;
                if (first || now >= _nextRecordAt)
                {
                    _nextRecordAt = now + 2f;
                    WorldTrace.Record(
                        first ? "phase9.camera_split.enabled" : "phase9.camera_split.hold",
                        "distance=" + WorldTrace.FormatFloat(distance)
                        + " local=" + Clean(WorldTrace.DescribePlayer(local))
                        + " remote=" + Clean(WorldTrace.DescribePlayer(remote)));
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase9.camera_split.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void ApplyLocalFocus(CameraFollowTarget cam, PlayerFarming local, string reason)
        {
            bool first = !_splitActive;
            _splitActive = true;

            try
            {
                cam.ClearAllTargets();
                cam.AddTarget(local.CameraBone, 1f);
                _overlayLine = "camera split: local p" + local.playerID + " " + Clean(reason);

                float now = Time.unscaledTime;
                if (first || now >= _nextRecordAt)
                {
                    _nextRecordAt = now + 2f;
                    WorldTrace.Record(
                        first ? "phase9.camera_split.enabled" : "phase9.camera_split.hold",
                        "reason=" + Clean(reason)
                        + " local=" + Clean(WorldTrace.DescribePlayer(local)));
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase9.camera_split.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void Release(string reason)
        {
            if (!_splitActive)
            {
                return;
            }

            _splitActive = false;
            try
            {
                GameManager manager = GameManager.GetInstance();
                if (manager != null)
                {
                    manager.RemoveAllFromCamera();
                    manager.AddPlayersToCamera();
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase9.camera_split.release_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }

            WorldTrace.Record("phase9.camera_split.disabled", "reason=" + Clean(reason));
        }

        private static PlayerFarming FindLocalFocusPlayer()
        {
            try
            {
                PlayerFarming best = null;
                if (PlayerFarming.players != null)
                {
                    for (int i = 0; i < PlayerFarming.players.Count; i++)
                    {
                        PlayerFarming player = PlayerFarming.players[i];
                        if (!IsUsableFocusPlayer(player) || IsBridgeOwnedPlayer(player))
                        {
                            continue;
                        }

                        if (player.playerID == 0)
                        {
                            return player;
                        }

                        if (best == null || player.isLamb)
                        {
                            best = player;
                        }
                    }
                }

                if (best != null)
                {
                    return best;
                }

                if (IsUsableFocusPlayer(PlayerFarming.Instance) && !IsBridgeOwnedPlayer(PlayerFarming.Instance))
                {
                    return PlayerFarming.Instance;
                }

                if (PlayerFarming.players != null)
                {
                    for (int i = 0; i < PlayerFarming.players.Count; i++)
                    {
                        PlayerFarming player = PlayerFarming.players[i];
                        if (IsUsableFocusPlayer(player))
                        {
                            return player;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static PlayerFarming FindFarthestOtherPlayer(PlayerFarming local)
        {
            if (local == null)
            {
                return null;
            }

            PlayerFarming farthest = null;
            float farthestDistance = 0f;
            try
            {
                if (PlayerFarming.players == null)
                {
                    return null;
                }

                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    PlayerFarming player = PlayerFarming.players[i];
                    if (!IsUsableFocusPlayer(player) || ReferenceEquals(player, local))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(local.transform.position, player.transform.position);
                    if (farthest == null || distance > farthestDistance)
                    {
                        farthest = player;
                        farthestDistance = distance;
                    }
                }
            }
            catch
            {
                return null;
            }

            return farthest;
        }

        private static bool ShouldForceLocalFocusForRemoteAway(out string reason)
        {
            reason = "";
            try
            {
                BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
                if (roster == null || !roster.HasRoster || roster.Clients == null)
                {
                    return false;
                }

                BridgeRosterClient self = null;
                BridgeRosterClient remote = null;
                for (int i = 0; i < roster.Clients.Length; i++)
                {
                    BridgeRosterClient client = roster.Clients[i];
                    if (client == null)
                    {
                        continue;
                    }

                    if (string.Equals(client.ClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                    {
                        self = client;
                        continue;
                    }

                    if (remote == null || IsPreferredRemote(self, client))
                    {
                        remote = client;
                    }
                }

                if (self == null || remote == null || !IsFresh(remote.AgeMs))
                {
                    return false;
                }

                string localScene = NormalizeToken(self.Scene);
                string remoteScene = NormalizeToken(remote.Scene);
                if (IsKnownToken(localScene) && IsKnownToken(remoteScene) && !string.Equals(localScene, remoteScene, StringComparison.Ordinal))
                {
                    reason = "remote-away scene " + localScene + "!=" + remoteScene;
                    return true;
                }

                string localLocation = NormalizeToken(self.Location);
                string remoteLocation = NormalizeToken(remote.Location);
                if (IsKnownToken(localLocation) && IsKnownToken(remoteLocation) && !string.Equals(localLocation, remoteLocation, StringComparison.Ordinal))
                {
                    reason = "remote-away location " + localLocation + "!=" + remoteLocation;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool ShouldForceLocalFocusForRemoteRelay(PlayerFarming local, out float distance, out string reason)
        {
            distance = 0f;
            reason = "";
            if (local == null || local.transform == null)
            {
                return false;
            }

            try
            {
                BridgeRemotePlayerSnapshot snapshot = BridgeRemotePlayerState.Snapshot();
                if (snapshot == null || !snapshot.HasPacket || snapshot.Players == null)
                {
                    return false;
                }

                BridgeRosterClient self = FindSelfClient();
                BridgeRemotePlayer remote = FindPreferredRemoteRelay(snapshot, self);
                if (remote == null || !IsFresh(remote.AgeMs))
                {
                    return false;
                }

                Vector3 remotePosition;
                if (!TryParsePosition(remote.Position, out remotePosition))
                {
                    return false;
                }

                distance = Vector3.Distance(local.transform.position, remotePosition);
                if (!_splitActive && distance < _splitDistance)
                {
                    return false;
                }

                if (_splitActive && distance <= _returnDistance)
                {
                    return false;
                }

                reason = "remote-relay p" + Clean(remote.PlayerId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static BridgeRosterClient FindSelfClient()
        {
            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            if (roster == null || roster.Clients == null)
            {
                return null;
            }

            for (int i = 0; i < roster.Clients.Length; i++)
            {
                BridgeRosterClient client = roster.Clients[i];
                if (client != null && string.Equals(client.ClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                {
                    return client;
                }
            }

            return null;
        }

        private static BridgeRemotePlayer FindPreferredRemoteRelay(BridgeRemotePlayerSnapshot snapshot, BridgeRosterClient self)
        {
            BridgeRemotePlayer fallback = null;
            string preferredRole = null;
            if (self != null && string.Equals(self.Role, "host-lamb", StringComparison.Ordinal))
            {
                preferredRole = "remote-p2";
            }
            else if (self != null && string.Equals(self.Role, "remote-p2", StringComparison.Ordinal))
            {
                preferredRole = "host-lamb";
            }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player == null || !string.Equals(player.PlayerId, "0", StringComparison.Ordinal))
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = player;
                }

                if (!string.IsNullOrEmpty(preferredRole) && string.Equals(player.Role, preferredRole, StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return fallback;
        }

        private static bool IsPreferredRemote(BridgeRosterClient self, BridgeRosterClient candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (self != null && string.Equals(self.Role, "host-lamb", StringComparison.Ordinal))
            {
                return string.Equals(candidate.Role, "remote-p2", StringComparison.Ordinal);
            }

            if (self != null && string.Equals(self.Role, "remote-p2", StringComparison.Ordinal))
            {
                return string.Equals(candidate.Role, "host-lamb", StringComparison.Ordinal);
            }

            return string.Equals(candidate.Role, "host-lamb", StringComparison.Ordinal)
                || string.Equals(candidate.Role, "remote-p2", StringComparison.Ordinal);
        }

        private static bool IsFresh(string ageMs)
        {
            float age;
            return float.TryParse(ageMs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out age)
                && age <= 2500f;
        }

        private static bool TryParsePosition(string value, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string cleaned = value.Trim();
            if (cleaned.StartsWith("(", StringComparison.Ordinal) && cleaned.EndsWith(")", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
            }

            string[] parts = cleaned.Split(',');
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

            position = new Vector3(x, y, z);
            return true;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            return value.Trim().Replace(" ", "_");
        }

        private static bool IsKnownToken(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsableFocusPlayer(PlayerFarming player)
        {
            return player != null
                && player.gameObject != null
                && player.gameObject.activeInHierarchy
                && player.transform != null
                && player.CameraBone != null;
        }

        private static bool IsBridgeOwnedPlayer(PlayerFarming player)
        {
            if (player == null)
            {
                return false;
            }

            return BridgeRemoteP2Driver.ShouldBlockLocalInput(player)
                || BridgeRemoteHostMirror.ShouldBlockLocalInput(player);
        }

        private static CameraFollowTarget SafeCam()
        {
            try
            {
                GameManager manager = GameManager.GetInstance();
                if (manager != null && manager.CamFollowTarget != null)
                {
                    return manager.CamFollowTarget;
                }

                return CameraFollowTarget.Instance;
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
                return "null";
            }

            return value.Replace(" ", "_").Replace("\r", "_").Replace("\n", "_");
        }
    }
}
