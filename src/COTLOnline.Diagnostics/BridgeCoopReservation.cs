using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeCoopReservation
    {
        private static readonly FieldInfo RewiredPlayerField = AccessTools.Field(typeof(PlayerFarming), "rewiredPlayer");

        private static bool _enabled;
        private static bool _manualSpawnHotkey;
        private static bool _manualClearHotkey;
        private static bool _holdWithoutController;
        private static bool _requireRemoteP2;
        private static bool _ownsTemporaryDisableRemoval;
        private static float _nextSnapshotAt;
        private static string _lastOverlayLine = "host p2 reserve: waiting";
        private static string _lastSnapshotSignature = "";

        public static void Configure(bool enabled, bool manualSpawnHotkey, bool manualClearHotkey, bool holdWithoutController, bool requireRemoteP2)
        {
            _enabled = enabled;
            _manualSpawnHotkey = manualSpawnHotkey;
            _manualClearHotkey = manualClearHotkey;
            _holdWithoutController = holdWithoutController;
            _requireRemoteP2 = requireRemoteP2;
            _ownsTemporaryDisableRemoval = false;
            _nextSnapshotAt = 0f;
            _lastOverlayLine = "host p2 reserve: waiting";
            _lastSnapshotSignature = "";

            WorldTrace.Record(
                "phase5.config",
                "hostP2Reservation=" + enabled
                + " manualSpawnF9=" + manualSpawnHotkey
                + " clearF10=" + manualClearHotkey
                + " holdNoControllerP2=" + holdWithoutController
                + " requireRemoteP2=" + requireRemoteP2);
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                ReleaseTemporaryDisableRemoval("disabled");
                return;
            }

            ReservationContext context = BuildContext();
            _lastOverlayLine = BuildOverlayLine(context);

            ApplyTemporaryDisableRemoval(context);
            RecordSnapshotIfNeeded(context);

            if (_manualSpawnHotkey && Input.GetKeyDown(KeyCode.F9))
            {
                TryReserveHostP2(context);
            }

            if (_manualClearHotkey && Input.GetKeyDown(KeyCode.F10))
            {
                TryClearHostP2(context);
            }
        }

        public static string OverlayLine()
        {
            return _enabled ? _lastOverlayLine : "";
        }

        public static void RecordRewiredRefresh(string stage)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord("phase5.coop.rewired_refresh." + stage, () => BuildMessage(BuildContext()));
        }

        public static void RecordRemoveMenu(string stage)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord(
                "phase5.coop.remove_menu." + stage,
                () => BuildMessage(BuildContext()) + " caller=" + Clean(FindInterestingCaller()));
        }

        private static void TryReserveHostP2(ReservationContext context)
        {
            if (!context.IsHost)
            {
                SafeRecord("phase5.coop.reserve.spawn.refused", () => BuildMessage(context) + " reason=not_host");
                return;
            }

            if (_requireRemoteP2 && !context.RemoteP2Present)
            {
                SafeRecord("phase5.coop.reserve.spawn.refused", () => BuildMessage(context) + " reason=no_remote_p2");
                return;
            }

            if (context.P2Active)
            {
                SafeRecord("phase5.coop.reserve.spawn.refused", () => BuildMessage(context) + " reason=p2_already_active");
                return;
            }

            if (CoopManager.Instance == null)
            {
                SafeRecord("phase5.coop.reserve.spawn.refused", () => BuildMessage(context) + " reason=no_coop_manager");
                return;
            }

            if (PlayerFarming.players == null || PlayerFarming.players.Count == 0)
            {
                SafeRecord("phase5.coop.reserve.spawn.refused", () => BuildMessage(context) + " reason=no_player_list");
                return;
            }

            SafeRecord("phase5.coop.reserve.spawn.request", () => BuildMessage(context) + " slot=1 playEffects=True startingHealth=-1");

            try
            {
                CoopManager.Instance.SpawnCoopPlayer(1, true, -1f);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase5.coop.reserve.spawn.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void TryClearHostP2(ReservationContext context)
        {
            if (!context.IsHost)
            {
                SafeRecord("phase5.coop.reserve.clear.refused", () => BuildMessage(context) + " reason=not_host");
                return;
            }

            if (!context.P2Active)
            {
                SafeRecord("phase5.coop.reserve.clear.refused", () => BuildMessage(context) + " reason=no_p2");
                return;
            }

            SafeRecord("phase5.coop.reserve.clear.request", () => BuildMessage(context));
            ReleaseTemporaryDisableRemoval("manual_clear");

            try
            {
                CoopManager.RemovePlayerFromMenu();
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase5.coop.reserve.clear.error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void ApplyTemporaryDisableRemoval(ReservationContext context)
        {
            bool shouldHold = _holdWithoutController
                && context.IsHost
                && context.RemoteP2Present
                && context.P2Present;

            if (!shouldHold)
            {
                ReleaseTemporaryDisableRemoval("not_needed");
                return;
            }

            try
            {
                if (CoopManager.Instance == null)
                {
                    return;
                }

                if (!CoopManager.Instance.temporaryDisableRemoval)
                {
                    CoopManager.Instance.temporaryDisableRemoval = true;
                    _ownsTemporaryDisableRemoval = true;
                    SafeRecord("phase5.coop.reserve.hold_enabled", () => BuildMessage(context));
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase5.coop.reserve.hold_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
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
                WorldTrace.Record("phase5.coop.reserve.hold_released", "reason=" + reason);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase5.coop.reserve.hold_release_error", ex.GetType().Name + ": " + Clean(ex.Message));
            }
        }

        private static void RecordSnapshotIfNeeded(ReservationContext context)
        {
            float now = Time.unscaledTime;
            if (now < _nextSnapshotAt)
            {
                return;
            }

            _nextSnapshotAt = now + 1.0f;
            string message = BuildMessage(context);
            string signature = context.Role
                + "|" + context.RemoteP2Present
                + "|" + context.P2Wanted
                + "|" + context.P2Active
                + "|" + context.P2NoController
                + "|" + context.P2State
                + "|" + context.PlayersCount
                + "|" + context.CoopActive;

            if (signature == _lastSnapshotSignature)
            {
                return;
            }

            _lastSnapshotSignature = signature;
            WorldTrace.Record("phase5.coop.reserve.snapshot", message);
        }

        private static ReservationContext BuildContext()
        {
            ReservationContext context = new ReservationContext
            {
                Role = "unknown",
                RemoteP2Present = false,
                P2Wanted = false,
                P2Present = false,
                P2Active = false,
                P2NoController = false,
                P2Held = _ownsTemporaryDisableRemoval || SafeBool(() => CoopManager.Instance != null && CoopManager.Instance.temporaryDisableRemoval),
                P2State = "none",
                P2Position = "none",
                P2Health = "none",
                P2Rewired = "none",
                Scene = SafeValue(() => SceneManager.GetActiveScene().name),
                Location = SafeValue(() => PlayerFarming.Location.ToString()),
                CoopActive = SafeValue(() => CoopManager.CoopActive.ToString()),
                PlayersCount = SafeValue(() => PlayerFarming.playersCount.ToString()),
                PlayersListCount = SafeValue(() => PlayerFarming.players != null ? PlayerFarming.players.Count.ToString() : "null")
            };

            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            BridgeRosterClient self = FindClient(roster, DiagnosticsPlugin.ClientId);
            context.Role = self != null ? self.Role : "unknown";
            context.RemoteP2Present = HasActiveRemoteP2(roster);
            context.P2Wanted = context.IsHost && context.RemoteP2Present;

            PlayerFarming p2 = FindP2();
            if (p2 == null)
            {
                return context;
            }

            RewiredStatus rewired = ReadRewiredStatus(p2);
            Health health = SafeObject(() => p2.health) as Health;
            context.P2Present = true;
            context.P2Active = SafeBool(() => p2.gameObject != null && p2.gameObject.activeSelf);
            context.P2NoController = !rewired.HasController;
            context.P2State = SafeValue(() => p2.state != null ? p2.state.CURRENT_STATE.ToString() : "null");
            context.P2Position = SafeValue(() => WorldTrace.FormatVector(p2.transform.position));
            context.P2Health = health != null
                ? WorldTrace.FormatFloat(health.HP) + "/" + WorldTrace.FormatFloat(health.totalHP)
                : "null";
            context.P2Rewired = rewired.ToToken();
            return context;
        }

        private static PlayerFarming FindP2()
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

        private static bool HasActiveRemoteP2(BridgeRosterSnapshot roster)
        {
            if (roster == null || roster.Clients == null)
            {
                return false;
            }

            for (int i = 0; i < roster.Clients.Length; i++)
            {
                BridgeRosterClient client = roster.Clients[i];
                if (client == null || !string.Equals(client.Role, "remote-p2", StringComparison.Ordinal))
                {
                    continue;
                }

                int ageMs = ParseInt(client.AgeMs, 0);
                bool tooOld = ageMs > 10000;
                bool explicitMismatch = string.Equals(client.SaveSlotOk, "False", StringComparison.OrdinalIgnoreCase);
                if (!tooOld && !explicitMismatch)
                {
                    return true;
                }
            }

            return false;
        }

        internal static PlayerFarming TryFindReservedP2()
        {
            return FindP2();
        }

        private static RewiredStatus ReadRewiredStatus(PlayerFarming player)
        {
            try
            {
                object rewiredPlayer = RewiredPlayerField != null ? RewiredPlayerField.GetValue(player) : null;
                if (rewiredPlayer == null)
                {
                    return new RewiredStatus("null", 0, false);
                }

                object id = ReadProperty(rewiredPlayer, "id");
                object controllers = ReadProperty(rewiredPlayer, "controllers");
                int joystickCount = ParseObjectInt(ReadProperty(controllers, "joystickCount"));
                bool hasKeyboard = ParseObjectBool(ReadProperty(controllers, "hasKeyboard"));
                return new RewiredStatus(id != null ? Convert.ToString(id, CultureInfo.InvariantCulture) : "unknown", joystickCount, hasKeyboard);
            }
            catch (Exception ex)
            {
                return new RewiredStatus("err:" + ex.GetType().Name, 0, false);
            }
        }

        private static object ReadProperty(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property != null ? property.GetValue(instance, null) : null;
        }

        private static string BuildMessage(ReservationContext context)
        {
            return "role=" + Clean(context.Role)
                + " remoteP2=" + context.RemoteP2Present
                + " p2Wanted=" + context.P2Wanted
                + " p2Present=" + context.P2Present
                + " p2Active=" + context.P2Active
                + " p2Hold=" + (_ownsTemporaryDisableRemoval || context.P2Held)
                + " p2NoController=" + context.P2NoController
                + " p2State=" + Clean(context.P2State)
                + " p2Pos=" + Clean(context.P2Position)
                + " p2Hp=" + Clean(context.P2Health)
                + " p2Rewired=" + Clean(context.P2Rewired)
                + " coopActive=" + Clean(context.CoopActive)
                + " playersCount=" + Clean(context.PlayersCount)
                + " playersList=" + Clean(context.PlayersListCount)
                + " scene=" + Clean(context.Scene)
                + " location=" + Clean(context.Location);
        }

        private static string BuildOverlayLine(ReservationContext context)
        {
            return "host p2 reserve: role=" + context.Role
                + " remote=" + context.RemoteP2Present
                + " wanted=" + context.P2Wanted
                + " p2=" + context.P2Active
                + " noCtl=" + context.P2NoController
                + " hold=" + (_ownsTemporaryDisableRemoval || context.P2Held)
                + " " + context.P2State
                + " F9 spawn F10 clear";
        }

        private static string FindInterestingCaller()
        {
            try
            {
                System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    MethodBase method = trace.GetFrame(i)?.GetMethod();
                    string declaringType = method?.DeclaringType != null ? method.DeclaringType.Name : "";
                    string methodName = method != null ? method.Name : "";
                    if (declaringType == nameof(PlayerFarming)
                        || declaringType == nameof(CoopManager)
                        || declaringType.IndexOf("Coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return declaringType + "." + methodName;
                    }
                }
            }
            catch
            {
            }

            return "unknown";
        }

        private static void SafeRecord(string category, Func<string> buildMessage)
        {
            try
            {
                WorldTrace.Record(category, buildMessage());
            }
            catch (Exception ex)
            {
                WorldTrace.Record(category + ".error", ex.GetType().Name + ": " + Clean(ex.Message));
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

        private static object SafeObject(Func<object> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
        }

        private static int ParseObjectInt(object value)
        {
            if (value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static bool ParseObjectBool(object value)
        {
            if (value == null)
            {
                return false;
            }

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
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

            return value
                .Replace(" ", "_")
                .Replace("\t", "_")
                .Replace("\r", "_")
                .Replace("\n", "_")
                .Replace("|", "/")
                .Replace(";", ",");
        }

        private sealed class ReservationContext
        {
            public string Role;
            public bool RemoteP2Present;
            public bool P2Wanted;
            public bool P2Present;
            public bool P2Active;
            public bool P2Held;
            public bool P2NoController;
            public string P2State;
            public string P2Position;
            public string P2Health;
            public string P2Rewired;
            public string Scene;
            public string Location;
            public string CoopActive;
            public string PlayersCount;
            public string PlayersListCount;
            public bool IsHost => string.Equals(Role, "host-lamb", StringComparison.Ordinal);
        }

        private readonly struct RewiredStatus
        {
            private readonly string _id;
            private readonly int _joystickCount;
            private readonly bool _hasKeyboard;

            public RewiredStatus(string id, int joystickCount, bool hasKeyboard)
            {
                _id = id ?? "unknown";
                _joystickCount = joystickCount;
                _hasKeyboard = hasKeyboard;
            }

            public bool HasController => _joystickCount > 0 || _hasKeyboard;

            public string ToToken()
            {
                return "id:" + _id + ",joy:" + _joystickCount + ",kb:" + _hasKeyboard;
            }
        }
    }
}
