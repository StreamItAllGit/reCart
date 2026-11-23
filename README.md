<div align="center">

<img src="Dev/assets/reCart.png" alt="reCart Logo" width="96" />

# reCart

Clean-room `.NET 8.0` ahead-of-time static recompilation engine, `6502 -> C#` lifter, and hardware-timed `RP2A03` / `RP2C02` (`platformId: "nes"`) runtime for [reEngine](https://github.com/StreamItAllGit/reEngine).

![Release](https://img.shields.io/github/v/release/StreamItAllGit/reCart?include_prereleases&style=flat) [![CI](https://github.com/StreamItAllGit/reCart/actions/workflows/ci.yml/badge.svg)](https://github.com/StreamItAllGit/reCart/actions/workflows/ci.yml) [![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue?style=flat)](https://streamitallgit.github.io/reCart/) ![License](https://img.shields.io/badge/license-MPL--2.0-green?style=flat) ![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat)

**Road to `v1.0.0`:** `[##--------] 15%` *(v0.1.3 shipped locally with `94/94` isolated tests across 10 test suites and `0` warnings; 7-minor backend path `v0.2.0`–`v0.7.0` toward `v1.0.0` Going Gold)*

</div>

> **Beta (Pre-1.0):** `reCart v0.1.3` ships the cycle-paced `6502 -> C#` AOT static core (`CartMachineHost.StepCpuAndApuCycle` with `_pendingAotNmi`/`_pendingAotIrq`), `LifterJumpTableScanner` (`recipe.JumpTables` inline table lifting), `RP2A03`/`RP2C02`/5-channel APU, Mappers `0–3`, `.decomp` platform provider, 5-backend `reEngine.Host v0.6.2` player (immediate window launch + async background AOT progress overlay `HostAotCompileOverlayView`, `--demo-cart`, interactive Clean-Room Media Gate via `ReCartSessionHost.MediaGateHandlers` with `Select .recomp Module...`, `.7z` extraction via `CartMediaArchiveLoader` + `Foundation.Retro.MediaContainerExtractor` + bundled `tools\7z.*`, external ROM staging into `data\rom\`, standalone raw-ROM Media Gate guidance, interactive `UnliftedPcTrapException` trap catch, and `HostSettingsStore` `SettingsDataDirectory` persistence), `--lockstep-oracle --lockstep-steps`, `< 2 MB` stub launcher, and 4-pillar side-by-side Setup installer (`Foundation v0.5.5`). Active planning remains focused on `v0.2.0` (Tier 3 `6502 -> Shared IR`, Collectible RyuJIT / Zero-RWX, and cooperative hardware scheduling) along a dedicated 7-minor backend ladder toward `v1.0.0` Going Gold (`win-x64`).

---

## About

**reCart** is a clean-room `.NET 8.0` C# static recompilation engine and `.decomp` platform provider (`platformId: "nes"`, 3-way engine alias `reCart` <-> `reCartOne` <-> `reNes`) built on `Foundation v0.5.5` and `reEngine v0.6.2` (`reEngine.Core` + shared SDL3 `reEngine.Host.dll`).

`reCart` is strictly an **ahead-of-time (AOT) static recompilation stack — not a generic compatibility emulator**:

- **AOT-Primary Execution (`staticAotOnly` Default):** Clean-room 2-member recipe (`[module.recomp.json, aot-recipe.json]` including `recipe.JumpTables` inline pointer table lifting via `LifterJumpTableScanner`) and 3-member lifted `.recomp` cartridges compile locally out-of-process on the player's machine into `<dataDir>\cache\<moduleId>\<key>\<moduleId>.Generated.dll` from user-supplied `data\rom\` media (`.nes`, `.prg` via `CartPrgHeaderSynthesizer`, `.zip`, and `.7z` via `CartMediaArchiveLoader`) verified by SHA-256 while the interactive SDL3 window opens immediately with live `HostAotCompileOverlayView` progress, failing closed cleanly (`UnliftedPcTrapException`) if an unlifted PC is encountered.
- **Unified `6502 -> Shared IR` & Raw-Media Bring-Up (`v0.2.0` -> `v0.7.0`):** Tier 3 Collectible RyuJIT and Zero-RWX Threaded-IR (`--jit-bringup`, arriving in `v0.2.0` and hardened through `v0.7.0`) share a single bank+RAM-epoch-keyed IR frontend that enables emergency fallback, `bringup.profile.json` profiling, best-effort standalone `.nes`/`.fds` JIT boot, and automated `100%` static AOT promotion — while `InterpreterOracle` remains strictly quarantined behind `--execution-tier interpreterOracle`.
- **Two-Phase Aspect-Locked Video & Band-Limited Audio:** Today `reCart` drives all 5 `reEngine.Host v0.6.2` backends (`OpenGL 3.3 Core`, `Vulkan 1.3`, `DirectX 12`, `ParallelCpu`, `Software`) defaulting to `nativePar` (`8:7` NTSC PAR pillarboxed — never stretched to `16:9`) alongside `fourByThree`, `squareInteger`, `nativeFit`, and opt-in `horPlus16By9`. The `v0.3.0`–`v0.4.0` backend upgrades add CPU-cycle Blip-buffer synthesis, `PAL`/`Dendy` timings, two-phase scanline-band `ParallelCpu` (`1x` bit-exact to `Software`), and native GPU sprite/tile/shader composition (`>8` sprites, 24-bit overrides, `120–240 Hz` `SubTick`, CRT/colorblind filters).
- **Proving Ground #1:** Designed alongside [reSlate](https://github.com/StreamItAllGit/reSlate) and verified with clean-room fixture **`IronFrame OuterFortress (Cart) 01`** (`ironframe-outerfortress-cart-01`) at deterministic `60.0988 Hz` timing.

---

## Key Features

- **Hardware-Timed `v0.1.3` Core (`NTSC` + Mappers `0/1/2/3`):** Cycle-stepped `6502` (`RP2A03` CPU: 56 official opcodes, zero BCD, `JMP ($xxFF)` page-wrap, `$4016/$4017` `0x40` open-bus, `513`/`514`-cycle `OAM DMA` + `DMC` stalls), cycle-paced `CartMachineHost` static AOT block dispatch (`StepCpuAndApuCycle`, `_pendingAotNmi`, `_pendingAotIrq`), dot-stepped `262`-scanline `RP2C02` PPU (`256x240`, odd-frame dot skip, Loopy `v`/`t`/`x`/`w` scroll, sprite-0 hit & NMI suppression), 5-channel `RP2A03` APU (`Pulse 1/2`, `Triangle`, `Noise`, `DMC`), and `ICartMapper` Mappers `0–3` (`NROM`, `MMC1`, `UxRom`, `CNROM`). Dedicated backend minors expand through Shared IR/JIT (`v0.2.0`), Blip-buffer DSP + `PAL`/`Dendy` (`v0.3.0`), two-phase `ParallelCpu`/GPU compositing (`v0.4.0`), commercial iNES/NES 2.0 mappers + peripherals + Dual-Installer (`v0.5.0`), clean-room disk-system HLE + 6 expansion audio chips + `.re*` packs (`v0.6.0`), and raw-media hardening + `R3` Promoter + Research rail (`v0.7.0`) -> `v1.0.0` Going Gold.
- **Static `6502 -> C#` Lifter, `recipe.JumpTables`, Cold AOT Cache & `reCart.Decomp`:** Emits and compiles clean-room 2-member recipe (`module.recomp.json`, `aot-recipe.json` with `LifterJumpTableScanner.DiscoverInlineJumpTableTargets` across fixed/switchable banks) and 3-member lifted (`.cs`, `dispatch.meta.json`) `.recomp` ZIP archives (zero `*.Generated.dll` or retail ROMs bundled), builds `.Generated.dll` locally out-of-process with `--disable-build-servers -p:UseSharedCompilation=false` asynchronously behind `HostAotCompileOverlayView`, and enforces clean-source craftsmanship (`SixTellDetector` + zero-warning build verification).
- **5-Backend `reEngine.Host v0.6.2` Player, Clean-Room Media Gate, `.7z` Staging & Settings Persistence:** Supports `opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, and `software` with `wasapi` audio, 3-tier input profiles (`settings.controls.json`), `HostSettingsStore` `SettingsDataDirectory` persistence (`F1` Video/Audio round-trips), jitter-free `0 px` `F3` HUD, 4-port controller mapping, `F5`/`F9` deterministic quick saves, `F11`/`Alt+Enter` fullscreen, `--demo-cart` clean-room verification, `CartMediaArchiveLoader` + bundled `tools\7z.*` fail-closed `.7z` extraction/staging, interactive `ReCartSessionHost.MediaGateHandlers` (`Select Media...`, `Select .recomp Module...`, Drag & Drop, external ROM staging into `<dataDir>\rom\`, standalone raw-ROM clean-room guidance, and interactive `UnliftedPcTrapException` trapping).
- **Zero-Daemon Containment & 4-Pillar Side-by-Side Installer:** `< 2 MB` single-file `reCart.exe` (`AllowAutoStart = false`), 4-pillar `reCart-Setup-v0.1.3-win-x64.exe` (`"suiteId": "reEngine"`, `HostProtocolVersion = "0.6.2"`, `MinSuiteVersion = "0.6.0"`, installing cleanly into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\reCart\v0.1.3\` with `app\tools\{7z.exe, 7z.dll, LICENSE-7ZIP.txt}`, `IsReEngineDirectory` collision guard, and atomic `.ready`), and dual-binary `Release` automation stripping (`ExitCode = 2` on both Player and Setup EXEs).

---

## Getting Started

1. Download `reCart-Setup-v0.1.3-win-x64.exe` or `reCart-v0.1.3-win-x64-Portable.zip` from [GitHub Releases](https://github.com/StreamItAllGit/reCart/releases).
2. Run `reCart-Setup-v0.1.3-win-x64.exe` (installs cleanly alongside `reEngine v0.6.2` under `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\reCart\v0.1.3\`) or extract the `-Portable.zip` bundle.
3. Place your legally dumped `.nes`, `.prg`, `.zip`, or `.7z` cartridge file inside your game module's local `data\rom\` folder, or try the built-in clean-room synthetic demo cart immediately:

```powershell
# Launch the built-in clean-room IronFrame demo cart (OpenGL, Native PAR, cold AOT into cache\)
reCart.exe --demo-cart --backend opengl --aspect-mode nativePar

# Launch a clean-room .recomp cartridge or local .nes/.7z ROM directly or via reEngine
reCart.exe --open "D:\Games\MyCart\app\MyCart.recomp" --rom "D:\Games\MyCart\data\rom\game.nes" --backend opengl --aspect-mode nativePar
reEngine.exe --open "D:\Games\MyCart\app\MyCart.recomp" --wait
```

<details>
<summary>Developer Build & Verification</summary>

The solution (`Dev\reCart.slnx`) builds cleanly with zero warnings and passes **`94/94` isolated `APP_ISOLATED_TEST=1` tests** across all 10 test suites in both `Release` and `Debug -p:ReCartTestAutomation=true`:

```powershell
# Clone & build from source
git clone https://github.com/StreamItAllGit/reCart.git
cd reCart
$env:APP_ISOLATED_TEST = "1"
dotnet build Dev\reCart.slnx -c Release /warnaserror
dotnet test Dev\reCart.slnx -c Release --no-build
dotnet build Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true /warnaserror
dotnet test Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true --no-build
```

</details>

---

## Project Structure

- **8-Project `.NET 8.0` Solution ([`Dev/reCart.slnx`](Dev/reCart.slnx)):**
  - `reCart.Contracts` (`net8.0`) — Public module ABI (`ICartRecompGameModule`, `ICartMachineRuntime`, `ICartMapper`, `ICartCompilerPipeline`, `ICartQuickSaveProvider`, `CartPackageManifest`, `CartAotRecipeSpec`, `CartPrgHeaderSynthesizer`, `CartRegionTimingProfile`, `CartRomHeaderParser`, `CartEngineConstants`)
  - `reCart.Core` (`net8.0`) — `Cpu\` (`Cpu2A03Core`, `CartMemoryBus`), `Mappers\` (`Nrom`, `Mmc1`, `UxRom`, `CnRom`), `Rom\` (`CartridgeLoader`, `InesRomImage`), `Ppu\` (`Ppu2C02Core`), `Apu\` (`Apu2A03Core`), `Input\` (`CartInputPorts`), `Runtime\` (`CartMachineHost` w/ cycle-paced `StepCpuAndApuCycle` AOT dispatch, `CartQuickSaveSerializer`), and `Oracle\` (`InterpreterOracle`); planned expansion slots for `Ir\`, `Jit\`, `Bringup\`, `Apu\Expansion\`, `Gpu\`, `Fds\`, `Saves\`, and `Research\`
  - `reCart.Compiler` (`net8.0`) — Clean-room `6502 -> C#` static lifter (`Lifter6502ToCSharp`, `LifterJumpTableScanner` `recipe.JumpTables` lifting), 2-member recipe + 3-member lifted `.recomp` ZIP packager (`CleanRoomRecompPackager`), `UnliftedPcTrapException`, and cold local `<dataDir>\cache\` AOT compiler (`ColdAotCacheCompiler`)
  - `reCart.Decomp` (`net8.0`) — `IDecompPlatformProvider` implementation (`platformId: "nes"`, `SixTellDetector`, `D4BuildReportValidator`)
  - `reCart.Player` (`net8.0-windows`) — `ReCartSessionHost` (immediate window + async `HostAotCompileOverlayView`, `MediaGateHandlers` `Select .recomp Module...` + standalone raw-ROM Clean-Room Media Gate + `SettingsDataDirectory` persistence, `MediaAndInput` `<DataDir>\rom\` discovery, `CartMediaArchiveLoader` `.7z`/`.zip`/external ROM staging, `ReCartLockstepRunner`) riding `reEngine.Host v0.6.2` across all 5 backends, `--demo-cart`, `--lockstep-oracle`, `F1`/`F3`/`F5`/`F9`/`F11`, and Debug-only `PlayerAutomationBridge`
  - `reCart.Launcher` (`net8.0-windows`) — Single-file `< 2 MB` `reCart.exe` clean-root stub (`AllowAutoStart = false`)
  - `reCart.Installer` (`net8.0-windows`) — 4-pillar Setup EXE (`"suiteId": "reEngine"`, `ReCartSuitePlacementHelper`, `IsReEngineDirectory` guard, bundled `tools\7z.*`, atomic `.ready`)
  - `reCart.Tests` (`net8.0-windows`) — Zero-asset isolated xUnit verification suite (`10` modular classes, `94/94` tests incl. `PlayerV013PrelaunchTests`)

---

## Documentation

- **Feature Map & Code Registry:** [`FEATURES.md`](FEATURES.md)
- **Milestones & Roadmap (`v0.1.0` -> `v1.0.0` + `v2.0.0–v5.0.0`):** [`ROADMAP.md`](ROADMAP.md)
- **Changelog:** [`CHANGELOG.md`](CHANGELOG.md)
- **Security Policy:** [`SECURITY.md`](SECURITY.md)
- **Contributing Guide:** [`CONTRIBUTING.md`](CONTRIBUTING.md)
- **Technical & User Manuals (`Dev/docs/`):**
  - [`ARCHITECTURE.md`](Dev/docs/ARCHITECTURE.md) — System design, 4-tier AOT/IR execution stack, two-phase video & Blip-buffer pipelines, and slice boundaries
  - [`INTERFACES.md`](Dev/docs/INTERFACES.md) — Public ABI, scheduled additive contracts, `.recomp`/`.decomp` manifests, and CLI switches
  - [`USER_GUIDE.md`](Dev/docs/USER_GUIDE.md) — BYO media setup, overlays, aspect-ratio guarantees, and troubleshooting
  - [`OPERATIONS.md`](Dev/docs/OPERATIONS.md) — Build/test commands, install topology, logs, and release workflow
- **Companion Suite Projects:** [reEngine](https://github.com/StreamItAllGit/reEngine) · [reSlate](https://github.com/StreamItAllGit/reSlate)

---

## Code Signing Policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

- **Authors:** Repository contributors and maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Reviewers:** Repository maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Approvers:** Repository owners ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Privacy Policy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

---

## License & Legal

- **Engine License:** Clean-room engine source and contracts are licensed under the **Mozilla Public License 2.0** ([`LICENSE`](LICENSE)).
- **Clean-Room & Non-Affiliation:** `reCart` ships zero copyrighted game ROMs, disk images, vendor BIOS dumps, CHR/PRG assets, or pre-generated retail game assemblies. End users supply legally owned `.nes` and `.fds` media locally inside gitignored `data\rom\` directories. *Nintendo*, *NES*, *Nintendo Entertainment System*, *Famicom*, *Famicom Disk System*, *Zapper*, and *Four Score* are trademarks of their respective owners; `reCart` is an independent clean-room project with zero affiliation or endorsement.
- **Bundled / Referenced Runtimes:** Built on `Foundation v0.5.5` and `reEngine v0.6.2` (`reEngine.Core`, `reEngine.Host` over `ppy.SDL3-CS` `MIT` / SDL3 `zlib`, and `7-Zip` `LGPL-2.1+` with unRAR restriction under `app\tools\`).
