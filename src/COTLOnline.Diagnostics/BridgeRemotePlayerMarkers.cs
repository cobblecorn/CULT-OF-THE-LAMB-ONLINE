using System;
using System.Globalization;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemotePlayerMarkers
    {
        private static bool _enabled;
        private static GUIStyle _labelStyle;
        private static GUIStyle _boxStyle;

        public static void Configure(bool enabled)
        {
            _enabled = enabled;
            WorldTrace.Record("bridge.markers.config", "enabled=" + enabled);
        }

        public static void Draw(string localClientId)
        {
            if (!_enabled)
            {
                return;
            }

            BridgeRemotePlayerSnapshot snapshot = BridgeRemotePlayerState.Snapshot();
            if (!snapshot.HasPacket || snapshot.Players.Length == 0)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            EnsureStyles();
            for (int i = 0; i < snapshot.Players.Length && i < 6; i++)
            {
                BridgeRemotePlayer player = snapshot.Players[i];
                if (player == null
                    || string.Equals(player.ClientId, localClientId, StringComparison.Ordinal)
                    || !TryParsePosition(player.Position, out Vector3 worldPosition))
                {
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(worldPosition);
                if (screen.z < 0f)
                {
                    continue;
                }

                float x = Mathf.Clamp(screen.x, 18f, Screen.width - 18f);
                float y = Mathf.Clamp(Screen.height - screen.y, 18f, Screen.height - 18f);
                string role = string.IsNullOrEmpty(player.Role) ? "remote" : player.Role;
                string label = role == "host-lamb" ? "P1" : role == "remote-p2" ? "P2" : "R";
                Color color = role == "host-lamb"
                    ? new Color(0.45f, 1f, 0.55f, 0.95f)
                    : new Color(0.75f, 0.55f, 1f, 0.95f);

                Color previousColor = GUI.color;
                GUI.color = color;
                GUI.Box(new Rect(x - 7f, y - 7f, 14f, 14f), GUIContent.none, _boxStyle);
                GUI.color = previousColor;

                GUI.Label(
                    new Rect(x + 10f, y - 12f, 220f, 24f),
                    label + " " + player.State + " " + player.AgeMs + "ms",
                    _labelStyle);
            }
        }

        private static void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _labelStyle.normal.textColor = Color.white;

            _boxStyle = new GUIStyle(GUI.skin.box);
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

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return false;
            }

            float z = 0f;
            if (parts.Length >= 3)
            {
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            }

            position = new Vector3(x, y, z);
            return true;
        }
    }
}
