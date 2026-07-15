using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Text;

namespace COTLOnline.ServerLedger;

internal static partial class Program
{
    private const string DefaultTraceDirectory = @"D:\SteamLibrary\steamapps\common\Cult of the Lamb\BepInEx\worldtrace";
    private const string DefaultWorldsDirectoryName = "server_worlds";

    public static async Task<int> Main(string[] args)
    {
        Arguments options = Arguments.Parse(args);
        if (options.ListenUdp)
        {
            using CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            await ListenUdp(options, cts.Token);
            return 0;
        }

        string tracePath = ResolveTracePath(options);

        if (options.Watch && string.IsNullOrWhiteSpace(options.TracePath))
        {
            string directory = !string.IsNullOrWhiteSpace(options.TraceDirectory)
                ? options.TraceDirectory
                : DefaultTraceDirectory;

            await WatchNewestTrace(directory, options, CancellationToken.None);
            return 0;
        }

        if (!File.Exists(tracePath))
        {
            Console.Error.WriteLine("Trace file not found: " + tracePath);
            return 1;
        }

        LedgerState ledger = new(Path.GetFullPath(tracePath));
        await ReplayFile(tracePath, ledger, CancellationToken.None);
        PrintSummary(ledger);
        WriteJsonIfRequested(options, ledger);

        if (options.Watch)
        {
            Console.WriteLine();
            Console.WriteLine("Watching for new trace events. Press Ctrl+C to stop.");
            using CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            await WatchFile(tracePath, ledger, cts.Token);
        }

        return 0;
    }

    private static string ResolveTracePath(Arguments options)
    {
        if (!string.IsNullOrWhiteSpace(options.TracePath))
        {
            return options.TracePath;
        }

        string directory = !string.IsNullOrWhiteSpace(options.TraceDirectory)
            ? options.TraceDirectory
            : DefaultTraceDirectory;

        DirectoryInfo info = new(directory);
        FileInfo? latest = info.Exists
            ? info.GetFiles("cotlonline-trace-*.jsonl").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;

        return latest?.FullName ?? Path.Combine(directory, "cotlonline-trace-missing.jsonl");
    }

    private static async Task WatchNewestTrace(string directory, Arguments options, CancellationToken cancellationToken)
    {
        Console.WriteLine("Watching newest trace in: " + directory);
        Console.WriteLine("Start/restart the game any time; this will switch to the newest cotlonline trace file.");

        string? activeTracePath = null;
        LedgerState? ledger = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? newestTracePath = FindNewestTrace(directory);
            if (newestTracePath == null)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!string.Equals(activeTracePath, newestTracePath, StringComparison.OrdinalIgnoreCase))
            {
                activeTracePath = newestTracePath;
                ledger = new LedgerState(activeTracePath);
                await ReplayFile(activeTracePath, ledger, cancellationToken).ConfigureAwait(false);
                Console.WriteLine();
                Console.WriteLine("Following trace: " + activeTracePath);
                Console.WriteLine("Replayed " + ledger.TotalEvents + " existing events. Waiting for new events...");
                WriteJsonIfRequested(options, ledger);
            }

            if (ledger != null)
            {
                await ReadNewLines(activeTracePath!, ledger, cancellationToken).ConfigureAwait(false);
                WriteJsonIfRequested(options, ledger);
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? FindNewestTrace(string directory)
    {
        DirectoryInfo info = new(directory);
        return info.Exists
            ? info.GetFiles("cotlonline-trace-*.jsonl").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.FullName
            : null;
    }

    private static async Task ReplayFile(string tracePath, LedgerState ledger, CancellationToken cancellationToken)
    {
        using FileStream stream = new(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                ApplyLine(ledger, line, printLive: false);
            }
        }

        ledger.BytesRead = stream.Position;
    }

    private static async Task WatchFile(string tracePath, LedgerState ledger, CancellationToken cancellationToken)
    {
        Console.WriteLine("Following trace: " + tracePath);
        using FileStream stream = new(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = ledger.BytesRead;
        using StreamReader reader = new(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            ledger.BytesRead = stream.Position;
            if (!string.IsNullOrWhiteSpace(line))
            {
                ApplyLine(ledger, line, printLive: true);
            }
        }
    }

    private static async Task ListenUdp(Arguments options, CancellationToken cancellationToken)
    {
        int port = options.UdpPort ?? 37622;
        using UdpClient udpClient = new(port);
        LedgerState ledger = new("udp://0.0.0.0:" + port)
        {
            WorldsDirectory = ResolveWorldsDirectory(options),
            ServerSaveSlot = options.ServerSaveSlot ?? 4,
            PreferredHostClientId = CleanClientId(options.HostClientId)
        };
        Directory.CreateDirectory(ledger.WorldsDirectory);

        Console.WriteLine("COTL Online Server Ledger UDP");
        Console.WriteLine("=============================");
        Console.WriteLine("Listening on 0.0.0.0:" + port + " (localhost and LAN interfaces)");
        Console.WriteLine("World state directory: " + ledger.WorldsDirectory);
        if (!string.IsNullOrWhiteSpace(ledger.PreferredHostClientId))
        {
            Console.WriteLine("Preferred host client: " + ledger.PreferredHostClientId);
        }

        Console.WriteLine("Start/restart the game with COTLOnline.Diagnostics 0.5.33+ loaded for host enemy authority diagnostics, guarded spell-cast relay, encounter diagnostics, loadout de-dupe, frame-state relay, seed-wait, pinned-host remote P2, remote-away, and cult-state diagnostics.");
        Console.WriteLine("Press Ctrl+C to stop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            string line = Encoding.UTF8.GetString(result.Buffer);
            TraceEvent? traceEvent = ParseTraceEvent(line);
            if (traceEvent == null)
            {
                ledger.MalformedLines++;
                continue;
            }

            TraceEvent receivedEvent = traceEvent with { Timestamp = DateTimeOffset.UtcNow };
            ApplyTraceEvent(ledger, receivedEvent, printLive: true);
            SendServerRepliesIfNeeded(udpClient, ledger, receivedEvent, result.RemoteEndPoint);
            WriteJsonIfRequested(options, ledger);
        }

        Console.WriteLine();
        PrintSummary(ledger);
    }

    private static async Task ReadNewLines(string tracePath, LedgerState ledger, CancellationToken cancellationToken)
    {
        using FileStream stream = new(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = Math.Min(ledger.BytesRead, stream.Length);
        using StreamReader reader = new(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                ledger.BytesRead = stream.Position;
                return;
            }

            ledger.BytesRead = stream.Position;
            if (!string.IsNullOrWhiteSpace(line))
            {
                ApplyLine(ledger, line, printLive: true);
            }
        }
    }

    private static LedgerEvent? ApplyLine(LedgerState ledger, string line, bool printLive)
    {
        TraceEvent? traceEvent = ParseTraceEvent(line);
        if (traceEvent == null)
        {
            ledger.MalformedLines++;
            return null;
        }

        return ApplyTraceEvent(ledger, traceEvent, printLive);
    }

    private static LedgerEvent? ApplyTraceEvent(LedgerState ledger, TraceEvent traceEvent, bool printLive)
    {
        LedgerEvent? ledgerEvent = LedgerReducer.Apply(ledger, traceEvent);
        if (printLive && ledgerEvent != null)
        {
            Console.WriteLine(ledgerEvent.ToDisplayString());
        }

        return ledgerEvent;
    }

    private static TraceEvent? ParseTraceEvent(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<TraceEvent>(line);
        }
        catch
        {
            return null;
        }
    }

    private static void SendServerRepliesIfNeeded(UdpClient udpClient, LedgerState ledger, TraceEvent traceEvent, System.Net.IPEndPoint remoteEndPoint)
    {
        string? clientId = ReadMessageToken(traceEvent.Message, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            return;
        }

        client.RemoteEndPoint = remoteEndPoint.ToString();
        if (client.LastRosterSent == null || traceEvent.Timestamp - client.LastRosterSent >= TimeSpan.FromSeconds(2))
        {
            client.LastRosterSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.roster", BuildRosterMessage(ledger, traceEvent.Timestamp));
        }

        if (ShouldSendRemotePlayers(traceEvent.Category)
            && (client.LastRemotePlayersSent == null || traceEvent.Timestamp - client.LastRemotePlayersSent >= TimeSpan.FromSeconds(0.05)))
        {
            client.LastRemotePlayersSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.remote_players", BuildRemotePlayersMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendRemoteInputs(traceEvent.Category)
            && (client.LastRemoteInputsSent == null || traceEvent.Timestamp - client.LastRemoteInputsSent >= TimeSpan.FromSeconds(0.05)))
        {
            client.LastRemoteInputsSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.remote_inputs", BuildRemoteInputsMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendLoadouts(traceEvent.Category)
            && (client.LastLoadoutsSent == null || traceEvent.Timestamp - client.LastLoadoutsSent >= TimeSpan.FromSeconds(0.25)))
        {
            client.LastLoadoutsSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.loadouts", BuildLoadoutsMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendRunAuthority(traceEvent.Category)
            && (client.LastRunAuthoritySent == null || traceEvent.Timestamp - client.LastRunAuthoritySent >= TimeSpan.FromSeconds(0.25)))
        {
            client.LastRunAuthoritySent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.run_authority", BuildRunAuthorityMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendRewardClaims(traceEvent.Category)
            && (client.LastRewardClaimsSent == null || traceEvent.Timestamp - client.LastRewardClaimsSent >= TimeSpan.FromSeconds(0.25)))
        {
            client.LastRewardClaimsSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.reward_claims", BuildRewardClaimsMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendSpellCasts(traceEvent.Category)
            && (client.LastSpellCastsSent == null || traceEvent.Timestamp - client.LastSpellCastsSent >= TimeSpan.FromSeconds(0.05)))
        {
            client.LastSpellCastsSent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.spell_casts", BuildSpellCastsMessage(ledger, client, traceEvent.Timestamp));
        }

        if (ShouldSendEnemyAuthority(traceEvent.Category)
            && (client.LastEnemyAuthoritySent == null || traceEvent.Timestamp - client.LastEnemyAuthoritySent >= TimeSpan.FromSeconds(0.25)))
        {
            client.LastEnemyAuthoritySent = traceEvent.Timestamp;
            SendServerPacket(udpClient, remoteEndPoint, "server.enemy_authority", BuildEnemyAuthorityMessage(ledger, client, traceEvent.Timestamp));
        }
    }

    private static void SendServerPacket(UdpClient udpClient, System.Net.IPEndPoint remoteEndPoint, string category, string message)
    {
        string reply = BuildServerPacket(category, message);
        byte[] bytes = Encoding.UTF8.GetBytes(reply);
        udpClient.Send(bytes, bytes.Length, remoteEndPoint);
    }

