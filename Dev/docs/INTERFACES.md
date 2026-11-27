# reCart — Interfaces & Contracts (`v0.1.4` Compiled -> `v0.2.0`–`v0.7.0` Additive Roadmap)

Documents the compiled `v0.1.4` public ABI in `Dev\src\reCart.Contracts\`, scheduled non-breaking contract additions (`v0.2.0–v0.7.0`), `.recomp`/`.decomp`/installer manifests, and CLI/exit-code surfaces for **`reCart`**.

---

## 1. Compiled Assembly & Namespace Index (`Dev\reCart.slnx`)

| Assembly | Target Framework | Public Entry Types (`v0.1.4` Live) | Location |
| :--- | :--- | :--- | :--- |
| `reCart.Contracts` | `net8.0` | `CartEngineConstants`, `CartPackageManifest`, `CartAotRecipeSpec`, `CartPrgHeaderSynthesizer`, `CartRegionTimingProfile`, `CartRomHeaderParser`, `ICartMapper`, `ICartRecompGameModule`, `ICartMachineRuntime`, `ICartCompilerPipeline` | `Dev\src\reCart.Contracts\` |
| `reCart.Core` | `net8.0` | `Cpu2A03Core`, `CartMemoryBus`, `CartMapperFactory`, `CartInputPorts`, `Ppu2C02Core`, `Apu2A03Core`, `CartMachineHost` (cycle-paced `StepCpuAndApuCycle` / `_pendingAotNmi` / `_pendingAotIrq` / IRQ short-circuit), `CartMachineFactory`, `InterpreterOracle` | `Dev\src\reCart.Core\` |
| `reCart.Compiler` | `net8.0` | `Lifter6502ToCSharp`, `LifterJumpTableScanner` (`DiscoverInlineJumpTableTargets`), `CleanRoomRecompPackager`, `ColdAotCacheCompiler`, `UnliftedPcTrapException`, `SyntheticCleanRoomCartBuilder` | `Dev\src\reCart.Compiler\` |
| `reCart.Decomp` | `net8.0` | `CartDecompPlatformProvider`, `SixTellDetector`, `D4BuildReportValidator` *(Standard 18 v1 + v2 `d5_byte_match.json` `"isVerified"`/`"romMatched"` aliases via `ValidateD5Core`)* | `Dev\src\reCart.Decomp\` |
| `reCart.Player` | `net8.0-windows` | `Program`, `PlayerCliOptions`, `PlayerCliOptionHelpers`, `PlayerF1DrawerBuilder`, `CartMediaArchiveLoader` (`Foundation.Retro.MediaContainerExtractor` + bundled `tools\7z.*`), `ReCartSessionHost` (`MediaGateHandlers`, `MediaAndInput`, `VerifyAndPump`, async `HostAotCompileOverlayView`, `HostSettingsStore` `SettingsDataDirectory`, `0/8/16` live `OverscanCrop`), `ReCartLockstepRunner` | `Dev\src\reCart.Player\` |
| `reCart.Launcher` | `net8.0-windows` | `Program`, `LauncherArgsHelper` (`reCart.exe`, interactive detach + `KILL_ON_JOB_CLOSE`) | `Dev\src\reCart.Launcher\` |
| `reCart.Installer` | `net8.0-windows` | `Program`, `InstallerCliAndAutomation`, `ReCartSuitePlacementHelper` | `Dev\src\reCart.Installer\` |

---

## 2. Compiled `v0.1.0` Core ABI (`reCart.Contracts`)

```csharp
public interface ICartMapper
{
    ushort MapperId { get; }
    byte SubmapperId { get; }
    string Name { get; }
    CartMirroringMode Mirroring { get; }
    uint ActivePrgBank { get; }
    uint ActiveChrBank { get; }
    bool IsIrqPending { get; }
    byte CpuRead(ushort address);
    void CpuWrite(ushort address, byte value);
    byte PpuRead(ushort address);
    void PpuWrite(ushort address, byte value);
    void OnPpuA12Edge(bool a12High, int scanline, int dot);
    void Reset();
    byte[] CaptureState();
    void RestoreState(ReadOnlySpan<byte> state);
}

