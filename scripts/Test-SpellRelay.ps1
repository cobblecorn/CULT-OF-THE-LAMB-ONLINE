param(
    [int]$Port = 38631
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root 'src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj'
$serverDll = Join-Path $root 'src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.dll'
$artifactRoot = Join-Path $root 'artifacts\spell-relay-smoke'
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

$arguments = @(
    ('"' + $serverDll + '"'),
    '--listen-udp',
    '--udp-port', $Port,
    '--host-client-id', 'client-sol-host',
    '--worlds-dir', ('"' + $worlds + '"')
)

$server = Start-Process -FilePath 'dotnet.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$hostClient = $null
$remoteClient = $null

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

    Send-TraceEvent $hostClient 'live.heartbeat' 'clientId=client-sol-host sessionId=host-session pluginVersion=0.5.33 scene=Dungeon2 location=Dungeon1_2 room=Room_A'
    Send-TraceEvent $remoteClient 'live.heartbeat' 'clientId=client-sol-remote sessionId=remote-session pluginVersion=0.5.33 scene=Dungeon2 location=Dungeon1_2 room=Room_A'
    [void](Receive-Category $hostClient 'server.roster' 1200)
    [void](Receive-Category $remoteClient 'server.roster' 1200)

    Send-TraceEvent $hostClient 'sync.spell_cast' 'clientId=client-sol-host sessionId=host-session seq=7 playerID=0 curse=Fireball curseLevel=4 autoAim=False damageMultiplier=1 facingAngle=90 lookAngle=90 aimAngle=90 targetOffset=(0,5,0) pos=(1,2,0) scene=Dungeon2 location=Dungeon1_2 room=Room_A'

    $spellPacket = $null
    for ($attempt = 0; $attempt -lt 12 -and $null -eq $spellPacket; $attempt++) {
        Send-TraceEvent $remoteClient 'sync.player_input' "clientId=client-sol-remote sessionId=remote-session seq=$($attempt + 9) playerID=0 ax=0 ay=0 scene=Dungeon2 location=Dungeon1_2 room=Room_A"
        $candidate = Receive-Category $remoteClient 'server.spell_casts' 400
        if ($null -ne $candidate `
            -and $candidate.message -match 'castCount=1' `
            -and $candidate.message -match 'source=client-sol-host' `
            -and $candidate.message -match 'seq=7' `
            -and $candidate.message -match 'curse=Fireball') {
            $spellPacket = $candidate
        }
        Start-Sleep -Milliseconds 60
    }
    if ($null -eq $spellPacket) {
        throw 'Remote client did not receive the expected host Fireball cast through server.spell_casts.'
    }

    Send-TraceEvent $remoteClient 'sync.spell_cast' 'clientId=client-sol-remote sessionId=remote-session seq=11 playerID=0 curse=MegaSlash curseLevel=3 autoAim=False damageMultiplier=1 facingAngle=180 lookAngle=180 aimAngle=180 targetOffset=(-4,0,0) pos=(3,2,0) scene=Dungeon2 location=Dungeon1_2 room=Room_A'

    $returnPacket = $null
    for ($attempt = 0; $attempt -lt 12 -and $null -eq $returnPacket; $attempt++) {
        Send-TraceEvent $hostClient 'sync.player_input' "clientId=client-sol-host sessionId=host-session seq=$($attempt + 30) playerID=0 ax=0 ay=0 scene=Dungeon2 location=Dungeon1_2 room=Room_A"
        $candidate = Receive-Category $hostClient 'server.spell_casts' 400
        if ($null -ne $candidate `
            -and $candidate.message -match 'castCount=1' `
            -and $candidate.message -match 'source=client-sol-remote' `
            -and $candidate.message -match 'role=remote-p2' `
            -and $candidate.message -match 'seq=11' `
            -and $candidate.message -match 'curse=MegaSlash') {
            $returnPacket = $candidate
        }
        Start-Sleep -Milliseconds 60
    }
    if ($null -eq $returnPacket) {
        throw 'Host client did not receive the expected remote P2 MegaSlash cast through server.spell_casts.'
    }

    [PSCustomObject]@{
        Result = 'PASS'
        HostToRemote = $spellPacket.message
        RemoteToHost = $returnPacket.message
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
