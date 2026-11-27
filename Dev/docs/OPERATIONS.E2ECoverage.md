# reCart — Operations E2E Coverage Ledger (`v0.1.4`)

Human-readable Manual Shelf mirror of gitignored `Dev\docs\tests\e2e_coverage.json` and `Dev\docs\tests\e2e_perf_history.json` (`e2e-qa` + `exebox-stream` at `http://127.0.0.1:5005`).

## 1. Coverage & Performance Summary

| Metric | Value | Notes |
| :--- | :---: | :--- |
| **Total Inventoried Surfaces** | `34` | Entrypoints (`6`), Media Gate & AOT (`5`), F1 Settings Drawer (`13`), Live Gameplay & Persistence (`5`), `FEATURES.md` Core (`5`) |
| **Verified (`v0.1.4` + `v0.1.3` Carried)** | `33` | `29` verified on `v0.1.4` (`23` live Stage 2 E2E + `6` isolated xUnit); `4` installer/release probes carried from `v0.1.3` pending Phase 4 packaging (`-SkipInstallerSandbox`) |
| **Stale Diff (`STALE_DIFF`)** | `0` | — |
| **Untested (`UNTESTED`)** | `1` | `player.default_entrypoint_and_eaddrinuse` (queued for Phase 4 full installer/default-launcher check) |
| **Failed (`FAILED`)** | `0` | `0.1.4.2026-11-27_1712.-.stage2-e2e-overscan-ui-and-version-badge.md` resolved & archived |
| **Last Random Seed** | `20261127` | `6` random regression surfaces re-verified alongside `100%` new/changed |
| **Overdue Perf Checks** | `0` | `v0.1.4` baseline anchored (`e2e-20261127-2220`, `hostLoadBand: CLEAN`) |

---

## 2. Entrypoints, CLI Modes, Launcher & Installer (`6`)

| `surfaceId` | Kind | Entrypoint / Selector | Status | Last Verified | Notes |
| :--- | :--- | :--- | :---: | :---: | :--- |
| `installer.release_automation_reject` | `entrypoint` | `reCart-Setup-v*.exe --automation-port` | `VERIFIED` | `0.1.3` | Carried; re-verified in Phase 4 packaging |
| `installer.silent_side_by_side_and_sibling_guard` | `mode` | `--silent --suite-dir --data-dir` | `VERIFIED` | `0.1.3` | Carried; re-verified in Phase 4 packaging |
| `installer.wizard_gui_install` | `screen` | `Wizard_InstallDir`, `Wizard_PrimaryButton` | `VERIFIED` | `0.1.3` | Carried; re-verified in Phase 4 packaging |
| `player.release_automation_reject` | `entrypoint` | `reCart.Player.exe --automation-port` | `VERIFIED` | `0.1.3` | Carried; re-verified in Phase 4 `build_release.ps1` |
| `launcher.interactive_detach_and_job_guard` | `entrypoint` | `reCart.exe --open` / `--wait` / `--doctor` | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit (`ContractsAndManifestTests`) |
| `cli.superset_v014` | `mode` | `--tray-only`, `--fps-mode`, `--target-fps`, `--vsync`, `--open-settings`, `--show-fps`, `--enable-logs`, `--doctor`, `--disc` | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit (`PlayerQuickSaveAndInputTests`) |

---

## 3. Media Gate & Cold AOT Overlay (`5`)

| `surfaceId` | Kind | Selector / Control ID | Status | Last Verified | Notes |
| :--- | :--- | :--- | :---: | :---: | :--- |
| `player.media_gate_empty_rom` | `screen` | `FirstRunMediaGateView` (`< 5s` feedback) | `VERIFIED` | `0.1.4` | Launch 1 (`3453.9 ms` healthy); `recart_media_gate.png` crisp |
| `player.media_gate_browse_button` | `control` | `MediaGate_BrowseButton` | `VERIFIED` | `0.1.4` | Verified visible & enabled in `/v1/tree` |
| `player.media_gate_select_module_button` | `control` | `MediaGate_SelectModuleButton` | `VERIFIED` | `0.1.4` | Verified visible & enabled in `/v1/tree` |
| `player.media_gate_launch_demo_button` | `control` | `MediaGate_LaunchDemoButton` | `VERIFIED` | `0.1.4` | Verified visible & enabled in `/v1/tree` |
| `player.aot_compile_overlay` | `screen` | `HostAotCompileOverlayView` (`cache\*.Generated.dll`) | `VERIFIED` | `0.1.4` | Cold AOT settled in `4097.4 ms` (`ironframe-outerfortress-cart-01.Generated.dll`) |

---

## 4. F1 Settings Drawer & Version Visibility (`13`)

