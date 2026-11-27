#Requires -Version 5.1
# @module: Dev/scripts/run_stage2_sandbox_e2e.ps1
# @purpose: Consolidated 1-3 launch 4-drive Stage 2 E2E harness with exebox-stream (http://127.0.0.1:5005), dynamic reEngine discovery, ephemeral ports, host-load perf, and cross-restart persistence.
# @depends-on: Dev/reCart.slnx, reEngine Builds\, exebox-stream/scripts/start_exebox_stream.ps1, audit_installer.ps1
# @exports: Stage 2 E2E harness (v0.1.4+)

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipInstallerSandbox,
    [string]$ArtifactDir = "",
    [string]$HubUrl = "http://127.0.0.1:5005"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not ("S2Win32" -as [type])) {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class S2Win32 { public delegate bool EnumProc(IntPtr h, IntPtr l); [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; } [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint); public static IntPtr FindVisibleByPid(int pid) { IntPtr f = IntPtr.Zero; EnumWindows((h, _) => { if (!IsWindowVisible(h)) return true; uint p; GetWindowThreadProcessId(h, out p); RECT r; if (p == (uint)pid && GetWindowRect(h, out r) && (r.R - r.L) >= 320 && (r.B - r.T) >= 200) { f = h; return false; } return true; }, IntPtr.Zero); return f; } }'
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ([System.IO.File]::ReadAllText((Join-Path $devDir "version.txt"))).Trim() }
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) { $ArtifactDir = Join-Path $env:TEMP ("recart_stage2_e2e_" + $Version) }
New-Item -ItemType Directory -Force $ArtifactDir | Out-Null

