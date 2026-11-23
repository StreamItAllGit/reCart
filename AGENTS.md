# Context: reCart

- **Canonical Engine (`ENGINE`):** `reCart` (`v0.1.3`; next `v0.2.0`; aliases `reCart` <-> `reCartOne` <-> `reNes`; workspace `Personal/reCartOne`, `Dev/reCart.slnx`). Built on `reEngine v0.6.2+` + `Foundation v0.5.5`; keep `Foundation\`, `reEngine\`, `reStation\`, `_DevBible\`, `IronFrame-OuterFortress-Cart-01-Decomp\`, and `%LOCALAPPDATA%\Programs\` strictly read-only.
- **Core Thesis:** **AOT-primary static recompilation stack** (`v0.2.0` Shared IR/RyuJIT/Zero-RWX; `v0.7.0` `R04` JIT boot; `interpreterOracle` quarantined); `v0.1->v0.7->v1.0.0` Gold (`v1.1-v5.0`); `APP_ISOLATED_TEST=1`.
- **Control:** [active.state](active.state) | [manifest](Dev/app.manifest.json) | [solution](Dev/reCart.slnx) | [workspace](Dev/reCart.code-workspace) | [reEngine](https://github.com/StreamItAllGit/reEngine)

## 1. Quartet & Policies
- [README](README.md) | [FEATURES](FEATURES.md) | [ROADMAP](ROADMAP.md) | [CHANGELOG](CHANGELOG.md) | [SECURITY](SECURITY.md) | [CONTRIBUTING](CONTRIBUTING.md)

## 2. Manual Shelf (`Dev/docs/`)
- [ARCHITECTURE](Dev/docs/ARCHITECTURE.md) | [INTERFACES](Dev/docs/INTERFACES.md) | [USER_GUIDE](Dev/docs/USER_GUIDE.md) | [OPERATIONS](Dev/docs/OPERATIONS.md)

## 3. Governance & Local Anchors
- **Standards:** Dev Bible Standards 00, 16, 18, 22, 24, 27, 28, 32, 36, 40 + `RecompBible R01–R14` (`_DevBible` read-only)
- **Notion Hub:** Manual-only from single master chat (Std 36; IDs in gitignored `Dev/data/notion_hub.local.json`)
- **Public Repo & Discord `/post` Hook:** `isPublicRepo: true` (`StreamItAllGit/reCart`), `requireHumanReviewBeforePush: false`; GitHub + Discord `/post` (`active.state` `discordPost` -> `DISCORD_WEBHOOK_FOUNDATION`, `@StreamDoesDev`) on approved builds/hotfixes; `7`-file `Auto` (`v0.5.0` `8`-file).
