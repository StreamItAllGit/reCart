# Operations Playbook — Timed Input Storyboards & Control Memory

Per-project Stage 2 E2E input-handling memory for **`reCart`** (`Dev\scripts\run_stage2_sandbox_e2e.ps1` + `exebox-stream` at `http://127.0.0.1:5005`). Ephemeral captures and traces stay strictly in gitignored `Dev\docs\tests\`.

---

## 1. Coordinate & Camera Axes Mapping

- **Host Window & Viewport (`1280x720` SDL3):** `reEngine.Host` renders the `256x240` NES framebuffer pillarboxed inside `HostViewport` (`nativePar` `8:7` pixel aspect ratio by default). Resizing to `1440x620` preserves side black pillarbox bands (`L/R` RGB sum `<= 120` at `x=16` and `x=width-17`).
- **Ironframe Clean-Room Demo Cart (`ironframe-outerfortress-cart-01`):**
  - Spawn coordinate: `(X=120, Y=112)` in `256x240` NES screen space.
  - D-Pad / `RIGHT` (`+X`), `LEFT` (`-X`), `DOWN` (`+Y`), `UP` (`-Y`) move the 16x16 amber operator metasprite at `2 px/frame` (`4 px/frame` under Fast-Forward `F6`/`Grave`).
  - NES `$4016` controller bitmask: `0x01` `A`, `0x02` `B`, `0x04` `Select`, `0x08` `Start`, `0x10` `Up`, `0x20` `Down`, `0x40` `Left`, `0x80` `Right`.

---

## 2. Menu & Cursor Defaults

- **Empty `data\rom\` Boot:** Launches `FirstRunMediaGateView` immediately with `CLEAN-ROOM MEDIA GATE - reCart v0.1.4` (never auto-starts retail media; `AllowAutoStart = false`). Focusable buttons in fallback order: `MediaGate_BrowseButton`, `MediaGate_SelectModuleButton`, `MediaGate_LaunchDemoButton`, plus `MediaGate_DropZone`.
- **F1 Settings Drawer (`Tab_Video` default):** Opening `F1` defaults to `Tab_Video`. `ExtraRow_OverscanCrop` cycles `0 -> 8 -> 16 -> 0` and refreshes via `RefreshDrawerDescriptor()`, while `reEngine.Host`'s native `Video_Toggle_Overscan` (`0px -> 8px -> 16px -> 0px`) persists across restarts to `<dataDir>\settings.video.json` (or `<dataDir>\config\settings.video.json` when `--config` is passed).
- **`Tab_System`:** Interactive controls in `/v1/tree` include `ExtraRow_Region` (cycles `NTSC -> PAL -> Dendy` via `RefreshDrawerDescriptor()`), `System_QuickSave`, `System_QuickLoad`, `System_ToggleFastForward`, `System_SkipCutscene`, `System_ToggleRuntimeLogs`, and `System_QuitGame`; `Mapper: NROM (#0)` and `Execution Tier: AOT Primary` (`IsInteractive: false`) render as static canvas text. (Duplicate `ExtraRow_FastForward` / `ExtraRow_CutsceneSkip` rows were removed in `v0.1.4`.)
- **Setup Wizard (`reCart.Installer`):** `Wizard_InstallDir` auto-sorts into `<suiteRoot>\engines\reCart\v<Version>`; `Wizard_PrimaryButton` transitions from `Install` -> `Finish` / `Close`.

---

## 3. Hold Thresholds, Settle Delays & Frame-Pacing Quirks

- **Cold AOT Compile Settle (Critical):** On a fresh `--demo-cart` boot, `ColdAotCacheCompiler` compiles `<dataDir>\cache\ironframe-outerfortress-cart-01.Generated.dll` asynchronously behind `HostAotCompileOverlayView`. **Never** send gameplay keys or `ESC` after a blind `500 ms` sleep—poll until `cache\*.Generated.dll` exists and `AotCompile_Progress` disappears from `/v1/tree` (`~2–15s` depending on host load; timeout `45s`), then settle `400 ms`.
- **Tap & Motion Pacing:** `PlayerAutomationBridge` maps `/v1/keyboard` `press` (`RIGHT`, `DOWN`, `LEFT`, `UP`, `F1`, `F3`, `F5`, `F9`, `F11`, `ESC`, `Grave`) into `HostSessionRunner.EnqueueKeyPress`, where `HostInputMapper` auto-releases synthetic taps after 2 frames (`~33 ms`). Issue `3` spaced presses (`120 ms` cadence) followed by a `300 ms` settle before snapping `recart_gameplay_after.png` (`~0.65s` total beat).
- **Audio Synthesis Quirk (`v0.1.4`):** While `_runtime` is mounting or falling back to `SynthesizeDemoAudio`, idle frames emit silent PCM (`maxAbs == 0`) and sound plays only on player input (`RIGHT`/`DOWN`/action) or once cold-AOT `CartMachineHost` Pulse1 starts. Always drive `RIGHT`/`DOWN` before asserting `Assert-WavAudible`.
- **Clean Exit Order:** Try `ESC` (`3s` wait); if a modal/drawer absorbed `ESC`, fall back to `F1` -> `Tab_System` -> `System_QuitGame` rather than killing the process.

---

## 4. Calibrated Waypoints, Obstacles & Bounds

- **Playfield Bounds (`256x240`):** Clamped playable interior `X in [18..224]` (`$12..$E0`), `Y in [18..208]` (`$12..$D0`).
- **Tactical Obstacles:** Perimeter border tiles at row `0` / `29` and column `0` / `31`; corner pylons at `(row 6, col 8)`, `(row 6, col 23)`, `(row 23, col 8)`, `(row 23, col 23)`. Spawn `(120, 112)` has clear `+X/+Y` travel for `> 40 px` without colliding with any pylon.

---

## 5. Proven Timed Storyboards (`Dev\scripts\run_stage2_sandbox_e2e.ps1`)

| Beat | Target Overlay / Screen | Pre-Condition | Action / Hold / Clear Sequence | Timing Window | Post-Condition Assert | Live-Correct Fallback |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `WIZ-01` | Setup Wizard GUI | Fresh 4-Drive Sandbox | Verify `Wizard_InstallDir`, click `Wizard_PrimaryButton` (`Install` -> `Finish`) | `~22s` max | `.ready` + `engine.manifest.json` + sibling `reSlate` intact | Read `Wizard_StatusLabel` via `/v1/tree` + snapshot PNG |
| `L1-01` | Empty-Media Gate | Empty `data\rom\` | Boot `reCart.Player`, verify `MediaGate_*` IDs, snap PNG, clean exit | `< 5s` | `MediaGate_BrowseButton`/`SelectModuleButton`/`LaunchDemoButton` visible | `F1` -> `Tab_System` -> `System_QuitGame` if `ESC` absorbed |
| `B01` | Cold AOT Settle | `--demo-cart` booted | Poll `cache\*.Generated.dll` + absent AOT overlay, settle `400 ms` | `<= 45s` | `.Generated.dll` created, gameplay viewport active | Inspect `/v1/tree` + `recart.player.log` |
| `B02` | Demo Gameplay + Audio | `B01` settled | Snap `before.png`; `RIGHT`+`DOWN` x3 (`120 ms` step) + `300 ms` settle; snap `after.png` | `~1.0s` | `diffCount >= 25`, SHA-256 changed, `wavPeak >= 50` | Capture `/v1/snapshot.png`, verify `HostViewport` focus |
| `B03` | `F3` HUD + `F1` Drawer | Active gameplay | Press `F3` (`250 ms`), `F1` (`250 ms`), cycle `ExtraRow_OverscanCrop` x3 (`0->8->16->0`), click `Video_Toggle_Overscan` (`8px`), click `Tab_System`, verify `ExtraRow_Region` + `0` dupes, snap PNG, touch all remaining tabs, close `F1` | `~2.2s` | `ExtraRow_Region` present; zero `ExtraRow_FastForward`/`CutsceneSkip` dupes; `reCart v0.1.4` + `Mapper`/`Execution Tier` crisp | Read `/v1/tree` node tree & inspect `recart_f1_f3_live.png` |
| `B04` | Resize + `F11` + `F5`/`F9` | Drawer closed | `MoveWindow(1440,620)` (`350 ms`), `F11` x2 (`350/250 ms`), `F5` (`250 ms`), `F9` (`250 ms`), clean `ESC` exit | `~2.0s` | Pillarbox edges black, `Fullscreen -> True/False`, `.slot0.savstate`, `AudioDroppedSamples=0` | Inspect `recart_resized_aspect.png` & `recart_fullscreen_live.png` |
| `L3-01` | Cross-Restart Persistence | `B04` exited `0` | Relaunch `--demo-cart` on same `--data-dir`, open `F1`, verify persisted `Video_Toggle_Overscan` (`8px`) + `settings.video.json`, quit + `reEngine --open --wait` | `~4.0s` | `settings.video.json` present, `Video_Toggle_Overscan` retains `8`, `routedPng > 500 B` | Inspect `<dataDir>\settings.video.json` + `reEngine` log |
