param(
    [int]$Port = 38633
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root 'src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj'
$serverDll = Join-Path $root 'src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.dll'
$artifactRoot = Join-Path $root 'artifacts\follower-authority-smoke'
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
    '--host-client-id', 'client-follow-host',
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

    Send-TraceEvent $hostClient 'live.heartbeat' 'clientId=client-follow-host sessionId=host-session pluginVersion=0.5.36 scene=Base_Biome_1 location=Base coop=True players=1'
    Send-TraceEvent $remoteClient 'live.heartbeat' 'clientId=client-follow-remote sessionId=remote-session pluginVersion=0.5.36 scene=Base_Biome_1 location=Base coop=True players=1'
    [void](Receive-Category $hostClient 'server.roster' 1200)
    [void](Receive-Category $remoteClient 'server.roster' 1200)

    $hostCatalog = '1|name=Amdusias|loc=Base|home=Base|task=Worship|state=Walking|pos=(1.25,2.5,0)|faith=10|happy=9|sat=8|age=30|curse=None|active=True;7|name=Barbatos|loc=Base|home=Base|task=Farm|state=Working|pos=(-3,4.5,0)|faith=7|happy=6|sat=5|age=44|curse=None|active=True'
    $remoteCatalog = '1|name=Amdusias|loc=Base|home=Base|task=None|state=Idle|pos=(99,99,0)|faith=10|happy=9|sat=8|age=30|curse=None|active=True'
    Send-TraceEvent $hostClient 'sync.follower_catalog' "clientId=client-follow-host sessionId=host-session seq=1 scene=Base_Biome_1 location=Base role=host-lamb worldId=smoke saveSlot=4 hash=hosthash followers=2 dataFollowers=2 activeFollowers=2 structures=1 cultFaith=10 day=1 catalog=$hostCatalog"
    Send-TraceEvent $remoteClient 'sync.follower_catalog' "clientId=client-follow-remote sessionId=remote-session seq=1 scene=Base_Biome_1 location=Base role=remote-p2 worldId=smoke saveSlot=4 hash=remotehash followers=1 dataFollowers=1 activeFollowers=1 structures=1 cultFaith=10 day=1 catalog=$remoteCatalog"

    $authorityPacket = $null
    for ($attempt = 0; $attempt -lt 12 -and $null -eq $authorityPacket; $attempt++) {
        Send-TraceEvent $remoteClient 'live.heartbeat' "clientId=client-follow-remote sessionId=remote-session pluginVersion=0.5.36 scene=Base_Biome_1 location=Base seq=$attempt"
        $candidate = Receive-Category $remoteClient 'server.follower_authority' 500
        if ($null -ne $candidate `
            -and $candidate.message -match 'host=client-follow-host' `
            -and $candidate.message -match 'hash=hosthash' `
            -and $candidate.message -match 'targetHash=remotehash' `
            -and $candidate.message -match 'targetMatch=False' `
            -and $candidate.message -match 'followers=1\|name=Amdusias' `
            -and $candidate.message -match '7\|name=Barbatos') {
            $authorityPacket = $candidate
        }
        Start-Sleep -Milliseconds 60
    }

    if ($null -eq $authorityPacket) {
        throw 'Remote client did not receive the expected server.follower_authority host catalog.'
    }

    $result = [PSCustomObject]@{
        Result = 'PASS'
        RelayMessage = $authorityPacket.message
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