$sandbox = Join-Path $env:TEMP ("recart_s2_box_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$sbC = Join-Path $sandbox "C"; $sbD = Join-Path $sandbox "D"
$sbRem = Join-Path $sandbox "Rem"; $sbDisc = Join-Path $sandbox "Disc"
$sbProf = Join-Path $sbC "Users\Sandbox"
$suiteRoot = Join-Path $sbD "Programs\StreamItAll\reEngine"
$enginesDir = Join-Path $suiteRoot "engines"
$dataDir = Join-Path $sbD "reCartData\data"; $logDir = Join-Path $sbD "reCartData\logs"; $saveDir = Join-Path $sbD "reCartData\saves"
$sbArt = Join-Path $sbD "artifacts"; $streamTmp = Join-Path $sandbox "stream_tmp"
New-Item -ItemType Directory -Force $sbC, $sbD, $sbRem, $sbDisc, $sbProf, $enginesDir, $dataDir, $logDir, $saveDir, $sbArt, $streamTmp | Out-Null

# Preserve host OS env vars while activating 4-Drive Sandbox
$preservedOsEnv = @{}
foreach ($k in @("SystemDrive", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ALLUSERSPROFILE", "PUBLIC", "WINDIR", "SystemRoot")) {
    $preservedOsEnv[$k] = [Environment]::GetEnvironmentVariable($k, "Process")
}

function Set-SandboxEnv([bool]$EnableSandbox) {
    Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue
    foreach ($kv in $preservedOsEnv.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace($kv.Value)) { Set-Item -Path ("Env:" + $kv.Key) -Value $kv.Value }
    }
    if ($EnableSandbox) {
        $env:FOUNDATION_SANDBOX_ROOT = $sandbox; $env:SANDBOX_C_DRIVE = $sbC; $env:SANDBOX_D_DRIVE = $sbD
        $env:SANDBOX_REMOVABLE_DRIVE = $sbRem; $env:SANDBOX_DISC_DRIVE = $sbDisc
        $env:FOUNDATION_SANDBOX_PROFILE_DIR = $sbProf
        $env:DOTNET_CLI_HOME = Join-Path $sbD ".dotnet"; New-Item -ItemType Directory -Force $env:DOTNET_CLI_HOME | Out-Null
    } else {
        foreach ($v in @("FOUNDATION_SANDBOX_ROOT","SANDBOX_C_DRIVE","SANDBOX_D_DRIVE","SANDBOX_REMOVABLE_DRIVE","SANDBOX_DISC_DRIVE","FOUNDATION_SANDBOX_PROFILE_DIR","DOTNET_CLI_HOME")) {
            Remove-Item "Env:$v" -ErrorAction SilentlyContinue
        }
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Get-HostLoadSample {
    $cpu = [double]((Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average)
    $os = Get-CimInstance Win32_OperatingSystem
    $ramPct = [math]::Round((($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize) * 100.0, 1)
    $top = @(Get-Process -ErrorAction SilentlyContinue | Sort-Object { try { if ($null -ne $_.CPU) { [double]$_.CPU } else { 0.0 } } catch { 0.0 } } -Descending | Select-Object -First 4 | ForEach-Object { @{ name = $_.ProcessName; pid = $_.Id; memMb = [math]::Round($_.WorkingSet64 / 1MB, 1) } })
    $band = if ($cpu -gt 75 -or $ramPct -ge 90) { "CONTENDED" } elseif ($cpu -ge 50 -or $ramPct -ge 80) { "MODERATE" } else { "CLEAN" }
    return [ordered]@{ band = $band; cpuPct = [math]::Round($cpu, 1); ramUsedPct = $ramPct; topProcesses = $top }
}

function Invoke-Api([int]$Port, [string]$Token, [string]$Method, [string]$Path, [string]$JsonBody = "") {
    $hdrs = @{ Authorization = "Bearer $Token"; Host = "127.0.0.1:$Port" }
    if ($Method -eq "GET") { return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Get -Headers $hdrs -TimeoutSec 5 }
    return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Post -Headers $hdrs -ContentType "application/json" -Body $JsonBody -TimeoutSec 5
}

function Find-NodeById($Node, [string]$Id) {
    if ($null -eq $Node) { return $null }
    if ($Node.id -eq $Id) { return $Node }
    foreach ($c in @($Node.children)) { $m = Find-NodeById $c $Id; if ($null -ne $m) { return $m } }
    return $null
}

function Test-TreeContainsText($Node, [string]$Needle) {
    if ($null -eq $Node) { return $false }
    $tProp = $Node.PSObject.Properties['text']
    $nProp = $Node.PSObject.Properties['name']
    if (($tProp -and $tProp.Value -like "*$Needle*") -or ($nProp -and $nProp.Value -like "*$Needle*")) { return $true }
    $cProp = $Node.PSObject.Properties['children']
    if ($cProp) { foreach ($c in @($cProp.Value)) { if (Test-TreeContainsText $c $Needle) { return $true } } }
    return $false
}

function Wait-ApiHealthy([System.Diagnostics.Process]$Proc, [int]$Port, [string]$Token, [int]$MaxAttempts = 60) {
    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        if ($Proc.HasExited) { throw "Process PID=$($Proc.Id) exited prematurely ($($Proc.ExitCode)) before /v1/health." }
        try { if ((Invoke-Api $Port $Token "GET" "/v1/health").isPassed) { return } } catch {}
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for /v1/health on 127.0.0.1:$Port"
}

function Send-HubEvent([string]$Type, [string]$StepId, [string]$Title, [string]$Status, [string]$Message, [string]$InputKind = "", [string]$InputLabel = "") {
    $body = [ordered]@{ type = $Type; project = "reCart"; stepId = $StepId; stepTitle = $Title; status = $Status; message = $Message }
    if (-not [string]::IsNullOrWhiteSpace($InputKind)) { $body.inputKind = $InputKind; $body.normX = 0.5; $body.normY = 0.5; $body.inputLabel = $InputLabel }
    try { $null = Invoke-RestMethod -Uri ($HubUrl.TrimEnd('/') + "/api/v1/event") -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 2 } catch {}
}

function Start-SuiteStream([int]$TargetPid, [int]$AutomationPort, [string]$AutomationToken) {
    $streamScript = Join-Path $env:USERPROFILE ".gemini\config\skills\exebox-stream\scripts\start_exebox_stream.ps1"
    if (-not (Test-Path -LiteralPath $streamScript)) { return $null }
    $ctrlFile = Join-Path $sandbox ("exebox_ctrl_" + [Guid]::NewGuid().ToString("N").Substring(0, 8) + ".txt")
    $argList = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $streamScript,
        "-HubUrl", $HubUrl.TrimEnd('/'), "-Project", "reCart", "-TargetPid", "$TargetPid",
        "-Fps", "20", "-SnapshotDir", $streamTmp, "-ControlPortFile", $ctrlFile
    )
    if ($AutomationPort -gt 0) { $argList += @("-AutomationPort", "$AutomationPort", "-AutomationToken", $AutomationToken) }
    $proc = Start-Process "powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList $argList
    $ctrlPort = 0
    for ($i = 0; $i -lt 40; $i++) {
        if ($proc.HasExited) { break }
        if (Test-Path -LiteralPath $ctrlFile) {
            $raw = Get-Content -LiteralPath $ctrlFile -Raw -ErrorAction SilentlyContinue
            if ($raw -and [int]::TryParse($raw.Trim(), [ref]$ctrlPort) -and $ctrlPort -gt 0) { break }
        }
        Start-Sleep -Milliseconds 100
    }
    return [PSCustomObject]@{ Process = $proc; ControlPort = $ctrlPort; ControlPortFile = $ctrlFile }
}

function Switch-StreamTarget($StreamHandle, [int]$TargetPid, [int]$AutomationPort, [string]$AutomationToken, [string]$Label) {
    if ($null -eq $StreamHandle -or $StreamHandle.ControlPort -le 0) { return }
    try { $null = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/detach" -f $StreamHandle.ControlPort) -Method Post -ContentType "application/json" -Body (@{ automationPort = 0; targetPid = 0 } | ConvertTo-Json -Compress) -TimeoutSec 2 } catch {}
    $body = [ordered]@{ automationPort = $AutomationPort; automationToken = $AutomationToken; targetPid = $TargetPid; label = $Label; snapshotDir = $streamTmp }
    try { $null = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/attach" -f $StreamHandle.ControlPort) -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 2 } catch {}
}

function Close-PlayerCleanly([System.Diagnostics.Process]$Proc, [int]$Port, [string]$Token) {
    if ($null -eq $Proc -or $Proc.HasExited) { return }
    try { $null = Invoke-Api $Port $Token "POST" "/v1/keyboard" (@{ action = "press"; key = "ESC" } | ConvertTo-Json) } catch {}
    if ($Proc.WaitForExit(3000) -and $Proc.ExitCode -eq 0) { return }
    try {
        $null = Invoke-Api $Port $Token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
        $null = Invoke-Api $Port $Token "POST" "/v1/pointer" (@{ action = "click"; targetId = "Tab_System" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
        $null = Invoke-Api $Port $Token "POST" "/v1/pointer" (@{ action = "click"; targetId = "System_QuitGame" } | ConvertTo-Json)
    } catch {}
    if (-not $Proc.WaitForExit(7000) -or $Proc.ExitCode -ne 0) {
        throw "Player PID=$($Proc.Id) did not exit 0 cleanly (ExitCode=$(if ($Proc.HasExited) { $Proc.ExitCode } else { 'TIMEOUT' }))"
    }
}

function Get-PngPixelDiffCount([string]$PathA, [string]$PathB) {
    $bmpA = [System.Drawing.Bitmap]::new($PathA); $bmpB = [System.Drawing.Bitmap]::new($PathB)
    try {
        $w = [Math]::Min($bmpA.Width, $bmpB.Width); $h = [Math]::Min($bmpA.Height, $bmpB.Height); $diff = 0
        for ($y = 0; $y -lt $h; $y += 2) {
            for ($x = 0; $x -lt $w; $x += 2) { if ($bmpA.GetPixel($x, $y).ToArgb() -ne $bmpB.GetPixel($x, $y).ToArgb()) { $diff++ } }
        }
        return $diff
    } finally { $bmpA.Dispose(); $bmpB.Dispose() }
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-PillarboxAspect([string]$PngPath) {
    $bmp = [System.Drawing.Bitmap]::new($PngPath)
    try {
        if ($bmp.Width -lt 800 -or $bmp.Height -lt 400) { throw "Unexpected resized dimensions $($bmp.Width)x$($bmp.Height)" }
        $l = $bmp.GetPixel(16, [int]($bmp.Height / 2)); $r = $bmp.GetPixel($bmp.Width - 17, [int]($bmp.Height / 2))
        if (($l.R + $l.G + $l.B) -gt 120 -or ($r.R + $r.G + $r.B) -gt 120) {
            throw "Aspect pillarbox violated on '$PngPath' (edges L=$($l.R),$($l.G),$($l.B) R=$($r.R),$($r.G),$($r.B))"
        }
    } finally { $bmp.Dispose() }
}

function Assert-WavAudible([string]$WavPath) {
    $raw = [System.IO.File]::ReadAllBytes($WavPath)
    if ($raw.Length -le 44) { throw "WAV empty/truncated: $WavPath" }
    $peak = 0
    for ($i = 44; $i + 1 -lt $raw.Length; $i += 2) { $s = [Math]::Abs([int][BitConverter]::ToInt16($raw, $i)); if ($s -gt $peak) { $peak = $s } }
    if ($peak -lt 50) { throw "WAV silent (peak=$peak): $WavPath" }
    return $peak
}

function Find-HighestReEngineExe([string]$PersonalDir) {
    $buildsRoot = Join-Path $PersonalDir "reEngine\Builds"
    if (-not (Test-Path -LiteralPath $buildsRoot)) { return $null }
    $verExes = @(Get-ChildItem -Path $buildsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
        Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending |
        ForEach-Object { Join-Path $_.FullName "reEngine.exe" })
    $latestExe = Join-Path $buildsRoot "Latest\reEngine.exe"
    return (@($verExes + $latestExe) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

function Invoke-DotnetPublishWithRetry([string]$Label, [string[]]$PubArgs) {
    $prevEap = $ErrorActionPreference
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $ErrorActionPreference = "Continue"
        $out = & dotnet @PubArgs 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prevEap
        if ($code -eq 0) { return }
        $txt = ($out -join "`n")
        if ($attempt -lt 6 -and $txt -match "being used by another process|package assets cache due to I/O error") {
            Write-Host "[S2-02] $Label contended on shared bin/obj (attempt $attempt/6); retrying in 4s..."
            Start-Sleep -Seconds 4
            continue
        }
        throw "$Label failed ($code):`n$txt"
    }
}

$streamer = $null; $playerProc = $null; $wizProc = $null
try {
    $hostLoadStart = Get-HostLoadSample
    if ($hostLoadStart.band -eq "CONTENDED") {
        Write-Host "[S2-00] Host load CONTENDED (CPU=$($hostLoadStart.cpuPct)%, RAM=$($hostLoadStart.ramUsedPct)%); cooling 10s..."
        Start-Sleep -Seconds 10
        $hostLoadStart = Get-HostLoadSample
    }
    $hubOk = $false
    try { $h = Invoke-RestMethod -Uri ($HubUrl.TrimEnd('/') + "/api/v1/health") -Method Get -TimeoutSec 2; $hubOk = ($null -ne $h -and $h.ok) } catch {}
    Write-Host "[S2-01] Test Viewer Hub ($HubUrl) active = $hubOk | HostLoad = $($hostLoadStart.band) (CPU=$($hostLoadStart.cpuPct)%, RAM=$($hostLoadStart.ramUsedPct)%)"

    $buildArt = Join-Path $sandbox "build_artifacts"; New-Item -ItemType Directory -Force $buildArt | Out-Null
    Write-Host "[S2-02] Publishing ReCartTestAutomation (-t:Rebuild,Publish) into sandbox..."
    $autoEngDir = Join-Path $enginesDir "reCart\v$Version"; $autoAppDir = Join-Path $autoEngDir "app"
    New-Item -ItemType Directory -Force $autoAppDir | Out-Null
    Invoke-DotnetPublishWithRetry "Publish ReCartTestAutomation player" @("publish", (Join-Path $devDir "src\reCart.Player\reCart.Player.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:ReCartTestAutomation=true", "-p:PublishSingleFile=false", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$buildArt", "-m:1", "-o", $autoAppDir, "/warnaserror", "--disable-build-servers", "-p:UseSharedCompilation=false")
    Invoke-DotnetPublishWithRetry "Publish reCart.Launcher" @("publish", (Join-Path $devDir "src\reCart.Launcher\reCart.Launcher.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:PublishSingleFile=true", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$buildArt", "-m:1", "-o", $autoEngDir, "/warnaserror", "--disable-build-servers", "-p:UseSharedCompilation=false")
    $sdlSrc = Join-Path $devDir "lib\SDL3.dll"; if (Test-Path $sdlSrc) { Copy-Item $sdlSrc (Join-Path $autoAppDir "SDL3.dll") -Force }
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $autoAppDir "LICENSE") -Force

    if (-not $SkipInstallerSandbox) {
        $instExe = Join-Path $root "Releases\v$Version\reCart-Setup-v$Version-win-x64.exe"
        if (-not (Test-Path $instExe)) { throw "Installer missing at $instExe" }
        Write-Host "[S2-03] Running Release ExitCode=2 probe, 4-pillar silent install, and Setup Wizard GUI automation..."
        $env:APP_ISOLATED_TEST = "1"
        $probePortInst = Get-FreeTcpPort
        $pProbe = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--automation-port", "$probePortInst")
        if ($pProbe.ExitCode -ne 2) { throw "Release installer automation reject expected 2, got $($pProbe.ExitCode)" }
        Set-SandboxEnv $true
        New-Item -ItemType Directory -Force (Join-Path $suiteRoot "engines\reSlate\v0.1.0") | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $suiteRoot "reEngine.exe"), "sibling-guard")
        $instData = Join-Path $sbD "inst_data"
        $pInst = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--silent", "--suite-dir", $suiteRoot, "--data-dir", $instData)
        if ($pInst.ExitCode -ne 0) { throw "Sandboxed silent install failed ($($pInst.ExitCode))" }
        if (-not (Test-Path (Join-Path $autoEngDir ".ready")) -or -not (Test-Path (Join-Path $autoEngDir "engine.manifest.json")) -or -not (Test-Path (Join-Path $suiteRoot "engines\reSlate\v0.1.0"))) {
            throw "Side-by-side install or sibling guard failed under $suiteRoot"
        }
        $pUninst = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--silent", "--uninstall", "--suite-dir", $suiteRoot, "--data-dir", $instData)
        if ($pUninst.ExitCode -ne 0) { throw "Sandboxed uninstall failed ($($pUninst.ExitCode))" }

        $wizPayload = Join-Path $sandbox "wiz_payload.zip"; $wizOut = Join-Path $sbD "wiz_bin"
        [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root "Builds\v$Version"), $wizPayload, [System.IO.Compression.CompressionLevel]::Fastest, $false)
        Invoke-DotnetPublishWithRetry "Publish test Setup Wizard" @("publish", (Join-Path $devDir "src\reCart.Installer\reCart.Installer.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:ReCartTestAutomation=true", "-p:InstallerPayloadZip=$wizPayload", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$buildArt", "-m:1", "-o", $wizOut, "/warnaserror", "--disable-build-servers", "-p:UseSharedCompilation=false")
        $wizExe = Join-Path $wizOut "reCart-Setup-v$Version-win-x64.exe"
        $wPort = Get-FreeTcpPort; $wToken = "recart_wiz_" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
        $wizProc = Start-Process $wizExe -PassThru -ArgumentList @("--automation-port", "$wPort", "--automation-token", $wToken, "--suite-dir", $suiteRoot, "--install-dir", $suiteRoot, "--data-dir", $instData, "--no-launch")
        Wait-ApiHealthy $wizProc $wPort $wToken 50
        $streamer = Start-SuiteStream $wizProc.Id $wPort $wToken
        Send-HubEvent "step" "WIZ-01" "Setup Wizard GUI" "running" "Verifying reEngine auto-sort and GUI install"
        $tree = Invoke-Api $wPort $wToken "GET" "/v1/tree"
        $dirNode = Find-NodeById $tree.data "Wizard_InstallDir"
        if (-not $dirNode -or $dirNode.text -notlike "*engines\reCart\v$Version") { throw "Wizard auto-sort failed: '$($dirNode.text)'" }
        $null = Invoke-Api $wPort $wToken "POST" "/v1/snapshot" (@{ filePath = (Join-Path $sbArt "recart_installer_wizard.png"); snapshotFormat = "png" } | ConvertTo-Json)
        Send-HubEvent "input" "WIZ-01" "Wizard Click" "info" "Clicking Install" "click" "Wizard_PrimaryButton"
        $null = Invoke-Api $wPort $wToken "POST" "/v1/pointer" (@{ action = "click"; targetId = "Wizard_PrimaryButton" } | ConvertTo-Json)
        $btn = $null; $t2 = $null
        for ($i = 0; $i -lt 90; $i++) {
            $t2 = Invoke-Api $wPort $wToken "GET" "/v1/tree"; $btn = Find-NodeById $t2.data "Wizard_PrimaryButton"
            if ($btn -and ($btn.text -match "Finish|Close")) { break }; Start-Sleep -Milliseconds 250
        }
        if (-not $btn -or $btn.text -notmatch "Finish|Close") {
            $stNode = if ($t2) { Find-NodeById $t2.data "Wizard_StatusLabel" } else { $null }
            throw "Wizard install did not reach Finish (btn='$($btn.text)', status='$($stNode.text)')"
        }
        try { $null = Invoke-Api $wPort $wToken "POST" "/v1/pointer" (@{ action = "click"; targetId = "Wizard_PrimaryButton" } | ConvertTo-Json) } catch { if (-not $wizProc.WaitForExit(5000) -or $wizProc.ExitCode -ne 0) { throw } }
        if (-not $wizProc.WaitForExit(10000) -or $wizProc.ExitCode -ne 0) { throw "Wizard GUI exited abnormally ($($wizProc.ExitCode))" }
        if (-not (Test-Path (Join-Path $suiteRoot "reEngine.exe")) -or -not (Test-Path (Join-Path $autoEngDir ".ready")) -or -not (Test-Path (Join-Path $autoEngDir "engine.manifest.json"))) { throw "Wizard post-install check failed" }
        $null = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--silent", "--uninstall", "--suite-dir", $suiteRoot, "--data-dir", $instData)
        Invoke-DotnetPublishWithRetry "Re-publish ReCartTestAutomation player" @("publish", (Join-Path $devDir "src\reCart.Player\reCart.Player.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:ReCartTestAutomation=true", "-p:PublishSingleFile=false", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$buildArt", "-m:1", "-o", $autoAppDir, "/warnaserror", "--disable-build-servers", "-p:UseSharedCompilation=false")
        Invoke-DotnetPublishWithRetry "Re-publish reCart.Launcher" @("publish", (Join-Path $devDir "src\reCart.Launcher\reCart.Launcher.csproj"), "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-t:Rebuild,Publish", "-p:PublishSingleFile=true", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$buildArt", "-m:1", "-o", $autoEngDir, "/warnaserror", "--disable-build-servers", "-p:UseSharedCompilation=false")
        if (Test-Path $sdlSrc) { Copy-Item $sdlSrc (Join-Path $autoAppDir "SDL3.dll") -Force }
    }

    $nowUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    [System.IO.File]::WriteAllText((Join-Path $autoEngDir ".ready"), (@{ engineId = "reCart"; legacyEngineIds = @("reCartOne","reNes"); appId = "recart"; version = $Version; hostProtocolVersion = "0.6.2"; minSuiteVersion = "0.6.0"; completedAtUtc = $nowUtc } | ConvertTo-Json))
    [System.IO.File]::WriteAllText((Join-Path $autoEngDir "engine.manifest.json"), (@{ schemaVersion = 2; manifestType = "engine-manifest"; engineId = "reCart"; legacyEngineIds = @("reCartOne","reNes"); appId = "recart"; displayName = "reCart"; version = $Version; hostProtocolVersion = "0.6.2"; minSuiteVersion = "0.6.0"; entryExecutable = "reCart.exe"; backendId = "opengl"; executionTierMode = "staticAotOnly" } | ConvertTo-Json))
    $relPlayer = Join-Path $root "Builds\v$Version\app\reCart.Player.exe"
    if (Test-Path $relPlayer) {
        $env:APP_ISOLATED_TEST = "1"
        $probePortPlr = Get-FreeTcpPort
        $pr = Start-Process $relPlayer -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--automation-port", "$probePortPlr", "--data-dir", $dataDir, "--log-dir", $logDir)
        if ($pr.ExitCode -ne 2) { throw "Release player automation reject expected 2, got $($pr.ExitCode)" }
    }

    Set-SandboxEnv $true
    $playerExe = Join-Path $autoAppDir "reCart.Player.exe"

    # Launch 1: Empty-Media Onboarding Gate (< 5s visible feedback + MediaGate_* controls)
    $l1Data = Join-Path $sbD "l1_empty_data"; $l1Log = Join-Path $sbD "l1_empty_logs"; New-Item -ItemType Directory -Force $l1Data, $l1Log | Out-Null
    $p1Port = Get-FreeTcpPort; $p1Token = "recart_l1_" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $swL1 = [System.Diagnostics.Stopwatch]::StartNew()
    $playerProc = Start-Process $playerExe -PassThru -WorkingDirectory $autoAppDir -ArgumentList @("--automation-port", "$p1Port", "--automation-token", $p1Token, "--data-dir", $l1Data, "--log-dir", $l1Log)
    Wait-ApiHealthy $playerProc $p1Port $p1Token 40
    $swL1.Stop()
    if ($null -eq $streamer) { $streamer = Start-SuiteStream $playerProc.Id $p1Port $p1Token } else { Switch-StreamTarget $streamer $playerProc.Id $p1Port $p1Token "reCart MediaGate" }
    Send-HubEvent "step" "L1-01" "Empty-Media Gate" "running" "Checking <5s Media Gate feedback"
    $l1Tree = Invoke-Api $p1Port $p1Token "GET" "/v1/tree"
    foreach ($mgId in @("MediaGate_BrowseButton", "MediaGate_SelectModuleButton", "MediaGate_LaunchDemoButton")) {
        if (-not (Find-NodeById $l1Tree.data $mgId)) { throw "Media Gate missing control '$mgId'" }
    }
    $null = Invoke-Api $p1Port $p1Token "POST" "/v1/snapshot" (@{ filePath = (Join-Path $sbArt "recart_media_gate.png"); snapshotFormat = "png" } | ConvertTo-Json)
    Close-PlayerCleanly $playerProc $p1Port $p1Token
    $playerProc = $null

    # Launch 2: Continuous Grand Tour (--demo-cart, cold AOT settle, movement + audio, F3 + F1 Drawer, Resize, F11, F5/F9)
    $port = Get-FreeTcpPort; $token = "recart_s2_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $wavPath = Join-Path $sbArt "recart_live_audio.wav"; $pngBefore = Join-Path $sbArt "recart_gameplay_before.png"
    $pngAfter = Join-Path $sbArt "recart_gameplay_after.png"; $pngHud = Join-Path $sbArt "recart_f1_f3_live.png"
    $pngResize = Join-Path $sbArt "recart_resized_aspect.png"; $pngFull = Join-Path $sbArt "recart_fullscreen_live.png"

    Write-Host "[S2-04] Launching Launch 2 Grand Tour (--demo-cart, ephemeral port=$port -> $HubUrl)..."
    $swStartup = [System.Diagnostics.Stopwatch]::StartNew()
    $playerProc = Start-Process $playerExe -PassThru -WorkingDirectory $autoAppDir -ArgumentList @("--demo-cart", "--backend", "opengl", "--aspect-mode", "nativePar", "--automation-port", "$port", "--automation-token", $token, "--data-dir", $dataDir, "--log-dir", $logDir, "--save-dir", $saveDir, "--dump-audio", $wavPath)
    Wait-ApiHealthy $playerProc $port $token 90
    $startupMs = [math]::Round($swStartup.Elapsed.TotalMilliseconds, 1)
    if ($null -eq $streamer) { $streamer = Start-SuiteStream $playerProc.Id $port $token } else { Switch-StreamTarget $streamer $playerProc.Id $port $token "reCart GrandTour" }

    Send-HubEvent "step" "B01" "Cold AOT Settle" "running" "Waiting for local AOT cache DLL before input beats"
    $swAot = [System.Diagnostics.Stopwatch]::StartNew(); $aotDlls = @()
    for ($i = 0; $i -lt 150; $i++) {
        if ($playerProc.HasExited) { throw "Player exited during AOT compile ($($playerProc.ExitCode))" }
        $aotDlls = @(Get-ChildItem (Join-Path $dataDir "cache") -Filter *.Generated.dll -Recurse -ErrorAction SilentlyContinue)
        if ($aotDlls.Count -gt 0) {
            $tCheck = Invoke-Api $port $token "GET" "/v1/tree"
            if (-not (Find-NodeById $tCheck.data "AotCompile_Progress")) { break }
        }
        Start-Sleep -Milliseconds 300
    }
    $aotSettleMs = [math]::Round($swAot.Elapsed.TotalMilliseconds, 1)
    if ($aotDlls.Count -lt 1) { throw "Cold AOT cache DLL missing under $dataDir\cache after settle wait" }
    Start-Sleep -Milliseconds 400

    Send-HubEvent "step" "B02" "Demo Movement + Audio" "running" "Capturing live delta on RIGHT/DOWN inputs"
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngBefore; snapshotFormat = "png" } | ConvertTo-Json)
    Send-HubEvent "input" "B02" "Move RIGHT/DOWN" "info" "Holding RIGHT and DOWN" "key" "RIGHT+DOWN"
    for ($k = 0; $k -lt 3; $k++) {
        $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "RIGHT" } | ConvertTo-Json); Start-Sleep -Milliseconds 120
        $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "DOWN" } | ConvertTo-Json); Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Milliseconds 300
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngAfter; snapshotFormat = "png" } | ConvertTo-Json)
    $diffCount = Get-PngPixelDiffCount $pngBefore $pngAfter
    if ($diffCount -lt 25 -or (Get-FileSha256 $pngBefore) -eq (Get-FileSha256 $pngAfter)) { throw "Anti-stale live-delta failed (diffCount=$diffCount)" }

    Send-HubEvent "step" "B03" "F3 HUD & F1 Drawer" "running" "Checking F3 HUD, Overscan 0->8->16->0, Tab_System, v$Version"
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F3" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $vidTree = Invoke-Api $port $token "GET" "/v1/tree"
    if (-not (Find-NodeById $vidTree.data "ExtraRow_OverscanCrop")) { throw "F1 Video tab missing ExtraRow_OverscanCrop" }
    if (Find-NodeById $vidTree.data "ExtraRow_FastForward") { throw "Duplicate ExtraRow_FastForward still present in F1 Video tab" }
    foreach ($expectedCrop in @("8", "16", "0")) {
        $null = Invoke-Api $port $token "POST" "/v1/pointer" (@{ action = "click"; targetId = "ExtraRow_OverscanCrop" } | ConvertTo-Json); Start-Sleep -Milliseconds 150
        $ovCheck = Find-NodeById (Invoke-Api $port $token "GET" "/v1/tree").data "ExtraRow_OverscanCrop"
        if (-not $ovCheck -or $ovCheck.text -notlike "*$expectedCrop*") { throw "OverscanCrop cycle expected '$expectedCrop', got '$($ovCheck.text)'" }
    }
    $null = Invoke-Api $port $token "POST" "/v1/pointer" (@{ action = "click"; targetId = "Video_Toggle_Overscan" } | ConvertTo-Json); Start-Sleep -Milliseconds 150
    $vidOvCheck = Find-NodeById (Invoke-Api $port $token "GET" "/v1/tree").data "Video_Toggle_Overscan"
    if (-not $vidOvCheck -or $vidOvCheck.text -notlike "*8*") { throw "Video_Toggle_Overscan expected '8', got '$($vidOvCheck.text)'" }
    $null = Invoke-Api $port $token "POST" "/v1/pointer" (@{ action = "click"; targetId = "Tab_System" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $sysTree = Invoke-Api $port $token "GET" "/v1/tree"
    if (-not (Find-NodeById $sysTree.data "ExtraRow_Region")) { throw "F1 System tab missing 'ExtraRow_Region'" }
    if (Find-NodeById $sysTree.data "ExtraRow_CutsceneSkip") { throw "Duplicate ExtraRow_CutsceneSkip still present in F1 System tab" }
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngHud; snapshotFormat = "png" } | ConvertTo-Json)
    foreach ($tabId in @("Tab_Audio", "Tab_Controls", "Tab_Keybinds", "Tab_DiscMedia")) {
        $null = Invoke-Api $port $token "POST" "/v1/pointer" (@{ action = "click"; targetId = $tabId } | ConvertTo-Json); Start-Sleep -Milliseconds 120
    }
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 200

    Send-HubEvent "step" "B04" "Resize + F11 + F5/F9" "running" "Verifying pillarbox resize, F11 fullscreen, and QuickSave/Load"
    $sdlHwnd = [S2Win32]::FindVisibleByPid($playerProc.Id)
    if ($sdlHwnd -eq [IntPtr]::Zero) { throw "Visible SDL3 HWND not found for PID $($playerProc.Id)" }
    [void][S2Win32]::MoveWindow($sdlHwnd, 20, 20, 1440, 620, $true); Start-Sleep -Milliseconds 350
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngResize; snapshotFormat = "png" } | ConvertTo-Json)
    Assert-PillarboxAspect $pngResize
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F11" } | ConvertTo-Json); Start-Sleep -Milliseconds 350
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngFull; snapshotFormat = "png" } | ConvertTo-Json)
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F11" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F5" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F9" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $playerRamMb = [math]::Round(($playerProc.WorkingSet64 / 1MB), 1)
    Close-PlayerCleanly $playerProc $port $token
    $playerProc = $null

    $saves = @(Get-ChildItem $saveDir -Filter *.slot0.savstate -ErrorAction SilentlyContinue)
    if ($saves.Count -lt 1) { throw "QuickSave .slot0.savstate missing under $saveDir" }
    $wavPeak = Assert-WavAudible $wavPath
    $playerLog = Get-Content (Join-Path $logDir "recart.player.log") -Raw
    if ($playerLog -notmatch "QuickSave saved" -or $playerLog -notmatch "QuickLoad restored") { throw "QuickSave/QuickLoad confirmation missing from player log" }
    if ($playerLog -notmatch "Fullscreen -> True" -or $playerLog -notmatch "Fullscreen -> False") { throw "F11 fullscreen toggle transitions missing from player log" }
    if ($playerLog -match "AudioDroppedSamples=[1-9]" -or $playerLog -match "DroppedSampleCount=[1-9]") { throw "Non-zero audio drops detected in player log" }

    # Launch 3: Cross-Restart Persistence + Dynamic reEngine Routing
    $p3Port = Get-FreeTcpPort; $p3Token = "recart_l3_" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    Send-HubEvent "step" "L3-01" "Cross-Restart Persistence" "running" "Relaunching against same data-dir to confirm saved F1 settings"
    $playerProc = Start-Process $playerExe -PassThru -WorkingDirectory $autoAppDir -ArgumentList @("--demo-cart", "--automation-port", "$p3Port", "--automation-token", $p3Token, "--data-dir", $dataDir, "--log-dir", $logDir, "--save-dir", $saveDir)
    Wait-ApiHealthy $playerProc $p3Port $p3Token 60
    Switch-StreamTarget $streamer $playerProc.Id $p3Port $p3Token "reCart Persistence"
    $settingsFile = @((Join-Path $dataDir "config\settings.video.json"), (Join-Path $dataDir "settings.video.json")) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $settingsFile) { throw "F1 persistence settings.video.json missing after restart under '$dataDir'" }
    $null = Invoke-Api $p3Port $p3Token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 250
    $p3Tree = Invoke-Api $p3Port $p3Token "GET" "/v1/tree"
    $ovNode = Find-NodeById $p3Tree.data "Video_Toggle_Overscan"
    if (-not $ovNode -or $ovNode.text -notlike "*8*") { throw "Expected persisted Video_Toggle_Overscan 8px across restart, got '$($ovNode.text)'" }
    Close-PlayerCleanly $playerProc $p3Port $p3Token
    $playerProc = $null

    $personalDir = Split-Path -Parent $root
    $reEngineExe = Find-HighestReEngineExe $personalDir
    $routedPng = Join-Path $sbArt "recart_routed_reengine.png"; $routedMs = 0.0
    if ($reEngineExe) {
        Write-Host "[S2-07] Routing clean-room .recomp via '$reEngineExe' --open --wait -> staged engines\reCart\v$Version..."
        $demoRecomp = Join-Path $dataDir "demo\ironframe-outerfortress-cart-01.recomp"
        $reData = Join-Path $sbD "reEngineData\data"; $reLog = Join-Path $sbD "reEngineData\logs"; New-Item -ItemType Directory -Force $reData, $reLog | Out-Null
        $swRoute = [System.Diagnostics.Stopwatch]::StartNew()
        $pRoute = Start-Process $reEngineExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--open", $demoRecomp, "--engines-dir", $enginesDir, "--data-dir", $reData, "--log-dir", $reLog, "--frames", "12", "--headless", "--dump-screenshot", $routedPng, "--wait")
        $routedMs = [math]::Round($swRoute.Elapsed.TotalMilliseconds, 1)
        if ($pRoute.ExitCode -ne 0 -or -not (Test-Path $routedPng) -or (Get-Item $routedPng).Length -lt 500) { throw "reEngine routing failed" }
    }

    $perfPath = Join-Path $devDir "docs\tests\e2e_perf_history.json"
    if (Test-Path -LiteralPath $perfPath) {
        $hist = Get-Content -LiteralPath $perfPath -Raw | ConvertFrom-Json
        $runEntry = [ordered]@{
            runId = "e2e-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmm")
            timestampUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            version = $Version
            isBase = ($null -eq $hist.baseVersion -or @($hist.runs).Count -eq 0)
            hostLoadBand = $hostLoadStart.band
            verdict = "PASS"
            metrics = [ordered]@{ startupToHealthyMs = $startupMs; coldAotCompileMs = $aotSettleMs; routedHeadlessMs = $routedMs; playerWorkingSetMb = $playerRamMb; mediaGateHealthyMs = [math]::Round($swL1.Elapsed.TotalMilliseconds, 1) }
            stepTimingsMs = [ordered]@{ emptyMediaGate = [math]::Round($swL1.Elapsed.TotalMilliseconds, 1); coldAotSettle = $aotSettleMs; reEngineRoute12Frames = $routedMs }
            notes = "3-launch consolidated Grand Tour via exebox-stream ($HubUrl)"
        }
        if ($runEntry.isBase) { $hist.baseVersion = $Version }
        $hist.runs = @($hist.runs) + $runEntry
        [System.IO.File]::WriteAllText($perfPath, ($hist | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    }

    Send-HubEvent "step" "DONE" "Stage 2 E2E Complete" "pass" "All 3 launches verified (diffCount=$diffCount, wavPeak=$wavPeak, saves=$($saves.Count))"
    Write-Host "STAGE 2 E2E PASSED (aotDll=$($aotDlls[0].Name), startupMs=$startupMs, aotMs=$aotSettleMs, diffCount=$diffCount, wavPeak=$wavPeak, saves=$($saves.Count), hostLoad=$($hostLoadStart.band), audioDrops=0)."
}
finally {
    if ($streamer -and $streamer.Process -and -not $streamer.Process.HasExited) { try { & cmd.exe /c ("taskkill /T /F /PID " + $streamer.Process.Id + " >nul 2>&1"); $streamer.Process.WaitForExit(2000) | Out-Null } catch {} }
    if ($streamer -and $streamer.ControlPortFile -and (Test-Path -LiteralPath $streamer.ControlPortFile)) { Remove-Item -LiteralPath $streamer.ControlPortFile -Force -ErrorAction SilentlyContinue }
    if ($playerProc -and -not $playerProc.HasExited) { try { $playerProc.Kill(); $playerProc.WaitForExit(2000) | Out-Null } catch {} }
    if ($wizProc -and -not $wizProc.HasExited) { try { $wizProc.Kill(); $wizProc.WaitForExit(2000) | Out-Null } catch {} }
    if (Test-Path $sbArt) { Copy-Item (Join-Path $sbArt "*") $ArtifactDir -Force -ErrorAction SilentlyContinue }
    Set-SandboxEnv $false; Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
