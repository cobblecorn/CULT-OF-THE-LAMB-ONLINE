using System;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeOverlay
    {
        private static GUIStyle _labelStyle;
        private static GUIStyle _dimStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _okStyle;
        private static GUIStyle _warnStyle;

        public static void Draw(string clientId, string sessionId, string pluginVersion)
        {
            EnsureStyles();

            BridgeRosterSnapshot snapshot = BridgeRosterState.Snapshot();
            BridgeRemotePlayerSnapshot remoteSnapshot = BridgeRemotePlayerState.Snapshot();
            float width = Math.Min(620f, Math.Max(360f, Screen.width - 32f));
            int rosterRows = Math.Min(6, Math.Max(1, snapshot.Clients.Length));
            int remoteRows = remoteSnapshot.HasPacket ? Math.Min(4, Math.Max(1, remoteSnapshot.Players.Length)) : 1;
            float desiredHeight = 220f + rosterRows * 24f + remoteRows * 24f;
            float height = Math.Min(Math.Max(260f, desiredHeight), Math.Max(220f, Screen.height - 32f));
            Rect rect = new Rect(16f, 16f, width, height);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 18f));
            GUILayout.Label("COTL Online Bridge", _titleStyle);
            GUILayout.Label("local " + clientId + " session=" + sessionId + " plugin=" + pluginVersion, _dimStyle);

            if (!snapshot.HasRoster)
            {
                GUILayout.Space(8f);
                GUILayout.Label("server roster: waiting", _warnStyle);
                GUILayout.Label("Run ServerLedger --listen-udp, then restart or keep moving in-game.", _dimStyle);
                GUILayout.EndArea();
                return;
            }

            double ageSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - snapshot.ReceivedUtc).TotalSeconds);
            GUIStyle statusStyle = ageSeconds <= 5.0 ? _okStyle : _warnStyle;
            GUILayout.Space(4f);
            GUILayout.Label(
                "server roster: " + snapshot.Clients.Length
                + " client(s), age=" + ageSeconds.ToString("0.0")
                + "s, from=" + snapshot.Source,
                statusStyle);
            GUILayout.Label(
                "world=" + snapshot.WorldId
                + " host=" + snapshot.WorldHost
                + " serverSaveSlot=" + snapshot.ServerSaveSlot
                + " runSeed=" + snapshot.RunSeed
                + " baseline=" + snapshot.BaselineHash,
                _dimStyle);
            GUILayout.Label(BridgeCoopReservation.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeRemoteP2Driver.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeRemoteHostMirror.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeSpellAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeWorldAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeLoadoutAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeRewardClaimAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeCombatAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeEnemyAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeSaveAuthority.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeCameraSplit.OverlayLine(), _dimStyle);
            GUILayout.Label(BridgeRunAuthority.OverlayLine(), _dimStyle);

            GUILayout.Space(4f);
            for (int i = 0; i < snapshot.Clients.Length && i < 6; i++)
            {
                BridgeRosterClient client = snapshot.Clients[i];
                string self = client.ClientId == clientId ? " *" : string.Empty;
                GUILayout.Label(
                    client.ClientId + self
                    + " role=" + client.Role
                    + " hostSlot=" + client.HostSlot
                    + " match=" + client.WorldMatch
                    + " cult=" + client.CultMatch
                    + "/" + client.CultFollowers
                    + " save=" + client.SaveSlot
                    + " slotOk=" + client.SaveSlotOk
                    + " hash=" + client.WorldHash
                    + " players=" + client.Players
                    + " p2=" + client.P2Wanted + "/" + client.P2Active + "/" + client.P2Hold + "/" + client.P2NoController
                    + " scene=" + client.Scene
                    + " loc=" + client.Location
                    + " ageMs=" + client.AgeMs,
                    _labelStyle);
            }

            if (snapshot.Clients.Length > 6)
            {
                GUILayout.Label("+" + (snapshot.Clients.Length - 6) + " more", _dimStyle);
            }

            GUILayout.Space(6f);
            if (!remoteSnapshot.HasPacket)
            {
                GUILayout.Label("remote relay: waiting for motion", _warnStyle);
            }
            else
            {
                double remoteAgeSeconds = Math.Max(0.0, (DateTimeOffset.UtcNow - remoteSnapshot.ReceivedUtc).TotalSeconds);
                GUIStyle remoteStatusStyle = remoteAgeSeconds <= 2.0 ? _okStyle : _warnStyle;
                GUILayout.Label(
                    "remote relay: " + remoteSnapshot.RemoteCount
                    + " player(s), age=" + remoteAgeSeconds.ToString("0.0")
                    + "s, target=" + remoteSnapshot.TargetClientId,
                    remoteStatusStyle);

                if (remoteSnapshot.Players.Length == 0)
                {
                    GUILayout.Label("no remote players in latest relay packet", _dimStyle);
                }

                for (int i = 0; i < remoteSnapshot.Players.Length && i < 4; i++)
                {
                    BridgeRemotePlayer player = remoteSnapshot.Players[i];
                    GUILayout.Label(
                        player.ClientId
                        + " slot=" + player.HostSlot
                        + " p=" + player.PlayerId
                        + " seq=" + player.Sequence
                        + " pos=" + player.Position
                        + " state=" + player.State
                        + " hp=" + player.HitPoints
                        + " ageMs=" + player.AgeMs,
                        _labelStyle);
                }

                if (remoteSnapshot.Players.Length > 4)
                {
                    GUILayout.Label("+" + (remoteSnapshot.Players.Length - 4) + " more remote player(s)", _dimStyle);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("F8 overlay. F9 host P2 reserve. F10 host P2 clear. Phase6 drives reserved P2 from relay.", _dimStyle);
            GUILayout.EndArea();
        }

        private static void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = false
            };
            _labelStyle.normal.textColor = Color.white;

            _dimStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = false
            };
            _dimStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _titleStyle.normal.textColor = Color.white;

            _okStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _okStyle.normal.textColor = new Color(0.45f, 1f, 0.55f, 1f);

            _warnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _warnStyle.normal.textColor = new Color(1f, 0.8f, 0.35f, 1f);
        }
    }
}
