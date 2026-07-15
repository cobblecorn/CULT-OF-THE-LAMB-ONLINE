using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace COTLOnline.Diagnostics
{
    internal static class WorldTrace
    {
        private static readonly object LifecycleLock = new object();
        private static readonly object WriteLock = new object();

        private static ManualLogSource _logger;
        private static string _logFile;
        private static StreamWriter _writer;
        private static bool _started;
        private static bool _traceHighFrequencyPackets;

        public static void Start(ManualLogSource logger)
        {
            lock (LifecycleLock)
            {
                if (_started)
                {
                    return;
                }

                _logger = logger;

                string traceDir = Path.Combine(Paths.BepInExRootPath, "worldtrace");
                Directory.CreateDirectory(traceDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                _logFile = Path.Combine(traceDir, "cotlonline-trace-" + timestamp + ".jsonl");
                FileStream stream = new FileStream(_logFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                _started = true;

                logger.LogInfo("WorldTrace writing to " + _logFile);
            }
        }

        public static void Stop()
        {
            lock (LifecycleLock)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                _logger = null;
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch
                {
                }

                _writer = null;
            }
        }

        public static void Configure(bool traceHighFrequencyPackets)
        {
            _traceHighFrequencyPackets = traceHighFrequencyPackets;
            Record("trace.config", "traceHighFrequencyPackets=" + traceHighFrequencyPackets);
        }

        public static void Record(string category, string message)
        {
            if (!_started || string.IsNullOrEmpty(_logFile))
            {
                return;
            }

            try
            {
                lock (WriteLock)
                {
                    if (!_started || string.IsNullOrEmpty(_logFile) || _writer == null)
                    {
                        return;
                    }

                    string jsonLine = ToJsonLine(category, WithIdentity(message));
                    _writer.WriteLine(jsonLine);
                    LiveEventClient.Record(category, jsonLine);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    _logger?.LogError("WorldTrace write failed: " + ex);
                }
                catch
                {
                    // Nothing else is safe to do here.
                }
            }
        }

        public static void RecordHighFrequency(string category, string message)
        {
            if (_traceHighFrequencyPackets)
            {
                Record(category, message);
                return;
            }

            RecordLiveOnly(category, message);
        }

        public static void RecordLiveOnly(string category, string message)
        {
            if (!_started || string.IsNullOrEmpty(category))
            {
                return;
            }

            try
            {
                string jsonLine = ToJsonLine(category, WithIdentity(message));
                LiveEventClient.Record(category, jsonLine);
            }
            catch (Exception ex)
            {
                try
                {
                    _logger?.LogError("WorldTrace live-only record failed: " + ex);
                }
                catch
                {
                }
            }
        }

        public static string DescribeObject(UnityEngine.Object obj)
        {
            try
            {
                if (obj == null)
                {
                    return "null";
                }

                return obj.GetType().Name + "(" + obj.name + ")";
            }
            catch (Exception ex)
            {
                return "describe_failed:" + ex.GetType().Name;
            }
        }

        public static string DescribeGameObject(UnityEngine.GameObject obj)
        {
            try
            {
                if (obj == null)
                {
                    return "null";
                }

                UnityEngine.Vector3 p = obj.transform.position;
                return obj.name + " pos=" + FormatVector(p) + " active=" + obj.activeSelf;
            }
            catch (Exception ex)
            {
                return "describe_failed:" + ex.GetType().Name;
            }
        }

        public static string DescribeHealth(Health health)
        {
            try
            {
                if (health == null)
                {
                    return "null";
                }

                return DescribeGameObject(health.gameObject)
                    + " team=" + health.team
                    + " hp=" + FormatFloat(health.HP)
                    + "/" + FormatFloat(health.totalHP);
            }
            catch (Exception ex)
            {
                return "describe_failed:" + ex.GetType().Name;
            }
        }

        public static string DescribePlayer(PlayerFarming player)
        {
            try
            {
                if (player == null)
                {
                    return "null";
                }

                return DescribeGameObject(player.gameObject)
                    + " playerID=" + player.playerID
                    + " state=" + (player.state != null ? player.state.CURRENT_STATE.ToString() : "null");
            }
            catch (Exception ex)
            {
                return "describe_failed:" + ex.GetType().Name;
            }
        }

        public static string FormatVector(UnityEngine.Vector3 value)
        {
            return "("
                + FormatFloat(value.x) + ","
                + FormatFloat(value.y) + ","
                + FormatFloat(value.z) + ")";
        }

        public static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string ToJsonLine(string category, string message)
        {
            string timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return "{\"ts\":\"" + Escape(timestamp)
                + "\",\"category\":\"" + Escape(category)
                + "\",\"message\":\"" + Escape(message)
                + "\"}";
        }

        private static string WithIdentity(string message)
        {
            if (!string.IsNullOrEmpty(message) && message.Contains("clientId="))
            {
                return message;
            }

            string clientId = string.IsNullOrEmpty(DiagnosticsPlugin.ClientId) ? "unknown" : DiagnosticsPlugin.ClientId;
            string sessionId = string.IsNullOrEmpty(DiagnosticsPlugin.SessionId) ? "unknown" : DiagnosticsPlugin.SessionId;
            return "clientId=" + clientId + " sessionId=" + sessionId + " " + (message ?? string.Empty);
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
