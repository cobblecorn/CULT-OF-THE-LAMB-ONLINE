using System;
using System.Collections.Generic;

namespace COTLOnline.Diagnostics
{
    internal static class BridgeRemoteInputState
    {
        private static readonly object Sync = new object();
        private static BridgeRemoteInputSnapshot _snapshot = BridgeRemoteInputSnapshot.Empty;

        public static void Reset()
        {
            lock (Sync)
            {
                _snapshot = BridgeRemoteInputSnapshot.Empty;
            }
        }

        public static void UpdateFromPacket(string source, string message)
        {
            BridgeRemoteInput[] inputs = ParseInputs(message);
            lock (Sync)
            {
                _snapshot = new BridgeRemoteInputSnapshot(
                    DateTimeOffset.UtcNow,
                    source ?? "unknown",
                    ReadToken(message, "serverTime") ?? "unknown",
                    ReadToken(message, "target") ?? "unknown",
                    ReadToken(message, "worldId") ?? "none",
                    ReadToken(message, "remoteCount") ?? inputs.Length.ToString(),
                    inputs);
            }
        }

        public static BridgeRemoteInputSnapshot Snapshot()
        {
            lock (Sync)
            {
                return _snapshot.Copy();
            }
        }

        private static BridgeRemoteInput[] ParseInputs(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Array.Empty<BridgeRemoteInput>();
            }

            int inputsIndex = message.IndexOf("inputs=", StringComparison.Ordinal);
            if (inputsIndex < 0)
            {
                return Array.Empty<BridgeRemoteInput>();
            }

            string payload = message.Substring(inputsIndex + "inputs=".Length).Trim();
            if (payload.Length == 0 || payload == "none")
            {
                return Array.Empty<BridgeRemoteInput>();
            }

            string[] entries = payload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<BridgeRemoteInput> inputs = new List<BridgeRemoteInput>(entries.Length);
            foreach (string entry in entries)
            {
                BridgeRemoteInput input = ParseInput(entry.Trim());
                if (!string.IsNullOrEmpty(input.ClientId))
                {
                    inputs.Add(input);
                }
            }

            return inputs.ToArray();
        }

