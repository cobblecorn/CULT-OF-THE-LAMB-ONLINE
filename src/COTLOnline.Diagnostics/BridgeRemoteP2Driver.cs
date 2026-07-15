using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteP2Driver
    {
        private static bool _enabled;
        private static float _maxAgeMs;
        private static float _snapDistance;
        private static float _moveSpeed;
        private static float _softCorrectionSpeed;
        private static bool _requireWorldMatch;
        private static bool _blockTransitions;
        private static bool _hideWhenSceneMismatch;
        private static bool _hideWhenLocationMismatch;
        private static bool _hideDuringTransitionStates;
        private static float _awayGraceSeconds;
        private static float _nextRecordAt;
        private static float _nextAwayRecordAt;
        private static float _remoteAwaySince;
        private static string _lastOverlayLine = "remote p2 driver: disabled";
        private static string _lastSequence = "";
        private static string _lastApplyMode = "";
        private static string _lastAwayReason = "";
        private static PlayerFarming _lastReservedP2;
        private static float _settleInputUntil;
        private static float _nextSettleRecordAt;
        private static bool _spawnSnapDone;
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

        public static void Configure(
            bool enabled,
            float maxAgeMs,
            float snapDistance,
            float moveSpeed,
            float softCorrectionSpeed,
            bool requireWorldMatch,
            bool blockTransitions,
            bool hideWhenSceneMismatch,
            bool hideWhenLocationMismatch,
            bool hideDuringTransitionStates,
            float awayGraceSeconds)
        {
            _enabled = enabled;
            _maxAgeMs = Mathf.Max(100f, maxAgeMs);
            _snapDistance = Mathf.Max(0f, snapDistance);
            _moveSpeed = Mathf.Max(0.1f, moveSpeed);
            _softCorrectionSpeed = Mathf.Max(0f, softCorrectionSpeed);
            _requireWorldMatch = requireWorldMatch;
            _blockTransitions = blockTransitions;
            _hideWhenSceneMismatch = hideWhenSceneMismatch;
            _hideWhenLocationMismatch = hideWhenLocationMismatch;
            _hideDuringTransitionStates = hideDuringTransitionStates;
            _awayGraceSeconds = Mathf.Max(0f, awayGraceSeconds);
            _nextRecordAt = 0f;
            _nextAwayRecordAt = 0f;
            _remoteAwaySince = 0f;
            _lastSequence = "";
            _lastApplyMode = "";
            _lastAwayReason = "";
            _lastReservedP2 = null;
            _settleInputUntil = 0f;
            _nextSettleRecordAt = 0f;
            _spawnSnapDone = false;
            _hiddenForRemoteAway = false;
            ResetRemotePresenceTokens();
            _hasVirtualInput = false;
            _remoteInputValid = false;
            _axisX = 0f;
            _axisY = 0f;
            _remoteState = "unknown";
            _previousRemoteState = "unknown";
            _lastInputEdgeSequence = "";
            _remoteAttackHeld = false;
            _remoteDodgeHeld = false;
            _remoteCurseHeld = false;
            _remoteHeavyHeld = false;
            _dodgeDownUntil = 0f;
            _attackDownUntil = 0f;
            _curseDownUntil = 0f;
            _curseUpUntil = 0f;
            _heavyDownUntil = 0f;
            _dodgeHeldUntil = 0f;
            _curseHeldUntil = 0f;
            _heavyHeldUntil = 0f;
            _lastOverlayLine = enabled ? "remote p2 driver: waiting" : "remote p2 driver: disabled";

            WorldTrace.Record(
                "phase6.config",
                "remoteP2MotionDriver=" + enabled
                + " maxAgeMs=" + FormatFloat(_maxAgeMs)
                + " snapDistance=" + FormatFloat(_snapDistance)
                + " moveSpeed=" + FormatFloat(_moveSpeed)
                + " correctionSpeed=" + FormatFloat(_softCorrectionSpeed)
                + " requireWorldMatch=" + requireWorldMatch
                + " blockTransitions=" + blockTransitions
                + " hideSceneMismatch=" + hideWhenSceneMismatch
                + " hideLocationMismatch=" + hideWhenLocationMismatch
                + " hideTransitionStates=" + hideDuringTransitionStates
                + " awayGraceSeconds=" + FormatFloat(_awayGraceSeconds));
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _lastOverlayLine = "remote p2 driver: disabled";
                ClearVirtualInput();
                return;
            }

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self == null || !string.Equals(self.Role, "host-lamb", StringComparison.Ordinal))
            {
                _lastOverlayLine = "remote p2 driver: not host";
                ClearVirtualInput();
                return;
            }

            BridgeRosterClient remoteClient = FindRemoteP2Client(roster);
            if (remoteClient != null && string.Equals(remoteClient.SaveSlotOk, "False", StringComparison.OrdinalIgnoreCase))
            {
                _lastOverlayLine = "remote p2 driver: paused remote save slot mismatch save=" + Clean(remoteClient.SaveSlot);
                ClearVirtualInput();
                return;
            }

            if (_requireWorldMatch && remoteClient != null && string.Equals(remoteClient.WorldMatch, "False", StringComparison.OrdinalIgnoreCase))
            {
                _lastOverlayLine = "remote p2 driver: paused remote world mismatch hash=" + Clean(remoteClient.WorldHash);
                ClearVirtualInput();
                return;
            }

            PlayerFarming p2 = BridgeCoopReservation.TryFindReservedP2();
            if (p2 == null || p2.gameObject == null)
            {
                _lastOverlayLine = "remote p2 driver: waiting for reserved P2";
                ClearVirtualInput();
                return;
            }

            TrackReservedP2(p2);
            BridgeRemoteInputSnapshot inputSnapshot = BridgeRemoteInputState.Snapshot();
            if (TryApplyRemoteInput(inputSnapshot, p2))
            {
                return;
            }

            BridgeRemotePlayerSnapshot snapshot = BridgeRemotePlayerState.Snapshot();
            if (!snapshot.HasPacket)
            {
                _lastOverlayLine = "remote p2 driver: waiting for remote input/motion";
                ClearVirtualInput();
                return;
            }

            BridgeRemotePlayer remote = FindRemoteP2(snapshot);
            if (remote == null)
            {
                _lastOverlayLine = "remote p2 driver: no remote-p2 in relay";
                ClearVirtualInput();
                return;
            }

            float ageMs = ParseFloat(remote.AgeMs, _maxAgeMs + 1f);
            if (ageMs > _maxAgeMs)
            {
                _lastOverlayLine = "remote p2 driver: stale ageMs=" + FormatFloat(ageMs);
                ClearVirtualInput();
                return;
            }

            if (HandleRemotePresence(p2, remote.Scene, remote.Location, remote.Room, remote.State, remote.Position, "motion"))
            {
                return;
            }

            if (p2.gameObject == null || !p2.gameObject.activeSelf)
            {
                _lastOverlayLine = HiddenOrWaitingOverlay();
                ClearVirtualInput();
                return;
            }

            Vector3 target;
            if (!TryParsePosition(remote.Position, out target))
            {
                _lastOverlayLine = "remote p2 driver: bad pos=" + remote.Position;
                ClearVirtualInput();
                return;
            }

            Vector3 current = p2.transform.position;
            Vector3 before = current;
            Vector3 delta = target - current;
            float distance = Vector3.Distance(current, target);
            Vector3 applied = before;
            string mode;
            bool suppressMotionInput = ShouldSuppressRemoteInput(remote.State);
            if (_snapDistance > 0f && distance > HardSnapDistance())
            {
                p2.transform.position = target;
                _axisX = 0f;
                _axisY = 0f;
                mode = "snap";
            }
            else
            {
                if (suppressMotionInput)
                {
                    SuppressInputState();
                }
                else
                {
                    ApplyVirtualAxis(delta, distance);
                }

                bool remoteDodging = string.Equals(remote.State, "Dodging", StringComparison.Ordinal);
                if (TrySoftCorrectPosition(p2, before, target, distance, !suppressMotionInput && _hasVirtualInput, !suppressMotionInput && remoteDodging, remote.State, out applied))
                {
                    mode = suppressMotionInput ? "settle-correct" : _hasVirtualInput ? "axis-correct" : "hold-correct";
                }
                else
                {
                    mode = suppressMotionInput ? "settle" : _hasVirtualInput ? "axis" : "hold";
                }
            }

            applied = p2.transform.position;
            _remoteInputValid = true;
            if (suppressMotionInput)
            {
                UpdateSuppressedRemoteState(remote.State);
            }
            else
            {
                UpdateRemoteButtonEdges(remote.State);
            }
            _lastOverlayLine = "remote p2 driver: " + mode
                + " seq=" + remote.Sequence
                + " ageMs=" + FormatFloat(ageMs)
                + " dist=" + FormatFloat(distance)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + WorldWarning(remoteClient)
                + " state=" + Clean(remote.State)
                + " target=" + Clean(remote.Position);

            RecordApplyIfNeeded(remote, before, applied, target, distance, mode, ageMs);
        }

        public static string OverlayLine()
        {
            return _lastOverlayLine;
        }

        public static bool TryGetAxis(PlayerFarming player, out float horizontal, out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (!_enabled || !_remoteInputValid || !_hasVirtualInput || !IsRemoteControlledP2(player))
            {
                return false;
            }

            horizontal = _axisX;
            vertical = _axisY;
            return true;
        }

        public static bool ShouldBlockLocalInput(PlayerFarming player)
        {
            return _enabled && IsLocalHostWithRemoteP2() && LooksLikeReservedP2(player);
        }

        public static bool TryGetDodgeButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsRemoteControlledP2(player))
            {
                return false;
            }

            return held
                ? Time.unscaledTime <= _dodgeHeldUntil
                : Time.unscaledTime <= _dodgeDownUntil;
        }

        public static bool TryGetAttackButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsRemoteControlledP2(player))
            {
                return false;
            }

            return !held && Time.unscaledTime <= _attackDownUntil;
        }

        public static bool TryGetCurseButton(PlayerFarming player, bool held, bool up)
        {
            if (!_enabled || !_remoteInputValid || !IsRemoteControlledP2(player))
            {
                return false;
            }

            if (held)
            {
                return Time.unscaledTime <= _curseHeldUntil;
            }

            return up
                ? Time.unscaledTime <= _curseUpUntil
                : Time.unscaledTime <= _curseDownUntil;
        }

        public static bool TryGetHeavyAttackButton(PlayerFarming player, bool held)
        {
            if (!_enabled || !_remoteInputValid || !IsRemoteControlledP2(player))
            {
                return false;
            }

            return held
                ? Time.unscaledTime <= _heavyHeldUntil
                : Time.unscaledTime <= _heavyDownUntil;
        }

        public static bool ShouldBlockRemoteP2Transition(PlayerFarming player, string reason)
        {
            if (!_enabled || !_blockTransitions || !IsRemoteControlledP2(player))
            {
                return false;
            }

            BridgeRemoteBodyGuard.CancelGoToForBridgeBody(player, "phase6.transition_blocked_" + reason);
            ForceSettledIdle(player, "transition_blocked_" + reason);
            StartInputSettle("transition_blocked_" + reason, 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase6.remote_p2.transition_blocked",
                "reason=" + Clean(reason)
                + " player=" + Clean(WorldTrace.DescribePlayer(player))
                + " state=" + Clean(_remoteState));
            return true;
        }

        private static bool TryApplyRemoteInput(BridgeRemoteInputSnapshot snapshot, PlayerFarming p2)
        {
            if (snapshot == null || !snapshot.HasPacket)
            {
                return false;
            }

            BridgeRemoteInput input = FindRemoteP2Input(snapshot);
            if (input == null)
            {
                return false;
            }

            float ageMs = ParseFloat(input.AgeMs, _maxAgeMs + 1f);
            if (ageMs > _maxAgeMs)
            {
                _lastOverlayLine = "remote p2 driver: stale input ageMs=" + FormatFloat(ageMs);
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
            BridgeRemoteFrameState.Apply(p2, input, "remote_p2_input");

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
            string correction = suppressInput ? "input-suppressed" : "input";
            float distance = 0f;
            Vector3 target;
            if (TryParsePosition(input.Position, out target))
            {
                distance = Vector3.Distance(before, target);
                float inputSnapDistance = HardSnapDistance();
                if (!_spawnSnapDone && distance > 0.25f)
                {
                    p2.transform.position = target;
                    after = p2.transform.position;
                    correction = "input-spawn-snap";
                    _spawnSnapDone = true;
                }
                else if (_snapDistance > 0f && distance > inputSnapDistance)
                {
                    p2.transform.position = target;
                    after = p2.transform.position;
                    correction = "input-snap";
                }
                else if (TrySoftCorrectPosition(p2, before, target, distance, _hasVirtualInput, _remoteDodgeHeld || (!suppressInput && ParseBool(input.DodgeDown)), inputState, out after))
                {
                    correction = suppressInput ? "input-suppressed-correct" : "input-correct";
                }
            }

            _lastOverlayLine = "remote p2 driver: " + correction
                + " seq=" + input.Sequence
                + " ageMs=" + FormatFloat(ageMs)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " atk=" + ParseBool(input.AttackDown) + "/" + _remoteAttackHeld
                + " dodge=" + ParseBool(input.DodgeDown) + "/" + _remoteDodgeHeld
                + " curse=" + ParseBool(input.CurseDown) + "/" + _remoteCurseHeld + "/" + ParseBool(input.CurseUp)
                + " heavy=" + ParseBool(input.HeavyDown) + "/" + _remoteHeavyHeld
                + " dist=" + FormatFloat(distance)
                + WorldWarning(FindRemoteP2Client(BridgeRosterState.Snapshot()))
                + " state=" + Clean(input.State);

            RecordInputApplyIfNeeded(input, before, after, distance, correction, ageMs);
            return true;
        }

        private static void TrackReservedP2(PlayerFarming p2)
        {
            if (!ReferenceEquals(_lastReservedP2, p2))
            {
                _lastReservedP2 = p2;
                _spawnSnapDone = false;
            _lastSequence = "";
                _lastApplyMode = "";
                _hiddenForRemoteAway = false;
                _remoteAwaySince = 0f;
                _lastAwayReason = "";
                ResetRemotePresenceTokens();
                ResetInputTracking();
                StartInputSettle("reserved_changed", 0.75f);
                WorldTrace.Record("phase6.remote_p2.reserved_changed", "player=" + Clean(WorldTrace.DescribePlayer(p2)));
            }
        }

        private static bool HandleRemotePresence(PlayerFarming p2, string scene, string location, string room, string state, string position, string source)
        {
            string reason;
            if (ShouldTreatRemoteAsAway(scene, location, room, state, out reason))
            {
                HideReservedP2(p2, reason, source);
                _lastOverlayLine = "remote p2 driver: hidden remote away " + Clean(reason);
                ClearVirtualInput();
                return true;
            }

            if (RestoreReservedP2IfNeeded(p2, position, source))
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

            if (Time.unscaledTime - _remoteAwaySince < _awayGraceSeconds)
            {
                return false;
            }

            reason = candidate;
            return true;
        }

        private static bool TryBuildRemoteAwayReason(string scene, string location, string room, string state, out string reason)
        {
            reason = "";
            if (_hideWhenSceneMismatch && IsKnownToken(scene))
            {
                string remoteScene = NormalizeToken(scene);
                string localScene = NormalizeToken(SafeLocalScene());
                if (IsKnownToken(localScene) && IsKnownToken(remoteScene) && !string.Equals(remoteScene, localScene, StringComparison.Ordinal))
                {
                    reason = "scene " + localScene + "!=" + remoteScene;
                    return true;
                }
            }

            if (_hideWhenLocationMismatch && IsKnownToken(location))
            {
                string remoteLocation = NormalizeToken(location);
                string localLocation = NormalizeToken(SafeLocalLocation());
                if (IsKnownToken(localLocation) && IsKnownToken(remoteLocation) && !string.Equals(remoteLocation, localLocation, StringComparison.Ordinal))
                {
                    reason = "location " + localLocation + "!=" + remoteLocation;
                    return true;
                }
            }

            if (_hideWhenLocationMismatch && IsKnownToken(room))
            {
                string remoteRoom = NormalizeToken(room);
                string localRoom = NormalizeToken(SafeLocalRoom());
                if (IsKnownToken(localRoom) && IsKnownToken(remoteRoom) && !string.Equals(remoteRoom, localRoom, StringComparison.Ordinal))
                {
                    reason = "room " + localRoom + "!=" + remoteRoom;
                    return true;
                }
            }

            if (_hideDuringTransitionStates && IsRemoteAwayTransitionState(state))
            {
                reason = "state " + state;
                return true;
            }

            return false;
        }

        private static void HideReservedP2(PlayerFarming p2, string reason, string source)
        {
            bool firstHide = !_hiddenForRemoteAway;
            _hiddenForRemoteAway = true;
            _spawnSnapDone = false;
            _lastApplyMode = "remote-away";
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
                    "phase6.remote_p2.away_hidden",
                    "source=" + Clean(source)
                    + " reason=" + Clean(reason)
                    + " player=" + Clean(WorldTrace.DescribePlayer(p2)));
            }
        }

        private static bool RestoreReservedP2IfNeeded(PlayerFarming p2, string position, string source)
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
            _spawnSnapDone = hasTarget;
            ResetRemotePresenceTokens();
            StartInputSettle("away_restored", 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase6.remote_p2.away_restored",
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
            _spawnSnapDone = true;
            _lastApplyMode = "transition-snap";
            StartInputSettle("remote_space_changed", 1.25f);
            ClearVirtualInput();
            WorldTrace.Record(
                "phase6.remote_p2.transition_snap",
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
                    BridgeRemoteBodyGuard.CancelGoToForBridgeBody(player, "phase6.idle_settle_" + reason);
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record(
                    "phase6.remote_p2.idle_settle_error",
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
                    "phase6.remote_p2.idle_settle_error",
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
                    "phase6.remote_p2.idle_settle_error",
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
                ? "remote p2 driver: hidden remote away " + Clean(_lastAwayReason)
                : "remote p2 driver: waiting for reserved P2";
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

        private static bool TrySoftCorrectPosition(PlayerFarming p2, Vector3 before, Vector3 target, float distance, bool hasInputAxis, bool remoteDodging, string remoteState, out Vector3 after)
        {
            after = before;
            float deadband = IdleCorrectionDeadband(remoteState, hasInputAxis, remoteDodging);
            if (_softCorrectionSpeed <= 0f || distance < deadband || p2 == null || p2.transform == null)
            {
                return false;
            }

            float speed = _softCorrectionSpeed;
            if (hasInputAxis)
            {
                speed *= 0.5f;
            }

            if (remoteDodging || IsRemoteState("Dodging"))
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
            float maxDistanceDelta = speed * deltaTime;
            if (maxDistanceDelta <= 0f)
            {
                return false;
            }

            p2.transform.position = Vector3.MoveTowards(before, target, maxDistanceDelta);
            after = p2.transform.position;
            return Vector3.Distance(before, after) > 0.001f;
        }

        private static float IdleCorrectionDeadband(string remoteState, bool hasInputAxis, bool remoteDodging)
        {
            return IsIdleLikeState(remoteState) && !hasInputAxis && !remoteDodging ? 0.7f : 0.35f;
        }

        private static bool IsIdleLikeState(string state)
        {
            return string.IsNullOrEmpty(state)
                || string.Equals(state, "Idle", StringComparison.Ordinal)
                || string.Equals(state, "None", StringComparison.Ordinal)
                || string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static float HardSnapDistance()
        {
            return Mathf.Max(_snapDistance * 3f, 12f);
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

            if (IsRemoteState("Dodging") && !string.Equals(_previousRemoteState, _remoteState, StringComparison.Ordinal))
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

            _remoteDodgeHeld = IsRemoteState("Dodging");
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

        private static string WorldWarning(BridgeRosterClient remoteClient)
        {
            if (remoteClient == null || !string.Equals(remoteClient.WorldMatch, "False", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return " worldWarn=mismatch";
        }

        private static bool IsReservedP2(PlayerFarming player)
        {
            PlayerFarming p2 = BridgeCoopReservation.TryFindReservedP2();
            return player != null && p2 != null && ReferenceEquals(player, p2);
        }

        private static bool IsRemoteControlledP2(PlayerFarming player)
        {
            return IsReservedP2(player) || (IsLocalHostWithRemoteP2() && LooksLikeReservedP2(player));
        }

        private static bool LooksLikeReservedP2(PlayerFarming player)
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

        private static bool IsLocalHostWithRemoteP2()
        {
            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            if (self == null || !string.Equals(self.Role, "host-lamb", StringComparison.Ordinal))
            {
                return false;
            }

            BridgeRosterClient remote = FindRemoteP2Client(roster);
            if (remote == null || string.Equals(remote.SaveSlotOk, "False", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ParseFloat(remote.AgeMs, 0f) <= 10000f;
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
                WorldTrace.Record("phase6.remote_p2.input_settle", "reason=" + Clean(reason) + " until=" + FormatFloat(_settleInputUntil));
            }
        }

        private static void UpdateSuppressedRemoteState(string state)
        {
            _previousRemoteState = _remoteState;
            _remoteState = string.IsNullOrEmpty(state) ? "unknown" : state;
            SuppressInputState();
        }

        private static bool IsRemoteState(string state)
        {
            return string.Equals(_remoteState, state, StringComparison.Ordinal);
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

        private static void RecordApplyIfNeeded(BridgeRemotePlayer remote, Vector3 before, Vector3 after, Vector3 target, float distance, string mode, float ageMs)
        {
            float now = Time.unscaledTime;
            bool snapMode = mode.IndexOf("snap", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!snapMode && now < _nextRecordAt)
            {
                return;
            }

            _lastSequence = remote.Sequence;
            _lastApplyMode = mode;
            _nextRecordAt = now + 1.0f;
            WorldTrace.Record(
                "phase6.remote_p2.apply",
                "remoteClient=" + Clean(remote.ClientId)
                + " seq=" + Clean(remote.Sequence)
                + " mode=" + mode
                + " ageMs=" + FormatFloat(ageMs)
                + " distance=" + FormatFloat(distance)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " state=" + Clean(remote.State)
                + " before=" + Clean(WorldTrace.FormatVector(before))
                + " after=" + Clean(WorldTrace.FormatVector(after))
                + " target=" + Clean(WorldTrace.FormatVector(target)));
        }

        private static BridgeRemotePlayer FindRemoteP2(BridgeRemotePlayerSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Players == null)
            {
                return null;
            }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player != null
                    && string.Equals(player.Role, "remote-p2", StringComparison.Ordinal)
                    && string.Equals(player.PlayerId, "0", StringComparison.Ordinal))
                {
                    return player;
                }
            }

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player != null && string.Equals(player.Role, "remote-p2", StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return snapshot.Players.Length > 0 && string.Equals(snapshot.Players[0].PlayerId, "0", StringComparison.Ordinal)
                ? snapshot.Players[0]
                : null;
        }

        private static BridgeRemoteInput FindRemoteP2Input(BridgeRemoteInputSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Inputs == null)
            {
                return null;
            }

            for (int i = 0; i < snapshot.Inputs.Length; i++)
            {
                BridgeRemoteInput input = snapshot.Inputs[i];
                if (input != null
                    && string.Equals(input.Role, "remote-p2", StringComparison.Ordinal)
                    && string.Equals(input.PlayerId, "0", StringComparison.Ordinal))
                {
                    return input;
                }
            }

            for (int i = 0; i < snapshot.Inputs.Length; i++)
            {
                BridgeRemoteInput input = snapshot.Inputs[i];
                if (input != null && string.Equals(input.Role, "remote-p2", StringComparison.Ordinal))
                {
                    return input;
                }
            }

            return snapshot.Inputs.Length > 0 && string.Equals(snapshot.Inputs[0].PlayerId, "0", StringComparison.Ordinal)
                ? snapshot.Inputs[0]
                : null;
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

        private static BridgeRosterClient FindRemoteP2Client(BridgeRosterSnapshot roster)
        {
            if (roster == null || roster.Clients == null)
            {
                return null;
            }

            for (int i = 0; i < roster.Clients.Length; i++)
            {
                BridgeRosterClient client = roster.Clients[i];
                if (client != null && string.Equals(client.Role, "remote-p2", StringComparison.Ordinal))
                {
                    return client;
                }
            }

            return null;
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

        private static void RecordInputApplyIfNeeded(BridgeRemoteInput input, Vector3 before, Vector3 after, float distance, string mode, float ageMs)
        {
            float now = Time.unscaledTime;
            bool snapMode = mode.IndexOf("snap", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!snapMode && now < _nextRecordAt)
            {
                return;
            }

            _lastSequence = input.Sequence;
            _lastApplyMode = mode;
            _nextRecordAt = now + 1.0f;
            WorldTrace.Record(
                "phase6.remote_p2.input",
                "remoteClient=" + Clean(input.ClientId)
                + " seq=" + Clean(input.Sequence)
                + " mode=" + mode
                + " ageMs=" + FormatFloat(ageMs)
                + " distance=" + FormatFloat(distance)
                + " axis=(" + FormatFloat(_axisX) + "," + FormatFloat(_axisY) + ")"
                + " attackDown=" + ParseBool(input.AttackDown)
                + " attackHeld=" + _remoteAttackHeld
                + " dodgeDown=" + ParseBool(input.DodgeDown)
                + " dodgeHeld=" + _remoteDodgeHeld
                + " curseDown=" + ParseBool(input.CurseDown)
                + " curseHeld=" + _remoteCurseHeld
                + " curseUp=" + ParseBool(input.CurseUp)
                + " heavyDown=" + ParseBool(input.HeavyDown)
                + " heavyHeld=" + _remoteHeavyHeld
                + " state=" + Clean(input.State)
                + " before=" + Clean(WorldTrace.FormatVector(before))
                + " after=" + Clean(WorldTrace.FormatVector(after)));
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