public interface ICartMachineRuntime : ICartQuickSaveProvider, IDisposable
{
    ICartMemoryBus Bus { get; }
    ICartCpuContext Cpu { get; }
    ICartPpuController Ppu { get; }
    ICartApuSink Apu { get; }
    ICartMapper Mapper { get; }
    CartRegionTimingProfile TimingProfile { get; }
    CartExecutionDiagnostics Diagnostics { get; }
    CartExecutionTierMode ExecutionTier { get; }
    void SetControllerButtons(int portIndex, uint nesButtonsMask);
    void MountStaticModule(ICartRecompGameModule module, ICartModuleManifest manifest, string preloadRomDir, string saveDir);
    void StepFrame(bool fastForward = false);
}
```

- **`CartEngineConstants` (`v0.1.4`):** `EngineId = "reCart"`, `LegacyAliasEngineIds = ["reCartOne", "reNes"]` (symmetric 3-way resolution in `reEngine.Core.EngineAliasCatalog`), `PlatformId = "nes"`, `HostProtocolVersion = "0.6.2"`, `MinSuiteVersion = "0.6.0"`, `Version = "0.1.4"`, `UmbrellaSubpath = "StreamItAll\\reEngine\\engines\\reCart\\v0.1.4"`, `CompatibleEngineTrack = "0.1.x"`, `AllowAutoStart = false`, `FrameWidth = 256`, `FrameHeight = 240`, `NtscFrameRateHz = 60.0988`, `DefaultAudioSampleRateHz = 44100`.
- **Clean-Room `.recomp` Package Format (Standard 22 / `R03`):** Accepts strictly 3 lifted entries (`module.recomp.json`, `lifted/CartLiftedModule.cs`, `lifted/dispatch.meta.json`) **or** 2 clean-room recipe entries (`module.recomp.json`, `aot-recipe.json` with `recipe.JumpTables` inline pointer table lifting via `LifterJumpTableScanner` across fixed/switchable banks, compiled asynchronously on user PC from verified BYO `.nes`/`.prg`/`.zip`/`.7z` via `ColdAotCacheCompiler` + `CartPrgHeaderSynthesizer` + `CartMediaArchiveLoader`); `CleanRoomRecompPackager` rejects extra entries, `*.Generated.dll`, `*.g.cs`, `*.nes`, `*.fds`, `*.prg`, `*.chr`, `*.rom`, `*.bin`, `*.sav`, `*.dll`, `*.exe`, and Zip-Slip traversal paths.
- **Lockstep CLI (`v0.1.1`):** `--lockstep-oracle --lockstep-steps <n>` invokes `ReCartLockstepRunner` to compare static AOT dispatch against quarantined `InterpreterOracle` across CPU registers and `2 KB` WRAM (`--lockstep-oracle` without `--lockstep-steps` retains direct `InterpreterOracle` frame execution).

---

## 3. Scheduled Additive Contracts (`v0.2.0–v0.7.0`, Zero `v0.1.x` Breaking Changes)

All planned subsystem backends extend `reCart.Contracts` via opt-in secondary interfaces (keeping `0.1.x` `ICartMapper` and `.recomp` modules compatible forward to `v1.0.0` Gold):

| Target Minor | Additive Contract / Format | Responsibility & Standard 18 Pre-Coding `/define` Tokens |
| :--- | :--- | :--- |
| **`v0.2.0`** | `ICartBlockAddressMapper`, extended `CartExecutionDiagnostics`, FourCC `RCARTQS2` QuickSave chunks | Exposes `ulong GetPrgBankSignature(ushort pc)` (8 KB PRG slots `$6000/$8000/$A000/$C000/$E000`) + `bool IsWritableCodeAddress(ushort pc)`; adds `JitDispatchCount`, `ThreadedIrDispatchCount`, `RamEpochInvalidationCount`, `DemotedHotSmcBlockCount`, `BlockRecompileCount`, `IsPureStaticReady`; FourCC snapshot envelope (`BUS0`, `CPU0`, `PPU0`, `APU0`, `MAP0`, `EXPA`, `FDS0`, `ZAP0`). |
| **`v0.3.0`** | `ICartExpansionAudio`, `ICartAudioBusClassifier`, `ICartSoundTestProvider` | Attaches expansion synths cleanly without breaking existing `ICartMapper` types; lets `.recomp` modules classify `BGM`/`SFX`/`Voice` attribution and expose Sound Test cues over `F1` / `R06` (`settings.audio.json`); forwards pre-mute, un-decimated `BlipBufferDsp` PCM frames (`48,000 Hz` / normalized metadata) to `reEngine.Host`'s `NPSB v1` stream even under `fastForwardMultiplier` (`2.0..4.0+`) or `shouldMuteOnFocusLoss` (`RPT-0518` NeuroPlay `v0.5.0` readiness). |
| **`v0.4.0`** | `PpuScanlineStateSnapshot`, `ICartGameFramePatch`, `ISubTickFrameHook` | Immutable per-scanline + mid-scanline dot-event ring (`PrimarySprites[0..7]`, `OverflowSprites[8..63]`, `BG_Opaque`/`Sprite_Behind` priority mask) driving `ParallelCpu` (`1x`-exact) + Native GPU Compositor + `settings.video.json`. Pre-coding `/define`: `isSpriteLimitRemoved`, `palettePresetId`, `customPalettePath`, `isSubTickInterpolationEnabled`, extra `videoFilterMode` tokens. |
| **`v0.5.0`** | Vaults, 1-Step Undo, Peripheral & `R07` Options + 8-File `Releases\` | `reEngine.Host v0.7.0/v0.8.0` 10-rail overlay menu hooks, `profileId` vaults, `.reSave`, optical light-gun raycast & 4-player multi-tap contracts. Pre-coding `/define`: `hasBatterySram`, `isMultiTapEnabled`, `opticalLightGunPort` / `expansionPortDevice`, `passwordVaultEntries`. |
| **`v0.6.0`** | `ICartAssetCatalogProvider`, `ICartFdsVolumeProvider`, `.re*` Hot-Reload Sinks | Clean-room disk-system BIOS HLE vectors + Unified Multi-Side Virtual Volume (`data\rom\*.fds` read-only + `<dataDir>\saves\*.fds.sav` CoW) + `assets.catalog.json` / `hd_recipe.json` / `subtitle_recipe.json` / Manual / Tracker providers. Pre-coding `/define`: `activeDiskSideIndex`, `diskSideCount`, `isDiskInserted`, `expansionAudioGain`. |
| **`v0.7.0`** | `ICartResearchInspector` & Opt-In `HostRuntimeApiServer` Hooks (`R12`) | Live `6502`/PPU/APU inspection across both `.recomp` modules and standalone raw-media JIT boot; Release `HostRuntimeApiServer` (`isRuntimeApiEnabled: false`, loopback + Bearer + `403` origin guard) kept strictly separate from stripped `--automation-port` (`ExitCode = 2`). |

---

## 4. CLI & Exit-Code Contracts (`v0.1.4` Active Baseline)

| Binary / Flag | Behavior | Exit Codes |
| :--- | :--- | :--- |
| `reCart.exe` (`Launcher`) | Strips `--autostart*`/`--auto-start*`, rebases caller-CWD relative paths (`LauncherArgsHelper`), detaches cleanly on interactive GUI launches (`--open` / positional path), and attaches console/binds Win32 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` when waiting synchronously (`--wait`, `--verify`, `--doctor`, `--headless`, `--tray-only`, `--frames`, `--dump-*`, `--automation-port`, `--require-backend`) | Child exit code (`0` on `--version` / `--help`) |
| `reCart.Player --demo-cart` | Generates/validates clean-room `ironframe-outerfortress-cart-01.recomp`, cold-compiles `.Generated.dll` asynchronously behind `HostAotCompileOverlayView` into `<dataDir>\cache\`, runs live `reEngine.Host v0.6.2` session with `SettingsDataDirectory` persistence and silent-idle demo audio (chime on input) | `0` clean exit |
| `reCart.Player --open / --module / --rom / --disc / --preload-media` | Opens `.recomp`/manifest or `.nes`/`.prg`/`.zip`/`.7z` (`CartMediaArchiveLoader` + `Foundation.Retro.MediaContainerExtractor` + bundled `tools\7z.*` with fail-closed `InvalidDataException`), preserves `<DataDir>\rom\` discovery ahead of module fallback & `SuiteMediaTreeWalker`, stages external verified ROMs cleanly into `<DataDir>\rom\`, persists `HostSettingsStore` (`SettingsDataDirectory`) across `F1` changes (`0/8/16` live `OverscanCrop` refresh), and opens interactive `FirstRunMediaGateView` (`CLEAN-ROOM MEDIA GATE - reCart v0.1.4`, silent Deep Slate backdrop, interactive `UnliftedPcTrapException` trap catch) whenever `loaded.RomBytes is null && !DemoCart` | `0` ok, `1` arg/format, `4` media/SHA-256, `5` `--jit-bringup` spin (`v0.2.0`), `6` missing `--require-backend` |
| `--fps-mode`, `--target-fps`, `--vsync`/`--no-vsync`, `--fast-forward`, `--open-settings`, `--show-fps`, `--enable-logs`, `--force-zero-rwx`, `--profile`, `--tray-only`, `--doctor` | Parsed via `PlayerCliOptionHelpers` and wired into `HostSessionOptions` / diagnostics (`--force-zero-rwx` promotes `JitBringup` / `AotWithJitFallback` safely to `AotWithThreadedIr`; `--doctor` prints clean-room environment diagnostics) | `0` ok, `1` invalid flag/range |
| `--data-dir`, `--config`, `--log-dir`, `--save-dir` | Enforces `AppEnvironment` & `FOUNDATION_SANDBOX_ROOT` path containment | `3` on sandbox/isolation escape |
| `--automation-port` (Release Player & Setup EXEs) | Stripped of `Foundation.Automation` & `Foundation.UI`; fails closed immediately before folder/registry writes | **`2`** in Release; loopback active under `-p:ReCartTestAutomation=true` |
| `reCart-Setup-v<Ver>-win-x64.exe` | `"suiteId": "reEngine"`, `IsReEngineDirectory` + `IsOwnInstallManifest` side-by-side placement (`engines\reCart\v<Ver>\`), atomic `.ready` + `engine.manifest.json` (`schemaVersion: 2`) *(Dual `Online`+`Offline` Setup EXEs scheduled at `v0.5.0`)* | `0` install/uninstall/dry-run, `2` Release automation gate, `3` containment violation, `4` payload hash mismatch (`Online`, `v0.5.0`) |
