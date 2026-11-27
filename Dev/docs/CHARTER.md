# Project Charter: reCart

**Status:** Active  
**Last Updated:** 2026-11-27  
**Primary Audience:** Players running legally owned 8-bit cartridge and disk-system titles through clean-room native builds, plus clean-room module and decompilation authors

---

## 1. Core Identity & Mission
**reCart** (aliases `reCartOne` and `reNes`) is a clean-room `.NET 8.0` ahead-of-time (AOT) static recompilation engine and decompilation platform provider (`platformId: "nes"`) built on `Foundation` and `reEngine`. It exists to turn clean-room cartridge recipes and player-supplied ROM/disk dumps into locally compiled, hardware-timed native C# modules rather than relying on generic runtime emulation. Success means deterministic, aspect-accurate, zero-copyright-leak execution from `v0.1.x` pre-release baselines through `v1.0.0` desktop gold and multi-platform horizons.

## 2. Who It Is For (and Not For)
- **Built For:** Players who want native, hardware-timed execution of legally dumped `.nes`, `.prg`, and `.fds` titles packaged as clean-room `.recomp` modules, alongside module authors building and verifying clean-room recompilation recipes.
- **Not Built For:** Users seeking plug-and-play piracy bundles, generic compatibility-first emulation hacks, background system services, or external achievement-network clients.

## 3. Non-Negotiable Principles
1. **AOT-Primary Static Recompilation:** Certified `.recomp` packages execute as locally compiled native C# assemblies (`staticAotOnly` default), failing closed on unlifted addresses rather than silently falling back to an interpreter. Shared IR / JIT bring-up exists for profiling, standalone raw-media bring-up, and static promotion, while the interpreter oracle stays strictly quarantined for differential testing.
2. **Zero-Copyright Clean-Room Discipline:** Tracked repositories, `.decomp` recipes, and `.recomp` archives never ship copyrighted retail ROMs, disk images, vendor disk-system BIOS ROMs, extracted retail assets, or pre-generated retail game assemblies. Players supply local media under `data\rom\` verified by SHA-256, and disk writes use copy-on-write overlays.
3. **Hardware Timing & Aspect-Ratio Fidelity:** CPU, PPU, APU, and mapper interactions remain cycle- and dot-synchronized without speculative rollback tricks, and video presentation preserves authentic pixel aspect ratios (`nativePar` pillarboxed default) without ever stretching `4:3` imagery to `16:9`.
4. **Zero-Daemon Local Containment:** Both launcher and player enforce `AllowAutoStart = false` with zero background services or startup entries. Shipping release binaries strip test-automation bridges completely and enforce path containment and archive safety limits.

## 4. Hard Boundaries & Non-Goals
- **Explicit Non-Goals:**
  - Generic compatibility-emulator framing or silent interpreter fallback under `staticAotOnly`.
  - Speculative Run-Ahead input prediction, `.snowpak` port-asset packaging, vendor FDS BIOS distribution, or third-party achievement network integration.
  - Cloud drive uploads of installers, builds, or archives, and automated external Notion syncs during development workflows.
- **Ecosystem & Ownership Seams:**
  - **`reCart` owns:** `6502` / `RP2A03` / `RP2C02` / `APU` hardware execution, cartridge mappers and clean-room FDS HLE, `6502 -> C#` static lifting, Shared IR / JIT bring-up, and NES clean-room decompilation validation.
  - **`reEngine` owns:** Shared SDL3 windowing, audio/video host rails, biquad LP/HP audio filtering, overlay drawers and 10-rail menus, multi-engine alias dispatch, and opt-in runtime API hosting.
  - **`Foundation` owns:** Single-file launcher, side-by-side/dual-installer plumbing, path containment, diagnostics, settings storage, and media container extraction helpers.
- **Read-Only & Operational Boundaries:**
  - Keep `Foundation\`, `reEngine\`, `reStation\`, `_DevBible\`, upstream research repositories (such as `MetalGear-NintendoNES`), and installed production directories (`D:\Programs\...`, `%LOCALAPPDATA%\Programs\...`) strictly read-only.
  - Run automated test suites strictly inside isolated temporary directories (`APP_ISOLATED_TEST=1`) with zero mutation of user installs.

## 5. Agent Steering & Trade-Off Heuristics
When planning, building, or reviewing changes, resolve ambiguity using these priority rules:
1. **Prefer Determinism and Fail-Closed Safety over Convenience:** Never mask missing lifted addresses, invalid ROM hashes, or containment violations with silent fallbacks.
2. **Prefer Upstream `reEngine` / `Foundation` Rails over Local Duplication:** Reuse shared host UI, DSP filters, settings persistence, and installer helpers rather than re-implementing them inside `reCart`.
3. **Prefer Clean Separation of Hardware Core, Lifter, and Host:** Keep `reCart.Contracts` additive and backwards-compatible across `0.1.x` -> `1.x`, and keep assembly count bounded (`Dev\reCart.slnx`) by organizing cohesive folders and small single-purpose files.
4. **When in Doubt:** Preserve read-only upstream boundaries, verify changes with isolated lockstep/unit tests, and flag scope or boundary tensions for human sign-off rather than loosening invariants.

## 6. Conceptual Mental Model & Tone
Think of **reCart** as a precision hardware workbench for static recompilation: transparent about what is native AOT versus bring-up JIT, strict about copyright and filesystem boundaries, and disciplined about keeping every subsystem clean, testable, and faithful to original silicon timing. Documentation and user-facing messages should be crisp, plain-English, and free of internal shorthand.

## 7. Amendment Policy
This charter guides scoping, planning, implementation, and audits. Contributors and automated workflows must flag conflicts between a proposed feature and this charter rather than silently bypassing it; relaxing core principles or hard boundaries requires explicit human approval.
