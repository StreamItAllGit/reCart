# reCart — Operations & Maintenance (`v0.1.3` Baseline -> `v0.2.0` Active)

Operational runbook covering repository layout, pinned SDKs (`Foundation v0.5.5` + `reEngine v0.6.2`), build/test commands, canonical 2-tier log roots, installer/AOT safety guards, Stage 2 4-drive E2E, and the `v0.1.x` Standard 28 §3.1 2-channel `--prerelease` workflow for **`reCart`**.

---

## 1. Repository & Target Install Topology

- **Tier A 4-Folder Workspace (`<PROJECTS_ROOT>\Personal\reCartOne\`):** Root contains strictly `Archive\`, `Builds\`, `Dev\`, `Releases\` (dual-junctioned at `<RELEASES_HUB>\Personal\reCartOne\` and `<RELEASES_HUB>\Personal\reCart\`), `active.state`, `AGENTS.md`, `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, Root Quartet (`README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`), and outer `.git\`.
- **Canonical Solution & Assets (`Dev\reCart.slnx`):** All 8 assemblies stay `reCart.*` (`reCart.Contracts`, `Core`, `Compiler`, `Decomp`, `Player`, `Launcher`, `Installer`, `Tests`), with `Dev\assets\reCart.{svg,png,ico}` as the sole icon set.
- **Target Installed Engine Root:** `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\reCart\v<Ver>\` — `{reCart.exe, .ready, engine.manifest.json, THIRDPARTY_LICENSES.txt, scripts\interactive\}` + `app\` (`reCart.Player.exe`, `reCart.*.dll`, `reEngine.{Core,Host}.dll`, `Foundation.*.dll`, `SDL3.dll`, `LICENSE`, `tools\{7z.exe,7z.dll,LICENSE-7ZIP.txt}`), `data\` (`config\`, `rom\`, `saves\`, `cache\`), and `logs\`. Never place `AGENTS.md` inside `Builds\v<Version>\`, `Releases\`, or deployed production folders.

---

## 2. Pinned SDKs (`Dev\lib\`) & Build Configuration

- **Pinned Read-Only SDKs (`v0.1.3`):**
  - `Foundation v0.5.5` (`<SHARED_LIBS>\Foundation\v0.5.5\`): `Foundation.Core`, `Foundation.Retro` (`MediaContainerExtractor`), `Foundation.Installer`, `Foundation.Launcher`, `Foundation.UI`, `Foundation.Server`, `Foundation.Sidecars`, Debug/Test-only `Foundation.Automation`, plus `sdk-manifest.json`.
  - `reEngine v0.6.2` (`<SHARED_LIBS>\reEngine\v0.6.2\`): `reEngine.Core`, `reEngine.Host` (`HostSessionRunner.SetAotCompileState`/`SetMediaGateState`/`UpdateDrawerDescriptor`, `HostAotCompileOverlayView`, `FirstRunMediaGateView` with `OnBrowseMediaRequested`/`OnBrowseModuleRequested`/`OnFileDropped`/`OnDemoCartRequested`, `HostSettingsStore` `SettingsDataDirectory` live `F1` persistence, `0 px` jitter `F3HudOverlayView`, `HostInputMapper` 2-frame synthetic tap auto-release, `HostViewportGeometry` 5-aspect modes, GL `0x0`/`0x8CD5` & `TryReadSurfaceRect`/`FlushAndSyncForSnapshot`, 4-port SDL3 gamepads, 3-tier `settings.controls.json`, interactive `F1`/`F3`, `F5`/`F9`/`F11`, 3-way `reCart`/`reCartOne`/`reNes` aliases, `hostProtocolVersion = "0.6.2"`, `minSuiteVersion = "0.6.0"`), `SDL3.dll`, plus `reEngine.sdk-manifest.json`.
  - `SDL3.dll`, `7z.exe`/`7z.dll`, and installer `.exe` binaries stay excluded from Git and are staged only during `Builds\` / `Releases\` packaging.
- **Deterministic Zero-PII Props:** `Dev\Directory.Build.props` enforces `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, `<Product>reCart</Product>`, and Release `<PathMap>` + `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>`.

---

## 3. Build, `94/94` Isolated Test & Audit Commands

```powershell
# Isolated Release + Debug/Automation builds and 94/94 xUnit test runs (0 warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet build Dev\reCart.slnx -c Release /warnaserror --disable-build-servers -p:UseSharedCompilation=false
dotnet test Dev\reCart.slnx -c Release --no-build
dotnet build Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true /warnaserror --disable-build-servers -p:UseSharedCompilation=false
dotnet test Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true --no-build
```

Verified 10 modular xUnit classes (`94/94` tests): `Cpu6502Tests` (`14`), `Mapper0To3Tests` (`9`), `Ppu2C02AndApuTests` (`12`), `CompilerAndAotCacheTests` (`3`), `DecompD4ValidationTests` (`12`), `ContractsAndManifestTests` (`15`), `PlayerQuickSaveAndInputTests` (`14`), `PlayerV013PrelaunchTests` (`3`), `InstallerManifestAndSideBySideTests` (`5`), and `RecipeAndLockstepTests` (`7`).

```powershell
# Static Dev Bible audit + Tier A 4-folder & dossier checks
python "$env:USERPROFILE\.gemini\config\skills\audit\scripts\audit_scanner.py" ".\Dev" --format markdown
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.gemini\config\skills\graduate\scripts\scaffold_project.ps1" `
  -ProjectName reCartOne -Group Personal -AuditOnly
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.gemini\config\skills\scope\scripts\audit_dossier.ps1" `
  -ProjectPath "."
```

---

## 4. Canonical 2-Tier Forensic Log Roots & Open Reports

Before diagnosing build, runtime, or sandbox failures, sweep the preceding 20–50 lines across both log roots:
1. **Canonical Installed Engine Logs (Strictly Read-Only):** `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\reCart\v<Version>\logs\` (`recart.player.log`) *(plus `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\logs\` for router handoffs)*
2. **Dev & Isolated Runner Logs:** `<RepoRoot>\Dev\logs\`

- **Tracked Open Reports (`3` Open -> Scheduled for `v0.2.0+`):**
  - `Dev\docs\reports\0.1.0.2026-11-20_0115.-.d5-v2-schema-bridge.md` *(v0.1.1 added `matchedBinaryCount`/`relativePath`/`fileSizeBytes`/`sha256Hex`; `isVerified` remains queued for full `D5` closure)*
  - `Dev\docs\reports\0.1.0.2026-11-20_0418.-.reslate-host-ui-behavior-parity.md` *(settings persistence, Esc quit, UI parity)*
  - `Dev\docs\reports\0.1.0.2026-11-21_0518.-.neuroplay-0.5-recart-nes-recomp-integration.md` *(v0.3.0 / v0.7.0 integration spec)*
  - *(Archived in `Dev\docs\reports\archive\`: `bug_preload_dir_media_gate_and_aot_frame_loop_v0.1.1.2026-11-23.md` [resolved in `v0.1.2`] and `v0.1.3.2026-11-23_1441.-.stage2-aot-esc-and-installer-gate.md` [resolved in `v0.1.3` — `94/94` PASS].)*

---

## 5. Release Packaging, Dual-Installer Transition & Stage 2 Sandbox E2E

1. **Setup `reEngine`-Root Collision Guard (`IsReEngineDirectory` + `"suiteId": "reEngine"`):** `ReCartSuitePlacementHelper` inspects `IsReEngineDirectory`, `appId` / `install-manifest.json` (`IsOwnInstallManifest`), and poisoned `HKCU` entries so Setup nests cleanly under `engines\reCart\v<Ver>\` and never overwrites `reEngine.exe`.
2. **Cold Local AOT Pipe Isolation, Async Progress Overlay, `.7z` Tools & Media Gate:** `ColdAotCacheCompiler` passes `--disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false` while `LifterJumpTableScanner` lifts `recipe.JumpTables` (`$C6B7`) across fixed/switchable banks and `ReCartSessionHost` opens `HostSessionRunner` immediately updating `HostAotCompileOverlayView`; `CartMachineHost.StepCpuAndApuCycle` paces AOT blocks and `_pendingAotNmi`/`_pendingAotIrq` cycle-by-cycle; `CartMediaArchiveLoader` + `Foundation.Retro.MediaContainerExtractor` use staged `app\tools\{7z.exe,7z.dll,LICENSE-7ZIP.txt}` for fail-closed `.7z` ROM loading/staging with ephemeral `finally` cleanup; `ReCartSessionHost.MediaGateHandlers` preserves `<DataDir>\rom\` discovery, wires `FirstRunMediaGateView` (`Browse`, `Select .recomp Module...`, `Drag-Drop`, `Demo Cart`, external ROM staging, standalone raw-ROM clean-room guidance, interactive `UnliftedPcTrapException` trap catch), and persists `F1` Video/Audio edits via `HostSettingsStore` (`SettingsDataDirectory`); `build_release.ps1` and Setup write `.ready` + `schemaVersion: 2` `engine.manifest.json` (`hostProtocolVersion: "0.6.2"`, `minSuiteVersion: "0.6.0"`, `legacyEngineIds: ["reCartOne", "reNes"]`) atomically (with `audit_installer.ps1` `Passed: True, FindingCount: 0`).
3. **Dual-Binary Release `ExitCode = 2` Gate:** **Both** `reCart.Player` and `reCart.Installer` compile `Foundation.Automation` / `Foundation.UI` only under `$(Configuration) == 'Debug' Or '$(ReCartTestAutomation)' == 'true'`, failing closed with `ExitCode = 2` on `--automation-port` in shipping `Release` EXEs.
4. **7-File `Auto` Mode (`v0.1.0–v0.4.0`) -> 8-File Dual-Installer (`v0.5.0`, Standards `27/28`):** Per Standards 27 & 28 rollout rules, `v0.1.0–v0.4.0` build the valid 7-file `Releases\` payload (`-ReleaseLayoutMode Auto`). Starting in **`v0.5.0`** (`reEngine v0.8.0` + `Foundation.Installer`), `build_release.ps1` emits the 8-file bundle (`reCart-Setup-v<Ver>-win-x64.exe` Online Setup + `reCart-Offline-Setup-v<Ver>-win-x64.exe` Offline Setup + `-Portable.zip`, both passing purity/signing/`ExitCode = 2`).
5. **Stage 2 4-Drive Sandbox E2E (`Dev\scripts\run_stage2_sandbox_e2e.ps1`):** Uses UDP `5004` Desktop Preflight + `exebox-stream` auto-streaming and `"-t:Rebuild,Publish"` with `-p:ReCartTestAutomation=true`. **Scheduled `v0.2.0` Script Hardening:** Replace hardcoded `reEngine` path & fixed ports with dynamic highest-SemVer `reEngine\Builds\v*` discovery, ephemeral loopback ports, and `Foundation\Dev\tools\run_sandbox_e2e.py` asserting **`0` host filesystem & `HKCU` registry diff** after uninstall (plus loopback HTTP mock feed SHA-256/`ExitCode = 4` checks in `v0.5.0`).
6. **Standard 28 §3.1 2-Channel Releases & Standard 36 Manual Notion Policy:** Publish exclusively to **`GitHub Releases`** (`--prerelease` for `0.x` after `requireHumanReviewBeforePush` review) + **`Discord /post`**. Never upload binaries, installers, or `.7z` archives to cloud drives (`Standard 28 §3.1`), and sync Notion strictly manually on demand (`Standard 36`).
