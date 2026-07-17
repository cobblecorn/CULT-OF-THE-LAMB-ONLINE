using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace COTLOnline.Diagnostics
{
    internal static class LiveEventClient
    {
        private const int MaxDatagramBytes = 60000;
        private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent Signal = new AutoResetEvent(false);

        private static ManualLogSource _logger;
        private static UdpClient _client;
        private static IPEndPoint _endpoint;
        private static Thread _worker;
        private static Thread _receiveWorker;
        private static volatile bool _enabled;
        private static volatile bool _running;

        public static void Configure(bool enabled, string host, int port, ManualLogSource logger)
        {
            Stop();

            _logger = logger;
            _enabled = enabled;
            if (!enabled)
            {
                BridgeRosterState.Reset();
                BridgeRemotePlayerState.Reset();
                BridgeRemoteInputState.Reset();
                BridgeLoadoutAuthority.Reset();
                BridgeRunAuthority.Reset();
                BridgeRewardClaimAuthority.Reset();
                BridgeSpellAuthority.Reset();
                BridgeEnemyAuthority.Reset();
                BridgeSaveAuthority.Reset();
                BridgeFollowerAuthority.Reset();
                WorldTrace.Record("live.config", "udp=False");
                return;
            }

            try
            {
                BridgeRosterState.Reset();
                BridgeRemotePlayerState.Reset();
                BridgeRemoteInputState.Reset();
                BridgeLoadoutAuthority.Reset();
                BridgeRunAuthority.Reset();
                BridgeRewardClaimAuthority.Reset();
                BridgeSpellAuthority.Reset();
                BridgeEnemyAuthority.Reset();
                BridgeSaveAuthority.Reset();
                BridgeFollowerAuthority.Reset();
                IPAddress address = IPAddress.Parse(host);
                _endpoint = new IPEndPoint(address, port);
                _client = new UdpClient();
                _running = true;
                _worker = new Thread(SendLoop)
                {
                    IsBackground = true,
                    Name = "COTLOnline.LiveEventClient"
                };
                _receiveWorker = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "COTLOnline.LiveEventClient.Receive"
                };
                _worker.Start();
                _receiveWorker.Start();
                WorldTrace.Record("live.config", "udp=True host=" + host + " port=" + port);
            }
            catch (Exception ex)
            {
                _enabled = false;
                _running = false;
                _logger?.LogWarning("Live UDP event stream disabled: " + ex.GetType().Name + ": " + ex.Message);
                WorldTrace.Record("live.config.error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void Record(string category, string jsonLine)
        {
            if (!_enabled || !_running || string.IsNullOrEmpty(jsonLine) || !ShouldSend(category))
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(jsonLine);
            if (bytes.Length > MaxDatagramBytes)
            {
                return;
            }

            Queue.Enqueue(jsonLine);
            Signal.Set();
        }

        public static void Stop()
        {
            _enabled = false;
            _running = false;
            Signal.Set();

            try
            {
                _worker?.Join(250);
            }
            catch
            {
            }

            try
            {
                _receiveWorker?.Join(250);
            }
            catch
            {
            }

            try
            {
                _client?.Close();
            }
            catch
            {
            }

            _worker = null;
            _receiveWorker = null;
            _client = null;
            _endpoint = null;
            BridgeRosterState.Reset();
            BridgeRemotePlayerState.Reset();
            BridgeRemoteInputState.Reset();
            BridgeLoadoutAuthority.Reset();
            BridgeRunAuthority.Reset();
            BridgeRewardClaimAuthority.Reset();
            BridgeSpellAuthority.Reset();
            BridgeEnemyAuthority.Reset();
            BridgeSaveAuthority.Reset();
            BridgeFollowerAuthority.Reset();
            while (Queue.TryDequeue(out _))
            {
            }
        }

        private static void SendLoop()
        {
            while (_running)
            {
                try
                {
                    while (Queue.TryDequeue(out string jsonLine))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(jsonLine);
                        _client.Send(bytes, bytes.Length, _endpoint);
                    }

                    Signal.WaitOne(250);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Live UDP send failed: " + ex.GetType().Name + ": " + ex.Message);
                    Signal.WaitOne(1000);
                }
            }
        }

        private static void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] bytes = _client.Receive(ref remote);
                    if (bytes == null || bytes.Length == 0)
                    {
                        continue;
                    }

                    string payload = Encoding.UTF8.GetString(bytes);
                    RecordServerPacket(remote, payload);
                }
                catch (SocketException)
                {
                    if (_running)
                    {
                        Signal.WaitOne(250);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        _logger?.LogWarning("Live UDP receive failed: " + ex.GetType().Name + ": " + ex.Message);
                        Signal.WaitOne(1000);
                    }
                }
            }
        }

        private static void RecordServerPacket(IPEndPoint remote, string payload)
        {
            string category = ReadJsonString(payload, "category") ?? "server.unknown";
            string message = ReadJsonString(payload, "message") ?? payload;
            string source = remote != null ? remote.ToString() : "unknown";

            if (category == "server.roster")
            {
                BridgeRosterState.UpdateFromRoster(source, message);
                WorldTrace.Record("bridge.roster", "from=" + source + " " + message);
                return;
            }

            if (category == "server.remote_players")
            {
                BridgeRemotePlayerState.UpdateFromPacket(source, message);
                WorldTrace.RecordHighFrequency("bridge.remote_players", "from=" + source + " " + message);
                return;
            }

            if (category == "server.remote_inputs")
            {
                BridgeRemoteInputState.UpdateFromPacket(source, message);
                WorldTrace.RecordHighFrequency("bridge.remote_inputs", "from=" + source + " " + message);
                return;
            }

            if (category == "server.loadouts")
            {
                BridgeLoadoutAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.loadouts", "from=" + source + " " + message);
                return;
            }

            if (category == "server.run_authority")
            {
                BridgeRunAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.run_authority", "from=" + source + " " + message);
                return;
            }

            if (category == "server.reward_claims")
            {
                BridgeRewardClaimAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.reward_claims", "from=" + source + " " + message);
                return;
            }

            if (category == "server.spell_casts")
            {
                BridgeSpellAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.spell_casts", "from=" + source + " " + message);
                return;
            }

            if (category == "server.enemy_authority")
            {
                BridgeEnemyAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.enemy_authority", "from=" + source + " " + message);
                return;
            }

            if (category == "server.follower_authority")
            {
                BridgeFollowerAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.follower_authority", "from=" + source + " " + message);
                return;
            }

            if (category == "server.save_chunk")
            {
                BridgeSaveAuthority.UpdateFromPacket(source, message);
                WorldTrace.Record("bridge.save_chunk", "from=" + source + " " + StripLargeData(message));
                return;
            }

            WorldTrace.Record("bridge.server_packet", "from=" + source + " category=" + category + " payload=" + Clean(payload));
        }

        private static string ReadJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = "\"" + key + "\":\"";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += pattern.Length;
            StringBuilder sb = new StringBuilder();
            bool escaping = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (escaping)
                {
                    if (c == 'n')
                    {
                        sb.Append('\n');
                    }
                    else if (c == 'r')
                    {
                        sb.Append('\r');
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    escaping = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    return sb.ToString();
                }

                sb.Append(c);
            }

            return null;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static bool ShouldSend(string category)
        {
            if (string.IsNullOrEmpty(category) || category.StartsWith("snapshot.", StringComparison.Ordinal))
            {
                return false;
            }

            return category.StartsWith("sync.", StringComparison.Ordinal)
                || category.StartsWith("phase2.", StringComparison.Ordinal)
                || category.StartsWith("phase3.", StringComparison.Ordinal)
                || category.StartsWith("phase5.", StringComparison.Ordinal)
                || category.StartsWith("phase6.", StringComparison.Ordinal)
                || category.StartsWith("phase7.", StringComparison.Ordinal)
                || category.StartsWith("phase8.", StringComparison.Ordinal)
                || category.StartsWith("phase9.", StringComparison.Ordinal)
                || category.StartsWith("phase10.", StringComparison.Ordinal)
                || category.StartsWith("phase11.", StringComparison.Ordinal)
                || category.StartsWith("phase15.", StringComparison.Ordinal)
                || category.StartsWith("phase16.", StringComparison.Ordinal)
                || category.StartsWith("perf.", StringComparison.Ordinal)
                || category.StartsWith("scene.", StringComparison.Ordinal)
                || category.StartsWith("save.", StringComparison.Ordinal)
                || string.Equals(category, "sync.save_chunk", StringComparison.Ordinal)
                || string.Equals(category, "sync.save_ack", StringComparison.Ordinal)
                || category.StartsWith("plugin.", StringComparison.Ordinal)
                || category.StartsWith("bepinex.", StringComparison.Ordinal)
                || category.StartsWith("live.", StringComparison.Ordinal)
                || category.StartsWith("coop.", StringComparison.Ordinal);
        }

        private static string StripLargeData(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "null";
            }

            int index = message.IndexOf(" data=", StringComparison.Ordinal);
            if (index < 0)
            {
                return message;
            }

            return message.Substring(0, index) + " data=<chunk>";
        }
    }
}
