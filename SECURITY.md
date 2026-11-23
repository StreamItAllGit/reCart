# Security Policy

## Supported Versions

| Version | Supported | Security Boundary Notes |
| :--- | :--- | :--- |
| `0.1.x` | Yes | Active `reCart v0.1.0` track (`reCart.*` binaries on `reEngine v0.5.0` + `Foundation v0.5.2`) |

## Built-In Security & Containment Invariants

1. **Zero-Daemon Execution (`AllowAutoStart = false`):**
   `reCart` never installs persistent background services or Windows sign-in Run registry entries; `--autostart` flags are rejected or ignored.
2. **Dual-Binary Release Automation Stripping (`ExitCode = 2`):**
   Shipping `Release` builds of both `reCart.Player` and `reCart-Setup` exclude `Foundation.Automation` and fail closed immediately with exit code `2` if `--automation-port` is supplied.
3. **Path Containment & Zip-Slip Protection:**
   `.recomp`, `.decomp`, `.nes`, and `.fds` archive/media readers enforce canonical path containment (`AppEnvironment.AssertPathContained`) and strict entry-size caps (`ExitCode = 3` on escape).
4. **Clean-Room & Zero-BIOS Copyright Boundaries:**
   Tracked repositories, `.decomp` recipes, and `.recomp` cartridges contain zero copyrighted retail ROMs, disk images, vendor FDS BIOS ROMs, extracted retail assets, retail C function bodies, or pre-generated `*.Generated.dll` assemblies (`data\rom\` and `<dataDir>\cache\` stay strictly local and gitignored; `.fds` writes use copy-on-write `<dataDir>\saves\` overlays).
5. **Isolated Test Sandbox (`APP_ISOLATED_TEST=1`):**
   Automated xUnit suites run strictly inside ephemeral OS temp directories (`Path.GetTempPath()`) and never read or mutate live user installs.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for suspected security vulnerabilities.

1. Report privately via GitHub **Security Advisories** (`Report a vulnerability` on the repository Security tab) or direct maintainer contact.
2. Include the affected assembly, version (`v0.1.x`), reproduction steps, and expected vs. actual behavior.
3. Maintainers acknowledge reports within **72 hours** and coordinate remediation prior to public disclosure.
