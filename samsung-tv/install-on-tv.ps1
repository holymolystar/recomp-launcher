# Puts Recomp TV on a Samsung TV (over your home network).
#   powershell -ExecutionPolicy Bypass -File samsung-tv\install-on-tv.ps1 -TvIp 192.168.1.50
# Needs, once (see README.md): Tizen Studio with the TV extensions, the TV in Developer Mode with this PC's address,
# and a Samsung certificate profile made in Tizen Studio's Certificate Manager (named RecompTV unless you pass -Profile).
param(
    [Parameter(Mandatory = $true)][string]$TvIp,
    [string]$Profile = "RecompTV",
    [string]$TizenStudio = ""
)
$ErrorActionPreference = "Stop"

# Tizen Studio: where it usually is
$roots = @(@($TizenStudio, "C:\tizen-studio", "$env:USERPROFILE\tizen-studio", "D:\tizen-studio") | Where-Object { $_ -and (Test-Path $_) })
if (-not $roots) { throw "Tizen Studio wasn't found. Install it (see samsung-tv\README.md), or pass -TizenStudio <its folder>." }
$root = $roots[0]
$tizen = Join-Path $root "tools\ide\bin\tizen.bat"
$sdb = Join-Path $root "tools\sdb.exe"
if (-not (Test-Path $tizen) -or -not (Test-Path $sdb)) { throw "Tizen Studio at $root is missing its command-line tools (tools\ide\bin\tizen.bat, tools\sdb.exe)." }

$app = Join-Path $PSScriptRoot "app"
$build = Join-Path $env:TEMP "recomp-tv-build"
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
Copy-Item $app $build -Recurse

Write-Host "Connecting to the TV at $TvIp..."
& $sdb connect $TvIp | Write-Host
$serial = "${TvIp}:26101"
$devices = & $sdb devices
if (-not ($devices -match [regex]::Escape($TvIp))) { throw "The TV didn't answer. Is it on, in Developer Mode, with this PC's address set (Apps, then 1 2 3 4 5 on the remote)?" }

Write-Host "Packing the app (signed with the '$Profile' certificate)..."
& $tizen package -t wgt -s $Profile -- $build
if ($LASTEXITCODE -ne 0) { throw "Packing failed. Is there a certificate profile called '$Profile' in Tizen Studio's Certificate Manager?" }
$wgt = Get-ChildItem $build -Filter *.wgt | Select-Object -First 1
if (-not $wgt) { throw "Packing made no .wgt file." }
# the TV's installer stops at a space in the name ("Recomp TV.wgt")
if ($wgt.Name -match '\s') { $wgt = Rename-Item $wgt.FullName ($wgt.Name -replace '\s', '') -PassThru }

Write-Host "Installing on the TV..."
& $tizen install -n $wgt.Name -s $serial -- $build
if ($LASTEXITCODE -ne 0) { throw "Installing failed. A Samsung certificate made for this TV (its DUID) is needed; see README.md." }

Write-Host "Starting it..."
& $tizen run -p "RcmpTvApp1.RecompTV" -s $serial
Write-Host "Done: Recomp TV is in the TV's apps."
