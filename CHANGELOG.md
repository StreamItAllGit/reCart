# Changelog

All notable changes to **`reCart`** are documented in this file following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and Universal Semantic Versioning (`MAJOR.MINOR.PATCH`).

## [Unreleased]

### Changed
- **Workspace Hygiene (/clean -- 2026-11-20):** Purged 16.84 MB of disposable caches.
- **Hardened 7-Minor Backend Roadmap & RecompBible Sync (/scope):** Re-aligned `ROADMAP.md`, `FEATURES.md`, `README.md`, `AGENTS.md`, `active.state`, `Dev\docs\*`, `Dev\docs\plans\tasklist.md`, and `R&D\Dev\task_graph.json` onto a dedicated single-subsystem backend progression (`v0.2.0` Cooperative Scheduler + Shared `6502 -> IR` / RyuJIT / Zero-RWX -> `v0.3.0` Blip-Buffer DSP + `PAL`/`Dendy` -> `v0.4.0` Two-Phase `ParallelCpu` PPU + GPU Compositor -> `v0.5.0` Commercial Mappers + Vaults/1-Step Undo + 10-Rail + 8-File Dual-Installer -> `v0.6.0` Clean-Room FDS HLE + 6-Chip Expansion Audio + `.re*`/Recipes/Manual -> `v0.7.0` Raw-Media `R04` Harden + `R3` Promoter + Research Rail + Runtime API + 3-Board Proving-Ground Matrix) converging on `v1.0.0` Going Gold (`win-x64`) and `v1.1.0–v5.0.0` horizons (`RecompBible R01–R14` & Standards `27`/`28`), advancing `activeMilestone` to `0.2.0` while `v0.1.0` awaits human sign-off for its 2-channel `--prerelease` push.

## [0.1.3] - 2026-11-23

