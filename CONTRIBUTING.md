# Contributing to reCart

Thank you for helping build **`reCart`**, a clean-room `.NET 8.0` ahead-of-time static recompilation engine (`platformId: "nes"`) hosted on [reEngine](https://github.com/StreamItAllGit/reEngine).

## Scope & Clean-Room Rules

1. **Ahead-of-Time Static Recompilation Only:** `reCart` is not a generic compatibility emulator. Runtime code executes statically recompiled C# assemblies (`<dataDir>\cache\<Game>.Generated.dll`), with Tier 3 RyuJIT / Zero-RWX IR serving strictly as emergency bring-up fallback (`v0.2.0+`) and Tier 4 `interpreterOracle` quarantined to differential parity verification.
2. **Zero Copyrighted Media or Vendor BIOS Assets:** Never commit, attach, or embed copyrighted `.nes` ROMs, `.fds` disk images, vendor FDS BIOS ROMs, extracted CHR/PRG assets, retail C function bodies, retail string literals, or pre-generated `*.g.cs` / `*.Generated.dll` binaries.
3. **Never Stretch Native Aspect Ratios:** Display defaults must render native pillarboxed aspect ratio (`4:3` / integer pixel-aspect). Wider `16:9` views are unlocked strictly through opt-in Hor+ extended-nametable hooks.
4. **Clean Licensing:** All clean-room contributions are accepted under the **Mozilla Public License 2.0** ([`LICENSE`](LICENSE)). Do not introduce `GPLv3` dependencies.

## Running Isolated Tests Locally

All automated tests run strictly isolated without modifying user installs:

```powershell
$env:APP_ISOLATED_TEST = "1"
dotnet build Dev\reCart.slnx -c Release /warnaserror
dotnet test Dev\reCart.slnx -c Release /warnaserror --no-build
dotnet build Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true /warnaserror
dotnet test Dev\reCart.slnx -c Debug -p:ReCartTestAutomation=true --no-build
```

## Pull Request Checklist

- Zero compiler warnings in both `Release` and `Debug` builds (`/warnaserror`).
- Zero workstation-specific paths, secrets, or personal data in diffs or docs.
- New config/manifest keys follow consistent `camelCase` naming and preserve unknown fields cleanly.
- Relevant Manual Shelf docs (`Dev\docs\`) updated alongside any public contract or CLI change.