        private static BridgeRemoteInput ParseInput(string entry)
        {
            string[] parts = entry.Split(new[] { '|' }, StringSplitOptions.None);
            BridgeRemoteInput input = new BridgeRemoteInput
            {
                ClientId = parts.Length > 0 ? parts[0] : "unknown",
                SessionId = "unknown",
                PlayerId = "unknown",
                Role = "unknown",
                HostSlot = "unknown",
                Sequence = "unknown",
                AxisX = "0",
                AxisY = "0",
                AttackDown = "False",
                AttackHeld = "False",
                AttackUp = "False",
                DodgeDown = "False",
                DodgeHeld = "False",
                CurseDown = "False",
                CurseHeld = "False",
                CurseUp = "False",
                HeavyDown = "False",
                HeavyHeld = "False",
                FacingAngle = "unknown",
                LookAngle = "unknown",
                AimAngle = "unknown",
                FaithAmmo = "unknown",
                FaithTotal = "unknown",
                FaithCost = "unknown",
                Position = "unknown",
                State = "unknown",
                Scene = "unknown",
                Location = "unknown",
                Room = "unknown",
                AgeMs = "unknown"
            };

            for (int i = 1; i < parts.Length; i++)
            {
                int equals = parts[i].IndexOf('=');
                if (equals <= 0 || equals >= parts[i].Length - 1)
                {
                    continue;
                }

                string key = parts[i].Substring(0, equals);
                string value = parts[i].Substring(equals + 1);
                switch (key)
                {
                    case "session":
                        input.SessionId = value;
                        break;
                    case "playerID":
                        input.PlayerId = value;
                        break;
                    case "role":
                        input.Role = value;
                        break;
                    case "hostSlot":
                        input.HostSlot = value;
                        break;
                    case "seq":
                        input.Sequence = value;
                        break;
                    case "ax":
                        input.AxisX = value;
                        break;
                    case "ay":
                        input.AxisY = value;
                        break;
                    case "attackDown":
                        input.AttackDown = value;
                        break;
                    case "attackHeld":
                        input.AttackHeld = value;
                        break;
                    case "attackUp":
                        input.AttackUp = value;
                        break;
                    case "dodgeDown":
                        input.DodgeDown = value;
                        break;
                    case "dodgeHeld":
                        input.DodgeHeld = value;
                        break;
                    case "curseDown":
                        input.CurseDown = value;
                        break;
                    case "curseHeld":
                        input.CurseHeld = value;
                        break;
                    case "curseUp":
                        input.CurseUp = value;
                        break;
                    case "heavyDown":
                        input.HeavyDown = value;
                        break;
                    case "heavyHeld":
                        input.HeavyHeld = value;
                        break;
                    case "facingAngle":
                        input.FacingAngle = value;
                        break;
                    case "lookAngle":
                        input.LookAngle = value;
                        break;
                    case "aimAngle":
                        input.AimAngle = value;
                        break;
                    case "faithAmmo":
                        input.FaithAmmo = value;
                        break;
                    case "faithTotal":
                        input.FaithTotal = value;
                        break;
                    case "faithCost":
                        input.FaithCost = value;
                        break;
                    case "pos":
                        input.Position = value;
                        break;
                    case "state":
                        input.State = value;
                        break;
                    case "scene":
                        input.Scene = value;
                        break;
                    case "location":
                        input.Location = value;
                        break;
                    case "room":
                        input.Room = value;
                        break;
                    case "ageMs":
                        input.AgeMs = value;
                        break;
                }
            }

            return input;
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
            if (end < 0)
            {
                end = message.Length;
            }

            return message.Substring(start, end - start);
        }
    }

    internal sealed class BridgeRemoteInputSnapshot
    {
        public static readonly BridgeRemoteInputSnapshot Empty = new BridgeRemoteInputSnapshot(
            DateTimeOffset.MinValue,
            "none",
            "unknown",
            "unknown",
            "none",
            "0",
            Array.Empty<BridgeRemoteInput>());

        public BridgeRemoteInputSnapshot(DateTimeOffset receivedUtc, string source, string serverTime, string targetClientId, string worldId, string remoteCount, BridgeRemoteInput[] inputs)
        {
            ReceivedUtc = receivedUtc;
            Source = source;
            ServerTime = serverTime;
            TargetClientId = targetClientId;
            WorldId = worldId;
            RemoteCount = remoteCount;
            Inputs = inputs ?? Array.Empty<BridgeRemoteInput>();
        }

        public DateTimeOffset ReceivedUtc { get; }
        public string Source { get; }
        public string ServerTime { get; }
        public string TargetClientId { get; }
        public string WorldId { get; }
        public string RemoteCount { get; }
        public BridgeRemoteInput[] Inputs { get; }
        public bool HasPacket => ReceivedUtc != DateTimeOffset.MinValue;

        public BridgeRemoteInputSnapshot Copy()
        {
            BridgeRemoteInput[] inputs = new BridgeRemoteInput[Inputs.Length];
            for (int i = 0; i < Inputs.Length; i++)
            {
                inputs[i] = Inputs[i].Copy();
            }

            return new BridgeRemoteInputSnapshot(ReceivedUtc, Source, ServerTime, TargetClientId, WorldId, RemoteCount, inputs);
        }
    }

    internal sealed class BridgeRemoteInput
    {
        public string ClientId { get; set; }
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string Role { get; set; }
        public string HostSlot { get; set; }
        public string Sequence { get; set; }
        public string AxisX { get; set; }
        public string AxisY { get; set; }
        public string AttackDown { get; set; }
        public string AttackHeld { get; set; }
        public string AttackUp { get; set; }
        public string DodgeDown { get; set; }
        public string DodgeHeld { get; set; }
        public string CurseDown { get; set; }
        public string CurseHeld { get; set; }
        public string CurseUp { get; set; }
        public string HeavyDown { get; set; }
        public string HeavyHeld { get; set; }
        public string FacingAngle { get; set; }
        public string LookAngle { get; set; }
        public string AimAngle { get; set; }
        public string FaithAmmo { get; set; }
        public string FaithTotal { get; set; }
        public string FaithCost { get; set; }
        public string Position { get; set; }
        public string State { get; set; }
        public string Scene { get; set; }
        public string Location { get; set; }
        public string Room { get; set; }
        public string AgeMs { get; set; }

        public BridgeRemoteInput Copy()
        {
            return new BridgeRemoteInput
            {
                ClientId = ClientId,
                SessionId = SessionId,
                PlayerId = PlayerId,
                Role = Role,
                HostSlot = HostSlot,
                Sequence = Sequence,
                AxisX = AxisX,
                AxisY = AxisY,
                AttackDown = AttackDown,
                AttackHeld = AttackHeld,
                AttackUp = AttackUp,
                DodgeDown = DodgeDown,
                DodgeHeld = DodgeHeld,
                CurseDown = CurseDown,
                CurseHeld = CurseHeld,
                CurseUp = CurseUp,
                HeavyDown = HeavyDown,
                HeavyHeld = HeavyHeld,
                FacingAngle = FacingAngle,
                LookAngle = LookAngle,
                AimAngle = AimAngle,
                FaithAmmo = FaithAmmo,
                FaithTotal = FaithTotal,
                FaithCost = FaithCost,
                Position = Position,
                State = State,
                Scene = Scene,
                Location = Location,
                Room = Room,
                AgeMs = AgeMs
            };
        }
    }
}