| `surfaceId` | Kind | Selector / Control ID | Status | Last Verified | Notes |
| :--- | :--- | :--- | :---: | :---: | :--- |
| `player.f1_tab_video` | `screen` | `Tab_Video`, `Video_Toggle_Overscan` | `VERIFIED` | `0.1.4` | Clicked & verified `Overscan: 8px` persisted across restart |
| `player.f1_overscan_crop_cycle` | `control` | `ExtraRow_OverscanCrop` | `VERIFIED` | `0.1.4` | Verified live `0 -> 8 -> 16 -> 0` cycle via `RefreshDrawerDescriptor()` |
| `player.f1_tab_audio` | `screen` | `Tab_Audio` | `VERIFIED` | `0.1.4` | Tab clicked & switched cleanly in Grand Tour |
| `player.f1_tab_controls` | `screen` | `Tab_Controls` | `VERIFIED` | `0.1.4` | Tab clicked & switched cleanly in Grand Tour |
| `player.f1_tab_keybinds` | `screen` | `Tab_Keybinds` | `VERIFIED` | `0.1.4` | Tab clicked & switched cleanly in Grand Tour |
| `player.f1_drawer_tab_system` | `screen` | `Tab_System`, `ExtraRow_Region`, `Mapper`, `Execution Tier` | `VERIFIED` | `0.1.4` | Verified in `recart_f1_f3_live.png` (`0` duplicate `FastForward`/`CutsceneSkip` rows) |
| `player.f1_tab_disc_media` | `screen` | `Tab_DiscMedia` | `VERIFIED` | `0.1.4` | Tab clicked & switched cleanly in Grand Tour |
| `player.f3_hud_overlay` | `screen` | `F3` -> `F3Hud` | `VERIFIED` | `0.1.4` | Verified in `recart_f1_f3_live.png` (`60.0 FPS`, `16.67ms`, `0` jitter) |
| `player.version_visibility_v014` | `control` | `reCart v0.1.4` in Media Gate + `F1` drawer | `VERIFIED` | `0.1.4` | Verified via `read_file` on `recart_media_gate.png` & `recart_f1_f3_live.png` |
| `player.f1_settings_persistence_cross_restart` | `mode` | `<dataDir>\settings.video.json` across Launch 2 -> Launch 3 | `VERIFIED` | `0.1.4` | Launch 3 confirmed `Video_Toggle_Overscan` retained `8px` |
| `player.default_entrypoint_and_eaddrinuse` | `entrypoint` | Default `reCart.exe` launch + `EADDRINUSE` probe | `UNTESTED` | `—` | Queued for Phase 4 full installer/launcher pass |
| `player.open_settings_flag` | `mode` | `--open-settings` | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit |
| `player.raw_rom_clean_room_guidance` | `mode` | Raw `.nes` without `.recomp` | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit (`PlayerV013PrelaunchTests`) |

---

## 5. Live Gameplay, Audio, Resize, Saves & Routing (`5`)

| `surfaceId` | Kind | Selector / Control ID | Status | Last Verified | Notes |
| :--- | :--- | :--- | :---: | :---: | :--- |
| `player.demo_cart_cold_aot_and_gameplay_delta` | `mode` | `--demo-cart` (`RIGHT`/`DOWN`, `HostViewport`) | `VERIFIED` | `0.1.4` | `diffCount = 1084` (`>= 25`) + SHA-256 delta verified |
| `player.demo_cart_audio_wasapi` | `mode` | `--dump-audio` (`Assert-WavAudible`) | `VERIFIED` | `0.1.4` | `wavPeak = 5803` (`>= 50`), `AudioDroppedSamples = 0` |
| `player.window_resize_pillarbox` | `mode` | `MoveWindow(1440, 620)` (`nativePar`) | `VERIFIED` | `0.1.4` | Black pillarbox bars verified in `recart_resized_aspect.png` |
| `player.fullscreen_f11_toggle` | `control` | `F11` (`Fullscreen -> True/False`) | `VERIFIED` | `0.1.4` | `Borderless` badge verified in `recart_fullscreen_live.png` + log transitions |
| `player.quicksave_quickload_f5_f9` | `control` | `F5` / `F9` (`*.slot0.savstate`) | `VERIFIED` | `0.1.4` | `saves = 1` + `QuickSave saved` / `QuickLoad restored` in log |

---

## 6. `FEATURES.md` Capabilities & Downstream Routing (`5`)

| `surfaceId` | Kind | Feature / Code | Status | Last Verified | Notes |
| :--- | :--- | :--- | :---: | :---: | :--- |
| `reengine.route_recomp_headless` | `mode` | `reEngine.exe --open <demo.recomp> --wait` | `VERIFIED` | `0.1.4` | Routed via `reEngine\Builds\v0.6.2\reEngine.exe` (`2054.2 ms`); `recart_routed_reengine.png` crisp |
| `feature.core_cpu_ppu_apu_mappers` | `feature` | `CORE-02`, `MAP-01`, `PPU-01`, `APU-01` | `VERIFIED` | `0.1.4` | Exercised live in `--demo-cart` cold AOT + `125/125` xUnit |
| `feature.aot_pipeline_and_trap` | `feature` | `AOT-01` | `VERIFIED` | `0.1.4` | Cold `<dataDir>\cache\*.Generated.dll` compile & mount (`4097.4 ms`) |
| `feature.decomp_d5_v2_parity` | `feature` | `DECOMP-01` (`ValidateD5Core` `isVerified`) | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit (`DecompD4ValidationTests`) |
| `feature.archive_7z_loader` | `feature` | `HOST-01` (`CartMediaArchiveLoader` + `tools\7z.*`) | `VERIFIED` | `0.1.4` | Verified via `125/125` isolated xUnit (`PlayerV013PrelaunchTests`) |