### Added
- **Instant Startup Window + Background AOT Compile Overlay & Live Settings Persistence (`ReCartSessionHost` + `reEngine.Host v0.6.2`):** Opened `HostSessionRunner` immediately on interactive launches while compiling local AOT modules asynchronously with progress reported onto `HostAotCompileOverlayView`, and wired `SettingsDataDirectory` so in-game `F1` Video/Audio settings persist cleanly across restarts.
- **Bundled `.7z` Archive Extraction & Staging (`CartMediaArchiveLoader` + `Foundation.Retro`):** Staged bundled `7z.exe`/`7z.dll`/`LICENSE-7ZIP.txt` under `app\tools\` and added ephemeral fail-closed `.7z` extraction, SHA-256 discovery in `data\rom\`, and external verified ROM staging.
- **`Select .recomp Module...` Live Loading & Raw-ROM Clean-Room Guidance (`MediaGateHandlers`):** Wired `OnBrowseModuleRequested` and live `.recomp` drops/picks, and replaced ad-hoc raw-ROM crashes with clear clean-room Media Gate guidance (plus interactive `UnliftedPcTrapException` trapping).

### Fixed
- **Clean-Room `recipe.JumpTables` Inline Table Lifting (`LifterJumpTableScanner` + `Lifter6502ToCSharp` + `ColdAotCacheCompiler`):** Passed `recipe.JumpTables` dispatcher addresses into the static 6502 lifter so inline pointer tables following `JSR` dispatchers lift cleanly across fixed and switchable banks without decoding table data as fallthrough code.

### Changed
- Upgraded `reEngine` SDK dependency to `v0.6.2` (`hostProtocolVersion: "0.6.2"`, jitter-free F3 telemetry HUD, deferred synthetic key tap release, Media Gate Browse error propagation) and bumped `reCart` to `0.1.3` (`SharedLibs\reCart\v0.1.3\`), preserving `activeMilestone: 0.2.0`.

## [0.1.2] - 2026-11-23

### Fixed
- **Cycle-Paced Static AOT Execution (`CartMachineHost`):** Dispatched statically lifted 6502 basic blocks and VBlank/IRQ edge interrupts inside `StepCpuAndApuCycle` keyed to CPU cycle budget (instead of one block per end-of-frame), fixing solid-grey video under `staticAotOnly` and matching `interpreterOracle` rendering frame-for-frame.
- **`data\rom\` Preload Discovery (`ReCartSessionHost`):** Fixed `LoadCartInputs` so opening `<InstallDir>\app\*.recomp` scans `<DataDir>\rom\` (plus module `data\rom\` and SHA-256-matched `SuiteMediaTreeWalker` candidates) instead of erroneously overwriting `preloadDir` to `<InstallDir>\app\data\rom\`.
- **Clean-Room Media Gate Activation & Callbacks (`ReCartSessionHost`):** Upgraded `reEngine.Core` / `reEngine.Host` to `v0.6.1` (`Foundation v0.5.5`), opened `FirstRunMediaGateView` automatically whenever a session launches without a verified ROM (`!options.DemoCart`), wired live `Browse`, `Drag & Drop`, and `Launch Demo / Test Cart` callbacks, and replaced unrequested demo-grid fallback with a Deep Slate backdrop and silent audio until media is loaded.

### Changed
- Bumped `reCart` SDK and installer version `0.1.1 -> 0.1.2` (`SharedLibs\reCart\v0.1.2\`), preserving `activeMilestone: 0.2.0`.

## [0.1.1] - 2026-11-23

### Added
- **Clean-Room `aot-recipe.json` `.recomp` Support (`reCart.Contracts`, `reCart.Compiler`):** Added `CartAotRecipeSpec` (`seeds`, `entryAddresses`, `jumpTables`, `trampolines`, `memoryWatches`) and updated `CleanRoomRecompPackager` + `ColdAotCacheCompiler` to validate and locally compile 2-member clean-room recipe archives (`[module.recomp.json, aot-recipe.json]`) alongside existing 3-member lifted archives into `<dataDir>\cache\`.
- **Headerless `.prg` Support (`CartPrgHeaderSynthesizer`, `reCart.Player`):** Added deterministic 16-byte iNES/NES 2.0 header synthesis (`CartPrgHeaderSynthesizer`) and `.prg` preload/checksum resolution in `ReCartSessionHost` (matching any declared entry in `requiredDiscChecksums` before synthesis).
- **Differential Lockstep CLI Runner (`--lockstep-oracle --lockstep-steps <N>`):** Added `--lockstep-steps` CLI option and `ReCartLockstepRunner` comparing AOT module execution against quarantined `InterpreterOracle` (`A, X, Y, SP, PC, P`, `ActivePrgBank`, and `2 KB` WRAM `..`).
- **Standard 18 v2 `d5_byte_match.json` Aliases (`reCart.Decomp`):** Extended `D4ReportSchemaHelpers.ValidateD5Core` to accept `matchedBinaryCount`, `relativePath`, `fileSizeBytes`, and `sha256Hex` alongside v1 keys.

### Changed
- Bumped `reCart` SDK and installer version `0.1.0 -> 0.1.1` (`SharedLibs\reCart\v0.1.1\`), keeping `activeMilestone` pinned at `0.2.0`.

## [0.1.0] - 2026-11-19

### Added
- **Clean `reCart.*` Solution & SDK Cutover:** Built the 8-project `.NET 8.0` solution (`reCart.Contracts`, `reCart.Core`, `reCart.Compiler`, `reCart.Decomp`, `reCart.Player`, `reCart.Launcher`, `reCart.Installer`, `reCart.Tests`) on `Foundation v0.5.2` and `reEngine v0.5.0` (`reEngine.Core`, `reEngine.Host.dll`) with zero-warning deterministic builds (`<Product>reCart</Product>`, `reCart.ico`, zero `Foundation.Automation` or `System.Windows.Forms` leakage in `Release`).
- **Hardened `reCart.Contracts` ABI:** Added extensible `ICartMapper` (`ushort MapperId`, `byte SubmapperId`), `CartRegionTimingProfile` (`Auto`, `Ntsc`, `Pal`, `Dendy`), `CartRomHeaderParser` (iNES 1.0 & NES 2.0 + 512-byte trainer stripping), `ICartMachineRuntime`, `ICartQuickSaveProvider`, `CartPackageManifest` (`hostProtocolVersion: "0.5.0"`, `minSuiteVersion: "0.5.0"`, `supportedEngineTracks: ["0.1.x"]`), and `CartEngineConstants` (`engineId: "reCart"`, `legacyEngineIds: ["reCartOne", "reNes"]`).
- **`RP2A03` CPU, Mappers `0–3`, Dot `2C02` PPU & 5-Channel APU (`reCart.Core`):** Implemented cycle-stepped `Cpu2A03Core` (`56` official opcodes, zero BCD, `JMP ($xxFF)` wrap, `OAM DMA` `513`/`514`-cycle + `DMC` stalls), `$4016/$4017` `0x40` open-bus `CartInputPorts`, Mappers `0/1/2/3` (`Nrom`, `Mmc1`, `UxRom` `$FFD0-$FFD7` bus-conflict safe, `CnRom`), dot-stepped `Ppu2C02Core` (`256x240`, `262` NTSC scanlines, odd-frame dot skip, Loopy scroll, sprite-0 hit & NMI suppression), `Apu2A03Core` (`Pulse 1/2`, `Triangle`, `Noise`, `DMC`), deterministic `CartQuickSaveSerializer`, and quarantined `InterpreterOracle` (`--execution-tier interpreterOracle`).
- **Static `6502 -> C#` Lifter & Cold Local AOT Cache (`reCart.Compiler`):** Implemented `Lifter6502ToCSharp`, `LifterJumpTableScanner`, `CleanRoomRecompPackager` (emitting strictly 3-member clean-room `.recomp` ZIP archives with zero retail ROM bytes or pre-built DLLs), `ColdAotCacheCompiler` (compiling `<dataDir>\cache\<moduleId>.Generated.dll` out-of-process with `--disable-build-servers -p:UseSharedCompilation=false`), fail-closed `UnliftedPcTrapException` under `staticAotOnly`, and `SyntheticCleanRoomCartBuilder`.
- **Clean-Room Decompilation Provider (`reCart.Decomp`):** Implemented `CartDecompPlatformProvider` (`platformId: "nes"`, `D0`–`D5`), `SixTellDetector`, `D4BuildReportValidator` (`D4_CleanSource`, zero `-Wall -Wextra -Wno-unused-parameter` warnings), `CartCleanRoomValidator`, and `CartMediaIntakeInspector`.
- **5-Backend `reEngine.Host v0.5.0` Player (`reCart.Player`):** Wired `ReCartSessionHost` across `OpenGL 3.3 Core`, `Vulkan`, `DirectX 12`, `ParallelCpu`, and `Software`, defaulting to `nativePar` (`8:7`/`4:3` pillarboxed) alongside `fourByThree`, `squareInteger`, `nativeFit`, and `horPlus16By9`, `WASAPI` audio, `F1` 5-tab settings drawer (`Mapper`, `Region`, `Execution Tier`, `Overscan Crop`, `Fast-Forward`, `Cutscene Skip`, `D-Pad / Stick`, `Four Score (4P)`, `Turbo Pulse`), `F3` HUD, `F5`/`F9` quick saves, `F11`/`Alt+Enter` fullscreen, 3-tier input profiles, 4-port controllers, and built-in clean-room `--demo-cart` (`ironframe-outerfortress-cart-01`).
- **`< 2 MB` Launcher, 4-Pillar Side-by-Side Installer & `81/81` Tests:** Shipped `< 2 MB` `reCart.exe` (`AllowAutoStart = false`, CWD path normalization, `--autostart*` strip), `reCart-Setup-v0.1.0-win-x64.exe` (`"suiteId": "reEngine"`, `IsReEngineDirectory` + `IsOwnInstallManifest` guard, atomic `.ready` + `schemaVersion: 2` `engine.manifest.json`), `build_release.ps1`, `run_stage2_sandbox_e2e.ps1`, and `81/81` isolated `APP_ISOLATED_TEST=1` tests across 8 modular classes (`0` warnings in Release and Debug+Automation).

### Changed
- Standardized pre-release packaging and documentation for clean 2-channel `--prerelease` distribution (`GitHub Releases` + `Discord` only; manual-only Notion hub updates), including `Code Signing Policy` attribution in `README.md`.
- Aligned `app.manifest.json` (`minSuiteVersion: "0.5.0"`, `hostProtocolVersion: "0.5.0"`, `engineId: "reCart"`, `legacyEngineIds: ["reCartOne", "reNes"]`) and `installer.manifest.json` (`"suiteId": "reEngine"`).

### Security
- Enforced Dual-Binary Release automation stripping (`ExitCode = 2` on `--automation-port` in both `Release` `reCart.Player` and `Release` `reCart-Setup-v0.1.0-win-x64.exe`), strict Zip-Slip and path containment checks (`ExitCode = 3` on sandbox escape), and zero copyrighted ROM/BIOS/generated-DLL distribution guarantees.
