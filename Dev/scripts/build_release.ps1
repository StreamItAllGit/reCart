#Requires -Version 5.1
# @module: Dev/scripts/build_release.ps1
# @purpose: Single-pass Release build/test, signing, purity checks, Builds\, 7-file Releases\ (+ Portable ZIP, hardlinked Latest, dual junctions), SharedLibs\reCart\, and Archive\.
# @depends-on: Dev/version.txt, Dev/reCart.slnx, Dev/scripts/THIRDPARTY_LICENSES.txt, Dev/assets/native/7z.exe, audit_installer.ps1
# @exports: Canonical release pipeline (v0.1.0+)

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$CertThumbprint = "A5414BDD36F7F113C7B2356F189B7A43F69C35E6",
    [string]$AuditScriptPath = "",
    [switch]$SkipSigning,
    [switch]$SkipSourceArchive,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8 = [System.Text.UTF8Encoding]::new($false)
function Read-Utf8([string]$p) { [System.IO.File]::ReadAllText($p, $utf8) }
function Write-Utf8([string]$p, [string]$t) { [System.IO.File]::WriteAllText($p, $t, $utf8) }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
Get-ChildItem -Path $root -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { -not $_.PSIsContainer -and ($_.Name -in @("desktop.ini", "Thumbs.db", "ehthumbs.db", ".DS_Store") -or $_.Name -like "._*") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = (Read-Utf8 (Join-Path $devDir "version.txt")).Trim() }
$licensePath = Join-Path $root "LICENSE"
$thirdPartyPath = Join-Path $devDir "scripts\THIRDPARTY_LICENSES.txt"
$sevenZip = Join-Path $devDir "assets\native\7z.exe"
if (-not (Test-Path -LiteralPath $sevenZip)) { $sevenZip = Join-Path $env:ProgramFiles "7-Zip\7z.exe" }
if (-not (Test-Path -LiteralPath $licensePath)) { throw "LICENSE missing at $licensePath" }
if (-not (Test-Path -LiteralPath $thirdPartyPath)) { throw "THIRDPARTY_LICENSES.txt missing at $thirdPartyPath" }
if (-not (Test-Path -LiteralPath $sevenZip)) { throw "7z.exe missing at Dev\assets\native\7z.exe" }

$cert = $null
if (-not $SkipSigning) {
    $cert = Get-ChildItem "Cert:\CurrentUser\My", "Cert:\LocalMachine\My" -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -ieq $CertThumbprint.Trim() } | Select-Object -First 1
    if (-not $cert) { throw "Signing certificate '$CertThumbprint' not found." }
}

function Invoke-Sign([string]$Target) {
    if ($SkipSigning) { return }
    $r = Set-AuthenticodeSignature -Certificate $cert -HashAlgorithm SHA256 -FilePath $Target
    if ($r.Status -ne "Valid") { throw "Signing failed for ${Target}: $($r.Status) - $($r.StatusMessage)" }
}

function Invoke-7z([string[]]$ArgList) {
    & $sevenZip @ArgList | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z.exe exited $LASTEXITCODE ($($ArgList -join ' '))" }
}

