# Context: reCart

- **Canonical Engine (`ENGINE`):** `reCart` (`v0.1.3`; next `v0.2.0`; aliases `reCart` <-> `reCartOne` <-> `reNes`; workspace `Personal/reCartOne`, `Dev/reCart.slnx`). Built on `reEngine v0.6.2+` + `Foundation v0.5.5`; keep `Foundation\`, `reEngine\`, `reStation\`, `_DevBible\`, `IronFrame-OuterFortress-Cart-01-Decomp\`, and `%LOCALAPPDATA%\Programs\` strictly read-only.
- **Core Thesis & Roadmap:** Strictly an **AOT-primary static recompilation stack** (never an emulator; Unified `6502 -> Shared IR` + Collectible RyuJIT / Zero-RWX IR in `v0.2.0`; raw-media JIT boot `R04 §2.2` in `v0.7.0`; `interpreterOracle` quarantined to `--execution-tier interpreterOracle`). Dedicated 7-minor backend ladder (`v0.1.0 -> v0.7.0`) to `v1.0.0` Going Gold (`v1.1–v5.0` horizons). Run isolated tests via `APP_ISOLATED_TEST=1`.
- **Control:** [active.state](active.state) | [manifest](Dev/app.manifest.json) | [solution](Dev/reCart.slnx) | [workspace](Dev/reCart.code-workspace) | [reEngine](https://github.com/StreamItAllGit/reEngine)

## 1. Quartet & Policies
- [README](README.md) | [FEATURES](FEATURES.md) | [ROADMAP](ROADMAP.md) | [CHANGELOG](CHANGELOG.md) | [SECURITY](SECURITY.md) | [CONTRIBUTING](CONTRIBUTING.md)

## 2. Manual Shelf (`Dev/docs/`)
- [ARCHITECTURE](Dev/docs/ARCHITECTURE.md) | [INTERFACES](Dev/docs/INTERFACES.md) | [USER_GUIDE](Dev/docs/USER_GUIDE.md) | [OPERATIONS](Dev/docs/OPERATIONS.md)

## 3. Governance & Local Anchors
- **Standards:** Dev Bible Standards 00, 16, 18, 22, 24, 27, 28, 32, 36, 40 + `RecompBible R01–R14` (`_DevBible` read-only)
- **Notion Hub:** Manual-only from single master chat (Std 36; IDs in gitignored `Dev/data/notion_hub.local.json`)
- **Pre-Release & Dual-Installer:** 2-channel releases (GitHub + Discord `/post`, Std 28 §3.1; zero Google Drive binaries; manual review before push); `7`-file `Auto` installer through `v0.4.0`, `8`-file Dual-Installer at `v0.5.0` (`reEngine v0.8.0`).
