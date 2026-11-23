## Summary

<!-- Describe what this PR changes and why. -->

## Clean-Room & Safety Checklist

- [ ] Zero copyrighted ROMs, disk images, vendor BIOS files, extracted retail assets, or `*.Generated.dll` / `*.g.cs` files included
- [ ] Zero PII, credentials, or workstation-specific file paths in code or Markdown
- [ ] `dotnet build Dev\reCart.slnx -c Release /warnaserror` and `-c Debug -p:ReCartTestAutomation=true` pass with `0` warnings
- [ ] `APP_ISOLATED_TEST=1 dotnet test Dev\reCart.slnx` passes cleanly with zero host mutations
- [ ] Manual Shelf (`Dev\docs\`) and `CHANGELOG.md` updated if CLI, ABI, or manifest behavior changed
