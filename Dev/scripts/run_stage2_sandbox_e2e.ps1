#Requires -Version 5.1
# @module: Dev/scripts/run_stage2_sandbox_e2e.ps1
# @purpose: 12-point 4-drive Stage 2 GUI/AOT/Audio/QuickSave/F11/Resize/Wizard/Routing E2E verification with exebox-stream UDP 5004 auto-streaming.
# @depends-on: Dev/reCart.slnx, reEngine Builds\, exebox-stream, audit_installer.ps1
# @exports: Stage 2 E2E harness (v0.1.0+)

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipInstallerSandbox,
    [string]$ArtifactDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
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
$sbArt = Join-Path $sbD "artifacts"
New-Item -ItemType Directory -Force $sbC, $sbD, $sbRem, $sbDisc, $sbProf, $enginesDir, $dataDir, $logDir, $saveDir, $sbArt | Out-Null

function Set-SandboxEnv([bool]$EnableSandbox) {
    Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue
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

$streamer = $null; $playerProc = $null; $wizProc = $null
try {
    $udp = Get-NetUDPEndpoint -LocalPort 5004 -ErrorAction SilentlyContinue | Where-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName -ieq "vlc" }
    Write-Host "[S2-01] Desktop UDP:5004 VLC bound = $(@($udp).Count -gt 0)"
    $streamScript = Join-Path $env:USERPROFILE ".gemini\config\skills\exebox-stream\scripts\start_exebox_vlc_stream.ps1"

    Write-Host "[S2-02] Publishing ReCartTestAutomation (-t:Rebuild,Publish) into sandbox..."
    $autoEngDir = Join-Path $enginesDir "reCart\v$Version"; $autoAppDir = Join-Path $autoEngDir "app"
    New-Item -ItemType Directory -Force $autoAppDir | Out-Null
    & dotnet publish (Join-Path $devDir "src\reCart.Player\reCart.Player.csproj") -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:ReCartTestAutomation=true -p:PublishSingleFile=false -o $autoAppDir /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Publish ReCartTestAutomation player failed ($LASTEXITCODE)" }
    & dotnet publish (Join-Path $devDir "src\reCart.Launcher\reCart.Launcher.csproj") -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:PublishSingleFile=true -o $autoEngDir /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Publish reCart.Launcher failed ($LASTEXITCODE)" }
    $sdlSrc = Join-Path $devDir "lib\SDL3.dll"; if (Test-Path $sdlSrc) { Copy-Item $sdlSrc (Join-Path $autoAppDir "SDL3.dll") -Force }
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $autoAppDir "LICENSE") -Force

    if (-not $SkipInstallerSandbox) {
        $instExe = Join-Path $root "Releases\v$Version\reCart-Setup-v$Version-win-x64.exe"
        if (-not (Test-Path $instExe)) { throw "Installer missing at $instExe" }
        Write-Host "[S2-03] Running Release ExitCode=2 probe, 4-pillar silent install, and Setup Wizard GUI automation..."
        $env:APP_ISOLATED_TEST = "1"
        $pProbe = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--automation-port", "5199")
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
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root "Builds\v$Version"), $wizPayload, [System.IO.Compression.CompressionLevel]::Fastest, $false)
        & dotnet publish (Join-Path $devDir "src\reCart.Installer\reCart.Installer.csproj") -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:ReCartTestAutomation=true -p:InstallerPayloadZip="$wizPayload" -o $wizOut /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Publish test Setup Wizard failed ($LASTEXITCODE)" }
        $wizExe = Join-Path $wizOut "reCart-Setup-v$Version-win-x64.exe"
        $wPort = 52479; $wToken = "recart_wiz_" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
        $wizProc = Start-Process $wizExe -PassThru -ArgumentList @("--automation-port", "$wPort", "--automation-token", $wToken, "--suite-dir", $suiteRoot, "--install-dir", $suiteRoot, "--data-dir", $instData, "--no-launch")
        for ($i = 0; $i -lt 50; $i++) { try { if ((Invoke-Api $wPort $wToken "GET" "/v1/health").isPassed) { break } } catch {}; Start-Sleep -Milliseconds 200 }
        $wizStreamer = $null
        if (Test-Path $streamScript) { $wizStreamer = Start-Process "powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $streamScript, "-AutomationPort", "$wPort", "-AutomationToken", $wToken, "-TargetPid", "$($wizProc.Id)", "-SnapshotDir", (Join-Path $sandbox "stream_tmp"), "-UdpPort", "5004", "-Fps", "20") }
        $tree = Invoke-Api $wPort $wToken "GET" "/v1/tree"
        $dirNode = Find-NodeById $tree.data "Wizard_InstallDir"
        if (-not $dirNode -or $dirNode.text -notlike "*engines\reCart\v$Version") { throw "Wizard auto-sort failed: '$($dirNode.text)'" }
        $null = Invoke-Api $wPort $wToken "POST" "/v1/snapshot" (@{ filePath = (Join-Path $sbArt "recart_installer_wizard.png"); snapshotFormat = "png" } | ConvertTo-Json)
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
        Write-Host "[S2-03] Wizard install succeeded (button=$($btn.text)); clicking Finish..."
        try { $null = Invoke-Api $wPort $wToken "POST" "/v1/pointer" (@{ action = "click"; targetId = "Wizard_PrimaryButton" } | ConvertTo-Json) } catch { if (-not $wizProc.WaitForExit(5000) -or $wizProc.ExitCode -ne 0) { throw } }
        if ($wizStreamer -and -not $wizStreamer.HasExited) { try { & cmd.exe /c ("taskkill /T /F /PID " + $wizStreamer.Id + " >nul 2>&1"); $wizStreamer.WaitForExit(1500) | Out-Null } catch {} }
        if (-not $wizProc.WaitForExit(10000) -or $wizProc.ExitCode -ne 0) { throw "Wizard GUI exited abnormally ($($wizProc.ExitCode))" }
        if (-not (Test-Path (Join-Path $suiteRoot "reEngine.exe")) -or -not (Test-Path (Join-Path $autoEngDir ".ready")) -or -not (Test-Path (Join-Path $autoEngDir "engine.manifest.json"))) { throw "Wizard post-install check failed" }
        $null = Start-Process $instExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--silent", "--uninstall", "--suite-dir", $suiteRoot, "--data-dir", $instData)
        & dotnet publish (Join-Path $devDir "src\reCart.Player\reCart.Player.csproj") -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:ReCartTestAutomation=true -p:PublishSingleFile=false -o $autoAppDir /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
        & dotnet publish (Join-Path $devDir "src\reCart.Launcher\reCart.Launcher.csproj") -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:PublishSingleFile=true -o $autoEngDir /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
        if (Test-Path $sdlSrc) { Copy-Item $sdlSrc (Join-Path $autoAppDir "SDL3.dll") -Force }
    }

    $nowUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    [System.IO.File]::WriteAllText((Join-Path $autoEngDir ".ready"), (@{ engineId = "reCart"; legacyEngineIds = @("reCartOne","reNes"); appId = "recart"; version = $Version; hostProtocolVersion = "0.5.0"; completedAtUtc = $nowUtc } | ConvertTo-Json))
    [System.IO.File]::WriteAllText((Join-Path $autoEngDir "engine.manifest.json"), (@{ schemaVersion = 2; manifestType = "engine-manifest"; engineId = "reCart"; legacyEngineIds = @("reCartOne","reNes"); appId = "recart"; displayName = "reCart"; version = $Version; hostProtocolVersion = "0.5.0"; entryExecutable = "reCart.exe"; backendId = "opengl"; executionTierMode = "staticAotOnly" } | ConvertTo-Json))
    $relPlayer = Join-Path $root "Builds\v$Version\app\reCart.Player.exe"
    if (Test-Path $relPlayer) {
        $env:APP_ISOLATED_TEST = "1"
        $pr = Start-Process $relPlayer -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--automation-port", "5198", "--data-dir", $dataDir, "--log-dir", $logDir)
        if ($pr.ExitCode -ne 2) { throw "Release player automation reject expected 2, got $($pr.ExitCode)" }
    }

    Set-SandboxEnv $true
    $port = 52471; $token = "recart_s2_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $wavPath = Join-Path $sbArt "recart_live_audio.wav"; $pngBefore = Join-Path $sbArt "recart_gameplay_before.png"
    $pngAfter = Join-Path $sbArt "recart_gameplay_after.png"; $pngHud = Join-Path $sbArt "recart_f1_f3_live.png"
    $pngResize = Join-Path $sbArt "recart_resized_aspect.png"; $pngFull = Join-Path $sbArt "recart_fullscreen_live.png"

    Write-Host "[S2-04] Launching live SDL3/OpenGL --demo-cart (cold AOT -> cache + WASAPI + exebox-stream)..."
    $playerExe = Join-Path $autoAppDir "reCart.Player.exe"
    $playerProc = Start-Process $playerExe -PassThru -WorkingDirectory $autoAppDir -ArgumentList @("--demo-cart", "--backend", "opengl", "--aspect-mode", "nativePar", "--automation-port", "$port", "--automation-token", $token, "--data-dir", $dataDir, "--log-dir", $logDir, "--save-dir", $saveDir, "--dump-audio", $wavPath)
    for ($i = 0; $i -lt 90; $i++) { if ($playerProc.HasExited) { throw "Live player exited ($($playerProc.ExitCode))" }; try { if ((Invoke-Api $port $token "GET" "/v1/health").isPassed) { break } } catch {}; Start-Sleep -Milliseconds 300 }
    if (Test-Path $streamScript) {
        $streamer = Start-Process "powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $streamScript, "-AutomationPort", "$port", "-AutomationToken", $token, "-TargetPid", "$($playerProc.Id)", "-SnapshotDir", (Join-Path $sandbox "stream_tmp"), "-UdpPort", "5004", "-Fps", "20")
        Write-Host "[S2-05] Streaming live GUI via exebox-stream -> udp://@127.0.0.1:5004"
    }

    Start-Sleep -Milliseconds 500
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngBefore; snapshotFormat = "png" } | ConvertTo-Json)
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "RIGHT" } | ConvertTo-Json)
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "DOWN" } | ConvertTo-Json)
    Start-Sleep -Milliseconds 700
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngAfter; snapshotFormat = "png" } | ConvertTo-Json)
    $diffCount = Get-PngPixelDiffCount $pngBefore $pngAfter
    Write-Host "[S2-06] Gameplay movement diffCount=$diffCount"
    if ($diffCount -lt 25) { throw "Expected sprite movement diffCount >= 25, got $diffCount" }

    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F3" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
    $null = Invoke-Api $port $token "POST" "/v1/pointer" (@{ action = "click"; targetId = "Tab_System" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngHud; snapshotFormat = "png" } | ConvertTo-Json)
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F1" } | ConvertTo-Json); Start-Sleep -Milliseconds 150
    $sdlHwnd = [S2Win32]::FindVisibleByPid($playerProc.Id)
    if ($sdlHwnd -eq [IntPtr]::Zero) { throw "Visible SDL3 HWND not found for PID $($playerProc.Id)" }
    [void][S2Win32]::MoveWindow($sdlHwnd, 20, 20, 1440, 620, $true); Start-Sleep -Milliseconds 350
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngResize; snapshotFormat = "png" } | ConvertTo-Json)
    Assert-PillarboxAspect $pngResize
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F11" } | ConvertTo-Json); Start-Sleep -Milliseconds 300
    $null = Invoke-Api $port $token "POST" "/v1/snapshot" (@{ filePath = $pngFull; snapshotFormat = "png" } | ConvertTo-Json)
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F11" } | ConvertTo-Json); Start-Sleep -Milliseconds 200
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F5" } | ConvertTo-Json); Start-Sleep -Milliseconds 150
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "F9" } | ConvertTo-Json); Start-Sleep -Milliseconds 150
    $null = Invoke-Api $port $token "POST" "/v1/keyboard" (@{ action = "press"; key = "ESC" } | ConvertTo-Json)
    if (-not $playerProc.WaitForExit(10000) -or $playerProc.ExitCode -ne 0) { throw "Player did not exit 0 cleanly on ESC" }

    $aotDlls = @(Get-ChildItem (Join-Path $dataDir "cache") -Filter *.Generated.dll -Recurse -ErrorAction SilentlyContinue)
    if ($aotDlls.Count -lt 1) { throw "Cold AOT cache DLL missing under $dataDir\cache" }
    $saves = @(Get-ChildItem $saveDir -Filter *.slot0.savstate -ErrorAction SilentlyContinue)
    if ($saves.Count -lt 1) { throw "QuickSave .slot0.savstate missing under $saveDir" }
    $wavPeak = Assert-WavAudible $wavPath
    $playerLog = Get-Content (Join-Path $logDir "recart.player.log") -Raw
    if ($playerLog -notmatch "QuickSave saved" -or $playerLog -notmatch "QuickLoad restored") { throw "QuickSave/QuickLoad confirmation missing from player log" }
    if ($playerLog -notmatch "Fullscreen -> True" -or $playerLog -notmatch "Fullscreen -> False") { throw "F11 fullscreen toggle transitions missing from player log" }
    if ($playerLog -match "AudioDroppedSamples=[1-9]" -or $playerLog -match "DroppedSampleCount=[1-9]") { throw "Non-zero audio drops detected in player log" }

    $personalDir = Split-Path -Parent $root
    $reEngineExe = @((Join-Path $personalDir "reEngine\Builds\Latest\reEngine.exe"), (Join-Path $personalDir "reEngine\Builds\v0.6.2\reEngine.exe")) | Where-Object { Test-Path $_ } | Select-Object -First 1
    $routedPng = Join-Path $sbArt "recart_routed_reengine.png"
    if ($reEngineExe) {
        Write-Host "[S2-07] Routing clean-room .recomp via reEngine --open --wait -> staged engines\reCart\v$Version..."
        $demoRecomp = Join-Path $dataDir "demo\ironframe-outerfortress-cart-01.recomp"
        $reData = Join-Path $sbD "reEngineData\data"; $reLog = Join-Path $sbD "reEngineData\logs"; New-Item -ItemType Directory -Force $reData, $reLog | Out-Null
        $pRoute = Start-Process $reEngineExe -WindowStyle Hidden -Wait -PassThru -ArgumentList @("--open", $demoRecomp, "--engines-dir", $enginesDir, "--data-dir", $reData, "--log-dir", $reLog, "--frames", "12", "--headless", "--dump-screenshot", $routedPng, "--wait")
        if ($pRoute.ExitCode -ne 0 -or -not (Test-Path $routedPng) -or (Get-Item $routedPng).Length -lt 500) { throw "reEngine routing failed" }
    }
    Write-Host "STAGE 2 E2E PASSED (aotDll=$($aotDlls[0].Name), diffCount=$diffCount, wavPeak=$wavPeak, saves=$($saves.Count), resize+F11+F9+Wizard verified, audioDrops=0)."
}
finally {
    if ($streamer -and -not $streamer.HasExited) { try { & cmd.exe /c ("taskkill /T /F /PID " + $streamer.Id + " >nul 2>&1"); $streamer.WaitForExit(2000) | Out-Null } catch {} }
    if ($playerProc -and -not $playerProc.HasExited) { try { $playerProc.Kill(); $playerProc.WaitForExit(2000) | Out-Null } catch {} }
    if ($wizProc -and -not $wizProc.HasExited) { try { $wizProc.Kill(); $wizProc.WaitForExit(2000) | Out-Null } catch {} }
    if (Test-Path $sbArt) { Copy-Item (Join-Path $sbArt "*") $ArtifactDir -Force -ErrorAction SilentlyContinue }
    Set-SandboxEnv $false; Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