function Sanitize-Markdown([string]$Text) {
    $c = $Text.Replace(([string][char]0x00C2 + [char]0x00A7), [string][char]0x00A7).Replace(([string][char]0x00C2 + [char]0x00B7), [string][char]0x00B7)
    $c = [regex]::Replace([regex]::Replace([regex]::Replace($c, '\[([^\]]+)\]\(file:///[^)]+\)', '$1'), '\[([^\]]+)\]\((?:\./|\.\.[\\/])*(?:Dev|src|tests|docs|scripts|lib|assets)[\\/][^)]+\)', '$1'), '\[([^\]]+)\]\((?:\.\.[\\/])+[^)]+\)', '$1')
    $c = [regex]::Replace($c, '`file:///[^`\r\n]+`|file:///[^\s`)\]]+', '')
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $drop = "(?i)Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_(?:installer|dossier)\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev Bible\b$userEsc"
    $lines = foreach ($line in ($c -split "\r?\n")) {
        if ($line -match '^\s*[-*]\s+\*\*(?:Entry Points|Verification Tests|Extension Point[^*]*|Tests):\*\*') { continue }
        $s = [regex]::Replace($line, '`?@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor|everyone)[a-z0-9-]*`?', '')
        $s = [regex]::Replace($s, "(?i)``?(?:per\s+)?\bStandards?\s+\d+(?:\s*[-–]\s*\d+)?(?:\s*§[\d.]+)?``?", '')
        if ($s.TrimStart().StartsWith("|")) { $s = [regex]::Replace($s, '(?i)`?Dev[\\/](?:src|tests|docs|scripts|lib|assets)[\\/][^`|]*`?', 'reCart module') }
        if ($s -match $drop) { continue }
        ([regex]::Replace([regex]::Replace($s, '\s*\(\s*\)', ''), '(?<=\S) {2,}', ' ')).TrimEnd()
    }
    return (($lines -join "`r`n") -replace "(\r?\n){3,}", "`r`n`r`n").Trim() + "`r`n"
}

function Assert-NoEndUserLeaks([string]$Path, [string]$Text) {
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $m = [regex]::Match($Text, "(?i)file:///|Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_(?:installer|dossier)\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev Bible\b|@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor|everyone)\b$userEsc")
    if ($m.Success) { throw "End-user markdown leak in '$Path': '$($m.Value)'." }
}

