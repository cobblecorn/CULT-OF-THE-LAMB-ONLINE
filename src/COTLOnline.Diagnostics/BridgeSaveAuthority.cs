using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeSaveAuthority
    {
        private const int SaveBundleVersion = 1;
        private const int ChunkBytes = 24000;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, IncomingSaveSnapshot> Incoming = new Dictionary<string, IncomingSaveSnapshot>(StringComparer.Ordinal);

        private static bool _enabled;
        private static bool _captureHostSnapshots;
        private static bool _applyServerSnapshots;
        private static bool _blockRemoteClientSaves;
        private static int _targetSaveSlot;
        private static float _snapshotIntervalSeconds;
        private static float _nextSnapshotAt;
        private static int _sequence;
        private static string _lastSnapshotSignature = "";
        private static string _lastAppliedSnapshot = "";
        private static string _overlayLine = "save authority: disabled";
        private static float _nextRecordAt;

        public static void Configure(
            bool enabled,
            bool captureHostSnapshots,
            bool applyServerSnapshots,
            bool blockRemoteClientSaves,
            int targetSaveSlot,
            float snapshotIntervalSeconds)
        {
            _enabled = enabled;
            _captureHostSnapshots = captureHostSnapshots;
            _applyServerSnapshots = applyServerSnapshots;
            _blockRemoteClientSaves = blockRemoteClientSaves;
            _targetSaveSlot = Math.Max(0, targetSaveSlot);
            _snapshotIntervalSeconds = Mathf.Max(0f, snapshotIntervalSeconds);
            _nextSnapshotAt = 0f;
            _sequence = 0;
            _lastSnapshotSignature = "";
            _lastAppliedSnapshot = "";
            _nextRecordAt = 0f;
            lock (Sync)
            {
                Incoming.Clear();
            }

            _overlayLine = enabled ? "save authority: waiting" : "save authority: disabled";
            WorldTrace.Record(
                "phase15.save.config",
                "enabled=" + enabled
                + " captureHost=" + captureHostSnapshots
                + " applyServer=" + applyServerSnapshots
                + " blockRemoteSaves=" + blockRemoteClientSaves
                + " targetSlot=" + _targetSaveSlot
                + " interval=" + WorldTrace.FormatFloat(_snapshotIntervalSeconds));
        }

        public static void Reset()
        {
            lock (Sync)
            {
                Incoming.Clear();
            }

            _lastAppliedSnapshot = "";
            _overlayLine = _enabled ? "save authority: waiting" : "save authority: disabled";
        }

        public static void Tick()
        {
            if (!_enabled)
            {
                _overlayLine = "save authority: disabled";
                return;
            }

            if (_captureHostSnapshots && IsLocalHost())
            {
                if (_snapshotIntervalSeconds > 0f && Time.unscaledTime >= _nextSnapshotAt)
                {
                    _nextSnapshotAt = Time.unscaledTime + _snapshotIntervalSeconds;
                    CaptureAndSendHostSave("periodic");
                }
            }

            TryApplyPendingSnapshot();
            UpdateOverlay();
        }

        public static string OverlayLine()
        {
            return _enabled ? _overlayLine : "";
        }

        public static void UpdateFromPacket(string source, string message)
        {
            if (!_enabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            string target = ReadToken(message, "target") ?? "unknown";
            if (!string.Equals(target, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
            {
                return;
            }

            string snapshotId = ReadToken(message, "snapshot") ?? "";
            int chunkIndex = ReadInt(message, "chunk", -1);
            int chunks = ReadInt(message, "chunks", -1);
            string data = ReadToken(message, "data") ?? "";
            if (string.IsNullOrEmpty(snapshotId) || chunkIndex < 0 || chunks <= 0 || string.IsNullOrEmpty(data))
            {
                return;
            }

            lock (Sync)
            {
                IncomingSaveSnapshot snapshot;
                if (!Incoming.TryGetValue(snapshotId, out snapshot))
                {
                    snapshot = new IncomingSaveSnapshot(snapshotId, chunks)
                    {
                        Source = ReadToken(message, "source") ?? "unknown",
                        ServerTime = ReadToken(message, "serverTime") ?? "unknown",
                        SourceSlot = ReadInt(message, "sourceSlot", -1),
                        TargetSlot = ReadInt(message, "targetSlot", _targetSaveSlot),
                        RawBytes = ReadInt(message, "rawBytes", -1),
                        CompressedBytes = ReadInt(message, "compressedBytes", -1),
                        Hash = ReadToken(message, "hash") ?? "unknown"
                    };
                    Incoming[snapshotId] = snapshot;
                }

                if (chunkIndex < snapshot.Chunks.Length)
                {
                    snapshot.Chunks[chunkIndex] = data;
                    snapshot.ReceivedUtc = DateTimeOffset.UtcNow;
                }
            }
        }

        public static bool ShouldBlockClientSave(string source)
        {
            if (!_enabled || !_blockRemoteClientSaves || !IsRemoteClient())
            {
                return false;
            }

            RecordThrottled(
                "phase15.save.blocked",
                "source=" + Clean(source)
                + " role=" + Clean(SelfRole())
                + " slot=" + SafeInt(() => SaveAndLoad.SAVE_SLOT, -1));
            return true;
        }

        public static void CaptureAndSendHostSave(string reason)
        {
            if (!_enabled || !_captureHostSnapshots || !IsLocalHost())
            {
                return;
            }

            try
            {
                int sourceSlot = SafeInt(() => SaveAndLoad.SAVE_SLOT, -1);
                if (sourceSlot < 0)
                {
                    return;
                }

                SaveBundle bundle = ReadSaveBundle(sourceSlot);
                if (bundle.Files.Count == 0)
                {
                    RecordThrottled("phase15.save.capture_missing", "reason=" + Clean(reason) + " sourceSlot=" + sourceSlot);
                    return;
                }

                byte[] raw = SerializeBundle(bundle);
                string hash = Sha256Hex(raw);
                string signature = sourceSlot + "|" + hash;
                if (string.Equals(signature, _lastSnapshotSignature, StringComparison.Ordinal) && !string.Equals(reason, "save_postfix", StringComparison.Ordinal))
                {
                    return;
                }

                _lastSnapshotSignature = signature;
                byte[] compressed = Compress(raw);
                int chunks = Math.Max(1, (compressed.Length + ChunkBytes - 1) / ChunkBytes);
                string snapshotId = DiagnosticsPlugin.ClientId + "-" + (++_sequence).ToString(CultureInfo.InvariantCulture) + "-" + hash.Substring(0, 12);

                for (int i = 0; i < chunks; i++)
                {
                    int offset = i * ChunkBytes;
                    int count = Math.Min(ChunkBytes, compressed.Length - offset);
                    string data = Convert.ToBase64String(compressed, offset, count);
                    WorldTrace.Record(
                        "sync.save_chunk",
                        "snapshot=" + snapshotId
                        + " reason=" + Clean(reason)
                        + " sourceSlot=" + sourceSlot
                        + " targetSlot=" + _targetSaveSlot
                        + " chunk=" + i
                        + " chunks=" + chunks
                        + " rawBytes=" + raw.Length
                        + " compressedBytes=" + compressed.Length
                        + " files=" + bundle.Files.Count
                        + " hash=" + hash
                        + " data=" + data);
                }

                WorldTrace.Record(
                    "phase15.save.capture_sent",
                    "snapshot=" + snapshotId
                    + " reason=" + Clean(reason)
                    + " sourceSlot=" + sourceSlot
                    + " targetSlot=" + _targetSaveSlot
                    + " files=" + bundle.Files.Count
                    + " rawBytes=" + raw.Length
                    + " compressedBytes=" + compressed.Length
                    + " chunks=" + chunks
                    + " hash=" + hash);
            }
            catch (Exception ex)
            {
                WorldTrace.Record("phase15.save.capture_error", ex.GetType().Name + ":" + Clean(ex.Message));
            }
        }

        private static void TryApplyPendingSnapshot()
        {
            if (!_applyServerSnapshots || !IsRemoteClient())
            {
                return;
            }

            IncomingSaveSnapshot ready = null;
            lock (Sync)
            {
                foreach (IncomingSaveSnapshot candidate in Incoming.Values)
                {
                    if (candidate.IsComplete && !candidate.Applied && !candidate.Applying)
                    {
                        ready = candidate;
                        ready.Applying = true;
                        break;
                    }
                }
            }

            if (ready == null)
            {
                return;
            }

            try
            {
                byte[] compressed = ready.CombineChunks();
                byte[] raw = Decompress(compressed);
                string hash = Sha256Hex(raw);
                if (IsKnown(ready.Hash) && !string.Equals(hash, ready.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("hash mismatch " + hash + "!=" + ready.Hash);
                }

                SaveBundle bundle = DeserializeBundle(raw);
                int targetSlot = ready.TargetSlot >= 0 ? ready.TargetSlot : _targetSaveSlot;
                int files = WriteBundleToSlot(bundle, targetSlot);
                ready.Applied = true;
                _lastAppliedSnapshot = ready.SnapshotId;

                WorldTrace.Record(
                    "phase15.save.applied",
                    "snapshot=" + Clean(ready.SnapshotId)
                    + " source=" + Clean(ready.Source)
                    + " sourceSlot=" + ready.SourceSlot
                    + " targetSlot=" + targetSlot
                    + " files=" + files
                    + " hash=" + hash);
                WorldTrace.Record(
                    "sync.save_ack",
                    "snapshot=" + Clean(ready.SnapshotId)
                    + " status=applied"
                    + " targetSlot=" + targetSlot
                    + " files=" + files
                    + " hash=" + hash);
            }
            catch (Exception ex)
            {
                ready.Applied = true;
                WorldTrace.Record(
                    "phase15.save.apply_error",
                    "snapshot=" + Clean(ready.SnapshotId)
                    + " error=" + ex.GetType().Name + ":" + Clean(ex.Message));
                WorldTrace.Record(
                    "sync.save_ack",
                    "snapshot=" + Clean(ready.SnapshotId)
                    + " status=error"
                    + " error=" + Clean(ex.GetType().Name));
            }
        }

        private static SaveBundle ReadSaveBundle(int sourceSlot)
        {
            string savesDirectory = Path.Combine(Application.persistentDataPath, "saves");
            SaveBundle bundle = new SaveBundle(sourceSlot);
            foreach (string fileName in SaveSlotFileNames(sourceSlot))
            {
                string path = Path.Combine(savesDirectory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                bundle.Files.Add(new SaveBundleFile(fileName, File.ReadAllBytes(path)));
            }

            return bundle;
        }

        private static int WriteBundleToSlot(SaveBundle bundle, int targetSlot)
        {
            string savesDirectory = Path.Combine(Application.persistentDataPath, "saves");
            Directory.CreateDirectory(savesDirectory);

            foreach (string fileName in SaveSlotFileNames(targetSlot))
            {
                string stalePath = Path.Combine(savesDirectory, fileName);
                if (File.Exists(stalePath))
                {
                    File.Delete(stalePath);
                }
            }

            int written = 0;
            for (int i = 0; i < bundle.Files.Count; i++)
            {
                SaveBundleFile file = bundle.Files[i];
                string fileName = MapFileNameToSlot(file.FileName, targetSlot);
                string path = Path.Combine(savesDirectory, fileName);
                string temp = path + ".cotlonline.tmp";
                File.WriteAllBytes(temp, file.Data);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                written++;
            }

            return written;
        }

        private static byte[] SerializeBundle(SaveBundle bundle)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("COTLONLINE_SAVE_BUNDLE");
                writer.Write(SaveBundleVersion);
                writer.Write(bundle.SourceSlot);
                writer.Write(bundle.Files.Count);
                for (int i = 0; i < bundle.Files.Count; i++)
                {
                    SaveBundleFile file = bundle.Files[i];
                    writer.Write(file.FileName ?? "");
                    writer.Write(file.Data.Length);
                    writer.Write(file.Data);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static SaveBundle DeserializeBundle(byte[] raw)
        {
            using (MemoryStream stream = new MemoryStream(raw))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                string magic = reader.ReadString();
                if (!string.Equals(magic, "COTLONLINE_SAVE_BUNDLE", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("bad save bundle magic");
                }

                int version = reader.ReadInt32();
                if (version != SaveBundleVersion)
                {
                    throw new InvalidDataException("unsupported save bundle version " + version);
                }

                SaveBundle bundle = new SaveBundle(reader.ReadInt32());
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string fileName = Path.GetFileName(reader.ReadString() ?? "");
                    int length = reader.ReadInt32();
                    if (length < 0 || length > 20 * 1024 * 1024)
                    {
                        throw new InvalidDataException("bad save file length " + length);
                    }

                    bundle.Files.Add(new SaveBundleFile(fileName, reader.ReadBytes(length)));
                }

                return bundle;
            }
        }

        private static byte[] Compress(byte[] raw)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] compressed)
        {
            using (MemoryStream input = new MemoryStream(compressed))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        private static string[] SaveSlotFileNames(int saveSlot)
        {
            return new[]
            {
                "slot_" + saveSlot + ".mp",
                "meta_" + saveSlot + ".mp",
                "slot_" + saveSlot + ".json",
                "meta_" + saveSlot + ".json"
            };
        }

        private static string MapFileNameToSlot(string fileName, int targetSlot)
        {
            string safe = Path.GetFileName(fileName ?? "");
            string extension = Path.GetExtension(safe);
            if (safe.StartsWith("slot_", StringComparison.OrdinalIgnoreCase))
            {
                return "slot_" + targetSlot + extension;
            }

            if (safe.StartsWith("meta_", StringComparison.OrdinalIgnoreCase))
            {
                return "meta_" + targetSlot + extension;
            }

            return safe;
        }

        private static bool IsLocalHost()
        {
            return string.Equals(SelfRole(), "host-lamb", StringComparison.Ordinal);
        }

        private static bool IsRemoteClient()
        {
            string role = SelfRole();
            return string.Equals(role, "remote-p2", StringComparison.Ordinal)
                || string.Equals(role, "pending", StringComparison.Ordinal);
        }

        private static string SelfRole()
        {
            BridgeRosterSnapshot roster = BridgeRosterState.Snapshot();
            if (roster == null || roster.Clients == null)
            {
                return "unknown";
            }

            for (int i = 0; i < roster.Clients.Length; i++)
            {
                BridgeRosterClient client = roster.Clients[i];
                if (client != null && string.Equals(client.ClientId, DiagnosticsPlugin.ClientId, StringComparison.Ordinal))
                {
                    return client.Role ?? "unknown";
                }
            }

            return "unknown";
        }

        private static void UpdateOverlay()
        {
            string role = SelfRole();
            int pending = 0;
            int complete = 0;
            lock (Sync)
            {
                foreach (IncomingSaveSnapshot snapshot in Incoming.Values)
                {
                    if (snapshot.IsComplete && !snapshot.Applied)
                    {
                        complete++;
                    }
                    else if (!snapshot.Applied)
                    {
                        pending++;
                    }
                }
            }

            if (IsLocalHost())
            {
                _overlayLine = "save authority: host slot=" + SafeInt(() => SaveAndLoad.SAVE_SLOT, -1)
                    + " target=" + _targetSaveSlot
                    + " last=" + Clean(_lastSnapshotSignature);
                return;
            }

            _overlayLine = "save authority: role=" + role
                + " apply=" + _applyServerSnapshots
                + " pending=" + pending
                + " ready=" + complete
                + " lastApplied=" + Clean(_lastAppliedSnapshot);
        }

        private static void RecordThrottled(string category, string message)
        {
            if (Time.unscaledTime < _nextRecordAt)
            {
                return;
            }

            _nextRecordAt = Time.unscaledTime + 1.5f;
            WorldTrace.Record(category, message);
        }

        private static string ReadToken(string message, string key)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string pattern = key + "=";
            int start = message.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += pattern.Length;
            int end = message.IndexOf(' ', start);
            return end < 0 ? message.Substring(start) : message.Substring(start, end - start);
        }

        private static int ReadInt(string message, string key, int fallback)
        {
            int parsed;
            return int.TryParse(ReadToken(message, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static int SafeInt(Func<int> read, int fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private static bool IsKnown(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "none";
            }

            return value.Replace(" ", "_").Replace("\t", "_").Replace("\r", "_").Replace("\n", "_").Replace("|", "/").Replace(";", ",");
        }

        private sealed class SaveBundle
        {
            public SaveBundle(int sourceSlot)
            {
                SourceSlot = sourceSlot;
            }

            public int SourceSlot { get; private set; }
            public List<SaveBundleFile> Files { get; } = new List<SaveBundleFile>();
        }

        private sealed class SaveBundleFile
        {
            public SaveBundleFile(string fileName, byte[] data)
            {
                FileName = Path.GetFileName(fileName ?? "");
                Data = data ?? new byte[0];
            }

            public string FileName { get; private set; }
            public byte[] Data { get; private set; }
        }

        private sealed class IncomingSaveSnapshot
        {
            public IncomingSaveSnapshot(string snapshotId, int chunks)
            {
                SnapshotId = snapshotId;
                Chunks = new string[chunks];
                ReceivedUtc = DateTimeOffset.UtcNow;
            }

            public string SnapshotId { get; private set; }
            public string Source { get; set; }
            public string ServerTime { get; set; }
            public int SourceSlot { get; set; }
            public int TargetSlot { get; set; }
            public int RawBytes { get; set; }
            public int CompressedBytes { get; set; }
            public string Hash { get; set; }
            public string[] Chunks { get; private set; }
            public DateTimeOffset ReceivedUtc { get; set; }
            public bool Applying { get; set; }
            public bool Applied { get; set; }

            public bool IsComplete
            {
                get
                {
                    for (int i = 0; i < Chunks.Length; i++)
                    {
                        if (string.IsNullOrEmpty(Chunks[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            public byte[] CombineChunks()
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    for (int i = 0; i < Chunks.Length; i++)
                    {
                        byte[] bytes = Convert.FromBase64String(Chunks[i]);
                        stream.Write(bytes, 0, bytes.Length);
                    }

                    return stream.ToArray();
                }
            }
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save), new Type[0])]
    internal static class Phase15SaveBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !BridgeSaveAuthority.ShouldBlockClientSave("Save()");
        }

        private static void Postfix()
        {
            BridgeSaveAuthority.CaptureAndSendHostSave("save_postfix");
        }
    }

    [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save), new[] { typeof(string) })]
    internal static class Phase15SaveFilenameBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(string filename)
        {
            return !BridgeSaveAuthority.ShouldBlockClientSave("Save(filename=" + filename + ")");
        }

        private static void Postfix()
        {
            BridgeSaveAuthority.CaptureAndSendHostSave("save_filename_postfix");
        }
    }
}
