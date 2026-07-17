param(
    [Parameter(Mandatory = $true)]
    [string] $Name
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$stage = Join-Path $env:TEMP ($Name + "-" + [Guid]::NewGuid().ToString("N"))
$zip = Join-Path $root ("releases\" + $Name + ".zip")
$sha = Join-Path $root ("releases\" + $Name + ".sha256.txt")

New-Item -ItemType Directory -Path $stage | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage "BepInEx\plugins") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage "server") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage "docs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage "scripts") -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.Diagnostics\bin\Release\net481\COTLOnline.Diagnostics.dll") -Destination (Join-Path $stage "BepInEx\plugins\COTLOnline.Diagnostics.dll")
Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.Diagnostics\bin\Release\net481\COTLOnline.Diagnostics.pdb") -Destination (Join-Path $stage "BepInEx\plugins\COTLOnline.Diagnostics.pdb")

Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.dll") -Destination (Join-Path $stage "server\COTLOnline.ServerLedger.dll")
Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.pdb") -Destination (Join-Path $stage "server\COTLOnline.ServerLedger.pdb")
Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.deps.json") -Destination (Join-Path $stage "server\COTLOnline.ServerLedger.deps.json")
Copy-Item -LiteralPath (Join-Path $root "src\COTLOnline.ServerLedger\bin\Release\net10.0\COTLOnline.ServerLedger.runtimeconfig.json") -Destination (Join-Path $stage "server\COTLOnline.ServerLedger.runtimeconfig.json")

Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $stage "README.md")
Copy-Item -LiteralPath (Join-Path $root "CREDITS.md") -Destination (Join-Path $stage "CREDITS.md")

Copy-Item -LiteralPath (Join-Path $root "docs\HEADLESS_FEASIBILITY.md") -Destination (Join-Path $stage "docs\HEADLESS_FEASIBILITY.md")
Copy-Item -LiteralPath (Join-Path $root "docs\LAN_CLIENT_SETUP.md") -Destination (Join-Path $stage "docs\LAN_CLIENT_SETUP.md")
Copy-Item -LiteralPath (Join-Path $root "docs\ONLINE_FINDINGS.md") -Destination (Join-Path $stage "docs\ONLINE_FINDINGS.md")
Copy-Item -LiteralPath (Join-Path $root "docs\PROJECT_STATUS.md") -Destination (Join-Path $stage "docs\PROJECT_STATUS.md")
Copy-Item -LiteralPath (Join-Path $root "docs\RELEASE_PACKAGE_USAGE.md") -Destination (Join-Path $stage "docs\RELEASE_PACKAGE_USAGE.md")
Copy-Item -LiteralPath (Join-Path $root "docs\SOL_AUTHORITY_AUDIT.md") -Destination (Join-Path $stage "docs\SOL_AUTHORITY_AUDIT.md")
Copy-Item -LiteralPath (Join-Path $root "docs\EXTERNAL_COTLMP_REVIEW.md") -Destination (Join-Path $stage "docs\EXTERNAL_COTLMP_REVIEW.md")

Copy-Item -LiteralPath (Join-Path $root "scripts\Test-SpellRelay.ps1") -Destination (Join-Path $stage "scripts\Test-SpellRelay.ps1")

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

if (Test-Path -LiteralPath $sha) {
    Remove-Item -LiteralPath $sha -Force
}

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
($hash.Hash.ToLowerInvariant() + " *" + (Split-Path -Leaf $zip)) | Set-Content -LiteralPath $sha -Encoding ASCII

Write-Output ("Package: " + $zip)
Write-Output ("SHA256: " + $hash.Hash.ToLowerInvariant())
Write-Output ("Stage: " + $stage)