function Assert-BinaryPurity([string]$BinPath, [string[]]$Tokens) {
    if (-not (Test-Path -LiteralPath $BinPath)) { throw "Binary missing: $BinPath" }
    $raw = [System.IO.File]::ReadAllBytes($BinPath)
    $ascii = [System.Text.Encoding]::ASCII.GetString($raw); $utf16 = [System.Text.Encoding]::Unicode.GetString($raw, 0, $raw.Length - ($raw.Length % 2))
    $deps = [System.IO.Path]::ChangeExtension($BinPath, ".deps.json"); $depsText = if (Test-Path -LiteralPath $deps) { Read-Utf8 $deps } else { "" }
    foreach ($t in $Tokens) {
        if ($ascii.IndexOf($t, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $utf16.IndexOf($t, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $depsText.IndexOf($t, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Purity violation in '$BinPath': '$t'." }
    }
}

function Probe-ReleaseAutomationReject([string]$ExePath, [string[]]$ExtraArgs) {
    $prev = $env:APP_ISOLATED_TEST; $env:APP_ISOLATED_TEST = "1"
    try {
        $p = Start-Process -FilePath $ExePath -WindowStyle Hidden -Wait -PassThru -ArgumentList (@("--automation-port", "5199") + $ExtraArgs)
        if ($p.ExitCode -ne 2) { throw "Release purity probe failed for '$ExePath': expected ExitCode=2, got $($p.ExitCode)" }
    } finally { if ($null -eq $prev) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prev } }
}

function Ensure-ReleaseJunction([string]$LinkPath, [string]$TargetDir) {
    if (Test-Path -LiteralPath $LinkPath) {
        $it = Get-Item -LiteralPath $LinkPath -Force
        if (($it.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            if (($it.Target | Select-Object -First 1).TrimEnd('\') -ieq $TargetDir.TrimEnd('\')) { return }
            & cmd /c rmdir "`"$LinkPath`"" | Out-Null
        } else { Remove-Item -LiteralPath $LinkPath -Recurse -Force }
    }
    & cmd /c mklink /J "`"$LinkPath`"" "`"$TargetDir`"" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed creating junction '$LinkPath' -> '$TargetDir'" }
}

Write-Host "[1/6] Building Release solution (/warnaserror) and running isolated Release tests..."
$slnxPath = Join-Path $devDir "reCart.slnx"
& dotnet build $slnxPath -c Release /warnaserror --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
if (-not $SkipTests) {
    $prevIso = $env:APP_ISOLATED_TEST; $env:APP_ISOLATED_TEST = "1"
    try { & dotnet test $slnxPath -c Release --no-build --logger "console;verbosity=minimal"; if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" } }
    finally { if ($null -eq $prevIso) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prevIso } }
}

Write-Host "[2/6] Staging single-pass Release build in Builds\v$Version..."
$buildsRoot = Join-Path $root "Builds"; $stageDir = Join-Path $buildsRoot "v$Version"
$stageApp = Join-Path $stageDir "app"; $stageInteractive = Join-Path $stageDir "scripts\interactive"
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force $stageApp, $stageInteractive | Out-Null

& dotnet publish (Join-Path $devDir "src\reCart.Player\reCart.Player.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $stageApp /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reCart.Player failed ($LASTEXITCODE)" }

$launcherTmp = Join-Path $env:TEMP "recart_launcher_$Version"
if (Test-Path -LiteralPath $launcherTmp) { Remove-Item -LiteralPath $launcherTmp -Recurse -Force }
& dotnet publish (Join-Path $devDir "src\reCart.Launcher\reCart.Launcher.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $launcherTmp /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reCart.Launcher failed ($LASTEXITCODE)" }
$launcherBin = Join-Path $launcherTmp "reCart.exe"
if ((Get-Item -LiteralPath $launcherBin).Length -ge 2MB) { throw "reCart.exe exceeded 2 MB cap" }
Copy-Item -LiteralPath $launcherBin -Destination (Join-Path $stageDir "reCart.exe") -Force
Remove-Item -LiteralPath $launcherTmp -Recurse -Force -ErrorAction SilentlyContinue

$sdlDst = Join-Path $stageApp "SDL3.dll"
foreach ($c in @((Join-Path $stageApp "runtimes\win-x64\native\SDL3.dll"), (Join-Path $devDir "lib\SDL3.dll"))) { if ((-not (Test-Path $sdlDst)) -and (Test-Path $c)) { Copy-Item $c $sdlDst -Force } }
if (-not (Test-Path -LiteralPath $sdlDst)) { throw "SDL3.dll missing in $stageApp" }

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stageApp "LICENSE") -Force
Copy-Item -LiteralPath $thirdPartyPath -Destination (Join-Path $stageDir "THIRDPARTY_LICENSES.txt") -Force
$stageTools = Join-Path $stageApp "tools"; New-Item -ItemType Directory -Force $stageTools | Out-Null
foreach ($tFile in @("7z.exe", "7z.dll", "LICENSE-7ZIP.txt")) { $tSrc = Join-Path $devDir "assets\native\$tFile"; if (Test-Path -LiteralPath $tSrc) { Copy-Item -LiteralPath $tSrc -Destination (Join-Path $stageTools $tFile) -Force } }
Copy-Item -Path (Join-Path $devDir "scripts\interactive\*.bat") -Destination $stageInteractive -Force
Write-Utf8 (Join-Path $stageApp "version.txt") $Version
$nowUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
Write-Utf8 (Join-Path $stageDir ".ready") (([ordered]@{ engineId = "reCart"; legacyEngineIds = @("reCartOne", "reNes"); appId = "recart"; version = $Version; hostProtocolVersion = "0.6.2"; minSuiteVersion = "0.6.0"; completedAtUtc = $nowUtc } | ConvertTo-Json -Depth 3) + "`r`n")
Write-Utf8 (Join-Path $stageDir "engine.manifest.json") (([ordered]@{ schemaVersion = 2; manifestType = "engine-manifest"; engineId = "reCart"; legacyEngineIds = @("reCartOne", "reNes"); appId = "recart"; displayName = "reCart"; version = $Version; engineVersion = $Version; hostProtocolVersion = "0.6.2"; minSuiteVersion = "0.6.0"; compatibleEngineTrack = "0.1.x"; supportedEngineTracks = @("0.1.x"); platformId = "nes"; entryExecutable = "reCart.exe"; decompProviderAssembly = "app/reCart.Decomp.dll"; backendId = "opengl"; executionTierMode = "staticAotOnly"; updatedAtUtc = $nowUtc } | ConvertTo-Json -Depth 4) + "`r`n")
Get-ChildItem $stageDir -Include *.pdb, *.xml -Recurse -File | Remove-Item -Force
$badStage = @(Get-ChildItem $stageDir -Recurse -File | Where-Object { ($_.Extension -in @(".pdb", ".db", ".sqlite", ".sqlite3", ".log")) -or ($_.Name -ieq ".env") -or ($_.Name -ilike "*secret*.json") -or ($_.Name -ieq "AGENTS.md") })
if ($badStage.Count -gt 0) { throw "Forbidden staging file: $($badStage.FullName -join ', ')" }

$noGui = @("Foundation.Automation", "System.Windows.Forms", "Foundation.UI")
foreach ($b in @("reCart.Player.exe", "reCart.Player.dll", "reCart.Contracts.dll", "reCart.Core.dll", "reCart.Compiler.dll", "reCart.Decomp.dll")) { Assert-BinaryPurity (Join-Path $stageApp $b) $noGui }
Assert-BinaryPurity (Join-Path $stageDir "reCart.exe") $noGui
Invoke-Sign (Join-Path $stageApp "reCart.Player.exe"); Invoke-Sign (Join-Path $stageDir "reCart.exe")
$probeDir = Join-Path $env:TEMP ("recart_probe_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force (Join-Path $probeDir "data"), (Join-Path $probeDir "logs") | Out-Null
try { Probe-ReleaseAutomationReject (Join-Path $stageApp "reCart.Player.exe") @("--data-dir", (Join-Path $probeDir "data"), "--log-dir", (Join-Path $probeDir "logs")) }
finally { Remove-Item -LiteralPath $probeDir -Recurse -Force -ErrorAction SilentlyContinue }
Get-ChildItem -Path $buildsRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
    Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending | Select-Object -Skip 2 | Remove-Item -Recurse -Force

Write-Host "[3/6] Building single-EXE 4-pillar installer..."
$workTemp = Join-Path $env:TEMP ("recart_pkg_" + $Version)
if (Test-Path -LiteralPath $workTemp) { Remove-Item -LiteralPath $workTemp -Recurse -Force }
New-Item -ItemType Directory -Force $workTemp | Out-Null
$payloadZip = Join-Path $workTemp "payload.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$instOut = Join-Path $workTemp "installer_out"
& dotnet publish (Join-Path $devDir "src\reCart.Installer\reCart.Installer.csproj") -c Release -r win-x64 --self-contained true -p:Version="$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -p:InstallerPayloadZip="$payloadZip" -o $instOut /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reCart.Installer failed ($LASTEXITCODE)" }
$builtInstaller = Get-ChildItem $instOut -Filter *.exe | Select-Object -First 1
if (-not $builtInstaller) { throw "No installer EXE under $instOut" }
Assert-BinaryPurity $builtInstaller.FullName @("Foundation.Automation", "Foundation.UI")
Probe-ReleaseAutomationReject $builtInstaller.FullName @()

Write-Host "[4/6] Assembling 7-file Releases\v$Version, NTFS-hardlinked Latest, .7z archives, dual junctions & audit..."
$releasesDir = Join-Path $root "Releases"; $relVerDir = Join-Path $releasesDir "v$Version"; $relLatestDir = Join-Path $releasesDir "Latest"
foreach ($d in @($relVerDir, $relLatestDir)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null }
$installerName = "reCart-Setup-v$Version-win-x64.exe"; $installerPath = Join-Path $relVerDir $installerName
Copy-Item -LiteralPath $builtInstaller.FullName -Destination $installerPath -Force; Invoke-Sign $installerPath

$rawNotes = ("# reCart — Release v$Version`r`n`r`nClean-room AOT-primary 6502 static recompilation engine (`reCart`, `platformId: ""nes""`) built on `reEngine` + SDL3 (`AllowAutoStart = false`).`r`n`r`n## Highlights`r`n- Cycle-stepped `RP2A03`/`RP2C02`, Mappers `0-3`, and `RP2A03` 5-channel APU.`r`n- Static `6502 -> C#` lifter emitting strictly 3-member clean-room `.recomp` archives + local `<dataDir>\cache\` AOT + quarantined `--execution-tier interpreterOracle`.`r`n- 5-backend `reEngine.Host` runtime (`NativePar` default) and side-by-side `engines\reCart\v$Version\` setup.")
$guideDocs = @((Read-Utf8 (Join-Path $devDir "docs\USER_GUIDE.md")))
Get-ChildItem (Join-Path $devDir "docs") -Filter "USER_GUIDE.*.md" -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $guideDocs += (Read-Utf8 $_.FullName) }
$docMap = [ordered]@{ "README.md" = Sanitize-Markdown ($guideDocs -join "`r`n`r`n"); "FEATURES.md" = Sanitize-Markdown (Read-Utf8 (Join-Path $root "FEATURES.md")); "RELEASE_NOTES.md" = Sanitize-Markdown $rawNotes; "CHANGELOG.md" = Sanitize-Markdown (Read-Utf8 (Join-Path $root "CHANGELOG.md")) }
foreach ($kv in $docMap.GetEnumerator()) { $p = Join-Path $relVerDir $kv.Key; Assert-NoEndUserLeaks $p $kv.Value; Write-Utf8 $p $kv.Value }

$portableZipName = "reCart-v$Version-win-x64-Portable.zip"; $portableZipPath = Join-Path $relVerDir $portableZipName
$portableStage = Join-Path $workTemp "portable_stage"; New-Item -ItemType Directory -Force $portableStage | Out-Null
Copy-Item -Path (Join-Path $stageDir "*") -Destination $portableStage -Recurse -Force
foreach ($kv in $docMap.GetEnumerator()) { Write-Utf8 (Join-Path $portableStage $kv.Key) $kv.Value }
if (@(Get-ChildItem $portableStage -Recurse -File | Where-Object { $_.Name -ilike "*-Setup-*.exe" -or $_.Name -ieq "AGENTS.md" }).Count -gt 0) { throw "Portable staging violation" }
[System.IO.Compression.ZipFile]::CreateFromDirectory($portableStage, $portableZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$bundleDocs = @($installerName, $portableZipName, "README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md")
$sumLines = foreach ($fn in $bundleDocs) { "$((Get-FileHash (Join-Path $relVerDir $fn) -Algorithm SHA256).Hash.ToLowerInvariant())  $fn" }
[System.IO.File]::WriteAllText((Join-Path $relVerDir "SHA256SUMS.txt"), ($sumLines -join "`r`n") + "`r`n", [System.Text.Encoding]::ASCII)
foreach ($fn in ($bundleDocs + @("SHA256SUMS.txt"))) {
    $src = Join-Path $relVerDir $fn; $dst = Join-Path $relLatestDir $fn
    try { New-Item -ItemType HardLink -Path $dst -Target $src | Out-Null } catch { Copy-Item $src $dst -Force }
}
Remove-Item (Join-Path $releasesDir "reCart-v$Version-win-x64.7z"), (Join-Path $releasesDir "Latest.7z") -Force -ErrorAction SilentlyContinue
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", (Join-Path $releasesDir "reCart-v$Version-win-x64.7z"), (Join-Path $relVerDir "*"))
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", (Join-Path $releasesDir "Latest.7z"), (Join-Path $relLatestDir "*"))

$groupDir = Split-Path -Parent $root; $projectsRoot = Split-Path -Parent $groupDir
$hubParent = Join-Path (Join-Path $projectsRoot "Releases") (Split-Path -Leaf $groupDir)
New-Item -ItemType Directory -Force $hubParent | Out-Null
foreach ($jName in @("reCartOne", "reCart")) { Ensure-ReleaseJunction (Join-Path $hubParent $jName) $releasesDir }

$auditScript = if (-not [string]::IsNullOrWhiteSpace($AuditScriptPath)) { $AuditScriptPath } else { Join-Path $env:USERPROFILE ".gemini\config\skills\dev\scripts\audit_installer.ps1" }
if ((-not $SkipSigning) -and (Test-Path -LiteralPath $auditScript)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $auditScript -InstallerPath $relVerDir -StagingDir $stageDir -ExpectedVersion $Version
    if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed ($LASTEXITCODE)" }
}

Write-Host "[5/6] Publishing immutable SharedLibs\reCart\v$Version & Latest..."
$sharedRoot = Join-Path $projectsRoot "SharedLibs\reCart"
$libOutputs = @("reCart.Contracts", "reCart.Core", "reCart.Compiler", "reCart.Decomp") | ForEach-Object { @{ Dir = (Join-Path $devDir "src\$_\bin\Release\net8.0"); Prefix = $_ } }
foreach ($lo in $libOutputs) { Assert-BinaryPurity (Join-Path $lo.Dir "$($lo.Prefix).dll") $noGui }
foreach ($sdkDir in @((Join-Path $sharedRoot "v$Version"), (Join-Path $sharedRoot "Latest"))) {
    if (Test-Path $sdkDir) { Remove-Item $sdkDir -Recurse -Force }; New-Item -ItemType Directory -Force $sdkDir | Out-Null
    foreach ($lo in $libOutputs) { foreach ($ext in @(".dll", ".xml", ".deps.json")) { $src = Join-Path $lo.Dir ($lo.Prefix + $ext); if (-not (Test-Path $src)) { throw "Missing $src" }; Copy-Item $src (Join-Path $sdkDir ($lo.Prefix + $ext)) -Force } }
    Copy-Item $licensePath (Join-Path $sdkDir "LICENSE") -Force
    Copy-Item $thirdPartyPath (Join-Path $sdkDir "THIRDPARTY_LICENSES.txt") -Force
    $artifacts = @(Get-ChildItem $sdkDir -File | Where-Object { $_.Name -ne "sdk-manifest.json" } | Sort-Object Name | ForEach-Object { [ordered]@{ fileName = $_.Name; fileSizeBytes = $_.Length; sha256Hex = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    Write-Utf8 (Join-Path $sdkDir "sdk-manifest.json") (([ordered]@{ schemaVersion = 1; appId = "recart"; engineId = "reCart"; displayName = "reCart"; sdkVersion = $Version; version = $Version; updatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); artifacts = $artifacts } | ConvertTo-Json -Depth 5) + "`r`n")
}

if (-not $SkipSourceArchive) {
    Write-Host "[6/6] Updating Archive\reCart-v$Version-src.7z..."
    $archiveDir = Join-Path $root "Archive"; New-Item -ItemType Directory -Force $archiveDir | Out-Null
    $srcArchive = Join-Path $archiveDir "reCart-v$Version-src.7z"; Remove-Item $srcArchive -Force -ErrorAction SilentlyContinue
    $srcItems = @("Dev", "README.md", "FEATURES.md", "ROADMAP.md", "CHANGELOG.md", "AGENTS.md", "LICENSE", "SECURITY.md", "CONTRIBUTING.md", "active.state") | Where-Object { Test-Path (Join-Path $root $_) }
    Push-Location $root
    try { Invoke-7z (@("a", "-t7z", "-mx=9", "-xr!bin", "-xr!obj", "-xr!TestResults", "-xr!data", "-xr!logs", "-xr!.vs", "-xr!*.pdb", "-xr!*.tmp", "-xr!*.bak", "-xr!*.user", "-xr!desktop.ini", "-xr!Thumbs.db", "-xr!ehthumbs.db", "-xr!.DS_Store", "-xr!._*", $srcArchive) + $srcItems) }
    finally { Pop-Location }
}

Remove-Item $workTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE: Single-pass Release build, signed installer, 7-file Releases (+ Portable ZIP, hardlinked Latest, dual junctions), SharedLibs\reCart, and Archive staged for v$Version."
