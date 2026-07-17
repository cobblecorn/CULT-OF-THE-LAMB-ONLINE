param(
    [int]$Port = 38632
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root 'src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj'
$serverDll = Join-Path $root 'src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.dll'
$artifactRoot = Join-Path $root 'artifacts\save-authority-smoke'
$worlds = Join-Path $artifactRoot 'worlds'
$stdout = Join-Path $artifactRoot 'server.out.log'
$stderr = Join-Path $artifactRoot 'server.err.log'

New-Item -ItemType Directory -Force -Path $artifactRoot, $worlds | Out-Null
if (-not (Test-Path -LiteralPath $serverDll)) {
    dotnet build $serverProject -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "ServerLedger build failed with exit code $LASTEXITCODE"
    }
}

Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue

$arguments = @(
    ('"' + $serverDll + '"'),
    '--listen-udp',
    '--udp-port', $Port,
    '--host-client-id', 'client-save-host',
    '--worlds-dir', ('"' + $worlds + '"'),
    '--server-save-slot', '4'
)

$server = Start-Process -FilePath 'dotnet.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$hostClient = $null
$remoteClient = $null
$result = $null

function Send-TraceEvent {
    param(
        [System.Net.Sockets.UdpClient]$Client,
        [string]$Category,
        [string]$Message
    )

    $payload = [ordered]@{
        ts = [DateTimeOffset]::UtcNow.ToString('O')
        category = $Category
        message = $Message
    } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    [void]$Client.Send($bytes, $bytes.Length)
}

function Receive-Category {
    param(
        [System.Net.Sockets.UdpClient]$Client,
        [string]$Category,
        [int]$TimeoutMs
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        $Client.Client.ReceiveTimeout = [Math]::Max(100, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        try {
            $endpoint = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
            $bytes = $Client.Receive([ref]$endpoint)
            $packet = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
            if ($packet.category -eq $Category) {
                return $packet
            }
        }
        catch [System.Net.Sockets.SocketException] {
            break
        }
    }
    return $null
}

try {
    Start-Sleep -Milliseconds 700
    if ($server.HasExited) {
        throw "ServerLedger exited early. See $stderr"
    }

    $hostClient = [System.Net.Sockets.UdpClient]::new()
    $remoteClient = [System.Net.Sockets.UdpClient]::new()
    $hostClient.Connect('127.0.0.1', $Port)
    $remoteClient.Connect('127.0.0.1', $Port)

    Send-TraceEvent $hostClient 'live.heartbeat' 'clientId=client-save-host sessionId=host-session pluginVersion=0.5.35 scene=Base location=Base coop=True players=1'
    Send-TraceEvent $remoteClient 'live.heartbeat' 'clientId=client-save-remote sessionId=remote-session pluginVersion=0.5.35 scene=Base location=Base coop=True players=1'
    [void](Receive-Category $hostClient 'server.roster' 1200)
    [void](Receive-Category $remoteClient 'server.roster' 1200)

    $snapshot = 'smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
    $raw = [Text.Encoding]::UTF8.GetBytes('cotlonline-save-authority-smoke')
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = -join ($sha.ComputeHash($raw) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
    $data = [Convert]::ToBase64String($raw)

    Send-TraceEvent $hostClient 'sync.save_chunk' "clientId=client-save-host sessionId=host-session snapshot=$snapshot reason=smoke sourceSlot=4 targetSlot=4 chunk=0 chunks=1 rawBytes=$($raw.Length) compressedBytes=$($raw.Length) files=1 hash=$hash data=$data"

    $savePacket = $null
    for ($attempt = 0; $attempt -lt 12 -and $null -eq $savePacket; $attempt++) {
        Send-TraceEvent $remoteClient 'live.heartbeat' "clientId=client-save-remote sessionId=remote-session pluginVersion=0.5.35 scene=Base location=Base seq=$attempt"
        $candidate = Receive-Category $remoteClient 'server.save_chunk' 500
        if ($null -ne $candidate `
            -and $candidate.message -match "snapshot=$snapshot" `
            -and $candidate.message -match 'chunk=0' `
            -and $candidate.message -match 'chunks=1' `
            -and $candidate.message -match "hash=$hash" `
            -and $candidate.message -match "data=$([Regex]::Escape($data))") {
            $savePacket = $candidate
        }
        Start-Sleep -Milliseconds 60
    }

    if ($null -eq $savePacket) {
        throw 'Remote client did not receive the expected server.save_chunk snapshot.'
    }

    Send-TraceEvent $remoteClient 'sync.save_ack' "clientId=client-save-remote sessionId=remote-session snapshot=$snapshot status=applied targetSlot=4"
    Start-Sleep -Milliseconds 300

    $latest = Join-Path $worlds 'pending-world\save\latest_save_snapshot.json'
    if (-not (Test-Path -LiteralPath $latest)) {
        throw "Server did not persist latest_save_snapshot.json at $latest"
    }

    $metadata = Get-Content -LiteralPath $latest -Raw | ConvertFrom-Json
    if ($metadata.SnapshotId -ne $snapshot) {
        throw "Persisted snapshot id was $($metadata.SnapshotId), expected $snapshot"
    }

    $result = [PSCustomObject]@{
        Result = 'PASS'
        Snapshot = $snapshot
        Hash = $hash
        RelayMessage = $savePacket.message
        Metadata = $latest
    }
}
finally {
    if ($null -ne $hostClient) { $hostClient.Dispose() }
    if ($null -ne $remoteClient) { $remoteClient.Dispose() }
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        [void]$server.WaitForExit(2000)
    }
}

if ($null -ne $result) {
    $result
}