    private static bool ShouldSendRemotePlayers(string category)
    {
        return string.Equals(category, "sync.player_motion", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_state", StringComparison.Ordinal);
    }

    private static bool ShouldSendRemoteInputs(string category)
    {
        return string.Equals(category, "sync.player_input", StringComparison.Ordinal);
    }

    private static bool ShouldSendLoadouts(string category)
    {
        return string.Equals(category, "sync.player_state", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_motion", StringComparison.Ordinal)
            || category.StartsWith("phase2.reward.", StringComparison.Ordinal)
            || category.StartsWith("phase3.equipment.", StringComparison.Ordinal);
    }

    private static bool ShouldSendRunAuthority(string category)
    {
        return string.Equals(category, "sync.world_identity", StringComparison.Ordinal)
            || category.StartsWith("phase3.run.", StringComparison.Ordinal)
            || category.StartsWith("phase3.biome.", StringComparison.Ordinal)
            || category.StartsWith("phase2.reward.", StringComparison.Ordinal);
    }

    private static bool ShouldSendRewardClaims(string category)
    {
        return string.Equals(category, "sync.player_motion", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_state", StringComparison.Ordinal)
            || category.StartsWith("phase2.reward.", StringComparison.Ordinal);
    }

    private static bool ShouldSendSpellCasts(string category)
    {
        return string.Equals(category, "sync.spell_cast", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_input", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_motion", StringComparison.Ordinal)
            || string.Equals(category, "live.heartbeat", StringComparison.Ordinal);
    }

    private static bool ShouldSendEnemyAuthority(string category)
    {
        return string.Equals(category, "sync.combat_roster", StringComparison.Ordinal)
            || string.Equals(category, "sync.combat_spawn", StringComparison.Ordinal)
            || string.Equals(category, "sync.encounter_island", StringComparison.Ordinal)
            || string.Equals(category, "sync.encounter_chance", StringComparison.Ordinal)
            || string.Equals(category, "sync.player_motion", StringComparison.Ordinal)
            || string.Equals(category, "live.heartbeat", StringComparison.Ordinal);
    }

    private static string BuildRosterMessage(LedgerState ledger, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder sb = new();
        sb.Append("serverTime=").Append(now.ToUnixTimeMilliseconds());
        sb.Append(" worldId=").Append(ledger.ServerWorld?.WorldId ?? "none");
        sb.Append(" worldHost=").Append(ledger.ServerWorld?.HostClientId ?? "unknown");
        sb.Append(" baselineHash=").Append(ledger.ServerWorld?.BaselineHash ?? "unknown");
        sb.Append(" cultHash=").Append(ledger.ServerWorld?.BaselineCultSnapshotHash ?? "unknown");
        sb.Append(" cultFollowers=").Append(ledger.ServerWorld?.BaselineCultFollowers?.ToString() ?? "unknown");
        sb.Append(" serverSaveSlot=").Append(ledger.ServerSaveSlot?.ToString() ?? "unknown");
        sb.Append(" runSeed=").Append(ledger.DungeonSeed?.ToString() ?? "unknown");
        sb.Append(" clients=");

        bool first = true;
        foreach (ClientLedger client in ledger.Clients.Values.OrderBy(client => client.ClientId))
        {
            if (client.LastSeen != null && now - client.LastSeen > TimeSpan.FromSeconds(20))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(",");
            }

            first = false;
            long ageMs = client.LastSeen != null ? (long)Math.Max(0, (now - client.LastSeen.Value).TotalMilliseconds) : -1;
            sb.Append(client.ClientId)
                .Append("|session=").Append(client.SessionId ?? "unknown")
                .Append("|role=").Append(client.ServerRole ?? "unknown")
                .Append("|hostSlot=").Append(client.HostPlayerSlot?.ToString() ?? "unknown")
                .Append("|saveSlot=").Append(client.SaveSlot?.ToString() ?? "unknown")
                .Append("|saveSlotOk=").Append(client.SaveSlotOk?.ToString() ?? "unknown")
                .Append("|worldHash=").Append(client.WorldHash ?? "unknown")
                .Append("|worldMatch=").Append(client.WorldMatch?.ToString() ?? "unknown")
                .Append("|plugin=").Append(client.PluginVersion ?? "unknown")
                .Append("|scene=").Append(client.Scene ?? "unknown")
                .Append("|location=").Append(client.Location ?? "unknown")
                .Append("|players=").Append(client.PlayersCount?.ToString() ?? "unknown")
                .Append("|p2Wanted=").Append(client.HostP2Wanted?.ToString() ?? "unknown")
                .Append("|p2Active=").Append(client.HostP2Active?.ToString() ?? "unknown")
                .Append("|p2Hold=").Append(client.HostP2Hold?.ToString() ?? "unknown")
                .Append("|p2NoController=").Append(client.HostP2NoController?.ToString() ?? "unknown")
                .Append("|cultHash=").Append(client.CultSnapshotHash ?? "unknown")
                .Append("|cultMatch=").Append(client.CultSnapshotMatch?.ToString() ?? "unknown")
                .Append("|cultFollowers=").Append(client.CultFollowersCount?.ToString() ?? "unknown")
                .Append("|ageMs=").Append(ageMs);
        }

        return sb.ToString();
    }

    private static string BuildRemotePlayersMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder players = new();
        int count = 0;
        foreach (PlayerMotionLedger motion in ledger.PlayerMotion.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            if (string.Equals(motion.ClientId, recipient.ClientId, StringComparison.Ordinal)
                || motion.LastSeen == null
                || now - motion.LastSeen > TimeSpan.FromSeconds(5))
            {
                continue;
            }

            if (count > 0)
            {
                players.Append(";");
            }

            ledger.Clients.TryGetValue(motion.ClientId, out ClientLedger? motionClient);
            long ageMs = (long)Math.Max(0, (now - motion.LastSeen.Value).TotalMilliseconds);
            players.Append(PacketValue(motion.ClientId))
                .Append("|session=").Append(PacketValue(motion.SessionId))
                .Append("|playerID=").Append(motion.PlayerId)
                .Append("|role=").Append(PacketValue(motionClient?.ServerRole))
                .Append("|hostSlot=").Append(motionClient?.HostPlayerSlot?.ToString() ?? "unknown")
                .Append("|seq=").Append(motion.Sequence?.ToString() ?? "unknown")
                .Append("|name=").Append(PacketValue(motion.Name))
                .Append("|pos=").Append(PacketValue(motion.Position))
                .Append("|state=").Append(PacketValue(motion.State))
                .Append("|hp=").Append(PacketValue(motion.HitPoints))
                .Append("|scene=").Append(PacketValue(motion.Scene))
                .Append("|location=").Append(PacketValue(motion.Location))
                .Append("|room=").Append(PacketValue(motion.Room))
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " remoteCount=" + count
            + " players=" + (count == 0 ? "none" : players.ToString());
    }

    private static string BuildRemoteInputsMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder inputs = new();
        int count = 0;
        foreach (PlayerInputLedger input in ledger.PlayerInputs.Values.OrderBy(input => input.ClientId).ThenBy(input => input.PlayerId))
        {
            if (string.Equals(input.ClientId, recipient.ClientId, StringComparison.Ordinal)
                || input.LastSeen == null
                || now - input.LastSeen > TimeSpan.FromSeconds(2))
            {
                continue;
            }

            if (count > 0)
            {
                inputs.Append(";");
            }

            ledger.Clients.TryGetValue(input.ClientId, out ClientLedger? inputClient);
            long ageMs = (long)Math.Max(0, (now - input.LastSeen.Value).TotalMilliseconds);
            inputs.Append(PacketValue(input.ClientId))
                .Append("|session=").Append(PacketValue(input.SessionId))
                .Append("|playerID=").Append(input.PlayerId)
                .Append("|role=").Append(PacketValue(inputClient?.ServerRole))
                .Append("|hostSlot=").Append(inputClient?.HostPlayerSlot?.ToString() ?? "unknown")
                .Append("|seq=").Append(input.Sequence?.ToString() ?? "unknown")
                .Append("|ax=").Append(PacketValue(input.AxisX))
                .Append("|ay=").Append(PacketValue(input.AxisY))
                .Append("|attackDown=").Append(input.AttackDown?.ToString() ?? "False")
                .Append("|attackHeld=").Append(input.AttackHeld?.ToString() ?? "False")
                .Append("|attackUp=").Append(input.AttackUp?.ToString() ?? "False")
                .Append("|dodgeDown=").Append(input.DodgeDown?.ToString() ?? "False")
                .Append("|dodgeHeld=").Append(input.DodgeHeld?.ToString() ?? "False")
                .Append("|curseDown=").Append(input.CurseDown?.ToString() ?? "False")
                .Append("|curseHeld=").Append(input.CurseHeld?.ToString() ?? "False")
                .Append("|curseUp=").Append(input.CurseUp?.ToString() ?? "False")
                .Append("|heavyDown=").Append(input.HeavyDown?.ToString() ?? "False")
                .Append("|heavyHeld=").Append(input.HeavyHeld?.ToString() ?? "False")
                .Append("|facingAngle=").Append(PacketValue(input.FacingAngle))
                .Append("|lookAngle=").Append(PacketValue(input.LookAngle))
                .Append("|aimAngle=").Append(PacketValue(input.AimAngle))
                .Append("|faithAmmo=").Append(PacketValue(input.FaithAmmo))
                .Append("|faithTotal=").Append(PacketValue(input.FaithTotal))
                .Append("|faithCost=").Append(PacketValue(input.FaithCost))
                .Append("|pos=").Append(PacketValue(input.Position))
                .Append("|state=").Append(PacketValue(input.State))
                .Append("|scene=").Append(PacketValue(input.Scene))
                .Append("|location=").Append(PacketValue(input.Location))
                .Append("|room=").Append(PacketValue(input.Room))
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " remoteCount=" + count
            + " inputs=" + (count == 0 ? "none" : inputs.ToString());
    }

    private static string BuildLoadoutsMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder loadouts = new();
        int count = 0;
        foreach (AuthoritativePlayerLedger loadout in ledger.AuthoritativePlayers.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            if (string.Equals(loadout.ClientId, recipient.ClientId, StringComparison.Ordinal)
                || (IsEmptyPacketEquipment(loadout.Weapon) && IsEmptyPacketEquipment(loadout.Curse)))
            {
                continue;
            }

            ledger.Clients.TryGetValue(loadout.ClientId, out ClientLedger? sourceClient);
            if (sourceClient?.LastSeen != null && now - sourceClient.LastSeen > TimeSpan.FromSeconds(20))
            {
                continue;
            }

            if (!ShouldRelayLoadoutForRole(sourceClient?.ServerRole, loadout.PlayerId))
            {
                continue;
            }

            if (count > 0)
            {
                loadouts.Append(";");
            }

            long ageMs = loadout.LastUpdated != null ? (long)Math.Max(0, (now - loadout.LastUpdated.Value).TotalMilliseconds) : -1;
            loadouts.Append(PacketValue(loadout.ClientId))
                .Append("|session=").Append(PacketValue(sourceClient?.SessionId))
                .Append("|playerID=").Append(loadout.PlayerId)
                .Append("|role=").Append(PacketValue(sourceClient?.ServerRole))
                .Append("|hostSlot=").Append(sourceClient?.HostPlayerSlot?.ToString() ?? "unknown")
                .Append("|weapon=").Append(PacketValue(loadout.Weapon ?? "unknown"))
                .Append("|curse=").Append(PacketValue(loadout.Curse ?? "unknown"))
                .Append("|decision=").Append(PacketValue(loadout.LastDecision ?? "unknown"))
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " loadoutCount=" + count
            + " loadouts=" + (count == 0 ? "none" : loadouts.ToString());
    }

    private static bool IsEmptyPacketEquipment(string? value)
    {
        return string.IsNullOrEmpty(value)
            || string.Equals(value, "None@0", StringComparison.Ordinal)
            || string.Equals(value, "None", StringComparison.Ordinal)
            || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRelayLoadoutForRole(string? role, int playerId)
    {
        if (string.Equals(role, "host-lamb", StringComparison.Ordinal)
            || string.Equals(role, "remote-p2", StringComparison.Ordinal))
        {
            return playerId == 0;
        }

        return true;
    }

    private static string BuildRunAuthorityMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        string hostClientId = ledger.ServerWorld?.HostClientId
            ?? ledger.Clients.Values.FirstOrDefault(client => string.Equals(client.ServerRole, "host-lamb", StringComparison.Ordinal))?.ClientId
            ?? "unknown";

        StringBuilder rewards = new();
        int count = 0;
        for (int i = 0; i < ledger.GeneratedRewards.Count; i++)
        {
            RewardLedger reward = ledger.GeneratedRewards[i];
            if ((ledger.Run.HasValue && (!reward.Run.HasValue || reward.Run.Value != ledger.Run.Value))
                || !string.Equals(reward.SourceClientId, hostClientId, StringComparison.Ordinal)
                || IsEmptyPacketEquipment(reward.Equipment)
                || string.Equals(reward.Type, "Relic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (count > 0)
            {
                rewards.Append(";");
            }

            long ageMs = (long)Math.Max(0, (now - reward.Timestamp).TotalMilliseconds);
            rewards.Append(i)
                .Append("|source=").Append(PacketValue(reward.SourceClientId))
                .Append("|role=").Append(PacketValue(reward.SourceRole))
                .Append("|run=").Append(PacketValue(reward.Run?.ToString(CultureInfo.InvariantCulture) ?? "unknown"))
                .Append("|type=").Append(PacketValue(reward.Type))
                .Append("|equipment=").Append(PacketValue(reward.Equipment))
                .Append("|level=").Append(PacketValue(reward.Level))
                .Append("|coopPodium=").Append(reward.CoopPodium?.ToString() ?? "unknown")
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        long seedAgeMs = ledger.DungeonSeedUpdatedAt != null
            ? (long)Math.Max(0, (now - ledger.DungeonSeedUpdatedAt.Value).TotalMilliseconds)
            : -1;

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " host=" + PacketValue(hostClientId)
            + " seed=" + (ledger.DungeonSeed?.ToString() ?? "unknown")
            + " seedSource=" + PacketValue(ledger.DungeonSeedSourceClientId ?? "unknown")
            + " seedAgeMs=" + seedAgeMs
            + " rewardCount=" + count
            + " rewards=" + (count == 0 ? "none" : rewards.ToString());
    }

    private static string BuildRewardClaimsMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder claims = new();
        int count = 0;
        int start = Math.Max(0, ledger.RewardClaims.Count - 32);
        for (int i = start; i < ledger.RewardClaims.Count; i++)
        {
            RewardClaimLedger claim = ledger.RewardClaims[i];
            if (string.Equals(claim.ClientId, recipient.ClientId, StringComparison.Ordinal)
                || now - claim.Timestamp > TimeSpan.FromSeconds(45)
                || IsEmptyPacketEquipment(claim.Equipment)
                || string.Equals(claim.Type, "Relic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (count > 0)
            {
                claims.Append(";");
            }

            long ageMs = (long)Math.Max(0, (now - claim.Timestamp).TotalMilliseconds);
            claims.Append(i)
                .Append("|source=").Append(PacketValue(claim.ClientId))
                .Append("|playerID=").Append(claim.PlayerId)
                .Append("|rewardIndex=").Append(claim.RewardIndex)
                .Append("|type=").Append(PacketValue(claim.Type))
                .Append("|equipment=").Append(PacketValue(claim.Equipment))
                .Append("|level=").Append(PacketValue(claim.Level))
                .Append("|coopPodium=").Append(claim.CoopPodium?.ToString() ?? "unknown")
                .Append("|podiumPos=").Append(PacketValue(claim.PodiumPosition))
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " claimCount=" + count
            + " claims=" + (count == 0 ? "none" : claims.ToString());
    }

    private static string BuildSpellCastsMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        StringBuilder casts = new();
        int count = 0;
        int start = Math.Max(0, ledger.SpellCasts.Count - 64);
        for (int i = start; i < ledger.SpellCasts.Count; i++)
        {
            SpellCastLedger cast = ledger.SpellCasts[i];
            if (string.Equals(cast.ClientId, recipient.ClientId, StringComparison.Ordinal)
                || now - cast.Timestamp > TimeSpan.FromSeconds(3.5))
            {
                continue;
            }

            ledger.Clients.TryGetValue(cast.ClientId, out ClientLedger? sourceClient);
            if (!string.Equals(sourceClient?.ServerRole, "host-lamb", StringComparison.Ordinal)
                && !string.Equals(sourceClient?.ServerRole, "remote-p2", StringComparison.Ordinal))
            {
                continue;
            }

            if (count > 0)
            {
                casts.Append(";");
            }

            long ageMs = (long)Math.Max(0, (now - cast.Timestamp).TotalMilliseconds);
            casts.Append("source=").Append(PacketValue(cast.ClientId))
                .Append("|session=").Append(PacketValue(cast.SessionId))
                .Append("|role=").Append(PacketValue(sourceClient?.ServerRole))
                .Append("|seq=").Append(cast.Sequence)
                .Append("|playerID=").Append(cast.PlayerId)
                .Append("|curse=").Append(PacketValue(cast.Curse))
                .Append("|curseLevel=").Append(PacketValue(cast.CurseLevel))
                .Append("|autoAim=").Append(PacketValue(cast.AutoAim))
                .Append("|consumeAmmo=").Append(PacketValue(cast.ConsumeAmmo))
                .Append("|wasSpell=").Append(PacketValue(cast.WasSpell))
                .Append("|damageMultiplier=").Append(PacketValue(cast.DamageMultiplier))
                .Append("|facingAngle=").Append(PacketValue(cast.FacingAngle))
                .Append("|lookAngle=").Append(PacketValue(cast.LookAngle))
                .Append("|aimAngle=").Append(PacketValue(cast.AimAngle))
                .Append("|targetOffset=").Append(PacketValue(cast.TargetOffset))
                .Append("|pos=").Append(PacketValue(cast.Position))
                .Append("|scene=").Append(PacketValue(cast.Scene))
                .Append("|location=").Append(PacketValue(cast.Location))
                .Append("|room=").Append(PacketValue(cast.Room))
                .Append("|ageMs=").Append(ageMs);
            count++;
        }

        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " worldId=" + PacketValue(ledger.ServerWorld?.WorldId ?? "none")
            + " castCount=" + count
            + " casts=" + (count == 0 ? "none" : casts.ToString());
    }

    private static string BuildEnemyAuthorityMessage(LedgerState ledger, ClientLedger recipient, DateTimeOffset now)
    {
        LedgerReducer.AssignClientRoles(ledger, now);

        ClientLedger? host = ledger.Clients.Values.FirstOrDefault(candidate =>
            candidate.HostPlayerSlot == 0 || string.Equals(candidate.ServerRole, "host-lamb", StringComparison.Ordinal));
        if (host == null || string.IsNullOrWhiteSpace(host.CombatHash))
        {
            return "serverTime=" + now.ToUnixTimeMilliseconds()
                + " target=" + PacketValue(recipient.ClientId)
                + " host=unknown"
                + " mode=waiting"
                + " room=unknown"
                + " hash=unknown"
                + " count=unknown"
                + " rounds=unknown"
                + " targetMatch=unknown"
                + " ageMs=unknown"
                + " preview=none";
        }

        long ageMs = host.LastSeen == null ? -1 : (long)Math.Max(0, (now - host.LastSeen.Value).TotalMilliseconds);
        return "serverTime=" + now.ToUnixTimeMilliseconds()
            + " target=" + PacketValue(recipient.ClientId)
            + " host=" + PacketValue(host.ClientId)
            + " role=" + PacketValue(host.ServerRole)
            + " mode=host-roster-observe"
            + " room=" + PacketValue(host.CombatRoom)
            + " hash=" + PacketValue(host.CombatHash)
            + " count=" + PacketValue(host.CombatCount?.ToString())
            + " rounds=" + PacketValue(host.CombatRounds)
            + " targetMatch=" + PacketValue(recipient.CombatMatch?.ToString())
            + " ageMs=" + (ageMs < 0 ? "unknown" : ageMs.ToString(CultureInfo.InvariantCulture))
            + " preview=" + PacketValue(host.CombatPreview);
    }

    private static string BuildServerPacket(string category, string message)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("O");
        return "{\"ts\":\"" + EscapeJson(timestamp)
            + "\",\"category\":\"" + EscapeJson(category)
            + "\",\"message\":\"" + EscapeJson(message)
            + "\"}";
    }

    private static string? ReadMessageToken(string message, string key)
    {
        Match match = Regex.Match(message ?? string.Empty, @"(?:^|\s)" + Regex.Escape(key) + @"=(?<value>[^\s\]]+)");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string PacketValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value
            .Replace("\r", "_")
            .Replace("\n", "_")
            .Replace(" ", "_")
            .Replace("|", "/")
            .Replace(";", ",");
    }

    private static string ResolveWorldsDirectory(Arguments options)
    {
        if (!string.IsNullOrWhiteSpace(options.WorldsDirectory))
        {
            return Path.GetFullPath(options.WorldsDirectory);
        }

        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor != null)
        {
            bool looksLikeWorkspace = File.Exists(Path.Combine(cursor.FullName, "README.md"))
                && Directory.Exists(Path.Combine(cursor.FullName, "src"));
            if (looksLikeWorkspace)
            {
                return Path.Combine(cursor.FullName, DefaultWorldsDirectoryName);
            }

            cursor = cursor.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, DefaultWorldsDirectoryName);
    }

    private static string? CleanClientId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static void PrintSummary(LedgerState ledger)
    {
        Console.WriteLine("COTL Online Server Ledger");
        Console.WriteLine("=========================");
        Console.WriteLine("trace: " + ledger.TracePath);
        Console.WriteLine("events: " + ledger.TotalEvents + " malformed: " + ledger.MalformedLines);
        Console.WriteLine("plugin: " + (ledger.PluginVersion ?? "unknown"));
        Console.WriteLine("scene: " + (ledger.Scene ?? "unknown") + " location: " + (ledger.Location ?? "unknown"));
        Console.WriteLine("run: " + (ledger.Run?.ToString() ?? "unknown") + " seed: " + (ledger.DungeonSeed?.ToString() ?? "unknown"));
        Console.WriteLine("world: " + (ledger.ServerWorld?.WorldId ?? "none") + " host=" + (ledger.ServerWorld?.HostClientId ?? "unknown") + " baselineHash=" + (ledger.ServerWorld?.BaselineHash ?? "unknown") + " serverSaveSlot=" + (ledger.ServerSaveSlot?.ToString() ?? "unknown"));
        Console.WriteLine("worldGraphRooms: " + (ledger.WorldGraphRoomCount?.ToString() ?? "unknown"));
        Console.WriteLine("lastWorldGraphStage: " + (ledger.LastWorldGraphStage ?? "unknown"));
        Console.WriteLine();
        Console.WriteLine("Connected Clients");
        foreach (ClientLedger client in ledger.Clients.Values.OrderBy(client => client.ClientId))
        {
            Console.WriteLine("  " + client.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Players");
        foreach (PlayerLedger player in ledger.Players.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            Console.WriteLine("  " + player.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Generated Rewards");
        foreach (RewardLedger reward in ledger.GeneratedRewards.TakeLast(12))
        {
            Console.WriteLine("  " + reward.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Equipment Events");
        foreach (EquipmentLedgerEvent equipmentEvent in ledger.EquipmentEvents.TakeLast(16))
        {
            Console.WriteLine("  " + equipmentEvent.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Authoritative Players");
        foreach (AuthoritativePlayerLedger player in ledger.AuthoritativePlayers.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            Console.WriteLine("  " + player.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Player Motion");
        Console.WriteLine("  samples=" + ledger.PlayerMotionSamples + " lastSeq=" + (ledger.LastPlayerMotionSequence?.ToString() ?? "unknown"));
        foreach (PlayerMotionLedger motion in ledger.PlayerMotion.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            Console.WriteLine("  " + motion.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Player Input");
        Console.WriteLine("  samples=" + ledger.PlayerInputSamples + " lastSeq=" + (ledger.LastPlayerInputSequence?.ToString() ?? "unknown"));
        foreach (PlayerInputLedger input in ledger.PlayerInputs.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            Console.WriteLine("  " + input.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Reward Claims");
        foreach (RewardClaimLedger claim in ledger.RewardClaims.TakeLast(16))
        {
            Console.WriteLine("  " + claim.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Server Decisions");
        foreach (ServerDecisionLedger decision in ledger.ServerDecisions.TakeLast(20))
        {
            Console.WriteLine("  " + decision.ToDisplayString());
        }

        Console.WriteLine();
        Console.WriteLine("Server Rule Candidates");
        foreach (string rule in ledger.RuleCandidates)
        {
            Console.WriteLine("  - " + rule);
        }
    }

    private static void WriteJsonIfRequested(Arguments options, LedgerState ledger)
    {
        if (string.IsNullOrWhiteSpace(options.JsonOutputPath))
        {
            return;
        }

        JsonSerializerOptions serializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(options.JsonOutputPath, JsonSerializer.Serialize(ledger, serializerOptions));
    }

    private sealed class Arguments
    {
        public string? TracePath { get; private init; }
        public string? TraceDirectory { get; private init; }
        public string? JsonOutputPath { get; private init; }
        public string? WorldsDirectory { get; private init; }
        public int? ServerSaveSlot { get; private init; }
        public string? HostClientId { get; private init; }
        public bool Watch { get; private init; }
        public bool ListenUdp { get; private init; }
        public int? UdpPort { get; private init; }

        public static Arguments Parse(string[] args)
        {
            string? tracePath = null;
            string? traceDirectory = null;
            string? jsonOutputPath = null;
            string? worldsDirectory = null;
            int? serverSaveSlot = null;
            string? hostClientId = null;
            bool watch = false;
            bool listenUdp = false;
            int? udpPort = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--trace" && i + 1 < args.Length)
                {
                    tracePath = args[++i];
                }
                else if (arg == "--trace-dir" && i + 1 < args.Length)
                {
                    traceDirectory = args[++i];
                }
                else if (arg == "--json-out" && i + 1 < args.Length)
                {
                    jsonOutputPath = args[++i];
                }
                else if (arg == "--worlds-dir" && i + 1 < args.Length)
                {
                    worldsDirectory = args[++i];
                }
                else if (arg == "--server-save-slot" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedSaveSlot))
                {
                    serverSaveSlot = parsedSaveSlot;
                    i++;
                }
                else if ((arg == "--host-client-id" || arg == "--preferred-host") && i + 1 < args.Length)
                {
                    hostClientId = args[++i];
                }
                else if (arg == "--watch")
                {
                    watch = true;
                }
                else if (arg == "--listen-udp")
                {
                    listenUdp = true;
                }
                else if (arg == "--udp-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort))
                {
                    udpPort = parsedPort;
                    i++;
                }
                else if (!arg.StartsWith("--", StringComparison.Ordinal) && tracePath == null)
                {
                    tracePath = arg;
                }
            }

            return new Arguments
            {
                TracePath = tracePath,
                TraceDirectory = traceDirectory,
                JsonOutputPath = jsonOutputPath,
                WorldsDirectory = worldsDirectory,
                ServerSaveSlot = serverSaveSlot,
                HostClientId = hostClientId,
                Watch = watch,
                ListenUdp = listenUdp,
                UdpPort = udpPort
            };
        }
    }
}

internal static partial class LedgerReducer
{
    public static LedgerEvent? Apply(LedgerState ledger, TraceEvent traceEvent)
    {
        ledger.TotalEvents++;
        ledger.LastTimestamp = traceEvent.Timestamp;

        string category = traceEvent.Category ?? "";
        string message = traceEvent.Message ?? "";

        UpdateCommonContext(ledger, message);
        LedgerEvent? clientPresenceEvent = UpdateClientPresence(ledger, traceEvent, category, message);

        if (category == "bepinex.plugin.loaded" && message.Contains("COTL Online Diagnostics", StringComparison.Ordinal))
        {
            ledger.PluginVersion = ReadToken(message, "version");
            return new LedgerEvent(traceEvent.Timestamp, "plugin", PlayerLabel(ReadClientId(message), null) + " diagnostics " + ledger.PluginVersion);
        }

        if (category == "live.heartbeat")
        {
            bool firstVersion = string.IsNullOrEmpty(ledger.PluginVersion);
            ledger.PluginVersion = ReadToken(message, "pluginVersion") ?? ledger.PluginVersion;
            ledger.Scene = ReadToken(message, "scene") ?? ledger.Scene;
            ledger.Location = ReadToken(message, "location") ?? ledger.Location;
            ledger.Run = ReadIntToken(message, "run") ?? ledger.Run;

            if (firstVersion && !string.IsNullOrEmpty(ledger.PluginVersion))
            {
                return new LedgerEvent(traceEvent.Timestamp, "plugin", PlayerLabel(ReadClientId(message), null) + " heartbeat diagnostics " + ledger.PluginVersion);
            }

            return null;
        }

        if (category == "sync.world_identity")
        {
            return ApplyWorldIdentity(ledger, traceEvent, message);
        }

        if (category == "sync.cult_snapshot")
        {
            return ApplyCultSnapshot(ledger, traceEvent, message);
        }

        if (category == "sync.world_hash")
        {
            string clientId = ReadClientId(message);
            if (ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
            {
                string? runtimeHash = ReadToken(message, "hash");
                client.RuntimeWorldHash = runtimeHash ?? client.RuntimeWorldHash;
                client.WorldSelectedFields = ReadIntToken(message, "selectedFields") ?? client.WorldSelectedFields;

                if (string.IsNullOrWhiteSpace(client.WorldHash) && string.IsNullOrWhiteSpace(client.SaveFileHash))
                {
                    client.WorldHash = runtimeHash ?? client.WorldHash;
                }

                client.WorldMatch = ComputeWorldMatch(ledger, client);
            }

            return null;
        }

        if (category.StartsWith("phase5.coop.reserve.", StringComparison.Ordinal)
            || category.StartsWith("phase5.coop.remove_menu.", StringComparison.Ordinal)
            || category.StartsWith("phase5.coop.rewired_refresh.", StringComparison.Ordinal))
        {
            return ApplyPhase5CoopReservation(ledger, traceEvent, category, message);
        }

        if (category.StartsWith("phase6.remote_p2.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "p2drv", PlayerLabel(ReadClientId(message), null) + " " + category + " " + Limit(message, 220));
        }

        if (category.StartsWith("phase7.remote_host_mirror.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "hmirror", PlayerLabel(ReadClientId(message), null) + " " + category + " " + Limit(message, 220));
        }

        if (category.StartsWith("phase9.camera_split.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "camera", PlayerLabel(ReadClientId(message), null) + " " + category + " " + Limit(message, 220));
        }

        if (category.StartsWith("phase10.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "run", PlayerLabel(ReadClientId(message), null) + " " + category + " " + Limit(message, 240));
        }

        if (category.StartsWith("phase11.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "spell", PlayerLabel(ReadClientId(message), null) + " " + category + " " + Limit(message, 260));
        }

        if (category.StartsWith("perf.", StringComparison.Ordinal))
        {
            return new LedgerEvent(traceEvent.Timestamp, "perf", PlayerLabel(ReadClientId(message), null) + " " + Limit(message, 220));
        }

        if (category == "phase3.run.set_new.postfix")
        {
            int? nextRun = ReadIntToken(message, "run");
            string? nextLocation = ReadToken(message, "location");
            bool resetAuthority = nextRun != null && (!ledger.Run.HasValue || ledger.Run.Value != nextRun.Value);
            int clearedRewards = ledger.GeneratedRewards.Count;
            int clearedClaims = ledger.RewardClaims.Count;
            if (resetAuthority)
            {
                ResetRunAuthorityState(ledger);
            }

            ledger.Location = nextLocation ?? ledger.Location;
            ledger.Run = nextRun ?? ledger.Run;
            return new LedgerEvent(
                traceEvent.Timestamp,
                "run",
                "new run " + ledger.Run
                + " location=" + ledger.Location
                + " resetAuthority=" + resetAuthority
                + " clearedRewards=" + (resetAuthority ? clearedRewards : 0)
                + " clearedClaims=" + (resetAuthority ? clearedClaims : 0));
        }

        if (category == "phase3.run.seed_added.postfix")
        {
            int? observedSeed = ReadIntToken(message, "seed");
            string clientId = ReadClientId(message);
            bool accepted = UpdateAuthoritativeDungeonSeed(ledger, traceEvent.Timestamp, clientId, observedSeed);
            return new LedgerEvent(
                traceEvent.Timestamp,
                "seed",
                "dungeon seed " + (observedSeed?.ToString() ?? "unknown")
                + " source=" + clientId
                + " accepted=" + accepted
                + " authority=" + (ledger.DungeonSeed?.ToString() ?? "unknown")
                + " authoritySource=" + (ledger.DungeonSeedSourceClientId ?? "unknown"));
        }

        if (category.StartsWith("phase3.world.", StringComparison.Ordinal) && category.EndsWith(".postfix", StringComparison.Ordinal))
        {
            ledger.LastWorldGraphStage = category;
            ledger.WorldGraphRoomCount = CountRoomGraphEntries(message) ?? ledger.WorldGraphRoomCount;
            ledger.LastWorldGraphPreview = Limit(message, 3000);
            return new LedgerEvent(traceEvent.Timestamp, "world", category + " rooms=" + ledger.WorldGraphRoomCount);
        }

        if (category.StartsWith("sync.player_state", StringComparison.Ordinal))
        {
            string clientId = ReadClientId(message);
            string sessionId = ReadSessionId(message);
            PlayerLedger player = GetOrCreatePlayer(ledger, clientId, sessionId, ReadIntToken(message, "playerID"), ReadToken(message, "name"));
            string previousSignature = player.LiveSignature;
            player.ClientId = clientId;
            player.SessionId = sessionId;
            player.Name = ReadToken(message, "name") ?? player.Name;
            player.Position = ReadToken(message, "pos") ?? player.Position;
            player.State = ReadToken(message, "state") ?? player.State;
            player.HitPoints = ReadToken(message, "hp") ?? player.HitPoints;
            player.LastSeen = traceEvent.Timestamp;
            string newSignature = player.BuildLiveSignature();
            player.LiveSignature = newSignature;

            if (previousSignature != newSignature)
            {
                return new LedgerEvent(traceEvent.Timestamp, "player", player.ToDisplayString());
            }

            return null;
        }

        if (category == "sync.player_motion")
        {
            return ApplyPlayerMotion(ledger, traceEvent, message);
        }

        if (category == "sync.player_input")
        {
            return ApplyPlayerInput(ledger, traceEvent, message);
        }

        if (category == "sync.spell_cast")
        {
            return ApplySpellCast(ledger, traceEvent, message);
        }

        if (category == "sync.player_life")
        {
            return ApplyPlayerLife(ledger, traceEvent, message);
        }

        if (category == "sync.combat_roster")
        {
            return ApplyCombatRoster(ledger, traceEvent, message);
        }

        if (category == "sync.combat_spawn")
        {
            return new LedgerEvent(
                traceEvent.Timestamp,
                "spawn",
                PlayerLabel(ReadClientId(message), null)
                + " room=" + (ReadToken(message, "room") ?? "unknown")
                + " source=" + (ReadToken(message, "source") ?? "unknown")
                + " prefab=" + (ReadToken(message, "prefab") ?? "unknown")
                + " result=" + (ReadToken(message, "result") ?? "unknown")
                + " pos=" + (ReadToken(message, "pos") ?? "unknown"));
        }

        if (category == "sync.world_manipulation")
        {
            return new LedgerEvent(
                traceEvent.Timestamp,
                "event",
                PlayerLabel(ReadClientId(message), null)
                + " manipulation=" + (ReadToken(message, "manipulation") ?? "unknown")
                + " room=" + (ReadToken(message, "room") ?? "unknown")
                + " delay=" + (ReadToken(message, "delay") ?? "unknown")
                + " twitch=" + (ReadToken(message, "twitch") ?? "unknown"));
        }

        if (category == "sync.encounter_island")
        {
            return new LedgerEvent(
                traceEvent.Timestamp,
                "encounter",
                PlayerLabel(ReadClientId(message), null)
                + " island phase=" + (ReadToken(message, "phase") ?? "unknown")
                + " room=" + (ReadToken(message, "room") ?? "unknown")
                + " pieceHash=" + (ReadToken(message, "pieceHash") ?? "unknown")
                + " piece=" + (ReadToken(message, "piece") ?? "unknown")
                + " encounters=" + (ReadToken(message, "encounters") ?? "unknown"));
        }

        if (category == "sync.encounter_chance")
        {
            return new LedgerEvent(
                traceEvent.Timestamp,
                "encounter",
                PlayerLabel(ReadClientId(message), null)
                + " chance phase=" + (ReadToken(message, "phase") ?? "unknown")
                + " room=" + (ReadToken(message, "room") ?? "unknown")
                + " source=" + (ReadToken(message, "source") ?? "unknown")
                + " units=" + (ReadToken(message, "units") ?? "unknown")
                + " unitHash=" + (ReadToken(message, "unitHash") ?? "unknown")
                + " preview=" + (ReadToken(message, "preview") ?? "unknown"));
        }

        if (category == "sync.death_screen")
        {
            return new LedgerEvent(
                traceEvent.Timestamp,
                "death",
                PlayerLabel(ReadClientId(message), null)
                + " result=" + (ReadToken(message, "result") ?? "unknown")
                + " levels=" + (ReadToken(message, "levels") ?? "unknown")
                + " scene=" + (ReadToken(message, "scene") ?? "unknown")
                + " location=" + (ReadToken(message, "location") ?? "unknown")
                + " autoRespawn=" + (ReadToken(message, "autoRespawn") ?? "unknown"));
        }

        if (category is "phase3.equipment.player_weapon_set.prefix" or "phase3.equipment.player_spell_set.prefix")
        {
            return ApplyEquipmentStackHint(ledger, traceEvent, category, message);
        }

        if (category is "phase3.equipment.player_weapon_set.postfix" or "phase3.equipment.player_spell_set.postfix")
        {
            return ApplyEquipmentAssignment(ledger, traceEvent, category, message);
        }

        if (category == "phase3.equipment.pickup_interact.prefix")
        {
            string clientId = ReadClientId(message);
            int? playerId = ReadIntToken(message, "playerID");
            string? equipment = ReadToken(message, "equipment");
            string? level = ReadToken(message, "level");
            string? type = ReadToken(message, "type");
            ledger.EquipmentEvents.Add(new EquipmentLedgerEvent(traceEvent.Timestamp, clientId, playerId, "pickup-interact", type, equipment, level, ReadToken(message, "scene")));
            return new LedgerEvent(traceEvent.Timestamp, "pickup", PlayerLabel(clientId, playerId) + " sees " + type + " " + equipment + "@" + level);
        }

        if (category == "phase2.reward.podium_interact.prefix" || category == "phase2.reward.choice_interact.prefix")
        {
            string clientId = ReadClientId(message);
            int? playerId = ReadIntToken(message, "playerID");
            string? type = ReadToken(message, "type");
            string? equipment = ReadToken(message, "weapon");
            string? level = ReadToken(message, "level");
            bool? coopPodium = ReadBoolToken(message, "coopPodium");
            string? podiumPos = ReadToken(message, "podiumPos");
            string source = category.Contains("choice_", StringComparison.Ordinal) ? "choice-interact" : "podium-interact";
            ledger.EquipmentEvents.Add(new EquipmentLedgerEvent(traceEvent.Timestamp, clientId, playerId, source + " coopPodium=" + (coopPodium?.ToString() ?? "unknown"), type, equipment, level, ReadToken(message, "scene")));
            ApplyRewardClaimDecision(ledger, traceEvent.Timestamp, clientId, playerId, type, equipment, level, coopPodium, podiumPos);
            return new LedgerEvent(traceEvent.Timestamp, "podium", PlayerLabel(clientId, playerId) + " interacts " + type + " " + equipment + "@" + level + " coopPodium=" + (coopPodium?.ToString() ?? "unknown"));
        }

        if (category.StartsWith("phase2.reward.podium_set_", StringComparison.Ordinal) && category.EndsWith(".postfix", StringComparison.Ordinal))
        {
            string clientId = ReadClientId(message);
            AssignClientRoles(ledger, traceEvent.Timestamp);
            ledger.Clients.TryGetValue(clientId, out ClientLedger? rewardClient);
            RewardLedger reward = new(
                traceEvent.Timestamp,
                clientId,
                rewardClient?.ServerRole,
                ledger.Run,
                ledger.Location,
                ReadToken(message, "type"),
                ReadToken(message, "weapon"),
                ReadToken(message, "level"),
                ReadBoolToken(message, "coopPodium"),
                ReadBoolToken(message, "destroyOtherInCoop"));
            ledger.GeneratedRewards.Add(reward);
            ledger.ServerDecisions.Add(ServerDecisionLedger.GeneratedReward(traceEvent.Timestamp, ledger.GeneratedRewards.Count - 1, reward));
            return new LedgerEvent(traceEvent.Timestamp, "reward", reward.ToDisplayString());
        }

        return clientPresenceEvent;
    }

    private static LedgerEvent? ApplyWorldIdentity(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        string? runtimeHash = ReadToken(message, "hash");
        string? saveFileHash = ReadToken(message, "saveHash");
        string? stableHash = !string.IsNullOrWhiteSpace(saveFileHash) && !string.Equals(saveFileHash, "none", StringComparison.Ordinal)
            ? saveFileHash
            : runtimeHash;
        if (string.IsNullOrWhiteSpace(stableHash))
        {
            return null;
        }

        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            client = new ClientLedger(clientId)
            {
                FirstSeen = traceEvent.Timestamp,
                JoinOrder = ++ledger.NextClientJoinOrder
            };
            ledger.Clients[clientId] = client;
        }

        client.SessionId = sessionId;
        client.RuntimeWorldHash = runtimeHash;
        client.SaveFileHash = saveFileHash;
        client.WorldHash = stableHash;
        client.SaveSlot = ReadIntToken(message, "saveSlot") ?? client.SaveSlot;
        client.SaveSlotOk = ComputeSaveSlotOk(ledger, client);
        client.WorldSelectedFields = ReadIntToken(message, "selectedFields") ?? client.WorldSelectedFields;
        client.FollowersCount = ReadIntToken(message, "followers") ?? client.FollowersCount;
        client.StructuresCount = ReadIntToken(message, "structures") ?? client.StructuresCount;
        client.CultName = ReadToken(message, "cultName") ?? client.CultName;
        client.LastDungeonSeeds = ReadToken(message, "lastDungeonSeeds") ?? client.LastDungeonSeeds;
        client.Scene = ReadToken(message, "scene") ?? client.Scene;
        client.Location = ReadToken(message, "location") ?? client.Location;
        client.LastSeen = traceEvent.Timestamp;
        client.LastCategory = "sync.world_identity";

        AssignClientRoles(ledger, traceEvent.Timestamp);
        EnsureServerWorld(ledger, client, traceEvent.Timestamp);
        UpdateHostWorldBaseline(ledger, client, traceEvent.Timestamp);
        client.WorldMatch = ComputeWorldMatch(ledger, client);
        client.SaveSlotOk = ComputeSaveSlotOk(ledger, client);
        UpdateServerWorldClient(ledger, client, traceEvent.Timestamp);
        PersistServerWorld(ledger);

        string signature = client.WorldSignature();
        if (client.LastWorldSignature == signature)
        {
            return null;
        }

        client.LastWorldSignature = signature;
        return new LedgerEvent(
            traceEvent.Timestamp,
            "world",
            "world=" + (ledger.ServerWorld?.WorldId ?? "none")
            + " " + client.ClientId
            + " save=" + (client.SaveSlot?.ToString() ?? "unknown")
            + " hash=" + (client.WorldHash ?? "unknown")
            + " runtimeHash=" + (client.RuntimeWorldHash ?? "unknown")
            + " match=" + (client.WorldMatch?.ToString() ?? "unknown")
            + " followers=" + (client.FollowersCount?.ToString() ?? "unknown")
            + " structures=" + (client.StructuresCount?.ToString() ?? "unknown"));
    }

    private static void EnsureServerWorld(LedgerState ledger, ClientLedger hostCandidate, DateTimeOffset timestamp)
    {
        if (ledger.ServerWorld != null)
        {
            return;
        }

        string seed = (hostCandidate.WorldHash ?? "unknown") + "-" + (hostCandidate.SaveSlot?.ToString() ?? "slot");
        string worldId = "world-" + StableShortHash(seed);
        ledger.ServerWorld = new ServerWorldLedger
        {
            WorldId = worldId,
            HostClientId = hostCandidate.ClientId,
            BaselineHash = hostCandidate.WorldHash,
            BaselineSaveSlot = hostCandidate.SaveSlot,
            TargetSaveSlot = ledger.ServerSaveSlot ?? 4,
            BaselineSelectedFields = hostCandidate.WorldSelectedFields,
            CreatedAt = timestamp,
            LastUpdatedAt = timestamp,
            SaveRedirectDryRunPath = BuildSaveRedirectPath(ledger, worldId, ledger.ServerSaveSlot)
        };
    }

    private static void UpdateHostWorldBaseline(LedgerState ledger, ClientLedger client, DateTimeOffset timestamp)
    {
        if (ledger.ServerWorld == null
            || string.IsNullOrWhiteSpace(client.WorldHash))
        {
            return;
        }

        bool isAssignedHost = IsAssignedHostClient(client);
        if (!isAssignedHost && !string.Equals(client.ClientId, ledger.ServerWorld.HostClientId, StringComparison.Ordinal))
        {
            return;
        }

        string? previousHash = ledger.ServerWorld.BaselineHash;
        string? previousHost = ledger.ServerWorld.HostClientId;
        if (isAssignedHost)
        {
            ledger.ServerWorld.HostClientId = client.ClientId;
        }

        ledger.ServerWorld.BaselineHash = client.WorldHash;
        ledger.ServerWorld.BaselineSaveSlot = client.SaveSlot;
        ledger.ServerWorld.TargetSaveSlot = ledger.ServerSaveSlot ?? ledger.ServerWorld.TargetSaveSlot;
        ledger.ServerWorld.BaselineSelectedFields = client.WorldSelectedFields;
        ledger.ServerWorld.LastUpdatedAt = timestamp;
        ledger.ServerWorld.SaveRedirectDryRunPath = BuildSaveRedirectPath(ledger, ledger.ServerWorld.WorldId ?? "world-unknown", ledger.ServerWorld.TargetSaveSlot);

        if (!string.IsNullOrWhiteSpace(previousHash)
            && (!string.Equals(previousHash, client.WorldHash, StringComparison.Ordinal)
                || !string.Equals(previousHost, ledger.ServerWorld.HostClientId, StringComparison.Ordinal)))
        {
            ledger.ServerWorld.BaselineRevisions++;
        }
    }

    private static bool IsAssignedHostClient(ClientLedger client)
    {
        return client.HostPlayerSlot == 0 || string.Equals(client.ServerRole, "host-lamb", StringComparison.Ordinal);
    }

    private static bool UpdateAuthoritativeDungeonSeed(LedgerState ledger, DateTimeOffset timestamp, string clientId, int? observedSeed)
    {
        if (observedSeed == null)
        {
            return false;
        }

        AssignClientRoles(ledger, timestamp);
        bool isHost = ledger.Clients.TryGetValue(clientId, out ClientLedger? client) && IsAssignedHostClient(client);
        bool noAuthorityYet = ledger.DungeonSeed == null || string.IsNullOrWhiteSpace(ledger.DungeonSeedSourceClientId);
        string? knownHostClientId = ledger.ServerWorld?.HostClientId ?? ledger.PreferredHostClientId;
        if (!isHost && noAuthorityYet && !string.IsNullOrWhiteSpace(knownHostClientId))
        {
            return false;
        }

        bool existingSourceIsHost = !string.IsNullOrWhiteSpace(ledger.DungeonSeedSourceClientId)
            && ledger.Clients.TryGetValue(ledger.DungeonSeedSourceClientId, out ClientLedger? existingSource)
            && IsAssignedHostClient(existingSource);

        if (!isHost && !noAuthorityYet && existingSourceIsHost)
        {
            return false;
        }

        if (!isHost && !noAuthorityYet && !string.Equals(ledger.DungeonSeedSourceClientId, clientId, StringComparison.Ordinal))
        {
            return false;
        }

        ledger.DungeonSeed = observedSeed;
        ledger.DungeonSeedSourceClientId = clientId;
        ledger.DungeonSeedUpdatedAt = timestamp;
        return true;
    }

    private static void ResetRunAuthorityState(LedgerState ledger)
    {
        ledger.DungeonSeed = null;
        ledger.DungeonSeedSourceClientId = null;
        ledger.DungeonSeedUpdatedAt = null;
        ledger.CurrentRunWeaponLevel = null;
        ledger.CurrentRunCurseLevel = null;
        ledger.WorldGraphRoomCount = null;
        ledger.LastWorldGraphStage = null;
        ledger.LastWorldGraphPreview = null;
        ledger.GeneratedRewards.Clear();
        ledger.RewardClaims.Clear();

        foreach (ClientLedger client in ledger.Clients.Values)
        {
            client.LastRunAuthoritySent = null;
            client.LastRewardClaimsSent = null;
        }
    }

    private static bool? ComputeWorldMatch(LedgerState ledger, ClientLedger client)
    {
        if (string.IsNullOrWhiteSpace(client.WorldHash) || string.IsNullOrWhiteSpace(ledger.ServerWorld?.BaselineHash))
        {
            return null;
        }

        return string.Equals(client.WorldHash, ledger.ServerWorld.BaselineHash, StringComparison.Ordinal);
    }

    private static bool? ComputeSaveSlotOk(LedgerState ledger, ClientLedger client)
    {
        if (client.SaveSlot == null || ledger.ServerSaveSlot == null)
        {
            return null;
        }

        return client.SaveSlot == ledger.ServerSaveSlot;
    }

    private static LedgerEvent? ApplyCultSnapshot(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        string? snapshotHash = ReadToken(message, "hash");
        if (string.IsNullOrWhiteSpace(snapshotHash))
        {
            return null;
        }

        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            client = new ClientLedger(clientId)
            {
                FirstSeen = traceEvent.Timestamp,
                JoinOrder = ++ledger.NextClientJoinOrder
            };
            ledger.Clients[clientId] = client;
        }

        client.SessionId = sessionId;
        client.CultSnapshotHash = snapshotHash;
        client.CultFollowersCount = ReadIntToken(message, "followers") ?? client.CultFollowersCount;
        client.CultDataFollowersCount = ReadIntToken(message, "dataFollowers") ?? client.CultDataFollowersCount;
        client.CultActiveFollowersCount = ReadIntToken(message, "activeFollowers") ?? client.CultActiveFollowersCount;
        client.CultStructuresCount = ReadIntToken(message, "structures") ?? client.CultStructuresCount;
        client.CultFaith = ReadToken(message, "cultFaith") ?? client.CultFaith;
        client.CultStaticFaith = ReadToken(message, "staticFaith") ?? client.CultStaticFaith;
        client.CultDay = ReadIntToken(message, "day") ?? client.CultDay;
        client.CultElapsed = ReadToken(message, "elapsed") ?? client.CultElapsed;
        client.CultPreview = ReadToken(message, "preview") ?? client.CultPreview;
        client.Scene = ReadToken(message, "scene") ?? client.Scene;
        client.Location = ReadToken(message, "location") ?? client.Location;
        client.SaveSlot = ReadIntToken(message, "saveSlot") ?? client.SaveSlot;
        client.LastSeen = traceEvent.Timestamp;
        client.LastCategory = "sync.cult_snapshot";

        AssignClientRoles(ledger, traceEvent.Timestamp);
        UpdateHostCultBaseline(ledger, client, traceEvent.Timestamp);
        client.CultSnapshotMatch = ComputeCultSnapshotMatch(ledger, client);
        UpdateServerWorldClient(ledger, client, traceEvent.Timestamp);
        PersistServerWorld(ledger);

        string signature = client.CultSignature();
        if (string.Equals(client.LastCultSignature, signature, StringComparison.Ordinal))
        {
            return null;
        }

        client.LastCultSignature = signature;
        return new LedgerEvent(
            traceEvent.Timestamp,
            "cult",
            "world=" + (ledger.ServerWorld?.WorldId ?? "none")
            + " " + client.ClientId
            + " role=" + (client.ServerRole ?? "unknown")
            + " hash=" + (client.CultSnapshotHash ?? "unknown")
            + " match=" + (client.CultSnapshotMatch?.ToString() ?? "unknown")
            + " followers=" + (client.CultFollowersCount?.ToString() ?? "unknown")
            + " active=" + (client.CultActiveFollowersCount?.ToString() ?? "unknown")
            + " faith=" + (client.CultFaith ?? "unknown")
            + " preview=" + (client.CultPreview ?? "unknown"));
    }

    private static void UpdateHostCultBaseline(LedgerState ledger, ClientLedger client, DateTimeOffset timestamp)
    {
        if (ledger.ServerWorld == null || string.IsNullOrWhiteSpace(client.CultSnapshotHash))
        {
            return;
        }

        bool isAssignedHost = IsAssignedHostClient(client);
        if (!isAssignedHost && !string.Equals(client.ClientId, ledger.ServerWorld.HostClientId, StringComparison.Ordinal))
        {
            return;
        }

        string? previousHash = ledger.ServerWorld.BaselineCultSnapshotHash;
        ledger.ServerWorld.BaselineCultSnapshotHash = client.CultSnapshotHash;
        ledger.ServerWorld.BaselineCultFollowers = client.CultFollowersCount;
        ledger.ServerWorld.BaselineCultActiveFollowers = client.CultActiveFollowersCount;
        ledger.ServerWorld.BaselineCultFaith = client.CultFaith;
        ledger.ServerWorld.BaselineCultPreview = client.CultPreview;
        ledger.ServerWorld.LastUpdatedAt = timestamp;

        if (!string.IsNullOrWhiteSpace(previousHash)
            && !string.Equals(previousHash, client.CultSnapshotHash, StringComparison.Ordinal))
        {
            ledger.ServerWorld.BaselineCultRevisions++;
        }
    }

    private static bool? ComputeCultSnapshotMatch(LedgerState ledger, ClientLedger client)
    {
        if (string.IsNullOrWhiteSpace(client.CultSnapshotHash) || string.IsNullOrWhiteSpace(ledger.ServerWorld?.BaselineCultSnapshotHash))
        {
            return null;
        }

        return string.Equals(client.CultSnapshotHash, ledger.ServerWorld.BaselineCultSnapshotHash, StringComparison.Ordinal);
    }

    private static void UpdateServerWorldClient(LedgerState ledger, ClientLedger client, DateTimeOffset timestamp)
    {
        if (ledger.ServerWorld == null)
        {
            return;
        }

        ledger.ServerWorld.LastUpdatedAt = timestamp;
        ledger.ServerWorld.Clients[client.ClientId] = new ServerWorldClientLedger
        {
            ClientId = client.ClientId,
            SessionId = client.SessionId,
            Role = client.ServerRole,
            SaveSlot = client.SaveSlot,
            SaveSlotOk = client.SaveSlotOk,
            WorldHash = client.WorldHash,
            WorldMatch = client.WorldMatch,
            FollowersCount = client.FollowersCount,
            StructuresCount = client.StructuresCount,
            CultName = client.CultName,
            LastDungeonSeeds = client.LastDungeonSeeds,
            HostP2Wanted = client.HostP2Wanted,
            HostP2Active = client.HostP2Active,
            HostP2Hold = client.HostP2Hold,
            HostP2NoController = client.HostP2NoController,
            HostP2State = client.HostP2State,
            HostP2Rewired = client.HostP2Rewired,
            CultSnapshotHash = client.CultSnapshotHash,
            CultSnapshotMatch = client.CultSnapshotMatch,
            CultFollowersCount = client.CultFollowersCount,
            CultDataFollowersCount = client.CultDataFollowersCount,
            CultActiveFollowersCount = client.CultActiveFollowersCount,
            CultStructuresCount = client.CultStructuresCount,
            CultFaith = client.CultFaith,
            CultStaticFaith = client.CultStaticFaith,
            CultDay = client.CultDay,
            CultElapsed = client.CultElapsed,
            CultPreview = client.CultPreview,
            CombatHash = client.CombatHash,
            CombatMatch = client.CombatMatch,
            CombatCount = client.CombatCount,
            CombatRoom = client.CombatRoom,
            CombatRounds = client.CombatRounds,
            CombatPreview = client.CombatPreview,
            LastSeen = timestamp
        };
    }

    private static string BuildSaveRedirectPath(LedgerState ledger, string worldId, int? saveSlot)
    {
        string worldsDir = string.IsNullOrWhiteSpace(ledger.WorldsDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "server_worlds")
            : ledger.WorldsDirectory;
        return Path.Combine(worldsDir, worldId, "save", "slot_" + (saveSlot?.ToString() ?? "unknown"));
    }

    private static void PersistServerWorld(LedgerState ledger)
    {
        if (ledger.ServerWorld == null || string.IsNullOrWhiteSpace(ledger.WorldsDirectory))
        {
            return;
        }

        try
        {
            string worldDir = Path.Combine(ledger.WorldsDirectory, ledger.ServerWorld.WorldId ?? "world-unknown");
            Directory.CreateDirectory(worldDir);
            Directory.CreateDirectory(Path.Combine(worldDir, "save"));

            JsonSerializerOptions serializerOptions = new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            File.WriteAllText(Path.Combine(worldDir, "world.json"), JsonSerializer.Serialize(ledger.ServerWorld, serializerOptions));

            string journalLine = JsonSerializer.Serialize(new
            {
                ts = ledger.ServerWorld.LastUpdatedAt,
                worldId = ledger.ServerWorld.WorldId,
                host = ledger.ServerWorld.HostClientId,
                baselineHash = ledger.ServerWorld.BaselineHash,
                clients = ledger.ServerWorld.Clients.Values
            });
            File.AppendAllText(Path.Combine(worldDir, "event_journal.jsonl"), journalLine + Environment.NewLine);
        }
        catch (Exception ex)
        {
            ledger.AddRuleCandidate("server_world_persist_failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string StableShortHash(string value)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        StringBuilder sb = new(12);
        for (int i = 0; i < 6; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }

        return sb.ToString();
    }

    private static LedgerEvent? ApplyPlayerMotion(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        int? playerId = ReadIntToken(message, "playerID");
        if (playerId == null)
        {
            return null;
        }

        ledger.PlayerMotionSamples++;
        ledger.LastPlayerMotionSequence = ReadIntToken(message, "seq") ?? ledger.LastPlayerMotionSequence;

        PlayerMotionLedger motion = GetOrCreatePlayerMotion(ledger, clientId, sessionId, playerId.Value);
        motion.ClientId = clientId;
        motion.SessionId = sessionId;
        motion.Name = ReadToken(message, "name") ?? motion.Name;
        motion.Position = ReadToken(message, "pos") ?? motion.Position;
        motion.State = ReadToken(message, "state") ?? motion.State;
        motion.HitPoints = ReadToken(message, "hp") ?? motion.HitPoints;
        motion.Scene = ReadToken(message, "scene") ?? motion.Scene;
        motion.Location = ReadToken(message, "location") ?? motion.Location;
        motion.Room = ReadToken(message, "room") ?? motion.Room;
        motion.Sequence = ReadIntToken(message, "seq") ?? motion.Sequence;
        motion.LastSeen = traceEvent.Timestamp;

        PlayerLedger player = GetOrCreatePlayer(ledger, clientId, sessionId, playerId, motion.Name);
        player.ClientId = clientId;
        player.SessionId = sessionId;
        player.Name = motion.Name ?? player.Name;
        player.Position = motion.Position ?? player.Position;
        player.State = motion.State ?? player.State;
        player.HitPoints = motion.HitPoints ?? player.HitPoints;
        player.LastSeen = traceEvent.Timestamp;

        if (ledger.LastPlayerMotionDisplay == null || traceEvent.Timestamp - ledger.LastPlayerMotionDisplay >= TimeSpan.FromSeconds(5))
        {
            ledger.LastPlayerMotionDisplay = traceEvent.Timestamp;
            return new LedgerEvent(traceEvent.Timestamp, "motion", BuildMotionDisplay(ledger));
        }

        return null;
    }

    private static LedgerEvent? ApplyPlayerInput(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        int? playerId = ReadIntToken(message, "playerID");
        if (playerId == null)
        {
            return null;
        }

        ledger.PlayerInputSamples++;
        ledger.LastPlayerInputSequence = ReadIntToken(message, "seq") ?? ledger.LastPlayerInputSequence;

        PlayerInputLedger input = GetOrCreatePlayerInput(ledger, clientId, sessionId, playerId.Value);
        input.ClientId = clientId;
        input.SessionId = sessionId;
        input.Sequence = ReadIntToken(message, "seq") ?? input.Sequence;
        input.AxisX = ReadToken(message, "ax") ?? input.AxisX;
        input.AxisY = ReadToken(message, "ay") ?? input.AxisY;
        input.AttackDown = ReadBoolToken(message, "attackDown") ?? input.AttackDown;
        input.AttackHeld = ReadBoolToken(message, "attackHeld") ?? input.AttackHeld;
        input.AttackUp = ReadBoolToken(message, "attackUp") ?? input.AttackUp;
        input.DodgeDown = ReadBoolToken(message, "dodgeDown") ?? input.DodgeDown;
        input.DodgeHeld = ReadBoolToken(message, "dodgeHeld") ?? input.DodgeHeld;
        input.CurseDown = ReadBoolToken(message, "curseDown") ?? input.CurseDown;
        input.CurseHeld = ReadBoolToken(message, "curseHeld") ?? input.CurseHeld;
        input.CurseUp = ReadBoolToken(message, "curseUp") ?? input.CurseUp;
        input.HeavyDown = ReadBoolToken(message, "heavyDown") ?? input.HeavyDown;
        input.HeavyHeld = ReadBoolToken(message, "heavyHeld") ?? input.HeavyHeld;
        input.FacingAngle = ReadToken(message, "facingAngle") ?? input.FacingAngle;
        input.LookAngle = ReadToken(message, "lookAngle") ?? input.LookAngle;
        input.AimAngle = ReadToken(message, "aimAngle") ?? input.AimAngle;
        input.FaithAmmo = ReadToken(message, "faithAmmo") ?? input.FaithAmmo;
        input.FaithTotal = ReadToken(message, "faithTotal") ?? input.FaithTotal;
        input.FaithCost = ReadToken(message, "faithCost") ?? input.FaithCost;
        input.Position = ReadToken(message, "pos") ?? input.Position;
        input.State = ReadToken(message, "state") ?? input.State;
        input.Scene = ReadToken(message, "scene") ?? input.Scene;
        input.Location = ReadToken(message, "location") ?? input.Location;
        input.Room = ReadToken(message, "room") ?? input.Room;
        input.LastSeen = traceEvent.Timestamp;

        if (ledger.LastPlayerInputDisplay == null || traceEvent.Timestamp - ledger.LastPlayerInputDisplay >= TimeSpan.FromSeconds(5))
        {
            ledger.LastPlayerInputDisplay = traceEvent.Timestamp;
            return new LedgerEvent(traceEvent.Timestamp, "input", BuildInputDisplay(ledger));
        }

        return null;
    }

    private static LedgerEvent? ApplySpellCast(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        int? sequence = ReadIntToken(message, "seq");
        int? playerId = ReadIntToken(message, "playerID");
        string? curse = ReadToken(message, "curse");
        if (sequence == null || playerId == null || string.IsNullOrWhiteSpace(curse))
        {
            return null;
        }

        bool duplicate = ledger.SpellCasts.Any(cast =>
            string.Equals(cast.ClientId, clientId, StringComparison.Ordinal)
            && string.Equals(cast.SessionId, sessionId, StringComparison.Ordinal)
            && cast.Sequence == sequence.Value);
        if (duplicate)
        {
            return null;
        }

        ledger.Clients.TryGetValue(clientId, out ClientLedger? sourceClient);
        SpellCastLedger cast = new(
            traceEvent.Timestamp,
            clientId,
            sessionId,
            sequence.Value,
            playerId.Value,
            curse,
            ReadToken(message, "curseLevel"),
            ReadToken(message, "autoAim"),
            ReadToken(message, "consumeAmmo"),
            ReadToken(message, "wasSpell"),
            ReadToken(message, "damageMultiplier"),
            ReadToken(message, "facingAngle"),
            ReadToken(message, "lookAngle"),
            ReadToken(message, "aimAngle"),
            ReadToken(message, "targetOffset"),
            ReadToken(message, "pos"),
            ReadToken(message, "scene"),
            ReadToken(message, "location"),
            ReadToken(message, "room"));

        ledger.SpellCasts.Add(cast);
        if (ledger.SpellCasts.Count > 256)
        {
            ledger.SpellCasts.RemoveRange(0, ledger.SpellCasts.Count - 256);
        }

        return new LedgerEvent(
            traceEvent.Timestamp,
            "spell",
            PlayerLabel(clientId, playerId)
            + " role=" + (sourceClient?.ServerRole ?? "unknown")
            + " cast=" + curse + "@" + (cast.CurseLevel ?? "unknown")
            + " seq=" + sequence.Value
            + " aim=" + (cast.AimAngle ?? "unknown")
            + " room=" + (cast.Room ?? "unknown"));
    }

    private static LedgerEvent? ApplyPlayerLife(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        int? playerId = ReadIntToken(message, "playerID");
        if (playerId == null)
        {
            return null;
        }

        PlayerLedger player = GetOrCreatePlayer(ledger, clientId, sessionId, playerId, ReadToken(message, "name"));
        player.ClientId = clientId;
        player.SessionId = sessionId;
        player.Name = ReadToken(message, "name") ?? player.Name;
        player.IsLamb = ReadBoolToken(message, "isLamb") ?? player.IsLamb;
        player.Position = ReadToken(message, "pos") ?? player.Position;
        player.State = ReadToken(message, "state") ?? player.State;
        player.HitPoints = ReadToken(message, "hp") ?? player.HitPoints;
        player.KnockedOut = ReadBoolToken(message, "knockedOut") ?? player.KnockedOut;
        player.Active = ReadBoolToken(message, "active") ?? player.Active;
        player.AutoRespawn = ReadBoolToken(message, "autoRespawn") ?? player.AutoRespawn;
        player.PlayersCount = ReadIntToken(message, "playersCount") ?? player.PlayersCount;
        player.LastRunResult = ReadToken(message, "lastRunResult") ?? player.LastRunResult;
        player.LastSeen = traceEvent.Timestamp;

        string signature = player.BuildLifeSignature();
        if (string.Equals(player.LifeSignature, signature, StringComparison.Ordinal))
        {
            return null;
        }

        player.LifeSignature = signature;
        return new LedgerEvent(
            traceEvent.Timestamp,
            "life",
            PlayerLabel(clientId, playerId)
            + " state=" + (player.State ?? "unknown")
            + " hp=" + (player.HitPoints ?? "unknown")
            + " knocked=" + (player.KnockedOut?.ToString() ?? "unknown")
            + " active=" + (player.Active?.ToString() ?? "unknown")
            + " result=" + (player.LastRunResult ?? "unknown")
            + " autoRespawn=" + (player.AutoRespawn?.ToString() ?? "unknown")
            + " scene=" + (ReadToken(message, "scene") ?? "unknown")
            + " loc=" + (ReadToken(message, "location") ?? "unknown"));
    }

    private static LedgerEvent? ApplyCombatRoster(LedgerState ledger, TraceEvent traceEvent, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        string? hash = ReadToken(message, "hash");
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            client = new ClientLedger(clientId)
            {
                FirstSeen = traceEvent.Timestamp,
                JoinOrder = ++ledger.NextClientJoinOrder
            };
            ledger.Clients[clientId] = client;
        }

        client.SessionId = sessionId;
        client.Scene = ReadToken(message, "scene") ?? client.Scene;
        client.Location = ReadToken(message, "location") ?? client.Location;
        client.CombatHash = hash;
        client.CombatCount = ReadIntToken(message, "count") ?? client.CombatCount;
        client.CombatRoom = ReadToken(message, "room") ?? client.CombatRoom;
        client.CombatRounds = ReadToken(message, "rounds") ?? client.CombatRounds;
        client.CombatPreview = ReadToken(message, "preview") ?? client.CombatPreview;
        client.LastSeen = traceEvent.Timestamp;
        client.LastCategory = "sync.combat_roster";

        AssignClientRoles(ledger, traceEvent.Timestamp);
        client.CombatMatch = ComputeCombatMatch(ledger, client);
        UpdateServerWorldClient(ledger, client, traceEvent.Timestamp);

        string signature = client.CombatSignature();
        if (string.Equals(client.LastCombatSignature, signature, StringComparison.Ordinal))
        {
            return null;
        }

        client.LastCombatSignature = signature;
        return new LedgerEvent(
            traceEvent.Timestamp,
            "combat",
            client.ClientId
            + " role=" + (client.ServerRole ?? "unknown")
            + " enemies=" + (client.CombatCount?.ToString() ?? "unknown")
            + " hash=" + (client.CombatHash ?? "unknown")
            + " match=" + (client.CombatMatch?.ToString() ?? "unknown")
            + " room=" + (client.CombatRoom ?? "unknown")
            + " rounds=" + (client.CombatRounds ?? "unknown")
            + " preview=" + (client.CombatPreview ?? "unknown"));
    }

    private static bool? ComputeCombatMatch(LedgerState ledger, ClientLedger client)
    {
        if (string.IsNullOrWhiteSpace(client.CombatHash))
        {
            return null;
        }

        ClientLedger? host = ledger.Clients.Values.FirstOrDefault(candidate => IsAssignedHostClient(candidate));
        if (host == null || string.IsNullOrWhiteSpace(host.CombatHash))
        {
            return null;
        }

        return string.Equals(client.CombatHash, host.CombatHash, StringComparison.Ordinal);
    }

    private static LedgerEvent? ApplyPhase5CoopReservation(LedgerState ledger, TraceEvent traceEvent, string category, string message)
    {
        string clientId = ReadClientId(message);
        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            return null;
        }

        string suffix = category.StartsWith("phase5.coop.", StringComparison.Ordinal)
            ? category["phase5.coop.".Length..]
            : category;
        string signature = suffix
            + "|" + (client.HostP2Wanted?.ToString() ?? "unknown")
            + "|" + (client.HostP2Active?.ToString() ?? "unknown")
            + "|" + (client.HostP2Hold?.ToString() ?? "unknown")
            + "|" + (client.HostP2NoController?.ToString() ?? "unknown")
            + "|" + (client.HostP2State ?? "unknown")
            + "|" + (client.HostP2Rewired ?? "unknown");

        if (string.Equals(client.LastHostP2Signature, signature, StringComparison.Ordinal)
            && (category.EndsWith(".snapshot", StringComparison.Ordinal)
                || category.Contains("rewired_refresh", StringComparison.Ordinal)))
        {
            return null;
        }

        client.LastHostP2Signature = signature;
        return new LedgerEvent(
            traceEvent.Timestamp,
            "p2",
            clientId
            + " " + suffix
            + " wanted=" + (client.HostP2Wanted?.ToString() ?? "unknown")
            + " active=" + (client.HostP2Active?.ToString() ?? "unknown")
            + " hold=" + (client.HostP2Hold?.ToString() ?? "unknown")
            + " noCtl=" + (client.HostP2NoController?.ToString() ?? "unknown")
            + " state=" + (client.HostP2State ?? "unknown"));
    }

    private static LedgerEvent? ApplyEquipmentStackHint(LedgerState ledger, TraceEvent traceEvent, string category, string message)
    {
        string clientId = ReadClientId(message);
        int? playerId = ReadIntToken(message, "playerID");
        string assignmentType = category.Contains("player_weapon", StringComparison.Ordinal) ? "weapon" : "curse";
        string? newValue = ReadToken(message, "new");

        if (playerId == 1
            && !string.Equals(newValue, "None@0", StringComparison.Ordinal)
            && message.Contains("<SpawnCoopPlayer>", StringComparison.Ordinal))
        {
            ledger.AddRuleCandidate("Late-join P2 equipment copy observed: SpawnCoopPlayer assigned " + assignmentType + " " + newValue + " from current P1 state.");
            AuthoritativePlayerLedger authoritativePlayer = GetOrCreateAuthoritativePlayer(ledger, clientId, playerId.Value);
            authoritativePlayer.LastDecision = "late_join_copy";
            authoritativePlayer.LastUpdated = traceEvent.Timestamp;
            if (assignmentType == "weapon")
            {
                authoritativePlayer.Weapon = newValue;
            }
            else
            {
                authoritativePlayer.Curse = newValue;
            }

            ledger.ServerDecisions.Add(ServerDecisionLedger.AssignEquipment(
                traceEvent.Timestamp,
                clientId,
                playerId.Value,
                assignmentType,
                newValue!,
                "late_join_copy_from_p1"));
            return new LedgerEvent(traceEvent.Timestamp, "rule", "late-join " + PlayerLabel(clientId, playerId) + " copy " + assignmentType + "=" + newValue);
        }

        return null;
    }

    private static LedgerEvent? ApplyEquipmentAssignment(LedgerState ledger, TraceEvent traceEvent, string category, string message)
    {
        string clientId = ReadClientId(message);
        string sessionId = ReadSessionId(message);
        int? playerId = ReadIntToken(message, "playerID");
        PlayerLedger player = GetOrCreatePlayer(ledger, clientId, sessionId, playerId, null);
        player.ClientId = clientId;
        player.SessionId = sessionId;
        player.IsLamb = ReadBoolToken(message, "lamb") ?? player.IsLamb;
        player.Weapon = ReadToken(message, "weapon") ?? player.Weapon;
        player.Curse = ReadToken(message, "curse") ?? player.Curse;
        player.HitPoints = ReadToken(message, "hp") ?? player.HitPoints;
        player.State = ReadToken(message, "state") ?? player.State;
        player.Position = ReadToken(message, "pos") ?? player.Position;
        player.LastSeen = traceEvent.Timestamp;

        string assignmentType = category.Contains("player_weapon", StringComparison.Ordinal) ? "weapon" : "curse";
        string? newValue = ReadToken(message, "new");
        bool bridgeApply = ReadBoolToken(message, "bridgeApply") == true;

        if (IsEmptyEquipment(newValue))
        {
            return null;
        }

        ledger.EquipmentEvents.Add(new EquipmentLedgerEvent(traceEvent.Timestamp, clientId, player.PlayerId, assignmentType + "-set", assignmentType, newValue, null, ledger.Scene));
        if (bridgeApply)
        {
            return new LedgerEvent(traceEvent.Timestamp, "equipment", PlayerLabel(clientId, player.PlayerId) + " bridge-applied " + assignmentType + "=" + newValue);
        }

        ApplyObservedEquipmentDecision(ledger, traceEvent.Timestamp, clientId, player.PlayerId, assignmentType, newValue!);
        return new LedgerEvent(traceEvent.Timestamp, "equipment", PlayerLabel(clientId, player.PlayerId) + " " + assignmentType + "=" + newValue);
    }

    private static void ApplyRewardClaimDecision(
        LedgerState ledger,
        DateTimeOffset timestamp,
        string clientId,
        int? playerId,
        string? type,
        string? equipment,
        string? level,
        bool? coopPodium,
        string? podiumPosition)
    {
        if (playerId == null || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(equipment))
        {
            return;
        }

        int rewardIndex = FindGeneratedRewardIndex(ledger, type, equipment, level, coopPodium);
        RewardClaimLedger claim = new(timestamp, clientId, playerId.Value, rewardIndex, type, equipment, level, coopPodium, podiumPosition);
        ledger.RewardClaims.Add(claim);
        ledger.ServerDecisions.Add(ServerDecisionLedger.ClaimReward(timestamp, claim));

        string slot = EquipmentSlotFromRewardType(type);
        string value = FormatEquipment(equipment, level);
        AuthoritativePlayerLedger authoritativePlayer = GetOrCreateAuthoritativePlayer(ledger, clientId, playerId.Value);
        authoritativePlayer.LastUpdated = timestamp;
        authoritativePlayer.LastDecision = "claim_reward";

        if (slot == "weapon")
        {
            authoritativePlayer.Weapon = value;
        }
        else if (slot == "curse")
        {
            authoritativePlayer.Curse = value;
        }

        if (slot is "weapon" or "curse")
        {
            ledger.ServerDecisions.Add(ServerDecisionLedger.AssignEquipment(timestamp, clientId, playerId.Value, slot, value, "reward_claim"));
        }
    }

    private static void ApplyObservedEquipmentDecision(LedgerState ledger, DateTimeOffset timestamp, string clientId, int playerId, string assignmentType, string value)
    {
        AuthoritativePlayerLedger authoritativePlayer = GetOrCreateAuthoritativePlayer(ledger, clientId, playerId);
        authoritativePlayer.LastUpdated = timestamp;
        authoritativePlayer.LastDecision = "observed_equipment_set";

        if (assignmentType == "weapon")
        {
            authoritativePlayer.Weapon = value;
        }
        else if (assignmentType == "curse")
        {
            authoritativePlayer.Curse = value;
        }

        ledger.ServerDecisions.Add(ServerDecisionLedger.AssignEquipment(timestamp, clientId, playerId, assignmentType, value, "observed_game_assignment"));
    }

    private static int FindGeneratedRewardIndex(LedgerState ledger, string? type, string? equipment, string? level, bool? coopPodium)
    {
        for (int i = ledger.GeneratedRewards.Count - 1; i >= 0; i--)
        {
            RewardLedger reward = ledger.GeneratedRewards[i];
            if (string.Equals(reward.Type, type, StringComparison.Ordinal)
                && string.Equals(reward.Equipment, equipment, StringComparison.Ordinal)
                && string.Equals(reward.Level, level, StringComparison.Ordinal)
                && (coopPodium == null || reward.CoopPodium == coopPodium))
            {
                return i;
            }
        }

        return -1;
    }

    private static AuthoritativePlayerLedger GetOrCreateAuthoritativePlayer(LedgerState ledger, string clientId, int playerId)
    {
        string key = PlayerKey(clientId, playerId);
        if (!ledger.AuthoritativePlayers.TryGetValue(key, out AuthoritativePlayerLedger? player))
        {
            player = new AuthoritativePlayerLedger(clientId, playerId);
            ledger.AuthoritativePlayers[key] = player;
        }

        return player;
    }

    private static string EquipmentSlotFromRewardType(string type)
    {
        return type.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
            ? "weapon"
            : type.Equals("Curse", StringComparison.OrdinalIgnoreCase)
                ? "curse"
                : type.ToLowerInvariant();
    }

    private static string FormatEquipment(string equipment, string? level)
    {
        return string.IsNullOrWhiteSpace(level) ? equipment : equipment + "@" + level;
    }

    private static void UpdateCommonContext(LedgerState ledger, string message)
    {
        ledger.Scene = ReadToken(message, "scene") ?? ledger.Scene;
        ledger.Location = ReadToken(message, "location") ?? ledger.Location;
        ledger.Run = ReadIntToken(message, "run") ?? ledger.Run;
        ledger.CurrentRunWeaponLevel = ReadIntToken(message, "currentRunWeaponLevel") ?? ledger.CurrentRunWeaponLevel;
        ledger.CurrentRunCurseLevel = ReadIntToken(message, "currentRunCurseLevel") ?? ledger.CurrentRunCurseLevel;
    }

    private static LedgerEvent? UpdateClientPresence(LedgerState ledger, TraceEvent traceEvent, string category, string message)
    {
        string? clientId = ReadToken(message, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        bool isNewClient = false;
        if (!ledger.Clients.TryGetValue(clientId, out ClientLedger? client))
        {
            client = new ClientLedger(clientId)
            {
                FirstSeen = traceEvent.Timestamp,
                JoinOrder = ++ledger.NextClientJoinOrder
            };
            ledger.Clients[clientId] = client;
            isNewClient = true;
        }

        client.LastSeen = traceEvent.Timestamp;
        client.SessionId = ReadSessionId(message);
        client.PluginVersion = ReadToken(message, "pluginVersion") ?? client.PluginVersion;
        client.Scene = ReadToken(message, "scene") ?? client.Scene;
        client.Location = ReadToken(message, "location") ?? client.Location;
        client.CoopActive = ReadBoolToken(message, "coopActive") ?? client.CoopActive;
        client.PlayersCount = ReadIntToken(message, "playersCount") ?? client.PlayersCount;
        client.HostP2Wanted = ReadBoolToken(message, "p2Wanted") ?? client.HostP2Wanted;
        client.HostP2Active = ReadBoolToken(message, "p2Active") ?? client.HostP2Active;
        client.HostP2Hold = ReadBoolToken(message, "p2Hold") ?? client.HostP2Hold;
        client.HostP2NoController = ReadBoolToken(message, "p2NoController") ?? client.HostP2NoController;
        client.HostP2State = ReadToken(message, "p2State") ?? client.HostP2State;
        client.HostP2Rewired = ReadToken(message, "p2Rewired") ?? client.HostP2Rewired;
        client.LastCategory = category;

        if (category == "bepinex.plugin.loaded" && message.Contains("COTL Online Diagnostics", StringComparison.Ordinal))
        {
            client.PluginVersion = ReadToken(message, "version") ?? client.PluginVersion;
        }

        AssignClientRoles(ledger, traceEvent.Timestamp);

        if (isNewClient)
        {
            return new LedgerEvent(traceEvent.Timestamp, "client", client.ToDisplayString());
        }

        return null;
    }

    private static PlayerLedger GetOrCreatePlayer(LedgerState ledger, string clientId, string sessionId, int? playerId, string? name)
    {
        int id = playerId ?? -1;
        string key = PlayerKey(clientId, id);
        if (!ledger.Players.TryGetValue(key, out PlayerLedger? player))
        {
            player = new PlayerLedger(clientId, id) { Name = name, SessionId = sessionId };
            ledger.Players[key] = player;
        }

        return player;
    }

    private static PlayerMotionLedger GetOrCreatePlayerMotion(LedgerState ledger, string clientId, string sessionId, int playerId)
    {
        string key = PlayerKey(clientId, playerId);
        if (!ledger.PlayerMotion.TryGetValue(key, out PlayerMotionLedger? motion))
        {
            motion = new PlayerMotionLedger(clientId, playerId) { SessionId = sessionId };
            ledger.PlayerMotion[key] = motion;
        }

        return motion;
    }

    private static PlayerInputLedger GetOrCreatePlayerInput(LedgerState ledger, string clientId, string sessionId, int playerId)
    {
        string key = PlayerKey(clientId, playerId);
        if (!ledger.PlayerInputs.TryGetValue(key, out PlayerInputLedger? input))
        {
            input = new PlayerInputLedger(clientId, playerId) { SessionId = sessionId };
            ledger.PlayerInputs[key] = input;
        }

        return input;
    }

    private static string BuildMotionDisplay(LedgerState ledger)
    {
        StringBuilder sb = new();
        sb.Append("samples=").Append(ledger.PlayerMotionSamples);
        foreach (PlayerMotionLedger motion in ledger.PlayerMotion.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            sb.Append(" ").Append(PlayerLabel(motion.ClientId, motion.PlayerId))
                .Append("=").Append(motion.Position ?? "unknown")
                .Append("/").Append(motion.State ?? "unknown");
        }

        return sb.ToString();
    }

    private static string BuildInputDisplay(LedgerState ledger)
    {
        StringBuilder sb = new();
        sb.Append("samples=").Append(ledger.PlayerInputSamples);
        foreach (PlayerInputLedger input in ledger.PlayerInputs.Values.OrderBy(player => player.ClientId).ThenBy(player => player.PlayerId))
        {
            sb.Append(" ")
                .Append(input.ClientId)
                .Append(":p").Append(input.PlayerId)
                .Append(" axis=(").Append(input.AxisX ?? "0")
                .Append(",").Append(input.AxisY ?? "0")
                .Append(")")
                .Append(" atk=").Append(input.AttackDown == true ? "D" : "-").Append(input.AttackHeld == true ? "H" : "-")
                .Append(" dodge=").Append(input.DodgeDown == true ? "D" : "-").Append(input.DodgeHeld == true ? "H" : "-")
                .Append(" curse=").Append(input.CurseDown == true ? "D" : "-").Append(input.CurseHeld == true ? "H" : "-").Append(input.CurseUp == true ? "U" : "-")
                .Append(" heavy=").Append(input.HeavyDown == true ? "D" : "-").Append(input.HeavyHeld == true ? "H" : "-")
                .Append("/").Append(input.State ?? "unknown");
        }

        return sb.ToString();
    }

    private static string ReadClientId(string message)
    {
        return ReadToken(message, "clientId") ?? "local";
    }

    private static string ReadSessionId(string message)
    {
        return ReadToken(message, "sessionId") ?? "unknown";
    }

    private static string PlayerKey(string clientId, int playerId)
    {
        return (string.IsNullOrWhiteSpace(clientId) ? "local" : clientId) + ":p" + playerId;
    }

    private static string PlayerLabel(string clientId, int? playerId)
    {
        return (string.IsNullOrWhiteSpace(clientId) ? "local" : clientId) + ":p" + (playerId?.ToString() ?? "?");
    }

    internal static void AssignClientRoles(LedgerState ledger, DateTimeOffset now)
    {
        List<ClientLedger> activeClients = ledger.Clients.Values
            .Where(client => client.LastSeen == null || now - client.LastSeen <= TimeSpan.FromSeconds(20))
            .OrderBy(client => client.JoinOrder == 0 ? int.MaxValue : client.JoinOrder)
            .ThenBy(client => client.FirstSeen ?? client.LastSeen ?? DateTimeOffset.MaxValue)
            .ThenBy(client => client.ClientId)
            .ToList();

        ClientLedger? hostClient = SelectHostClient(ledger, activeClients);
        int remoteIndex = 0;
        foreach (ClientLedger client in activeClients)
        {
            if (ReferenceEquals(client, hostClient))
            {
                client.ServerRole = "host-lamb";
                client.HostPlayerSlot = 0;
            }
            else if (remoteIndex == 0)
            {
                client.ServerRole = "remote-p2";
                client.HostPlayerSlot = 1;
                remoteIndex++;
            }
            else
            {
                client.ServerRole = "pending";
                client.HostPlayerSlot = null;
                remoteIndex++;
            }
        }

        foreach (ClientLedger inactive in ledger.Clients.Values.Except(activeClients))
        {
            inactive.ServerRole = "stale";
            inactive.HostPlayerSlot = null;
        }
    }

    private static ClientLedger? SelectHostClient(LedgerState ledger, List<ClientLedger> activeClients)
    {
        if (activeClients.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(ledger.PreferredHostClientId))
        {
            ClientLedger? preferred = activeClients.FirstOrDefault(client =>
                string.Equals(client.ClientId, ledger.PreferredHostClientId, StringComparison.Ordinal));
            if (preferred != null)
            {
                return preferred;
            }
        }

        return activeClients[0];
    }

    private static int? CountRoomGraphEntries(string message)
    {
        Match graphMatch = GraphRegex().Match(message);
        if (!graphMatch.Success)
        {
            return null;
        }

        return RoomEntryRegex().Matches(graphMatch.Value).Count;
    }

    private static string? ReadToken(string message, string key)
    {
        Match match = Regex.Match(message, @"(?:^|\s)" + Regex.Escape(key) + @"=(?<value>[^\s\]]+)");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static int? ReadIntToken(string message, string key)
    {
        string? value = ReadToken(message, key);
        return int.TryParse(value, out int result) ? result : null;
    }

    private static bool? ReadBoolToken(string message, string key)
    {
        string? value = ReadToken(message, key);
        return bool.TryParse(value, out bool result) ? result : null;
    }

    private static bool IsEmptyEquipment(string? value)
    {
        return string.IsNullOrEmpty(value)
            || string.Equals(value, "None@0", StringComparison.Ordinal)
            || string.Equals(value, "None", StringComparison.Ordinal);
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...truncated";
    }

    [GeneratedRegex(@"graph=\[[^\]]*\]")]
    private static partial Regex GraphRegex();

    [GeneratedRegex(@"(?:^|\s|\|)\s*\d+:")]
    private static partial Regex RoomEntryRegex();
}

internal sealed record TraceEvent(
    [property: JsonPropertyName("ts")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("message")] string Message);

internal sealed record LedgerEvent(DateTimeOffset Timestamp, string Kind, string Detail)
{
    public string ToDisplayString() => Timestamp.ToLocalTime().ToString("HH:mm:ss") + " [" + Kind + "] " + Detail;
}

internal sealed class LedgerState(string tracePath)
{
    public string TracePath { get; } = tracePath;
    public string? WorldsDirectory { get; set; }
    public int? ServerSaveSlot { get; set; }
    public string? PreferredHostClientId { get; set; }
    public long BytesRead { get; set; }
    public int TotalEvents { get; set; }
    public int MalformedLines { get; set; }
    public DateTimeOffset? LastTimestamp { get; set; }
    public string? PluginVersion { get; set; }
    public string? Scene { get; set; }
    public string? Location { get; set; }
    public int? Run { get; set; }
    public int? DungeonSeed { get; set; }
    public string? DungeonSeedSourceClientId { get; set; }
    public DateTimeOffset? DungeonSeedUpdatedAt { get; set; }
    public int? CurrentRunWeaponLevel { get; set; }
    public int? CurrentRunCurseLevel { get; set; }
    public int? WorldGraphRoomCount { get; set; }
    public string? LastWorldGraphStage { get; set; }
    public string? LastWorldGraphPreview { get; set; }
    public ServerWorldLedger? ServerWorld { get; set; }
    public int NextClientJoinOrder { get; set; }
    public Dictionary<string, ClientLedger> Clients { get; } = [];
    public Dictionary<string, PlayerLedger> Players { get; } = [];
    public int PlayerMotionSamples { get; set; }
    public int? LastPlayerMotionSequence { get; set; }
    public DateTimeOffset? LastPlayerMotionDisplay { get; set; }
    public Dictionary<string, PlayerMotionLedger> PlayerMotion { get; } = [];
    public int PlayerInputSamples { get; set; }
    public int? LastPlayerInputSequence { get; set; }
    public DateTimeOffset? LastPlayerInputDisplay { get; set; }
    public Dictionary<string, PlayerInputLedger> PlayerInputs { get; } = [];
    public List<SpellCastLedger> SpellCasts { get; } = [];
    public Dictionary<string, AuthoritativePlayerLedger> AuthoritativePlayers { get; } = [];
    public List<RewardLedger> GeneratedRewards { get; } = [];
    public List<RewardClaimLedger> RewardClaims { get; } = [];
    public List<EquipmentLedgerEvent> EquipmentEvents { get; } = [];
    public List<ServerDecisionLedger> ServerDecisions { get; } = [];
    public List<string> RuleCandidates { get; } = [];

    public void AddRuleCandidate(string rule)
    {
        if (!RuleCandidates.Contains(rule, StringComparer.Ordinal))
        {
            RuleCandidates.Add(rule);
        }
    }
}

internal sealed class ClientLedger(string clientId)
{
    public string ClientId { get; } = clientId;
    public int JoinOrder { get; set; }
    public string? SessionId { get; set; }
    public string? PluginVersion { get; set; }
    public string? ServerRole { get; set; }
    public int? HostPlayerSlot { get; set; }
    public int? SaveSlot { get; set; }
    public bool? SaveSlotOk { get; set; }
    public string? WorldHash { get; set; }
    public string? RuntimeWorldHash { get; set; }
    public string? SaveFileHash { get; set; }
    public int? WorldSelectedFields { get; set; }
    public bool? WorldMatch { get; set; }
    public int? FollowersCount { get; set; }
    public int? StructuresCount { get; set; }
    public string? CultName { get; set; }
    public string? LastDungeonSeeds { get; set; }
    [JsonIgnore]
    public string LastWorldSignature { get; set; } = "";
    public string? Scene { get; set; }
    public string? Location { get; set; }
    public bool? CoopActive { get; set; }
    public int? PlayersCount { get; set; }
    public bool? HostP2Wanted { get; set; }
    public bool? HostP2Active { get; set; }
    public bool? HostP2Hold { get; set; }
    public bool? HostP2NoController { get; set; }
    public string? HostP2State { get; set; }
    public string? HostP2Rewired { get; set; }
    [JsonIgnore]
    public string LastHostP2Signature { get; set; } = "";
    public string? LastCategory { get; set; }
    public string? RemoteEndPoint { get; set; }
    public DateTimeOffset? FirstSeen { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public DateTimeOffset? LastRosterSent { get; set; }
    public DateTimeOffset? LastRemotePlayersSent { get; set; }
    public DateTimeOffset? LastRemoteInputsSent { get; set; }
    public DateTimeOffset? LastLoadoutsSent { get; set; }
    public DateTimeOffset? LastRunAuthoritySent { get; set; }
    public DateTimeOffset? LastRewardClaimsSent { get; set; }
    public DateTimeOffset? LastSpellCastsSent { get; set; }
    public DateTimeOffset? LastEnemyAuthoritySent { get; set; }
    public string? CultSnapshotHash { get; set; }
    public bool? CultSnapshotMatch { get; set; }
    public int? CultFollowersCount { get; set; }
    public int? CultDataFollowersCount { get; set; }
    public int? CultActiveFollowersCount { get; set; }
    public int? CultStructuresCount { get; set; }
    public string? CultFaith { get; set; }
    public string? CultStaticFaith { get; set; }
    public int? CultDay { get; set; }
    public string? CultElapsed { get; set; }
    public string? CultPreview { get; set; }
    [JsonIgnore]
    public string LastCultSignature { get; set; } = "";
    public string? CombatHash { get; set; }
    public bool? CombatMatch { get; set; }
    public int? CombatCount { get; set; }
    public string? CombatRoom { get; set; }
    public string? CombatRounds { get; set; }
    public string? CombatPreview { get; set; }
    [JsonIgnore]
    public string LastCombatSignature { get; set; } = "";

    public string ToDisplayString()
    {
        return ClientId
            + " session=" + (SessionId ?? "unknown")
            + " role=" + (ServerRole ?? "unknown")
            + " hostSlot=" + (HostPlayerSlot?.ToString() ?? "unknown")
            + " saveSlot=" + (SaveSlot?.ToString() ?? "unknown")
            + " saveSlotOk=" + (SaveSlotOk?.ToString() ?? "unknown")
            + " worldHash=" + (WorldHash ?? "unknown")
            + " runtimeHash=" + (RuntimeWorldHash ?? "unknown")
            + " worldMatch=" + (WorldMatch?.ToString() ?? "unknown")
            + " plugin=" + (PluginVersion ?? "unknown")
            + " scene=" + (Scene ?? "unknown")
            + " location=" + (Location ?? "unknown")
            + " coop=" + (CoopActive?.ToString() ?? "unknown")
            + " players=" + (PlayersCount?.ToString() ?? "unknown")
            + " p2Wanted=" + (HostP2Wanted?.ToString() ?? "unknown")
            + " p2Active=" + (HostP2Active?.ToString() ?? "unknown")
            + " p2Hold=" + (HostP2Hold?.ToString() ?? "unknown")
            + " p2NoController=" + (HostP2NoController?.ToString() ?? "unknown")
            + " p2State=" + (HostP2State ?? "unknown")
            + " cultHash=" + (CultSnapshotHash ?? "unknown")
            + " cultMatch=" + (CultSnapshotMatch?.ToString() ?? "unknown")
            + " cultFollowers=" + (CultFollowersCount?.ToString() ?? "unknown")
            + " combatHash=" + (CombatHash ?? "unknown")
            + " combatMatch=" + (CombatMatch?.ToString() ?? "unknown")
            + " endpoint=" + (RemoteEndPoint ?? "unknown")
            + " last=" + (LastCategory ?? "unknown");
    }

    public string WorldSignature()
    {
        return (SaveSlot?.ToString() ?? "unknown")
            + "|" + (WorldHash ?? "unknown")
            + "|" + (RuntimeWorldHash ?? "unknown")
            + "|" + (SaveFileHash ?? "unknown")
            + "|" + (WorldMatch?.ToString() ?? "unknown")
            + "|" + (SaveSlotOk?.ToString() ?? "unknown")
            + "|" + (FollowersCount?.ToString() ?? "unknown")
            + "|" + (StructuresCount?.ToString() ?? "unknown")
            + "|" + (CultName ?? "unknown")
            + "|" + (LastDungeonSeeds ?? "unknown");
    }

    public string CultSignature()
    {
        return (CultSnapshotHash ?? "unknown")
            + "|" + (CultSnapshotMatch?.ToString() ?? "unknown")
            + "|" + (CultFollowersCount?.ToString() ?? "unknown")
            + "|" + (CultActiveFollowersCount?.ToString() ?? "unknown")
            + "|" + (CultFaith ?? "unknown")
            + "|" + (CultDay?.ToString() ?? "unknown")
            + "|" + (CultPreview ?? "unknown");
    }

    public string CombatSignature()
    {
        return (CombatHash ?? "unknown")
            + "|" + (CombatMatch?.ToString() ?? "unknown")
            + "|" + (CombatCount?.ToString() ?? "unknown")
            + "|" + (CombatRoom ?? "unknown")
            + "|" + (CombatRounds ?? "unknown")
            + "|" + (CombatPreview ?? "unknown");
    }
}

internal sealed class ServerWorldLedger
{
    public string? WorldId { get; set; }
    public string? HostClientId { get; set; }
    public string? BaselineHash { get; set; }
    public int? BaselineSaveSlot { get; set; }
    public int? TargetSaveSlot { get; set; }
    public int? BaselineSelectedFields { get; set; }
    public int BaselineRevisions { get; set; }
    public string? BaselineCultSnapshotHash { get; set; }
    public int? BaselineCultFollowers { get; set; }
    public int? BaselineCultActiveFollowers { get; set; }
    public string? BaselineCultFaith { get; set; }
    public string? BaselineCultPreview { get; set; }
    public int BaselineCultRevisions { get; set; }
    public string? SaveRedirectDryRunPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public Dictionary<string, ServerWorldClientLedger> Clients { get; } = [];
}

internal sealed class ServerWorldClientLedger
{
    public string? ClientId { get; set; }
    public string? SessionId { get; set; }
    public string? Role { get; set; }
    public int? SaveSlot { get; set; }
    public bool? SaveSlotOk { get; set; }
    public string? WorldHash { get; set; }
    public string? RuntimeWorldHash { get; set; }
    public string? SaveFileHash { get; set; }
    public bool? WorldMatch { get; set; }
    public int? FollowersCount { get; set; }
    public int? StructuresCount { get; set; }
    public string? CultName { get; set; }
    public string? LastDungeonSeeds { get; set; }
    public bool? HostP2Wanted { get; set; }
    public bool? HostP2Active { get; set; }
    public bool? HostP2Hold { get; set; }
    public bool? HostP2NoController { get; set; }
    public string? HostP2State { get; set; }
    public string? HostP2Rewired { get; set; }
    public string? CultSnapshotHash { get; set; }
    public bool? CultSnapshotMatch { get; set; }
    public int? CultFollowersCount { get; set; }
    public int? CultDataFollowersCount { get; set; }
    public int? CultActiveFollowersCount { get; set; }
    public int? CultStructuresCount { get; set; }
    public string? CultFaith { get; set; }
    public string? CultStaticFaith { get; set; }
    public int? CultDay { get; set; }
    public string? CultElapsed { get; set; }
    public string? CultPreview { get; set; }
    public string? CombatHash { get; set; }
    public bool? CombatMatch { get; set; }
    public int? CombatCount { get; set; }
    public string? CombatRoom { get; set; }
    public string? CombatRounds { get; set; }
    public string? CombatPreview { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

internal sealed class PlayerLedger(string clientId, int playerId)
{
    public string ClientId { get; set; } = clientId;
    public string? SessionId { get; set; }
    public int PlayerId { get; } = playerId;
    public string? Name { get; set; }
    public bool? IsLamb { get; set; }
    public string? Weapon { get; set; }
    public string? Curse { get; set; }
    public string? Relic { get; set; }
    public string? HitPoints { get; set; }
    public string? Position { get; set; }
    public string? State { get; set; }
    public bool? KnockedOut { get; set; }
    public bool? Active { get; set; }
    public bool? AutoRespawn { get; set; }
    public int? PlayersCount { get; set; }
    public string? LastRunResult { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public string LiveSignature { get; set; } = "";
    public string LifeSignature { get; set; } = "";

    public string BuildLiveSignature()
    {
        return (Name ?? "")
            + "|" + (Weapon ?? "")
            + "|" + (Curse ?? "")
            + "|" + (HitPoints ?? "")
            + "|" + (State ?? "");
    }

    public string BuildLifeSignature()
    {
        return (Name ?? "")
            + "|" + (HitPoints ?? "")
            + "|" + (State ?? "")
            + "|" + (KnockedOut?.ToString() ?? "")
            + "|" + (Active?.ToString() ?? "")
            + "|" + (AutoRespawn?.ToString() ?? "")
            + "|" + (LastRunResult ?? "");
    }

    public string ToDisplayString()
    {
        return "p" + PlayerId
            + " client=" + ClientId
            + " name=" + (Name ?? "unknown")
            + " lamb=" + (IsLamb?.ToString() ?? "unknown")
            + " weapon=" + (Weapon ?? "unknown")
            + " curse=" + (Curse ?? "unknown")
            + " hp=" + (HitPoints ?? "unknown")
            + " state=" + (State ?? "unknown");
    }
}

internal sealed class PlayerMotionLedger(string clientId, int playerId)
{
    public string ClientId { get; set; } = clientId;
    public string? SessionId { get; set; }
    public int PlayerId { get; } = playerId;
    public int? Sequence { get; set; }
    public string? Name { get; set; }
    public string? Position { get; set; }
    public string? State { get; set; }
    public string? HitPoints { get; set; }
    public string? Scene { get; set; }
    public string? Location { get; set; }
    public string? Room { get; set; }
    public DateTimeOffset? LastSeen { get; set; }

    public string ToDisplayString()
    {
        return "p" + PlayerId
            + " client=" + ClientId
            + " seq=" + (Sequence?.ToString() ?? "unknown")
            + " name=" + (Name ?? "unknown")
            + " pos=" + (Position ?? "unknown")
            + " state=" + (State ?? "unknown")
            + " hp=" + (HitPoints ?? "unknown")
            + " scene=" + (Scene ?? "unknown")
            + " room=" + (Room ?? "unknown");
    }
}

internal sealed class PlayerInputLedger(string clientId, int playerId)
{
    public string ClientId { get; set; } = clientId;
    public string? SessionId { get; set; }
    public int PlayerId { get; } = playerId;
    public int? Sequence { get; set; }
    public string? AxisX { get; set; }
    public string? AxisY { get; set; }
    public bool? AttackDown { get; set; }
    public bool? AttackHeld { get; set; }
    public bool? AttackUp { get; set; }
    public bool? DodgeDown { get; set; }
    public bool? DodgeHeld { get; set; }
    public bool? CurseDown { get; set; }
    public bool? CurseHeld { get; set; }
    public bool? CurseUp { get; set; }
    public bool? HeavyDown { get; set; }
    public bool? HeavyHeld { get; set; }
    public string? FacingAngle { get; set; }
    public string? LookAngle { get; set; }
    public string? AimAngle { get; set; }
    public string? FaithAmmo { get; set; }
    public string? FaithTotal { get; set; }
    public string? FaithCost { get; set; }
    public string? Position { get; set; }
    public string? State { get; set; }
    public string? Scene { get; set; }
    public string? Location { get; set; }
    public string? Room { get; set; }
    public DateTimeOffset? LastSeen { get; set; }

    public string ToDisplayString()
    {
        return "p" + PlayerId
            + " client=" + ClientId
            + " seq=" + (Sequence?.ToString() ?? "unknown")
            + " axis=(" + (AxisX ?? "0") + "," + (AxisY ?? "0") + ")"
            + " attack=" + BoolPair(AttackDown, AttackHeld)
            + " dodge=" + BoolPair(DodgeDown, DodgeHeld)
            + " curse=" + BoolTriple(CurseDown, CurseHeld, CurseUp)
            + " heavy=" + BoolPair(HeavyDown, HeavyHeld)
            + " facing=" + (FacingAngle ?? "unknown")
            + " aim=" + (AimAngle ?? "unknown")
            + " faith=" + (FaithAmmo ?? "unknown") + "/" + (FaithTotal ?? "unknown")
            + " pos=" + (Position ?? "unknown")
            + " state=" + (State ?? "unknown")
            + " scene=" + (Scene ?? "unknown")
            + " room=" + (Room ?? "unknown");
    }

    private static string BoolPair(bool? down, bool? held)
    {
        return (down == true ? "D" : "-") + (held == true ? "H" : "-");
    }

    private static string BoolTriple(bool? down, bool? held, bool? up)
    {
        return (down == true ? "D" : "-") + (held == true ? "H" : "-") + (up == true ? "U" : "-");
    }
}

internal sealed class AuthoritativePlayerLedger(string clientId, int playerId)
{
    public string ClientId { get; } = clientId;
    public int PlayerId { get; } = playerId;
    public string? Weapon { get; set; }
    public string? Curse { get; set; }
    public DateTimeOffset? LastUpdated { get; set; }
    public string? LastDecision { get; set; }

    public string ToDisplayString()
    {
        return "p" + PlayerId
            + " client=" + ClientId
            + " weapon=" + (Weapon ?? "unknown")
            + " curse=" + (Curse ?? "unknown")
            + " decision=" + (LastDecision ?? "unknown");
    }
}

internal sealed record SpellCastLedger(
    DateTimeOffset Timestamp,
    string ClientId,
    string SessionId,
    int Sequence,
    int PlayerId,
    string Curse,
    string? CurseLevel,
    string? AutoAim,
    string? ConsumeAmmo,
    string? WasSpell,
    string? DamageMultiplier,
    string? FacingAngle,
    string? LookAngle,
    string? AimAngle,
    string? TargetOffset,
    string? Position,
    string? Scene,
    string? Location,
    string? Room);

internal sealed record RewardLedger(
    DateTimeOffset Timestamp,
    string SourceClientId,
    string? SourceRole,
    int? Run,
    string? Location,
    string? Type,
    string? Equipment,
    string? Level,
    bool? CoopPodium,
    bool? DestroyOtherInCoop)
{
    public string ToDisplayString()
    {
        return Timestamp.ToLocalTime().ToString("HH:mm:ss")
            + " source=" + SourceClientId
            + " role=" + (SourceRole ?? "unknown")
            + " run=" + (Run?.ToString() ?? "unknown")
            + " location=" + (Location ?? "unknown")
            + " type=" + (Type ?? "unknown")
            + " equipment=" + (Equipment ?? "unknown")
            + "@" + (Level ?? "unknown")
            + " coopPodium=" + (CoopPodium?.ToString() ?? "unknown")
            + " destroyOtherInCoop=" + (DestroyOtherInCoop?.ToString() ?? "unknown");
    }
}

internal sealed record EquipmentLedgerEvent(
    DateTimeOffset Timestamp,
    string ClientId,
    int? PlayerId,
    string Source,
    string? Type,
    string? Equipment,
    string? Level,
    string? Scene)
{
    public string ToDisplayString()
    {
        return Timestamp.ToLocalTime().ToString("HH:mm:ss")
            + " " + ClientId + ":p" + (PlayerId?.ToString() ?? "?")
            + " " + Source
            + " " + (Type ?? "equipment")
            + "=" + (Equipment ?? "unknown")
            + (Level != null ? "@" + Level : "")
            + " scene=" + (Scene ?? "unknown");
    }
}

internal sealed record RewardClaimLedger(
    DateTimeOffset Timestamp,
    string ClientId,
    int PlayerId,
    int RewardIndex,
    string? Type,
    string? Equipment,
    string? Level,
    bool? CoopPodium,
    string? PodiumPosition)
{
    public string ToDisplayString()
    {
        return Timestamp.ToLocalTime().ToString("HH:mm:ss")
            + " " + ClientId + ":p" + PlayerId
            + " reward#" + RewardIndex
            + " " + (Type ?? "equipment")
            + "=" + (Equipment ?? "unknown")
            + (Level != null ? "@" + Level : "")
            + " coopPodium=" + (CoopPodium?.ToString() ?? "unknown")
            + " podiumPos=" + (PodiumPosition ?? "unknown");
    }
}

internal sealed record ServerDecisionLedger(
    DateTimeOffset Timestamp,
    string Command,
    string? ClientId,
    int? PlayerId,
    int? RewardIndex,
    string? Slot,
    string? Value,
    string Reason)
{
    public static ServerDecisionLedger GeneratedReward(DateTimeOffset timestamp, int rewardIndex, RewardLedger reward)
    {
        return new ServerDecisionLedger(
            timestamp,
            "register_reward",
            null,
            null,
            rewardIndex,
            reward.Type,
            (reward.Equipment ?? "unknown") + "@" + (reward.Level ?? "unknown"),
            "game_generated_reward");
    }

    public static ServerDecisionLedger ClaimReward(DateTimeOffset timestamp, RewardClaimLedger claim)
    {
        return new ServerDecisionLedger(
            timestamp,
            "claim_reward",
            claim.ClientId,
            claim.PlayerId,
            claim.RewardIndex,
            claim.Type,
            (claim.Equipment ?? "unknown") + "@" + (claim.Level ?? "unknown"),
            "owner_is_interacting_player");
    }

    public static ServerDecisionLedger AssignEquipment(DateTimeOffset timestamp, string clientId, int playerId, string slot, string value, string reason)
    {
        return new ServerDecisionLedger(timestamp, "assign_equipment", clientId, playerId, null, slot, value, reason);
    }

    public string ToDisplayString()
    {
        return Timestamp.ToLocalTime().ToString("HH:mm:ss")
            + " " + Command
            + (PlayerId != null ? " " + (ClientId ?? "local") + ":p" + PlayerId : "")
            + (RewardIndex != null ? " reward#" + RewardIndex : "")
            + (Slot != null ? " " + Slot : "")
            + (Value != null ? "=" + Value : "")
            + " reason=" + Reason;
    }
}
