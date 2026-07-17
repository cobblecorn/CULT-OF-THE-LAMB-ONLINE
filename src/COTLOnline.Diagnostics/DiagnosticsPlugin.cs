using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class DiagnosticsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.codex.cotlonline.diagnostics";
        public const string PluginName = "COTL Online Diagnostics";
        public const string PluginVersion = "0.5.36";

        private Harmony _harmony;
        private ConfigEntry<bool> _verboseSnapshots;
        private ConfigEntry<bool> _syncEvents;
        private ConfigEntry<bool> _traceRawHookEvents;
        private ConfigEntry<bool> _traceObjectPoolEvents;
        private ConfigEntry<bool> _traceHighFrequencyPackets;
        private ConfigEntry<bool> _perfDiagnostics;
        private ConfigEntry<bool> _syncAppliedDamageOnly;
        private ConfigEntry<bool> _syncIgnoreNeutralDamage;
        private ConfigEntry<bool> _persistentSnapshotPreview;
        private ConfigEntry<bool> _deepPersistentWorldHash;
        private ConfigEntry<bool> _phase2CoopDiagnostics;
        private ConfigEntry<bool> _phase2RoomDiagnostics;
        private ConfigEntry<bool> _phase2RewardDiagnostics;
        private ConfigEntry<bool> _phase3AuthorityDiagnostics;
        private ConfigEntry<bool> _liveUdpEventStream;
        private ConfigEntry<bool> _livePlayerMotionStream;
        private ConfigEntry<bool> _livePlayerInputStream;
        private ConfigEntry<bool> _bridgeRosterOverlay;
        private ConfigEntry<bool> _bridgeRemotePlayerWorldMarkers;
        private ConfigEntry<bool> _phase5HostP2ReservationDiagnostics;
        private ConfigEntry<bool> _phase5HostP2ManualSpawnHotkey;
        private ConfigEntry<bool> _phase5HostP2ClearHotkey;
        private ConfigEntry<bool> _phase5HoldReservedP2WithoutController;
        private ConfigEntry<bool> _phase5RequireRemoteP2ForReservation;
        private ConfigEntry<bool> _phase6RemoteP2MotionDriver;
        private ConfigEntry<bool> _phase6RequireWorldMatchForRemoteP2Driver;
        private ConfigEntry<bool> _phase6BlockRemoteP2Transitions;
        private ConfigEntry<bool> _phase6RemoteP2HideWhenSceneMismatch;
        private ConfigEntry<bool> _phase6RemoteP2HideWhenLocationMismatch;
        private ConfigEntry<bool> _phase6RemoteP2HideDuringTransitionStates;
        private ConfigEntry<bool> _phase7RemoteHostVisualMirror;
        private ConfigEntry<bool> _phase7RemoteHostMirrorAutoSpawn;
        private ConfigEntry<bool> _phase7BlockRemoteHostMirrorTransitions;
        private ConfigEntry<bool> _phase8WorldAuthorityDiagnostics;
        private ConfigEntry<bool> _phase9LoadoutRelay;
        private ConfigEntry<bool> _phase9LoadoutOverrideRemoteBodies;
        private ConfigEntry<bool> _phase9LocalCameraSplit;
        private ConfigEntry<bool> _phase10RunAuthority;
        private ConfigEntry<bool> _phase10ForceServerDungeonSeed;
        private ConfigEntry<bool> _phase10ForceServerRewards;
        private ConfigEntry<bool> _phase10WaitForServerSeedBeforeDungeonGenerate;
        private ConfigEntry<bool> _phase11SpellCastRelay;
        private ConfigEntry<bool> _phase11SpellRelayVisualOnly;
        private ConfigEntry<bool> _phase12RewardClaimRelay;
        private ConfigEntry<bool> _phase13CombatAuthorityDiagnostics;
        private ConfigEntry<bool> _phase15ServerSaveAuthority;
        private ConfigEntry<bool> _phase15CaptureHostSaveSnapshots;
        private ConfigEntry<bool> _phase15ApplyServerSaveSnapshots;
        private ConfigEntry<bool> _phase15BlockRemoteClientSaves;
        private ConfigEntry<bool> _phase16FollowerAuthority;
        private ConfigEntry<bool> _phase16ApplyHostFollowerPositions;
        private ConfigEntry<string> _clientId;
        private ConfigEntry<string> _liveUdpHost;
        private ConfigEntry<int> _liveUdpPort;
        private ConfigEntry<int> _phase15ServerSaveSlot;
        private ConfigEntry<float> _livePlayerMotionInterval;
        private ConfigEntry<float> _livePlayerInputInterval;
        private ConfigEntry<float> _phase6RemoteP2MaxAgeMs;
        private ConfigEntry<float> _phase6RemoteP2SnapDistance;
        private ConfigEntry<float> _phase6RemoteP2MoveSpeed;
        private ConfigEntry<float> _phase6RemoteP2CorrectionSpeed;
        private ConfigEntry<float> _phase6RemoteP2AwayGraceSeconds;
        private ConfigEntry<float> _phase7RemoteHostMirrorMaxAgeMs;
        private ConfigEntry<float> _phase7RemoteHostMirrorSnapDistance;
        private ConfigEntry<float> _phase7RemoteHostMirrorCorrectionSpeed;
        private ConfigEntry<float> _phase7RemoteHostMirrorHoldGraceSeconds;
        private ConfigEntry<float> _phase8CultSnapshotIntervalSeconds;
        private ConfigEntry<float> _phase9CameraSplitDistance;
        private ConfigEntry<float> _phase9CameraReturnDistance;
        private ConfigEntry<float> _perfSlowTickThresholdMs;
        private ConfigEntry<float> _phase10ServerSeedWaitTimeoutSeconds;
        private ConfigEntry<float> _phase13CombatRosterIntervalSeconds;
        private ConfigEntry<float> _phase15HostSaveSnapshotIntervalSeconds;
        private ConfigEntry<float> _phase16FollowerCatalogIntervalSeconds;
        private ConfigEntry<float> _phase16FollowerAuthorityMaxAgeMs;
        private ConfigEntry<float> _phase16FollowerSnapDistance;
        private ConfigEntry<float> _phase16FollowerCorrectionSpeed;
        private ConfigEntry<float> _runtimeSnapshotInterval;
        private ConfigEntry<float> _persistentSnapshotInterval;
        private ConfigEntry<float> _saveFileHashInterval;

        internal static bool TraceRawHookEvents { get; private set; }
        internal static bool TraceObjectPoolEvents { get; private set; }
        internal static string ClientId { get; private set; }
        internal static string SessionId { get; private set; }
        private bool _bridgeOverlayVisible;
        private float _nextLiveHeartbeatAt;

        private void Awake()
        {
            _verboseSnapshots = Config.Bind(
                "Trace",
                "VerboseSnapshots",
                true,
                "When enabled, records throttled runtime snapshots in addition to event hooks.");

            _syncEvents = Config.Bind(
                "Trace",
                "SyncEvents",
                true,
                "When enabled, emits compact sync.* events shaped like future network/server packets.");

            _traceRawHookEvents = Config.Bind(
                "Trace",
                "TraceRawHookEvents",
                false,
                "When enabled, records raw hook events such as health.damage.before/after. This is noisy and can lag.");

            _traceObjectPoolEvents = Config.Bind(
                "Trace",
                "TraceObjectPoolEvents",
                false,
                "When enabled, records raw ObjectPool spawn/recycle events. This is extremely noisy.");

            _traceHighFrequencyPackets = Config.Bind(
                "Trace",
                "TraceHighFrequencyPackets",
                false,
                "When enabled, writes every high-frequency live packet to the JSONL trace. Disabled by default because packet file writes can hitch during movement tests.");

            _perfDiagnostics = Config.Bind(
                "Trace",
                "PerfDiagnostics",
                true,
                "When enabled, records perf.tick_slow when a diagnostics subsystem takes longer than PerfSlowTickThresholdMs on the main thread.");

            _syncAppliedDamageOnly = Config.Bind(
                "Sync",
                "AppliedDamageOnly",
                true,
                "When enabled, sync.damage is emitted only when Health.DealDamage actually applies damage.");

            _syncIgnoreNeutralDamage = Config.Bind(
                "Sync",
                "IgnoreNeutralDamage",
                true,
                "When enabled, sync.damage skips Neutral-team victims such as grass, sprites, crates, and many breakables.");

            _persistentSnapshotPreview = Config.Bind(
                "Trace",
                "PersistentSnapshotPreview",
                false,
                "When enabled, snapshot.persistent includes a long DataManager field preview. Hashes are still recorded when disabled.");

            _deepPersistentWorldHash = Config.Bind(
                "Trace",
                "DeepPersistentWorldHash",
                false,
                "When enabled, persistent snapshots reflectively scan many DataManager fields. This is useful for forensic traces but can hitch during live movement tests.");

            _phase2CoopDiagnostics = Config.Bind(
                "Phase2",
                "CoopDiagnostics",
                true,
                "When enabled, records co-op join/leave/player-presence hooks used to evaluate network player emulation.");

            _phase2RoomDiagnostics = Config.Bind(
                "Phase2",
                "RoomDiagnostics",
                true,
                "When enabled, records room/world generation and player placement hooks used to identify server-owned world choke points.");

            _phase2RewardDiagnostics = Config.Bind(
                "Phase2",
                "RewardDiagnostics",
                true,
                "When enabled, records weapon, curse, relic, tarot, and pickup selection hooks relevant to co-op reward sync.");

            _phase3AuthorityDiagnostics = Config.Bind(
                "Phase3",
                "AuthorityDiagnostics",
                true,
                "When enabled, records run seed, dungeon graph, and direct equipment assignment hooks used to locate server-authoritative state.");

            _liveUdpEventStream = Config.Bind(
                "Live",
                "UdpEventStream",
                true,
                "When enabled, sends compact diagnostics events to a local UDP server prototype.");

            _livePlayerMotionStream = Config.Bind(
                "Live",
                "PlayerMotionStream",
                true,
                "When enabled, emits higher-frequency sync.player_motion events for player coordinate streaming tests.");

            _livePlayerInputStream = Config.Bind(
                "Live",
                "PlayerInputStream",
                true,
                "When enabled, emits higher-frequency sync.player_input events with local axes and button edges for remote P2 driving tests.");

            _bridgeRosterOverlay = Config.Bind(
                "Bridge",
                "RosterOverlay",
                true,
                "When enabled, draws a read-only F8-toggle server roster overlay inside the game.");

            _bridgeRemotePlayerWorldMarkers = Config.Bind(
                "Bridge",
                "RemotePlayerWorldMarkers",
                true,
                "When enabled, draws lightweight world-space markers for remote players received from the server relay.");

            _phase5HostP2ReservationDiagnostics = Config.Bind(
                "Phase5",
                "HostP2ReservationDiagnostics",
                true,
                "When enabled, records host-side vanilla P2 reservation diagnostics for remote co-op emulation tests.");

            _phase5HostP2ManualSpawnHotkey = Config.Bind(
                "Phase5",
                "HostP2ManualSpawnHotkey",
                true,
                "When enabled, F9 on the assigned host attempts to spawn/reserve vanilla P2 when a remote-p2 client is present.");

            _phase5HostP2ClearHotkey = Config.Bind(
                "Phase5",
                "HostP2ClearHotkey",
                true,
                "When enabled, F10 on the assigned host removes the reserved vanilla P2 through the normal co-op removal path.");

            _phase5HoldReservedP2WithoutController = Config.Bind(
                "Phase5",
                "HoldReservedP2WithoutController",
                true,
                "When enabled, the host temporarily disables vanilla no-controller P2 removal while a remote-p2 reservation is active.");

            _phase5RequireRemoteP2ForReservation = Config.Bind(
                "Phase5",
                "RequireRemoteP2ForReservation",
                true,
                "When enabled, F9 host P2 reservation is refused until the server roster contains an active remote-p2 client.");

            _phase6RemoteP2MotionDriver = Config.Bind(
                "Phase6",
                "RemoteP2MotionDriver",
                true,
                "When enabled on the host, drives the reserved vanilla P2 body from remote-p2 relay data using virtual input axes and snap correction.");

            _phase6RequireWorldMatchForRemoteP2Driver = Config.Bind(
                "Phase6",
                "RequireWorldMatchForRemoteP2Driver",
                false,
                "When enabled, pauses host P2 driving if the remote-p2 roster row reports worldMatch=False. Disabled by default because live save hashes can drift during normal play.");

            _phase6BlockRemoteP2Transitions = Config.Bind(
                "Phase6",
                "BlockRemoteP2Transitions",
                true,
                "When enabled, blocks reserved remote P2 from triggering host-side building and door transitions during movement sync tests.");

            _phase6RemoteP2HideWhenSceneMismatch = Config.Bind(
                "Phase6",
                "RemoteP2HideWhenSceneMismatch",
                true,
                "When enabled, the host hides the reserved remote P2 body while the remote-p2 client reports a different active Unity scene.");

            _phase6RemoteP2HideWhenLocationMismatch = Config.Bind(
                "Phase6",
                "RemoteP2HideWhenLocationMismatch",
                true,
                "When enabled, the host hides the reserved remote P2 body while the remote-p2 client reports a different PlayerFarming.Location.");

            _phase6RemoteP2HideDuringTransitionStates = Config.Bind(
                "Phase6",
                "RemoteP2HideDuringTransitionStates",
                false,
                "When enabled, the host hides the reserved remote P2 body while the remote-p2 client stays in transition states such as InActive or CustomAnimation. Disabled by default because those states occur during normal co-op spawn.");

            _phase7RemoteHostVisualMirror = Config.Bind(
                "Phase7",
                "RemoteHostVisualMirror",
                true,
                "When enabled on a remote-p2 client, auto-spawns a local vanilla P2 body as a non-authoritative visual mirror of the host-lamb player.");

            _phase7RemoteHostMirrorAutoSpawn = Config.Bind(
                "Phase7",
                "RemoteHostMirrorAutoSpawn",
                true,
                "When enabled, the remote-p2 client automatically calls SpawnCoopPlayer for the host visual mirror once host motion arrives.");

            _phase7BlockRemoteHostMirrorTransitions = Config.Bind(
                "Phase7",
                "BlockRemoteHostMirrorTransitions",
                true,
                "When enabled, blocks the remote host visual mirror from triggering local building and door transitions.");

            _phase6RemoteP2MaxAgeMs = Config.Bind(
                "Phase6",
                "RemoteP2MaxAgeMs",
                1000f,
                "Maximum age in milliseconds for remote-p2 motion packets before the host refuses to apply them.");

            _phase6RemoteP2SnapDistance = Config.Bind(
                "Phase6",
                "RemoteP2SnapDistance",
                4.0f,
                "Distance in Unity units where the host snaps reserved P2 directly to the remote position instead of smoothing.");

            _phase6RemoteP2MoveSpeed = Config.Bind(
                "Phase6",
                "RemoteP2MoveSpeed",
                30.0f,
                "Reserved for future direct-smoothing tests. Current Phase6 movement primarily uses vanilla controller speed from virtual input axes.");

            _phase6RemoteP2CorrectionSpeed = Config.Bind(
                "Phase6",
                "RemoteP2CorrectionSpeed",
                6.0f,
                "How quickly the host nudges reserved P2 toward the remote-p2 position while using input relay. 0 disables soft correction.");

            _phase6RemoteP2AwayGraceSeconds = Config.Bind(
                "Phase6",
                "RemoteP2AwayGraceSeconds",
                0.45f,
                "How long a remote scene/location/transition mismatch must persist before the host hides the reserved P2 body.");

            _phase7RemoteHostMirrorMaxAgeMs = Config.Bind(
                "Phase7",
                "RemoteHostMirrorMaxAgeMs",
                1000f,
                "Maximum age in milliseconds for host-lamb motion/input packets before the remote visual mirror refuses to apply them.");

            _phase7RemoteHostMirrorSnapDistance = Config.Bind(
                "Phase7",
                "RemoteHostMirrorSnapDistance",
                4.0f,
                "Base snap distance for the remote host visual mirror. Hard snaps are internally guarded to larger errors.");

            _phase7RemoteHostMirrorCorrectionSpeed = Config.Bind(
                "Phase7",
                "RemoteHostMirrorCorrectionSpeed",
                8.0f,
                "How quickly the remote-p2 client nudges the host visual mirror toward the relayed host position.");

            _phase7RemoteHostMirrorHoldGraceSeconds = Config.Bind(
                "Phase7",
                "RemoteHostMirrorHoldGraceSeconds",
                10.0f,
                "How long to keep the no-controller mirror hold after host-lamb relay packets briefly stop or go stale.");

            _phase8WorldAuthorityDiagnostics = Config.Bind(
                "Phase8",
                "WorldAuthorityDiagnostics",
                true,
                "When enabled, emits compact cult/follower snapshots so the server can compare active world state across clients.");

            _phase8CultSnapshotIntervalSeconds = Config.Bind(
                "Phase8",
                "CultSnapshotIntervalSeconds",
                2.0f,
                "How often to emit sync.cult_snapshot events for host-owned world authority tests.");

            _phase9LoadoutRelay = Config.Bind(
                "Phase9",
                "ServerLoadoutRelay",
                true,
                "When enabled, applies server-relayed weapon/curse loadouts to remote-controlled local bodies so dungeon co-op gates see both players equipped.");

            _phase9LoadoutOverrideRemoteBodies = Config.Bind(
                "Phase9",
                "LoadoutOverrideRemoteBodies",
                true,
                "When enabled, server loadouts can replace copied or stale weapon/curse values on remote P2/mirror bodies. The local real player is never overwritten.");

            _phase9LocalCameraSplit = Config.Bind(
                "Phase9",
                "LocalCameraSplit",
                true,
                "When enabled, the bridge focuses the camera on the local real player when remote/mirror bodies are far enough away to break vanilla co-op framing.");

            _phase9CameraSplitDistance = Config.Bind(
                "Phase9",
                "CameraSplitDistance",
                14.0f,
                "Distance in Unity units between local and remote bodies before the camera follows only the local real player.");

            _phase9CameraReturnDistance = Config.Bind(
                "Phase9",
                "CameraReturnDistance",
                8.0f,
                "Distance in Unity units below which the camera returns to vanilla co-op multi-target framing.");

            _perfSlowTickThresholdMs = Config.Bind(
                "Trace",
                "PerfSlowTickThresholdMs",
                12.0f,
                "Main-thread milliseconds a diagnostics subsystem may take before perf.tick_slow is recorded.");

            _phase10RunAuthority = Config.Bind(
                "Phase10",
                "ServerRunAuthority",
                true,
                "When enabled, receives server-owned dungeon seed and reward-choice authority packets.");

            _phase10ForceServerDungeonSeed = Config.Bind(
                "Phase10",
                "ForceServerDungeonSeed",
                true,
                "When enabled on non-host clients, replaces locally generated dungeon seeds with the server/host seed when available.");

            _phase10ForceServerRewards = Config.Bind(
                "Phase10",
                "ForceServerRewards",
                true,
                "When enabled on non-host clients, forces weapon/curse podium randomizers to follow the server/host reward order when available.");

            _phase10WaitForServerSeedBeforeDungeonGenerate = Config.Bind(
                "Phase10",
                "WaitForServerSeedBeforeDungeonGenerate",
                true,
                "When enabled on non-host clients, briefly holds BiomeGenerator.GenerateRoutine so dungeon loading can receive the host/server seed before graph generation starts.");

            _phase10ServerSeedWaitTimeoutSeconds = Config.Bind(
                "Phase10",
                "ServerSeedWaitTimeoutSeconds",
                5.0f,
                "Maximum seconds a non-host dungeon loading coroutine waits for the server/host dungeon seed before proceeding.");

            _phase11SpellCastRelay = Config.Bind(
                "Phase11",
                "SpellCastRelay",
                true,
                "When enabled, relays completed local curse casts as sequenced commands and replays each cast once on the opposite bridge-owned P2 body.");

            _phase11SpellRelayVisualOnly = Config.Bind(
                "Phase11",
                "SpellRelayVisualOnly",
                true,
                "When enabled, relayed spell replay uses zero damage multiplier on the receiving client so spell visuals do not intentionally author local enemy damage.");

            _phase12RewardClaimRelay = Config.Bind(
                "Phase12",
                "RewardClaimRelay",
                true,
                "When enabled, applies server-relayed weapon/curse reward-claim state by marking matching local podiums inactive.");

            _phase13CombatAuthorityDiagnostics = Config.Bind(
                "Phase13",
                "CombatAuthorityDiagnostics",
                true,
                "When enabled, emits compact enemy roster, combat spawn, world manipulation, and death-state diagnostics for server-owned combat tests.");

            _phase15ServerSaveAuthority = Config.Bind(
                "Phase15",
                "ServerSaveAuthority",
                true,
                "When enabled, tests host-owned save snapshot transport through ServerLedger. The server save slot is treated as the online/LAN save slot.");

            _phase15CaptureHostSaveSnapshots = Config.Bind(
                "Phase15",
                "CaptureHostSaveSnapshots",
                true,
                "When enabled on the assigned host-lamb client, captures the active save files, compresses them, and streams chunks to ServerLedger.");

            _phase15ApplyServerSaveSnapshots = Config.Bind(
                "Phase15",
                "ApplyServerSaveSnapshots",
                true,
                "When enabled on remote clients, writes server-relayed host save snapshots into ServerSaveSlot.");

            _phase15BlockRemoteClientSaves = Config.Bind(
                "Phase15",
                "BlockRemoteClientSaves",
                true,
                "When enabled on non-host clients, blocks SaveAndLoad.Save while connected so the host/server save remains authoritative.");

            _phase15ServerSaveSlot = Config.Bind(
                "Phase15",
                "ServerSaveSlot",
                4,
                "Raw save slot used for the online/LAN server save copy. COTL's UI shows raw slot 4 as slot 5.");

            _phase13CombatRosterIntervalSeconds = Config.Bind(
                "Phase13",
                "CombatRosterIntervalSeconds",
                1.0f,
                "How often to emit sync.combat_roster while in dungeon rooms.");

            _phase15HostSaveSnapshotIntervalSeconds = Config.Bind(
                "Phase15",
                "HostSaveSnapshotIntervalSeconds",
                30.0f,
                "How often the host sends a save snapshot if the active save files changed. SaveAndLoad.Save also triggers an immediate snapshot.");

            _phase16FollowerAuthority = Config.Bind(
                "Phase16",
                "FollowerAuthority",
                true,
                "When enabled, emits and receives host-authored follower catalog packets keyed by stable FollowerInfo.ID.");

            _phase16ApplyHostFollowerPositions = Config.Bind(
                "Phase16",
                "ApplyHostFollowerPositions",
                true,
                "When enabled on non-host clients, gently moves local followers toward host-authored positions by FollowerInfo.ID. This is visual/position authority only; jobs/interactions still need command sync.");

            _phase16FollowerCatalogIntervalSeconds = Config.Bind(
                "Phase16",
                "FollowerCatalogIntervalSeconds",
                0.75f,
                "How often to emit sync.follower_catalog while in cult/base scenes.");

            _phase16FollowerAuthorityMaxAgeMs = Config.Bind(
                "Phase16",
                "FollowerAuthorityMaxAgeMs",
                2500.0f,
                "Maximum age in milliseconds for host follower authority packets before remote clients stop applying them.");

            _phase16FollowerSnapDistance = Config.Bind(
                "Phase16",
                "FollowerSnapDistance",
                5.0f,
                "Distance in Unity units before a remote follower body snaps to the host position instead of smoothing.");

            _phase16FollowerCorrectionSpeed = Config.Bind(
                "Phase16",
                "FollowerCorrectionSpeed",
                12.0f,
                "How quickly remote follower bodies move toward host positions when under the snap threshold.");

            _clientId = Config.Bind(
                "Live",
                "ClientId",
                "auto",
                "Stable client identity for LAN/server tests. Leave as auto to generate and persist a local id.");

            _liveUdpHost = Config.Bind(
                "Live",
                "UdpHost",
                "127.0.0.1",
                "Host for the local UDP server prototype.");

            _liveUdpPort = Config.Bind(
                "Live",
                "UdpPort",
                37622,
                "Port for the local UDP server prototype.");

            _livePlayerMotionInterval = Config.Bind(
                "Live",
                "PlayerMotionIntervalSeconds",
                0.1f,
                "How often to emit sync.player_motion events while PlayerMotionStream is enabled. 0.1 is 10Hz; 0.05 is 20Hz stress testing.");

            _livePlayerInputInterval = Config.Bind(
                "Live",
                "PlayerInputIntervalSeconds",
                0.05f,
                "How often to emit sync.player_input events while PlayerInputStream is enabled. 0.05 is 20Hz.");

            _runtimeSnapshotInterval = Config.Bind(
                "Trace",
                "RuntimeSnapshotIntervalSeconds",
                1.0f,
                "How often to record scene/player/health/team state while verbose snapshots are enabled.");

            _persistentSnapshotInterval = Config.Bind(
                "Trace",
                "PersistentSnapshotIntervalSeconds",
                60.0f,
                "How often to record a selected DataManager persistence hash while verbose snapshots are enabled. Runtime clamps this to at least 30 seconds.");

            _saveFileHashInterval = Config.Bind(
                "Trace",
                "SaveFileHashIntervalSeconds",
                0.0f,
                "How often to re-hash save slot files after the first world identity sample. 0 keeps the startup hash cached to avoid live disk-hash hitches.");

            ClientId = ResolveClientId(_clientId);
            SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            _bridgeOverlayVisible = _bridgeRosterOverlay.Value;

            WorldTrace.Start(Logger);
            WorldTrace.Configure(_traceHighFrequencyPackets.Value);
            BridgePerfProfiler.Configure(_perfDiagnostics.Value, _perfSlowTickThresholdMs.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: trace started.");
            WorldTrace.Record("bridge.config", "rosterOverlay=" + _bridgeRosterOverlay.Value + " toggle=F8");
            BridgeRemotePlayerMarkers.Configure(_bridgeRemotePlayerWorldMarkers.Value);
            LiveEventClient.Configure(_liveUdpEventStream.Value, _liveUdpHost.Value, _liveUdpPort.Value, Logger);
            Logger.LogInfo("Diagnostics startup checkpoint: live event client configured.");
            TraceRawHookEvents = _traceRawHookEvents.Value;
            TraceObjectPoolEvents = _traceObjectPoolEvents.Value;
            float effectivePlayerMotionInterval = Mathf.Min(_livePlayerMotionInterval.Value, 0.1f);
            SyncEventRecorder.Configure(
                _syncEvents.Value,
                _syncAppliedDamageOnly.Value,
                _syncIgnoreNeutralDamage.Value,
                _livePlayerMotionStream.Value,
                effectivePlayerMotionInterval,
                _livePlayerInputStream.Value,
                _livePlayerInputInterval.Value,
                ClientId,
                SessionId);
            Logger.LogInfo("Diagnostics startup checkpoint: sync recorder configured.");
            Phase2Trace.Configure(
                _phase2CoopDiagnostics.Value,
                _phase2RoomDiagnostics.Value,
                _phase2RewardDiagnostics.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 2 diagnostics configured.");
            Phase3Trace.Configure(_phase3AuthorityDiagnostics.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 3 diagnostics configured.");
            BridgeCoopReservation.Configure(
                _phase5HostP2ReservationDiagnostics.Value,
                _phase5HostP2ManualSpawnHotkey.Value,
                _phase5HostP2ClearHotkey.Value,
                _phase5HoldReservedP2WithoutController.Value,
                _phase5RequireRemoteP2ForReservation.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 5 host P2 reservation diagnostics configured.");
            BridgeRemoteP2Driver.Configure(
                _phase6RemoteP2MotionDriver.Value,
                _phase6RemoteP2MaxAgeMs.Value,
                _phase6RemoteP2SnapDistance.Value,
                _phase6RemoteP2MoveSpeed.Value,
                _phase6RemoteP2CorrectionSpeed.Value,
                _phase6RequireWorldMatchForRemoteP2Driver.Value,
                _phase6BlockRemoteP2Transitions.Value,
                _phase6RemoteP2HideWhenSceneMismatch.Value,
                _phase6RemoteP2HideWhenLocationMismatch.Value,
                _phase6RemoteP2HideDuringTransitionStates.Value,
                _phase6RemoteP2AwayGraceSeconds.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 6 remote P2 motion driver configured.");
            BridgeRemoteHostMirror.Configure(
                _phase7RemoteHostVisualMirror.Value,
                _phase7RemoteHostMirrorAutoSpawn.Value,
                _phase7RemoteHostMirrorMaxAgeMs.Value,
                _phase7RemoteHostMirrorSnapDistance.Value,
                _phase7RemoteHostMirrorCorrectionSpeed.Value,
                _phase7RemoteHostMirrorHoldGraceSeconds.Value,
                _phase7BlockRemoteHostMirrorTransitions.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 7 remote host visual mirror configured.");
            BridgeWorldAuthority.Configure(
                _phase8WorldAuthorityDiagnostics.Value,
                _phase8CultSnapshotIntervalSeconds.Value,
                _phase16FollowerAuthority.Value,
                _phase16FollowerCatalogIntervalSeconds.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 8/16 world and follower authority diagnostics configured.");
            BridgeFollowerAuthority.Configure(
                _phase16FollowerAuthority.Value,
                _phase16ApplyHostFollowerPositions.Value,
                _phase16FollowerAuthorityMaxAgeMs.Value,
                _phase16FollowerSnapDistance.Value,
                _phase16FollowerCorrectionSpeed.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 16 follower authority configured.");
            BridgeLoadoutAuthority.Configure(
                _phase9LoadoutRelay.Value,
                _phase9LoadoutOverrideRemoteBodies.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 9 loadout relay configured.");
            BridgeCameraSplit.Configure(
                _phase9LocalCameraSplit.Value,
                _phase9CameraSplitDistance.Value,
                _phase9CameraReturnDistance.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 9 local camera split configured.");
            BridgeRunAuthority.Configure(
                _phase10RunAuthority.Value,
                _phase10ForceServerDungeonSeed.Value,
                _phase10ForceServerRewards.Value,
                _phase10WaitForServerSeedBeforeDungeonGenerate.Value,
                _phase10ServerSeedWaitTimeoutSeconds.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 10 run authority configured.");
            BridgeSpellAuthority.Configure(
                _phase11SpellCastRelay.Value,
                _phase11SpellRelayVisualOnly.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 11 explicit spell relay configured.");
            BridgeRewardClaimAuthority.Configure(_phase12RewardClaimRelay.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 12 reward claim relay configured.");
            BridgeCombatAuthority.Configure(
                _phase13CombatAuthorityDiagnostics.Value,
                _phase13CombatRosterIntervalSeconds.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 13 combat authority diagnostics configured.");
            BridgeSaveAuthority.Configure(
                _phase15ServerSaveAuthority.Value,
                _phase15CaptureHostSaveSnapshots.Value,
                _phase15ApplyServerSaveSnapshots.Value,
                _phase15BlockRemoteClientSaves.Value,
                _phase15ServerSaveSlot.Value,
                _phase15HostSaveSnapshotIntervalSeconds.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: phase 15 server save authority configured.");
            WorldSnapshotSampler.Configure(
                _verboseSnapshots.Value,
                _runtimeSnapshotInterval.Value,
                _persistentSnapshotInterval.Value,
                _persistentSnapshotPreview.Value,
                _saveFileHashInterval.Value,
                _deepPersistentWorldHash.Value);
            Logger.LogInfo("Diagnostics startup checkpoint: snapshot sampler configured.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(DiagnosticsPlugin).Assembly);
            Logger.LogInfo("Diagnostics startup checkpoint: Harmony patches applied.");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            WorldTrace.Record("plugin.loaded", "Diagnostics plugin loaded and Harmony patches applied.");
            LogLoadedPlugins();
        }

        private void Update()
        {
            if (_bridgeRosterOverlay.Value && Input.GetKeyDown(KeyCode.F8))
            {
                _bridgeOverlayVisible = !_bridgeOverlayVisible;
                WorldTrace.Record("bridge.overlay", "visible=" + _bridgeOverlayVisible);
            }

            BridgePerfProfiler.Measure("WorldSnapshotSampler", WorldSnapshotSampler.Tick);
            BridgePerfProfiler.Measure("SyncEventRecorder.TickPlayerInput", SyncEventRecorder.TickPlayerInput);
            BridgePerfProfiler.Measure("SyncEventRecorder.TickPlayerMotion", SyncEventRecorder.TickPlayerMotion);
            BridgePerfProfiler.Measure("SyncEventRecorder.TickPlayerLife", SyncEventRecorder.TickPlayerLife);
            BridgePerfProfiler.Measure("BridgeCoopReservation", BridgeCoopReservation.Tick);
            BridgePerfProfiler.Measure("BridgeRemoteP2Driver", BridgeRemoteP2Driver.Tick);
            BridgePerfProfiler.Measure("BridgeRemoteHostMirror", BridgeRemoteHostMirror.Tick);
            BridgePerfProfiler.Measure("BridgeSpellAuthority", BridgeSpellAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeWorldAuthority", BridgeWorldAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeFollowerAuthority", BridgeFollowerAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeLoadoutAuthority", BridgeLoadoutAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeRewardClaimAuthority", BridgeRewardClaimAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeCombatAuthority", BridgeCombatAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeSaveAuthority", BridgeSaveAuthority.Tick);
            BridgePerfProfiler.Measure("BridgeCameraSplit", BridgeCameraSplit.Tick);
            BridgePerfProfiler.Measure("BridgeRunAuthority", BridgeRunAuthority.Tick);
            BridgePerfProfiler.Measure("LiveHeartbeat", RecordLiveHeartbeat);
        }

        private void LateUpdate()
        {
            BridgePerfProfiler.Measure("BridgeCameraSplit.Late", BridgeCameraSplit.Tick);
        }

        private void OnGUI()
        {
            if (_bridgeRemotePlayerWorldMarkers.Value)
            {
                BridgeRemotePlayerMarkers.Draw(ClientId);
            }

            if (_bridgeRosterOverlay.Value && _bridgeOverlayVisible)
            {
                BridgeOverlay.Draw(ClientId, SessionId, PluginVersion);
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                WorldTrace.Record("plugin.unpatch.error", ex.GetType().Name + ": " + ex.Message);
            }

            WorldTrace.Record("plugin.unloaded", "Diagnostics plugin stopped.");
            WorldTrace.Stop();
            LiveEventClient.Stop();
            _harmony = null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            WorldTrace.Record("scene.loaded", "name=" + scene.name + " mode=" + mode);
            SyncEventRecorder.RecordScene("loaded", scene.name, mode.ToString());
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            WorldTrace.Record("scene.active_changed", "from=" + previous.name + " to=" + next.name);
            SyncEventRecorder.RecordScene("active_changed", next.name, "from=" + previous.name);
        }

        private static void LogLoadedPlugins()
        {
            try
            {
                foreach (var plugin in Chainloader.PluginInfos)
                {
                    WorldTrace.Record(
                        "bepinex.plugin.loaded",
                        "guid=" + plugin.Key
                        + " name=" + plugin.Value.Metadata.Name
                        + " version=" + plugin.Value.Metadata.Version);
                }
            }
            catch (Exception ex)
            {
                WorldTrace.Record("bepinex.plugin.scan_failed", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void RecordLiveHeartbeat()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextLiveHeartbeatAt)
            {
                return;
            }

            _nextLiveHeartbeatAt = now + 5.0f;

            try
            {
                WorldTrace.Record(
                    "live.heartbeat",
                    "clientId=" + ClientId
                    + " sessionId=" + SessionId
                    + " pluginVersion=" + PluginVersion
                    + " scene=" + SceneManager.GetActiveScene().name
                    + " location=" + SafeValue(() => PlayerFarming.Location.ToString())
                    + " coopActive=" + SafeValue(() => CoopManager.CoopActive.ToString())
                    + " playersCount=" + SafeValue(() => PlayerFarming.playersCount.ToString())
                    + " run=" + SafeValue(() => DataManager.Instance != null ? DataManager.Instance.dungeonRun.ToString() : "null")
                    + " lastDungeonSeeds=[" + SafeValue(() => DataManager.Instance != null && DataManager.Instance.LastDungeonSeeds != null ? string.Join(",", DataManager.Instance.LastDungeonSeeds) : "null") + "]");
            }
            catch (Exception ex)
            {
                WorldTrace.Record("live.heartbeat.error", ex.GetType().Name + ": " + ex.Message);
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

        private static string ResolveClientId(ConfigEntry<string> entry)
        {
            string value = entry.Value;
            if (string.IsNullOrWhiteSpace(value) || value == "auto")
            {
                value = "client-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                entry.Value = value;
            }

            return CleanToken(value);
        }

        private static string CleanToken(string value)
        {
            return (value ?? "unknown")
                .Replace(" ", "_")
                .Replace("\t", "_")
                .Replace("\r", "_")
                .Replace("\n", "_");
        }
    }
}
