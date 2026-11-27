# reCart — User Guide (`v0.1.4` Beta)

Welcome to **`reCart`**, the clean-room `.NET 8.0` ahead-of-time (AOT) static recompilation engine powering `Cart` (`platformId: "nes"`) game modules under [reEngine](https://github.com/StreamItAllGit/reEngine).

> **Beta (Pre-1.0):** `reCart v0.1.4` ships the cycle-paced AOT-primary `6502 -> C#` compiler and runtime (`RP2A03` CPU, `RP2C02` NTSC PPU, 5-channel APU, Mappers `0–3`), 2-member clean-room `aot-recipe.json` (`recipe.JumpTables` inline table lifting) & `.nes`/`.prg`/`.zip`/`.7z` support, `--lockstep-oracle --lockstep-steps <n>` differential verification, 5-backend `reEngine.Host v0.6.2` SDL3 player with immediate window launch + async `"Preparing Native Module..."` AOT progress overlay, interactive Clean-Room Media Gate (`CLEAN-ROOM MEDIA GATE - reCart v0.1.4`), automatic `F1` Video/Audio persistence + live `0/8/16 px` Overscan Crop refresh, jitter-free `F3` HUD, silent-idle `--demo-cart` clean-room cartridge, expanded CLI superset, `< 2 MB` detaching launcher (`reCart.exe`), and 4-pillar Setup installer (`reCart-Setup-v0.1.4-win-x64.exe`, `Foundation v0.5.5`). Planned backend milestones (`v0.2.0`–`v0.7.0`) add Shared IR/RyuJIT (`--jit-bringup`), Blip-buffer audio & `PAL`/`Dendy`, `ParallelCpu` scanline PPU + GPU compositor, full mappers/peripherals, clean-room `.fds` HLE & packs, and standalone raw-media boot (`v1.0.0` Gold).

---

## 1. What `reCart` Is & How It Runs Games

`reCart` is an **ahead-of-time (AOT) static recompilation engine — not a generic compatibility emulator**. When you launch a clean-room `.recomp` module with your own SHA-256 verified ROM (or run the built-in clean-room `--demo-cart`), `reCart` compiles native C# game code once locally on your PC inside `<dataDir>\cache\` and executes hardware-timed `RP2A03` / `RP2C02` (NTSC `60.0988 Hz`) across Mappers `0/1/2/3` (`NROM`, `MMC1`, `UNROM`, `CNROM`). If an unlifted code address is encountered under the default `staticAotOnly` mode, execution fails closed instead of silently falling back to an interpreter.

- **5 Aspect Ratio Modes (`Native PAR` Default — Never Stretched):** `reCart` renders through `reEngine.Host` across `opengl`, `vulkan`, `directx` (`dx12`), `parallelCpu`, and `software` defaulting to **`nativePar`** (pillarboxed NTSC `8:7` pixel aspect ratio), and never stretches `4:3` visuals across `16:9` displays during window resizing. Select `--aspect-mode` (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`) or cycle live in the `F1` drawer (`Overscan Crop` supported via `--overscan t,b,l,r`).

---

## 2. Install Topology & Zero-Daemon Guarantee

- **Side-by-Side Suite Root:** `reCart-Setup-v0.1.4-win-x64.exe` installs cleanly under `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\reCart\v0.1.4\` (with bundled `app\tools\{7z.exe, 7z.dll, LICENSE-7ZIP.txt}` or extracted standalone from `reCart-v0.1.4-win-x64-Portable.zip`), using `"suiteId": "reEngine"` and root-collision protection (`IsReEngineDirectory`) so existing `reEngine` installations are never overwritten. *(Dual `Online` + `Offline` Setup EXEs arrive in `v0.5.0` alongside `reEngine v0.8.0`.)*
- **Zero Background Footprint (`allowAutoStart: false`):** Ships with zero Windows Startup entries, zero scheduled tasks, and `0 MB` resident background memory when closed (`--autostart` flags are ignored).
- **Non-Destructive Upgrades & Isolated Storage:** Re-installs and upgrades preserve your local `data\rom\` media dumps, `<dataDir>\cache\` AOT assemblies, `<dataDir>\config\settings.json`, `<dataDir>\config\settings.controls.json`, and `<dataDir>\saves\` files.

---

## 3. First-Run Clean-Room Demo & BYO Media Flow

1. **Try the Built-In Clean-Room Cart (`--demo-cart`):** Run `reCart.exe --demo-cart --backend opengl --aspect-mode nativePar` (or launch `demo_ironframe_cart.bat`). `reCart` synthesizes clean-room `IronFrame OuterFortress (Cart) 01` (`ironframe-outerfortress-cart-01.recomp`), compiles its `.Generated.dll` locally under `<dataDir>\cache\`, and boots into a live interactive `reEngine.Host` window with `WASAPI` audio.
2. **Launch Your Own `.recomp` Module & BYO Media (`.nes`, `.prg`, `.zip`, `.7z`):** Place your legally dumped `.nes`/`.prg` cartridge file (or `.zip`/`.7z` archive, extracted fail-closed via bundled `app\tools\7z.*` with zero leftover temp folders) inside your game module's local `<DataDir>\rom\` directory, then launch via `reEngine` (`reEngine.exe --open <Game.recomp> --wait`, resolving `reCart`, `reCartOne`, and legacy `reNes` aliases) or directly via `reCart.exe --open <Game.recomp> --rom <path-to-rom>`. Verified external files chosen interactively are staged cleanly into `<DataDir>\rom\` automatically.
3. **Interactive Clean-Room Media Gate (`--preload-media`, Missing Media, or Standalone Raw ROM):** Whenever a session starts without verified ROM bytes and without `--demo-cart` — or when opening an unmatched standalone `.nes`/`.prg` without a `.recomp` module — `reCart.Player` keeps `<DataDir>\rom\` discovery intact, displays a silent Deep Slate idle backdrop (zero unrequested demo grid or test tone), and opens `FirstRunMediaGateView` where you can click **`Select Media...`**, click **`Select .recomp Module...`**, **Drag & Drop** your verified `.nes`/`.prg`/`.zip`/`.7z`/`.recomp` file onto the window (invalid archives retain a clear warning banner instead of crashing), or select **`Launch Demo / Test Cart`** to compile and run cleanly on the fly.
4. **Instant Window & Async Background Cold AOT Compilation:** On first launch, `HostSessionRunner` opens the window immediately while `ColdAotCacheCompiler` builds `<moduleId>.Generated.dll` asynchronously into `<dataDir>\cache\<moduleId>\<key>\` with live progress shown on `HostAotCompileOverlayView` (`"Preparing Native Module..."`); unlifted runtime traps inside interactive sessions route back safely rather than hard-crashing silently.
5. **Read-Only Media, Saves & Persisted `F1` Settings:** Your original `data\rom\` `.nes`/`.7z` files stay strictly read-only; quick saves (`*.slot0.savstate`), `F1` Video/Audio settings (`settings.json`, `settings.video.json`, `settings.audio.json`), and runtime logs persist inside `<dataDir>\saves\`, `<dataDir>\config\`, and `<LogDir>\`.
6. **Pack & Mod Authoring Docs (`v0.6.0`):** Creator workflows for `assets.catalog.json`, the Asset Dumper Wizard, `.re*` packs, and deterministic `hd_recipe.json`/`subtitle_recipe.json` authoring will ship under `Dev\docs\USER_GUIDE.Modding.md` in `v0.6.0`.

---

## 4. CLI Options, Controls & `F1` / `F3` Overlays (`reEngine.Host`)

### 4.1 Common CLI Switches
- `--demo-cart`: Build/run the built-in clean-room IronFrame synthetic cart (`ironframe-outerfortress-cart-01`, silent when idle, playing a soft chime on input).
- `--open <path>` / `--module <path>` / `--rom <path>` / `--disc <path>` / `--preload-media <path>`: Load a `.recomp` package or local BYO `.nes` / `.prg` / `.zip` / `.7z` cartridge.
- `--backend <opengl|vulkan|directx|dx12|parallelCpu|software>` & `--require-backend [<id>]`: Choose presentation backend (`ExitCode = 6` if required backend is unavailable).
- `--aspect-mode <nativePar|fourByThree|squareInteger|nativeFit|horPlus16By9>`, `--scale <1|2|4|8>`, `--overscan <t,b,l,r>`, `--fps-mode <original|enhanced>`, `--target-fps <n>`, `--vsync` / `--no-vsync`, `--fast-forward`, `--fullscreen`, `--window`, `--tray-only`: Configure aspect ratio (default `nativePar`), integer scale (`2` default), edge crop, frame pacing, and window mode.
- `--region <auto|ntsc|pal|dendy>`, `--execution-tier <staticAotOnly|jitBringup|aotWithJitFallback|aotWithThreadedIr|interpreterOracle>`, `--force-zero-rwx`, `--lockstep-oracle`, `--lockstep-steps <n>`: Select timing profile (`auto` default), force zero-RWX execution, or run N-step differential AOT vs `InterpreterOracle` verification (`staticAotOnly` default).
- `--open-settings`, `--show-fps`, `--enable-logs`, `--profile <name>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--save-dir <dir>`, `--audio-driver <drv>`, `--frames <n>`, `--dump-screenshot <png>`, `--dump-audio <wav>`, `--wait`, `--verify`, `--doctor`, `--version`, `--help`: Startup overlays, diagnostics, storage overrides, captures, and synchronous launcher waits (`Release` builds fail closed with `ExitCode = 2` on `--automation-port`).

### 4.2 Interactive Hotkeys & 5-Tab `F1` Drawer (`v0.1.4`)
- **`F1`:** Open / Close the 5-tab `Settings` drawer (`Video`, `Ctrl`, `Keys`, `Sys`, `Media`) featuring live `Overscan Crop` (`0 -> 8 -> 16 -> 0` + right-click reset), `Aspect`, `Mapper`, `Region`, `Execution Tier`, `D-Pad / Stick`, `Four Score (4P)`, and `Turbo Pulse` rows alongside host `Fast-Forward` and `Cutscene Skip` buttons, plus 3-tier control switching (`Global 1:1`, `Game 1:1`, `Modern PC` persisted in `settings.controls.json`) and 4-port SDL3 gamepad + 3-slot KB/M capture (with Menu `D-Pad` + `Left Stick` navigation isolation).
- **`F3`:** Toggle the jitter-free fixed-width live telemetry HUD (FPS, frame time, backend, scale, aspect mode, fast-forward state, connected pad count, execution tier, audio buffer fill).
- **`F5` / `F9`:** Quick Save (`*.slot0.savstate`, flushing GPU readback sync prior to capture) and Quick Load.
- **`F6` / `Grave` & `F7`:** Fast-Forward toggle/hold (`2x`) and Cutscene Skip trigger *(note: in `v0.6.0` / `reEngine v0.9.0`, `F6` transitions cleanly to **Hot-Reload `.re*` Packs**, leaving `Grave` / `Tab` for Fast-Forward)*.
- **`F11` / `Alt+Enter` & `ESC`:** Toggle borderless Fullscreen, cancel active keybind capture, close open drawers, or exit cleanly.

---

## 5. License & Legal

- **License:** Mozilla Public License Version 2.0 (see `LICENSE` included alongside `app\LICENSE` and the repository root).
- **Third-Party Runtimes:** `ppy.SDL3-CS` (`MIT`), `SDL3` (`zlib`), and build-time `7-Zip` (`GNU LGPL v2.1+` with unRAR restriction / `BSD-3-Clause`; see `THIRDPARTY_LICENSES.txt`).
- **Clean-Room & Non-Affiliation:** `reCart` contains zero copyrighted retail ROMs, `.fds` disk images, vendor FDS BIOS ROMs, CHR/PRG dumps, or manufacturer assets; end users supply legally owned dumps locally under `data\rom\`. *Nintendo*, *NES*, *Nintendo Entertainment System*, *Famicom*, *Famicom Disk System*, *Zapper*, and *Four Score* are trademarks of their respective owners; `reCart` is an independent clean-room project with zero affiliation or endorsement.
