using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteHostMirror
    {
        private static bool _enabled;
        private static bool _autoSpawn;
        private static bool _blockTransitions;
        private static bool _ownsTemporaryDisableRemoval;
        private static float _maxAgeMs;
        private static float _snapDistance;
        private static float _correctionSpeed;
        private static float _holdGraceSeconds;
        private static float _nextRecordAt;
        private static float _nextSpawnAttemptAt;
        private static float _nextInactiveRecordAt;
        private static float _nextRemovalBlockRecordAt;
        private static float _nextAwayRecordAt;
        private static float _lastValidRelayAt;
        private static float _remoteAwaySince;
        private static string _lastOverlayLine = "remote host mirror: disabled";
        private static string _lastMode = "";
        private static string _lastRecordedSequence = "";
        private static string _lastAwayReason = "";
        private static PlayerFarming _mirrorP2;
        private static float _settleInputUntil;
        private static float _nextSettleRecordAt;
        private static bool _hiddenForRemoteAway;
        private static string _lastRemoteScene = "";
        private static string _lastRemoteLocation = "";
        private static string _lastRemoteRoom = "";
        private static bool _hasVirtualInput;
        private static bool _remoteInputValid;
        private static float _axisX;
        private static float _axisY;
        private static string _remoteState = "unknown";
        private static string _previousRemoteState = "unknown";
        private static string _lastInputEdgeSequence = "";
        private static bool _remoteAttackHeld;
        private static bool _remoteDodgeHeld;
        private static bool _remoteCurseHeld;
        private static bool _remoteHeavyHeld;
        private static float _dodgeDownUntil;
        private static float _attackDownUntil;
        private static float _curseDownUntil;
        private static float _curseUpUntil;
        private static float _heavyDownUntil;
        private static float _dodgeHeldUntil;
        private static float _curseHeldUntil;
        private static float _heavyHeldUntil;

        public static void Configure(bool enabled, bool autoSpawn, float maxAgeMs, float snapDistance, float correctionSpeed, float holdGraceSeconds, bool blockTransitions)
        {
            _enabled = enabled;
            _autoSpawn = autoSpawn;
            _maxAgeMs = Mathf.Max(100f, maxAgeMs);
            _snapDistance = Mathf.Max(0f, snapDistance);
            _correctionSpeed = Mathf.Max(0f, correctionSpeed);
            _holdGraceSeconds = Mathf.Max(0f, holdGraceSeconds);
            _blockTransitions = blockTransitions;
            _ownsTemporaryDisableRemoval = false;
            _nextRecordAt = 0f;
            _nextSpawnAttemptAt = 0f;
            _nextInactiveRecordAt = 0f;
            _nextRemovalBlockRecordAt = 0f;
            _nextAwayRecordAt = 0f;
            _lastValidRelayAt = -9999f;
            _remoteAwaySince = 0f;
            _lastOverlayLine = enabled ? "remote host mirror: waiting" : "remote host mirror: disabled";
            _lastMode = "";
            _lastRecordedSequence = "";
            _lastAwayReason = "";
            _mirrorP2 = null;
            _settleInputUntil = 0f;
            _nextSettleRecordAt = 0f;
            _hiddenForRemoteAway = false;
            ResetRemotePresenceTokens();
            _lastInputEdgeSequence = "";
            ClearVirtualInput();

            WorldTrace.Record(
                "phase7.config",
                "remoteHostVisualMirror=" + enabled
                + " autoSpawn=" + autoSpawn
                + " maxAgeMs=" + FormatFloat(_maxAgeMs)
                + " snapDistance=" + FormatFloat(_snapDistance)
                + " correctionSpeed=" + FormatFloat(_correctionSpeed)
                + " holdGraceSeconds=" + FormatFloat(_holdGraceSeconds)
                + " blockTransitions=" + blockTransitions);
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _lastOverlayLine = "remote host mirror: disabled";
                ReleaseTemporaryDisableRemoval("disabled");
                ClearVirtualInput();
                return;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self == null || !string.Equals(self.Role, "remote-p2", StringComparison.Ordinal))
            {
                _lastOverlayLine = "remote host mirror: not remote-p2";
                ReleaseTemporaryDisableRemoval("not_remote_p2");
                ClearVirtualInput();
                return;
            }

            BridgeRemotePlayerSnapshot playerSnapshot = BridgeRemotePlayerState.Snapshot();
            BridgeRemotePlayer remoteHost = FindRemoteHost(playerSnapshot);
            if (remoteHost == null)
            {
                if (TryHoldThroughRelayGap("no_host_motion"))
                {
                    return;
                }

                _lastOverlayLine = "remote host mirror: waiting for host-lamb motion";
                ReleaseTemporaryDisableRemoval("no_host_motion");
                ClearVirtualInput();
                return;
            }

            float ageMs = ParseFloat(remoteHost.AgeMs, _maxAgeMs + 1f);
            if (ageMs > _maxAgeMs)
            {
                if (TryHoldThroughRelayGap("stale_host_motion_" + FormatFloat(ageMs)))
                {
                    return;
                }

                _lastOverlayLine = "remote host mirror: stale host ageMs=" + FormatFloat(ageMs);
                ReleaseTemporaryDisableRemoval("stale_host_motion");
                ClearVirtualInput();
                return;
            }

            _lastValidRelayAt = Time.unscaledTime;
            PlayerFarming p2 = FindMirrorP2();
            EnsureTemporaryDisableRemoval("pre_spawn_or_active_mirror");

            if (p2 == null || p2.gameObject == null)
            {
                ClearVirtualInput();
                TryAutoSpawnMirror(self, remoteHost);
                _lastOverlayLine = "remote host mirror: waiting for local visual P2";
                return;
            }

            TrackMirrorP2(p2);

            BridgeRemoteInputSnapshot inputSnapshot = BridgeRemoteInputState.Snapshot();
            if (TryApplyHostInput(inputSnapshot, p2))
            {
                return;
            }

            if (HandleRemotePresence(p2, remoteHost.Scene, remoteHost.Location, remoteHost.Room, remoteHost.State, remoteHost.Position, "motion"))
            {
                return;
            }

            if (p2.gameObject == null || !p2.gameObject.activeSelf)
            {
                ClearVirtualInput();
                RecordInactiveMirrorIfNeeded(p2);
                _lastOverlayLine = HiddenOrWaitingOverlay();
                return;
            }

            ApplyHostMotion(remoteHost, p2, ageMs);
        }

        public static string OverlayLine()
        {
            return _enabled ? _lastOverlayLine : "";
        }

        public static bool TryGetAxis(PlayerFarming player, out float horizontal, out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (!_enabled || !_remoteInputValid || !_hasVirtualInput || !IsMirrorP2(player))
            {
                return false;
            }

            horizontal = _axisX;
            vertical = _axisY;
            return true;
        }

        public static bool ShouldBlockLocalInput(PlayerFarming player)
        {
            return _enabled && IsLocalRemoteP2() && LooksLikeMirrorP2(player);
        }

        public static bool TryGetDodgeButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsMirrorP2(player))
            {
                return false;
            }

            return held ? Time.unscaledTime <= _dodgeHeldUntil : Time.unscaledTime <= _dodgeDownUntil;
        }

        public static bool TryGetAttackButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsMirrorP2(player))
            {
                return false;
            }

            return !held && Time.unscaledTime <= _attackDownUntil;
        }

        public static bool TryGetCurseButton(PlayerFarming player, bool held, bool up)
        {
            if (!_enabled || !_remoteInputValid || !IsMirrorP2(player))
            {
                return false;
            }

            if (held)
            {
                return Time.unscaledTime <= _curseHeldUntil;
            }

            return up ? Time.unscaledTime <= _curseUpUntil : Time.unscaledTime <= _curseDownUntil;
        }

        public static bool TryGetHeavyAttackButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsMirrorP2(player))
            {
                return false;
            }

            return held ? Time.unscaledTime <= _heavyHeldUntil : Time.unscaledTime <= _heavyDownUntil;
        }

        public static bool ShouldBlockRemoteHostMirrorTransition(PlayerFarming player, string reason)
        {
            if (!_enabled || !_blockTransitions || !IsMirrorP2(player))
            {
                return false;
            }

            BridgeRemoteBodyGuard.CancelGoToForBridgeBody(player, "phase7.transition_blocked_" + reason);
            ForceSettledIdle(player, "transition_blocked_" + reason);
            StartInputSettle("transition_blocked_" + reason, 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase7.remote_host_mirror.transition_blocked",
                "reason=" + Clean(reason)
                + " player=" + Clean(WorldTrace.DescribePlayer(player))
                + " state=" + Clean(_remoteState));
            return true;
        }

        public static bool ShouldBlockCoopRemoval(PlayerFarming player, string source)
        {
            if (!_enabled || !IsLocalRemoteP2())
            {
                return false;
            }

            PlayerFarming mirror = FindMirrorP2();
            bool targetsMirror = player == null
                ? mirror != null || HasSecondPlayer()
                : LooksLikeMirrorP2(player);
            if (!targetsMirror)
            {
                return false;
            }

            EnsureTemporaryDisableRemoval("blocked_remove_" + source);
            RecordRemovalBlockIfNeeded(player ?? mirror, source);
            return true;
        }

        private static void TryAutoSpawnMirror(BridgeRosterClient self, BridgeRemotePlayer remoteHost)
        {
            if (!_autoSpawn || Time.unscaledTime < _nextSpawnAttemptAt)
            {
                return;
            }

            _nextSpawnAttemptAt = Time.unscaledTime + 3f;

            if (CoopManager.Instance == null || PlayerFarming.players == null || PlayerFarming.players.Count == 0)
            {
                return;
            }

            WorldTrace.Record(
                "phase7.remote_host_mirror.spawn.request",
                "role=" + Clean(self != null ? self.Role : "unknown")
                + " hostClient=" + Clean(remoteHost.ClientId)
                + " hostSeq=" + Clean(remoteHost.Sequence)
                + " hostPos=" + Clean(remoteHost.Position)
                + " slot=1 playEffects=True startingHealth=-1");

            try
            {
                EnsureTemporaryDisableRemoval("before_spawn");
                CoopManager.Instance.SpawnCoopPlayer(1, true, -1f);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase7.remote_host_mirror.spawn.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static bool TryApplyHostInput(BridgeRemoteInputSnapshot snapshot, PlayerFarming p2)
        {
            if (snapshot == null || !snapshot.HasPacket)
            {
                return false;
            }

            BridgeRemoteInput input = FindRemoteHostInput(snapshot);
            if (input == null)
            {
                return false;
            }

            float ageMs = ParseFloat(input.AgeMs, _maxAgeMs + 1f);
            if (ageMs > _maxAgeMs)
            {
                _lastOverlayLine = "remote host mirror: stale input ageMs=" + FormatFloat(ageMs);
                ClearVirtualInput();
                return true;
            }

            if (HandleRemotePresence(p2, input.Scene, input.Location, input.Room, input.State, input.Position, "input"))
            {
                return true;
            }

            if (p2.gameObject == null || !p2.gameObject.activeSelf)
            {
                _lastOverlayLine = HiddenOrWaitingOverlay();
                ClearVirtualInput();
                return true;
            }

            string inputState = string.IsNullOrEmpty(input.State) ? "unknown" : input.State;
            bool suppressInput = ShouldSuppressRemoteInput(inputState);
            if (suppressInput)
            {
                SuppressInputState();
            }
            else
            {
                _axisX = ClampAxis(ParseFloat(input.AxisX, 0f));
                _axisY = ClampAxis(ParseFloat(input.AxisY, 0f));
                Vector2 axis = new Vector2(_axisX, _axisY);
                if (axis.sqrMagnitude > 1f)
                {
                    axis.Normalize();
                    _axisX = axis.x;
                    _axisY = axis.y;
                }
            }

            _hasVirtualInput = !suppressInput && (Mathf.Abs(_axisX) > 0.05f || Mathf.Abs(_axisY) > 0.05f);
            _remoteInputValid = true;
            _remoteState = inputState;
            bool newInputSequence = MarkInputSequence(input.Sequence);
            _remoteAttackHeld = false;
            _remoteDodgeHeld = !suppressInput && ParseBool(input.DodgeHeld);
            _remoteCurseHeld = !suppressInput && ParseBool(input.CurseHeld);
            _remoteHeavyHeld = !suppressInput && ParseBool(input.HeavyHeld);
            BridgeRemoteFrameState.Apply(p2, input, "remote_host_input");

            if (suppressInput)
            {
                ClearButtonTimers();
            }

            if (!suppressInput && newInputSequence && ParseBool(input.DodgeDown))
            {
                _dodgeDownUntil = Time.unscaledTime + 0.1f;
            }

            if (!suppressInput && newInputSequence && ParseBool(input.AttackDown))
            {
                _attackDownUntil = Time.unscaledTime + 0.045f;
            }

            if (!suppressInput && newInputSequence && ParseBool(input.CurseDown))
            {
                _curseDownUntil = Time.unscaledTime + 0.06f;
            }

            if (!suppressInput && newInputSequence && ParseBool(input.CurseUp))
            {
                _curseUpUntil = Time.unscaledTime + 0.06f;
            }

            if (!suppressInput && newInputSequence && ParseBool(input.HeavyDown))
            {
                _heavyDownUntil = Time.unscaledTime + 0.06f;
            }

            if (!suppressInput && newInputSequence && _remoteDodgeHeld)
            {
                _dodgeHeldUntil = Time.unscaledTime + 0.12f;
            }

            if (!suppressInput && newInputSequence && _remoteCurseHeld)
            {
                _curseHeldUntil = Time.unscaledTime + 0.12f;
            }

            if (!suppressInput && newInputSequence && _remoteHeavyHeld)
            {
                _heavyHeldUntil = Time.unscaledTime + 0.12f;
            }

            Vector3 before = p2.transform.position;
            Vector3 after = before;
            float distance = 0f;
            string mode = suppressInput ? "input-suppressed" : "input";
            Vector3 target;
            if (TryParsePosition(input.Position, out target))
            {
                distance = Vector3.Distance(before, target);
                mode = CorrectPosition(p2, before, target, distance, _hasVirtualInput, _remoteDodgeHeld || (!suppressInput && ParseBool(input.DodgeDown)), suppressInput ? "input-suppressed" : "input", inputState, out after);
            }

            _lastOverlayLine = "remote host mirror: " + mode
                + " seq=" + input.Sequence
                + " ageMs=" + FormatFloat(ageMs)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " atk=" + ParseBool(input.AttackDown) + "/" + _remoteAttackHeld
                + " dodge=" + ParseBool(input.DodgeDown) + "/" + _remoteDodgeHeld
                + " curse=" + ParseBool(input.CurseDown) + "/" + _remoteCurseHeld + "/" + ParseBool(input.CurseUp)
                + " heavy=" + ParseBool(input.HeavyDown) + "/" + _remoteHeavyHeld
                + " dist=" + FormatFloat(distance)
                + " state=" + Clean(input.State);

            RecordApplyIfNeeded(input.ClientId, input.Sequence, before, after, distance, mode, ageMs, input.State);
            return true;
        }

        private static void ApplyHostMotion(BridgeRemotePlayer remoteHost, PlayerFarming p2, float ageMs)
        {
            Vector3 target;
            if (!TryParsePosition(remoteHost.Position, out target))
            {
                _lastOverlayLine = "remote host mirror: bad host pos=" + Clean(remoteHost.Position);
                ClearVirtualInput();
                return;
            }

            Vector3 before = p2.transform.position;
            float distance = Vector3.Distance(before, target);
            bool suppressMotionInput = ShouldSuppressRemoteInput(remoteHost.State);
            if (suppressMotionInput)
            {
                SuppressInputState();
            }
            else
            {
                ApplyVirtualAxis(target - before, distance);
            }

            Vector3 after;
            string mode = CorrectPosition(p2, before, target, distance, !suppressMotionInput && _hasVirtualInput, !suppressMotionInput && string.Equals(remoteHost.State, "Dodging", StringComparison.Ordinal), suppressMotionInput ? "settle" : "motion", remoteHost.State, out after);
            _remoteInputValid = true;
            if (suppressMotionInput)
            {
                UpdateSuppressedRemoteState(remoteHost.State);
            }
            else
            {
                UpdateRemoteButtonEdges(remoteHost.State);
            }

            _lastOverlayLine = "remote host mirror: " + mode
                + " seq=" + remoteHost.Sequence
                + " ageMs=" + FormatFloat(ageMs)
                + " dist=" + FormatFloat(distance)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " state=" + Clean(remoteHost.State)
                + " target=" + Clean(remoteHost.Position);

            RecordApplyIfNeeded(remoteHost.ClientId, remoteHost.Sequence, before, after, distance, mode, ageMs, remoteHost.State);
        }

        private static string CorrectPosition(PlayerFarming p2, Vector3 before, Vector3 target, float distance, bool hasInputAxis, bool remoteDodging, string prefix, string remoteState, out Vector3 after)
        {
            after = before;
            if (_snapDistance > 0f && distance > HardSnapDistance())
            {
                p2.transform.position = target;
                after = p2.transform.position;
                return prefix + "-snap";
            }

            float deadband = IdleCorrectionDeadband(remoteState, hasInputAxis, remoteDodging);
            if (_correctionSpeed <= 0f || distance < deadband)
            {
                after = p2.transform.position;
                return prefix;
            }

            float speed = _correctionSpeed;
            if (hasInputAxis)
            {
                speed *= 0.5f;
            }

            if (remoteDodging)
            {
                speed *= 0.25f;
            }
            else if (distance > 5f)
            {
                speed *= 3f;
            }
            else if (distance > 2f)
            {
                speed *= 2f;
            }

            float deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
            p2.transform.position = Vector3.MoveTowards(before, target, speed * deltaTime);
            after = p2.transform.position;
            return prefix + "-correct";
        }

        private static float IdleCorrectionDeadband(string remoteState, bool hasInputAxis, bool remoteDodging)
        {
            return IsIdleLikeState(remoteState) && !hasInputAxis && !remoteDodging ? 0.7f : 0.25f;
        }

        private static bool IsIdleLikeState(string state)
        {
            return string.IsNullOrEmpty(state)
                || string.Equals(state, "Idle", StringComparison.Ordinal)
                || string.Equals(state, "None", StringComparison.Ordinal)
                || string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyVirtualAxis(Vector3 delta, float distance)
        {
            if (distance < 0.15f)
            {
                _axisX = 0f;
                _axisY = 0f;
                _hasVirtualInput = false;
                return;
            }

            Vector2 axis = new Vector2(delta.x, delta.y);
            if (axis.sqrMagnitude > 1f)
            {
                axis.Normalize();
            }

            _axisX = axis.x;
            _axisY = axis.y;
            _hasVirtualInput = Mathf.Abs(_axisX) > 0.05f || Mathf.Abs(_axisY) > 0.05f;
        }

        private static void TrackMirrorP2(PlayerFarming p2)
        {
            if (ReferenceEquals(_mirrorP2, p2))
            {
                return;
            }

            _mirrorP2 = p2;
            _lastMode = "";
            ResetRemotePresenceTokens();
            ResetInputTracking();
            StartInputSettle("body_changed", 0.75f);
            WorldTrace.Record("phase7.remote_host_mirror.body_changed", "player=" + Clean(WorldTrace.DescribePlayer(p2)));
        }

        private static bool HandleRemotePresence(PlayerFarming p2, string scene, string location, string room, string state, string position, string source)
        {
            string reason;
            if (ShouldTreatRemoteAsAway(scene, location, room, state, out reason))
            {
                HideMirrorP2(p2, reason, source);
                _lastOverlayLine = "remote host mirror: hidden remote away " + Clean(reason);
                ClearVirtualInput();
                return true;
            }

            if (RestoreMirrorP2IfNeeded(p2, position, source))
            {
                return true;
            }

            return TrySnapOnRemoteSpaceChange(p2, scene, location, room, position, source);
        }

        private static bool ShouldTreatRemoteAsAway(string scene, string location, string room, string state, out string reason)
        {
            reason = "";
            string candidate;
            if (!TryBuildRemoteAwayReason(scene, location, room, state, out candidate))
            {
                _remoteAwaySince = 0f;
                _lastAwayReason = "";
                return false;
            }

            if (_remoteAwaySince <= 0f || !string.Equals(_lastAwayReason, candidate, StringComparison.Ordinal))
            {
                _remoteAwaySince = Time.unscaledTime;
                _lastAwayReason = candidate;
            }

            if (Time.unscaledTime - _remoteAwaySince < 0.45f)
            {
                return false;
            }

            reason = candidate;
            return true;
        }

        private static bool TryBuildRemoteAwayReason(string scene, string location, string room, string state, out string reason)
        {
            reason = "";
            if (IsKnownToken(scene))
            {
                string remoteScene = NormalizeToken(scene);
                string localScene = NormalizeToken(SafeLocalScene());
                if (IsKnownToken(localScene) && IsKnownToken(remoteScene) && !string.Equals(remoteScene, localScene, StringComparison.Ordinal))
                {
                    reason = "scene " + localScene + "!=" + remoteScene;
                    return true;
                }
            }

            if (IsKnownToken(location))
            {
                string remoteLocation = NormalizeToken(location);
                string localLocation = NormalizeToken(SafeLocalLocation());
                if (IsKnownToken(localLocation) && IsKnownToken(remoteLocation) && !string.Equals(remoteLocation, localLocation, StringComparison.Ordinal))
                {
                    reason = "location " + localLocation + "!=" + remoteLocation;
                    return true;
                }
            }

            if (IsKnownToken(room))
            {
                string remoteRoom = NormalizeToken(room);
                string localRoom = NormalizeToken(SafeLocalRoom());
                if (IsKnownToken(localRoom) && IsKnownToken(remoteRoom) && !string.Equals(remoteRoom, localRoom, StringComparison.Ordinal))
                {
                    reason = "room " + localRoom + "!=" + remoteRoom;
                    return true;
                }
            }

            if (IsRemoteAwayTransitionState(state))
            {
                reason = "state " + state;
                return true;
            }

            return false;
        }

        private static void HideMirrorP2(PlayerFarming p2, string reason, string source)
        {
            bool firstHide = !_hiddenForRemoteAway;
            _hiddenForRemoteAway = true;
            _lastMode = "remote-away";
            StartInputSettle("away_hidden", 0.75f);
            if (p2 != null && p2.gameObject != null && p2.gameObject.activeSelf)
            {
                p2.gameObject.SetActive(false);
            }

            float now = Time.unscaledTime;
            if (firstHide || now >= _nextAwayRecordAt)
            {
                _nextAwayRecordAt = now + 2f;
                WorldTrace.Record(
                    "phase7.remote_host_mirror.away_hidden",
                    "source=" + Clean(source)
                    + " reason=" + Clean(reason)
                    + " player=" + Clean(WorldTrace.DescribePlayer(p2)));
            }
        }

        private static bool RestoreMirrorP2IfNeeded(PlayerFarming p2, string position, string source)
        {
            if (!_hiddenForRemoteAway)
            {
                return false;
            }

            Vector3 target;
            bool hasTarget = TryParsePosition(position, out target);
            if (hasTarget && p2 != null && p2.transform != null)
            {
                p2.transform.position = target;
            }

            if (p2 != null && p2.gameObject != null && !p2.gameObject.activeSelf)
            {
                p2.gameObject.SetActive(true);
            }

            ForceSettledIdle(p2, "away_restored");
            _hiddenForRemoteAway = false;
            _remoteAwaySince = 0f;
            _lastAwayReason = "";
            ResetRemotePresenceTokens();
            StartInputSettle("away_restored", 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase7.remote_host_mirror.away_restored",
                "source=" + Clean(source)
                + " snapped=" + hasTarget
                + " target=" + Clean(position)
                + " player=" + Clean(WorldTrace.DescribePlayer(p2)));
            return true;
        }

        private static bool TrySnapOnRemoteSpaceChange(PlayerFarming p2, string scene, string location, string room, string position, string source)
        {
            if (p2 == null || p2.transform == null || RemoteSpaceMismatchesLocal(scene, location, room))
            {
                return false;
            }

            string remoteScene = NormalizeKnownToken(scene);
            string remoteLocation = NormalizeKnownToken(location);
            string remoteRoom = NormalizeKnownToken(room);
            bool changed = TokenChanged(_lastRemoteScene, remoteScene)
                || TokenChanged(_lastRemoteLocation, remoteLocation)
                || TokenChanged(_lastRemoteRoom, remoteRoom);

            bool hadPreviousToken = IsKnownToken(_lastRemoteScene)
                || IsKnownToken(_lastRemoteLocation)
                || IsKnownToken(_lastRemoteRoom);

            UpdateRemotePresenceTokens(remoteScene, remoteLocation, remoteRoom);
            if (!changed || !hadPreviousToken)
            {
                return false;
            }

            Vector3 target;
            if (!TryParsePosition(position, out target))
            {
                StartInputSettle("remote_space_changed_no_position", 0.85f);
                ClearVirtualInput();
                return true;
            }

            Vector3 before = p2.transform.position;
            p2.transform.position = target;
            ForceSettledIdle(p2, "remote_space_changed");
            _lastMode = "transition-snap";
            StartInputSettle("remote_space_changed", 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase7.remote_host_mirror.transition_snap",
                "source=" + Clean(source)
                + " scene=" + Clean(remoteScene)
                + " location=" + Clean(remoteLocation)
                + " room=" + Clean(remoteRoom)
                + " before=" + Clean(WorldTrace.FormatVector(before))
                + " target=" + Clean(position));
            return true;
        }

        private static void ForceSettledIdle(PlayerFarming player, string reason)
        {
            if (player == null)
            {
                return;
            }

            try
            {
                if (player.GoToAndStopping)
                {
                    BridgeRemoteBodyGuard.CancelGoToForBridgeBody(player, "phase7.idle_settle_" + reason);
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase7.remote_host_mirror.idle_settle_error",
                    "abort reason=" + Clean(reason)
                    + " " + ex.GetType().Name + ": " + Clean(ex.Message)
                    + " player=" + Clean(WorldTrace.DescribePlayer(player)));
            }

            try
            {
                if (player.state != null && ShouldForceIdleAfterSnap(player.state.CURRENT_STATE))
                {
                    player.state.CURRENT_STATE = StateMachine.State.Idle;
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase7.remote_host_mirror.idle_settle_error",
                    "state reason=" + Clean(reason)
                    + " " + ex.GetType().Name + ": " + Clean(ex.Message)
                    + " player=" + Clean(WorldTrace.DescribePlayer(player)));
            }

            try
            {
                object animator = player.simpleSpineAnimator;
                if (animator != null)
                {
                    animator.GetType().GetMethod("Animate", new[] { typeof(string), typeof(int), typeof(bool) })
                        ?.Invoke(animator, new object[] { "idle", 0, true });
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase7.remote_host_mirror.idle_settle_error",
                    "anim reason=" + Clean(reason)
                    + " " + ex.GetType().Name + ": " + Clean(ex.Message)
                    + " player=" + Clean(WorldTrace.DescribePlayer(player)));
            }
        }

        private static bool ShouldForceIdleAfterSnap(StateMachine.State state)
        {
            return state == StateMachine.State.Moving
                || state == StateMachine.State.Moving_Winter
                || state == StateMachine.State.InActive
                || state == StateMachine.State.CustomAnimation
                || state == StateMachine.State.SpawnIn
                || state == StateMachine.State.SpawnOut
                || state == StateMachine.State.Teleporting;
        }

        private static bool RemoteSpaceMismatchesLocal(string scene, string location, string room)
        {
            string reason;
            return TryBuildRemoteAwayReason(scene, location, room, "", out reason);
        }

        private static bool TokenChanged(string previous, string current)
        {
            return IsKnownToken(previous)
                && IsKnownToken(current)
                && !string.Equals(previous, current, StringComparison.Ordinal);
        }

        private static void UpdateRemotePresenceTokens(string scene, string location, string room)
        {
            string remoteScene = NormalizeKnownToken(scene);
            if (IsKnownToken(remoteScene))
            {
                _lastRemoteScene = remoteScene;
            }

            string remoteLocation = NormalizeKnownToken(location);
            if (IsKnownToken(remoteLocation))
            {
                _lastRemoteLocation = remoteLocation;
            }

            string remoteRoom = NormalizeKnownToken(room);
            if (IsKnownToken(remoteRoom))
            {
                _lastRemoteRoom = remoteRoom;
            }
        }

        private static void ResetRemotePresenceTokens()
        {
            _lastRemoteScene = "";
            _lastRemoteLocation = "";
            _lastRemoteRoom = "";
        }

        private static string NormalizeKnownToken(string value)
        {
            return IsKnownToken(value) ? NormalizeToken(value) : "";
        }

        private static string HiddenOrWaitingOverlay()
        {
            return _hiddenForRemoteAway
                ? "remote host mirror: hidden remote away " + Clean(_lastAwayReason)
                : "remote host mirror: holding inactive visual P2";
        }

        private static void EnsureTemporaryDisableRemoval(string reason)
        {
            try
            {
                if (CoopManager.Instance == null)
                {
                    return;
                }

                if (CoopManager.Instance.temporaryDisableRemoval)
                {
                    _ownsTemporaryDisableRemoval = true;
                    return;
                }

                if (!CoopManager.Instance.temporaryDisableRemoval)
                {
                    CoopManager.Instance.temporaryDisableRemoval = true;
                    _ownsTemporaryDisableRemoval = true;
                    WorldTrace.Record("phase7.remote_host_mirror.hold_enabled", "reason=" + Clean(reason));
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase7.remote_host_mirror.hold_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static bool TryHoldThroughRelayGap(string reason)
        {
            PlayerFarming p2 = FindMirrorP2();
            if (p2 == null || _lastValidRelayAt < 0f)
            {
                return false;
            }

            float age = Time.unscaledTime - _lastValidRelayAt;
            if (age > _holdGraceSeconds)
            {
                return false;
            }

            EnsureTemporaryDisableRemoval("relay_grace_" + reason);
            ClearVirtualInput();
            _lastOverlayLine = "remote host mirror: holding relay grace age=" + FormatFloat(age) + " reason=" + Clean(reason);
            return true;
        }

        private static void RecordRemovalBlockIfNeeded(PlayerFarming player, string source)
        {
            float now = Time.unscaledTime;
            if (now < _nextRemovalBlockRecordAt)
            {
                return;
            }

            _nextRemovalBlockRecordAt = now + 1f;
            WorldTrace.Record(
                "phase7.remote_host_mirror.remove_blocked",
                "source=" + Clean(source)
                + " player=" + Clean(WorldTrace.DescribePlayer(player))
                + " hold=" + _ownsTemporaryDisableRemoval
                + " coopTempDisable=" + SafeBool(() => CoopManager.Instance != null && CoopManager.Instance.temporaryDisableRemoval)
                + " role=remote-p2");
        }

        private static void RecordInactiveMirrorIfNeeded(PlayerFarming p2)
        {
            float now = Time.unscaledTime;
            if (now < _nextInactiveRecordAt)
            {
                return;
            }

            _nextInactiveRecordAt = now + 2f;
            WorldTrace.Record(
                "phase7.remote_host_mirror.inactive_body",
                "player=" + Clean(WorldTrace.DescribePlayer(p2))
                + " hold=" + _ownsTemporaryDisableRemoval
                + " coopTempDisable=" + SafeBool(() => CoopManager.Instance != null && CoopManager.Instance.temporaryDisableRemoval)
                + " state=" + Clean(SafeValue(() => p2.state != null ? p2.state.CURRENT_STATE.ToString() : "null")));
        }

        private static void ReleaseTemporaryDisableRemoval(string reason)
        {
            if (!_ownsTemporaryDisableRemoval)
            {
                return;
            }

            try
            {
                if (CoopManager.Instance != null)
                {
                    CoopManager.Instance.temporaryDisableRemoval = false;
                }

                _ownsTemporaryDisableRemoval = false;
                WorldTrace.Record("phase7.remote_host_mirror.hold_released", "reason=" + Clean(reason));
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase7.remote_host_mirror.hold_release_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static PlayerFarming FindMirrorP2()
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
                    if (player != null && !player.isLamb && player.playerID == 1)
                    {
                        return player;
                    }
                }

                if (PlayerFarming.players.Count > 1)
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

        private static bool HasSecondPlayer()
        {
            try
            {
                return PlayerFarming.players != null && PlayerFarming.players.Count > 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeMirrorP2(PlayerFarming player)
        {
            if (player == null)
            {
                return false;
            }

            try
            {
                return !player.isLamb && player.playerID == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocalRemoteP2()
        {
            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            return self != null && string.Equals(self.Role, "remote-p2", StringComparison.Ordinal);
        }

        private static BridgeRemotePlayer FindRemoteHost(BridgeRemotePlayerSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Players == null)
            {
                return null;
            }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player != null
                    && string.Equals(player.Role, "host-lamb", StringComparison.Ordinal)
                    && (string.Equals(player.PlayerId, "0", StringComparison.Ordinal) || string.Equals(player.HostSlot, "0", StringComparison.Ordinal)))
                {
                    return player;
                }
            }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player != null && string.Equals(player.Role, "host-lamb", StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return null;
        }

        private static BridgeRemoteInput FindRemoteHostInput(BridgeRemoteInputSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Inputs == null)
            {
                return null;
            }

            for (int i = 0; i < snapshot.Inputs.Length; i++)
            {
                BridgeRemoteInput input = snapshot.Inputs[i];
                if (input != null
                    && string.Equals(input.Role, "host-lamb", StringComparison.Ordinal)
                    && (string.Equals(input.PlayerId, "0", StringComparison.Ordinal) || string.Equals(input.HostSlot, "0", StringComparison.Ordinal)))
                {
                    return input;
                }
            }

            for (int i = 0; i < snapshot.Inputs.Length; i++)
            {
                BridgeRemoteInput input = snapshot.Inputs[i];
                if (input != null && string.Equals(input.Role, "host-lamb", StringComparison.Ordinal))
                {
                    return input;
                }
            }

            return null;
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

        private static bool IsMirrorP2(PlayerFarming player)
        {
            PlayerFarming mirror = FindMirrorP2();
            return player != null && mirror != null && ReferenceEquals(player, mirror);
        }

        private static void UpdateRemoteButtonEdges(string state)
        {
            _previousRemoteState = _remoteState;
            _remoteState = string.IsNullOrEmpty(state) ? "unknown" : state;

            if (!string.IsNullOrEmpty(_lastInputEdgeSequence) || IsRemoteInputSuppressedState(_remoteState))
            {
                ClearButtonTimers();
                _remoteAttackHeld = false;
                _remoteDodgeHeld = false;
                _remoteCurseHeld = false;
                _remoteHeavyHeld = false;
                return;
            }

            if (string.Equals(_remoteState, "Dodging", StringComparison.Ordinal)
                && !string.Equals(_previousRemoteState, _remoteState, StringComparison.Ordinal))
            {
                _dodgeDownUntil = Time.unscaledTime + 0.1f;
            }

            if (IsAttackState(_remoteState) && !IsAttackState(_previousRemoteState))
            {
                _attackDownUntil = Time.unscaledTime + 0.045f;
            }

            if (IsCurseState(_remoteState) && !IsCurseState(_previousRemoteState))
            {
                _curseDownUntil = Time.unscaledTime + 0.06f;
            }

            if (IsHeavyState(_remoteState) && !IsHeavyState(_previousRemoteState))
            {
                _heavyDownUntil = Time.unscaledTime + 0.06f;
            }

            _remoteDodgeHeld = string.Equals(_remoteState, "Dodging", StringComparison.Ordinal);
            _remoteAttackHeld = false;
            _remoteCurseHeld = IsCurseState(_remoteState);
            _remoteHeavyHeld = IsHeavyState(_remoteState);
            if (_remoteDodgeHeld)
            {
                _dodgeHeldUntil = Time.unscaledTime + 0.12f;
            }

            if (_remoteCurseHeld)
            {
                _curseHeldUntil = Time.unscaledTime + 0.12f;
            }

            if (_remoteHeavyHeld)
            {
                _heavyHeldUntil = Time.unscaledTime + 0.12f;
            }
        }

        private static bool IsAttackState(string state)
        {
            return string.Equals(state, "SignPostAttack", StringComparison.Ordinal)
                || string.Equals(state, "Attacking", StringComparison.Ordinal)
                || string.Equals(state, "RecoverFromAttack", StringComparison.Ordinal)
                || string.Equals(state, "ChargingHeavyAttack", StringComparison.Ordinal)
                || string.Equals(state, "Casting", StringComparison.Ordinal);
        }

        private static bool IsCurseState(string state)
        {
            return string.Equals(state, "Casting", StringComparison.Ordinal)
                || string.Equals(state, "Aiming", StringComparison.Ordinal);
        }

        private static bool IsHeavyState(string state)
        {
            return string.Equals(state, "ChargingHeavyAttack", StringComparison.Ordinal);
        }

        private static void ClearVirtualInput()
        {
            _hasVirtualInput = false;
            _remoteInputValid = false;
            _axisX = 0f;
            _axisY = 0f;
            _remoteAttackHeld = false;
            _remoteDodgeHeld = false;
            _remoteCurseHeld = false;
            _remoteHeavyHeld = false;
            ClearButtonTimers();
        }

        private static void SuppressInputState()
        {
            _axisX = 0f;
            _axisY = 0f;
            _hasVirtualInput = false;
            _remoteAttackHeld = false;
            _remoteDodgeHeld = false;
            _remoteCurseHeld = false;
            _remoteHeavyHeld = false;
            ClearButtonTimers();
        }

        private static void ClearButtonTimers()
        {
            _dodgeDownUntil = 0f;
            _attackDownUntil = 0f;
            _curseDownUntil = 0f;
            _curseUpUntil = 0f;
            _heavyDownUntil = 0f;
            _dodgeHeldUntil = 0f;
            _curseHeldUntil = 0f;
            _heavyHeldUntil = 0f;
        }

        private static void ResetInputTracking()
        {
            ClearVirtualInput();
            _remoteState = "unknown";
            _previousRemoteState = "unknown";
            _lastInputEdgeSequence = "";
        }

        private static bool MarkInputSequence(string sequence)
        {
            string normalized = string.IsNullOrEmpty(sequence) ? "unknown" : sequence;
            if (string.Equals(normalized, _lastInputEdgeSequence, StringComparison.Ordinal))
            {
                return false;
            }

            _lastInputEdgeSequence = normalized;
            return true;
        }

        private static bool ShouldSuppressRemoteInput(string state)
        {
            if (IsRemoteInputSuppressedState(state))
            {
                StartInputSettle("state_" + state, 0.75f);
                return true;
            }

            return BridgeLoadoutAuthority.RecentlyApplied(0.75f) || Time.unscaledTime < _settleInputUntil;
        }

        private static void StartInputSettle(string reason, float seconds)
        {
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (until > _settleInputUntil)
            {
                _settleInputUntil = until;
            }

            if (Time.unscaledTime >= _nextSettleRecordAt)
            {
                _nextSettleRecordAt = Time.unscaledTime + 1f;
                WorldTrace.Record("phase7.remote_host_mirror.input_settle", "reason=" + Clean(reason) + " until=" + FormatFloat(_settleInputUntil));
            }
        }

        private static void UpdateSuppressedRemoteState(string state)
        {
            _previousRemoteState = _remoteState;
            _remoteState = string.IsNullOrEmpty(state) ? "unknown" : state;
            SuppressInputState();
        }

        private static void RecordApplyIfNeeded(string clientId, string sequence, Vector3 before, Vector3 after, float distance, string mode, float ageMs, string state)
        {
            float now = Time.unscaledTime;
            bool snapMode = mode.IndexOf("snap", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!snapMode && now < _nextRecordAt)
            {
                return;
            }

            _lastMode = mode;
            _lastRecordedSequence = sequence ?? "";
            _nextRecordAt = now + 1.0f;
            WorldTrace.Record(
                "phase7.remote_host_mirror.apply",
                "remoteClient=" + Clean(clientId)
                + " seq=" + Clean(sequence)
                + " mode=" + Clean(mode)
                + " ageMs=" + FormatFloat(ageMs)
                + " distance=" + FormatFloat(distance)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " state=" + Clean(state)
                + " before=" + Clean(WorldTrace.FormatVector(before))
                + " after=" + Clean(WorldTrace.FormatVector(after)));
        }

        private static float HardSnapDistance()
        {
            return Mathf.Max(_snapDistance * 3f, 12f);
        }

        private static bool TryParsePosition(string value, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(value))
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

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static float ClampAxis(float value)
        {
            if (value < -1f)
            {
                return -1f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        private static bool ParseBool(string value)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) && parsed;
        }

        private static bool IsKnownToken(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeToken(string value)
        {
            return Clean(value);
        }

        private static string SafeLocalScene()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                return scene.IsValid() ? scene.name : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeLocalLocation()
        {
            try
            {
                return Convert.ToString(PlayerFarming.Location, CultureInfo.InvariantCulture) ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeLocalRoom()
        {
            try
            {
                return GameManager.IsDungeon(PlayerFarming.Location)
                    ? BridgeCombatAuthority.CurrentRoomKey()
                    : "none";
            }
            catch
            {
                return "unknown";
            }
        }

        private static bool IsRemoteAwayTransitionState(string state)
        {
            return string.Equals(state, "InActive", StringComparison.Ordinal);
        }

        private static bool IsRemoteInputSuppressedState(string state)
        {
            return string.Equals(state, "InActive", StringComparison.Ordinal)
                || string.Equals(state, "CustomAnimation", StringComparison.Ordinal)
                || string.Equals(state, "FoundItem", StringComparison.Ordinal)
                || string.Equals(state, "KnockedOut", StringComparison.Ordinal)
                || string.Equals(state, "Respawning", StringComparison.Ordinal)
                || string.Equals(state, "Dieing", StringComparison.Ordinal)
                || string.Equals(state, "Dead", StringComparison.Ordinal)
                || string.Equals(state, "GameOver", StringComparison.Ordinal)
                || string.Equals(state, "FinalGameOver", StringComparison.Ordinal);
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

        private static string SafeValue(Func<string> read)
        {
            try
            {
                return read() ?? "null";
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value
                .Replace(" ", "_")
                .Replace("\t", "_")
                .Replace("\r", "_")
                .Replace("\n", "_")
                .Replace("|", "/")
                .Replace(";", ",");
        }
    }
}
