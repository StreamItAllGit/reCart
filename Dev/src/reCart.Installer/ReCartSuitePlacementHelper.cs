// @module: reCart.Installer.ReCartSuitePlacementHelper
// @purpose: Side-by-side <SuiteRoot>\engines\reCart\v0.1.0\ placement, reEngine/sibling collision guards, wizard auto-sort, and atomic .ready + engine.manifest.json writers.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, System.Windows.Forms, Foundation.Core, Foundation.Installer, reCart.Contracts
// @exports: SuitePlacementResolution, ReCartSuitePlacementHelper

using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using reCart.Contracts;

namespace reCart.Installer;

internal sealed record SuitePlacementResolution(string SuiteRootDir, string InstallDir);

/// <summary>
/// Resolves side-by-side <c>&lt;SuiteRoot&gt;\engines\reCart\v&lt;Version&gt;\</c> targets, protects peer <c>reEngine</c>/engine directories,
/// and atomically writes/cleans <c>.ready</c> and <c>engine.manifest.json</c>.
/// </summary>
internal static class ReCartSuitePlacementHelper
{
    private const string ReCartUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reCart";
    private const string ReEngineUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reEngine";
    private const string StreamItAllSubKey = @"Software\StreamItAll";
    private static string VersionFolder => $"v{CartEngineConstants.Version}";

    internal static bool IsReEngineDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        string norm = NormalizeTrimmedFullPath(dir);
        return string.Equals(Path.GetFileName(norm), CartEngineConstants.SuiteName, StringComparison.OrdinalIgnoreCase) ||
               File.Exists(Path.Combine(norm, "reEngine.exe")) || File.Exists(Path.Combine(norm, "app", "reEngine.exe")) ||
               File.Exists(Path.Combine(norm, "app", "reEngine-Uninstall.exe")) || File.Exists(Path.Combine(norm, "reEngine.Core.dll")) ||
               Directory.Exists(Path.Combine(norm, "engines")) || Directory.Exists(Path.Combine(norm, "toolchain"));
    }

    internal static bool IsOwnInstallManifest(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.EnumerateObject().Any(p =>
                (p.Name.Equals("product_name", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Equals("appId", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Equals("engineId", StringComparison.OrdinalIgnoreCase)) &&
                p.Value.ValueKind == JsonValueKind.String && IsOwnEngineOrAliasToken(p.Value.GetString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    internal static bool HasOtherInstallManifest(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        string full = NormalizeTrimmedFullPath(dir);
        return new[] { Path.Combine(full, "app", "install-manifest.json"), Path.Combine(full, "install-manifest.json") }
            .Any(p => File.Exists(p) && !IsOwnInstallManifest(p));
    }

    internal static bool HasDirectInstallation(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        string full = NormalizeTrimmedFullPath(dir);
        if (IsReEngineDirectory(full) || HasOtherInstallManifest(full)) return false;
        return File.Exists(Path.Combine(full, "reCart.exe")) || File.Exists(Path.Combine(full, "reCartOne.exe")) ||
               File.Exists(Path.Combine(full, "reNes.exe")) || File.Exists(Path.Combine(full, "app", "reCart.Player.exe")) ||
               IsOwnInstallManifest(Path.Combine(full, "app", "install-manifest.json")) ||
               IsOwnInstallManifest(Path.Combine(full, "install-manifest.json")) ||
               IsOwnEngineManifest(Path.Combine(full, "engine.manifest.json"));
    }

    internal static string BuildSideBySideInstallDir(string suiteRoot) =>
        Path.Combine(NormalizeTrimmedFullPath(suiteRoot), "engines", CartEngineConstants.EngineId, VersionFolder);

    internal static string ResolveDefaultSuiteInstallDir(SandboxResolution? sandbox = null) =>
        Path.GetFullPath(Path.Combine(KnownFolderPaths.ResolveLocalProgramsDirectory(sandbox),
            CartEngineConstants.StudioName, CartEngineConstants.SuiteName));

    internal static string ResolveSuiteRootFromCandidate(string candidatePath)
    {
        string norm = NormalizeTrimmedFullPath(candidatePath);
        if (Directory.Exists(Path.Combine(norm, "engines")) || Directory.Exists(Path.Combine(norm, "toolchain"))) return norm;
        for (DirectoryInfo? cur = new(norm); cur is not null; cur = cur.Parent)
            if (string.Equals(cur.Name, "engines", StringComparison.OrdinalIgnoreCase) && cur.Parent is not null)
                return NormalizeTrimmedFullPath(cur.Parent.FullName);

        if (IsReEngineDirectory(norm) && Directory.GetParent(norm) is { } parent &&
            !string.Equals(parent.FullName, Path.GetPathRoot(parent.FullName), StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(parent.Name, CartEngineConstants.SuiteName, StringComparison.OrdinalIgnoreCase) ||
             Directory.Exists(Path.Combine(parent.FullName, "engines")) || Directory.Exists(Path.Combine(parent.FullName, "toolchain"))))
            return NormalizeTrimmedFullPath(parent.FullName);
        return norm;
    }

    internal static string NormalizeWizardInstallDir(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        string norm = NormalizeTrimmedFullPath(rawPath), leaf = Path.GetFileName(norm), parent = Directory.GetParent(norm)?.Name ?? string.Empty;
        if (IsReEngineDirectory(norm) || HasOtherInstallManifest(norm)) return BuildSideBySideInstallDir(ResolveSuiteRootFromCandidate(norm));
        if (HasDirectInstallation(norm) || string.Equals(leaf, VersionFolder, StringComparison.OrdinalIgnoreCase) ||
            (IsOwnEngineOrAliasToken(parent) && leaf.StartsWith("v", StringComparison.OrdinalIgnoreCase))) return norm;
        if (IsOwnEngineOrAliasToken(leaf)) return Path.Combine(norm, VersionFolder);
        if (string.Equals(leaf, "engines", StringComparison.OrdinalIgnoreCase)) return Path.Combine(norm, CartEngineConstants.EngineId, VersionFolder);
        return BuildSideBySideInstallDir(string.Equals(leaf, CartEngineConstants.SuiteName, StringComparison.OrdinalIgnoreCase)
            ? norm : Path.Combine(norm, CartEngineConstants.SuiteName));
    }

    internal static SuitePlacementResolution ResolvePlacement(
        InstallerRuntimeContext ctx, string? explicitInstallDir, string? explicitSuiteDir, string? explicitReEngineDir)
    {
        string? explicitSuite = !string.IsNullOrWhiteSpace(explicitSuiteDir) ? explicitSuiteDir : explicitReEngineDir;
        string? isoRoot = ctx.IsIsolatedTestProcess ? ResolveIsolatedRootOrThrow(ctx, explicitInstallDir) : null;
        SandboxResolution? sandbox = ctx.IsSandboxActive ? ctx.Sandbox : null;
        string? discovered = ctx.ShouldSuppressHostMutations || ctx.IsIsolatedTestProcess ? null : TryDiscoverHostSuiteRoot(ctx.Registry, sandbox);

        string suiteRoot = !string.IsNullOrWhiteSpace(explicitSuite) ? ResolveSuiteRootFromCandidate(explicitSuite)
            : (!string.IsNullOrWhiteSpace(explicitInstallDir) ? ResolveSuiteRootFromCandidate(explicitInstallDir)
            : (discovered ?? (isoRoot is not null ? Path.Combine(isoRoot, CartEngineConstants.SuiteName) : ResolveDefaultSuiteInstallDir(sandbox))));
        if (isoRoot is not null) suiteRoot = AssertContainedOrThrow3(suiteRoot, isoRoot);

        string installDir = BuildSideBySideInstallDir(suiteRoot);
        if (!string.IsNullOrWhiteSpace(explicitInstallDir))
        {
            string fullExplicit = isoRoot is not null
                ? AssertContainedOrThrow3(NormalizeTrimmedFullPath(explicitInstallDir), isoRoot)
                : NormalizeTrimmedFullPath(explicitInstallDir);
            installDir = IsReEngineDirectory(fullExplicit) || HasOtherInstallManifest(fullExplicit)
                ? BuildSideBySideInstallDir(ResolveSuiteRootFromCandidate(fullExplicit)) : fullExplicit;
        }
        if (isoRoot is not null) installDir = AssertContainedOrThrow3(installDir, isoRoot);
        if (sandbox is { IsSandboxActive: true })
        {
            suiteRoot = SandboxEnvironmentSeam.AssertSandboxPathIfActive(suiteRoot, sandbox);
            installDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(installDir, sandbox);
        }
        return new SuitePlacementResolution(suiteRoot, installDir);
    }

    internal static void WriteEngineMetadataAndReadyMarker(string installDir)
    {
        string fullInstall = NormalizeTrimmedFullPath(installDir);
        Directory.CreateDirectory(fullInstall);
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        WriteAtomicJson(Path.Combine(fullInstall, "engine.manifest.json"), new
        {
            schemaVersion = 2, manifestType = "engine-manifest", engineId = CartEngineConstants.EngineId,
            legacyEngineIds = CartEngineConstants.LegacyAliasEngineIds, appId = CartEngineConstants.AppId,
            displayName = CartEngineConstants.DisplayName, version = CartEngineConstants.Version,
            engineVersion = CartEngineConstants.Version, hostProtocolVersion = CartEngineConstants.HostProtocolVersion,
            minSuiteVersion = CartEngineConstants.MinSuiteVersion, compatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack,
            supportedEngineTracks = CartEngineConstants.SupportedEngineTracks, platformId = CartEngineConstants.PlatformId,
            entryExecutable = "reCart.exe", decompProviderAssembly = "app/reCart.Decomp.dll", backendId = CartRenderBackendIds.OpenGl,
            executionTierMode = CartExecutionTierModeCodec.ToCanonicalId(CartExecutionTierMode.StaticAotOnly), updatedAtUtc = nowUtc
        });
        WriteAtomicJson(Path.Combine(fullInstall, ".ready"), new
        {
            engineId = CartEngineConstants.EngineId, legacyEngineIds = CartEngineConstants.LegacyAliasEngineIds,
            appId = CartEngineConstants.AppId, version = CartEngineConstants.Version,
            hostProtocolVersion = CartEngineConstants.HostProtocolVersion, minSuiteVersion = CartEngineConstants.MinSuiteVersion,
            completedAtUtc = nowUtc
        });
    }

    internal static void RemoveEngineMetadataOnUninstall(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return;
        string full = NormalizeTrimmedFullPath(installDir);
        foreach (string rel in new[] { ".ready", "engine.manifest.json", Path.Combine("app", "engine.manifest.json") })
            if (File.Exists(Path.Combine(full, rel))) File.Delete(Path.Combine(full, rel));
        if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full, false);
    }

    internal static void WireWizardAutoSortHandlers(InstallerWizardForm wizard)
    {
        DisableMnemonics(wizard);
        bool updating = false, customPrimary = false, customSecondary = false;
        string lastPrimary = NormalizeTrimmedFullPath(wizard.InstallDirTextBox.Text), lastSuite = NormalizeTrimmedFullPath(wizard.SecondaryDirTextBox.Text);

        void Sync(bool fromSuite)
        {
            if (updating) return;
            updating = true;
            try
            {
                if (!fromSuite)
                {
                    string sorted = NormalizeWizardInstallDir(wizard.InstallDirTextBox.Text);
                    if (string.IsNullOrWhiteSpace(sorted)) return;
                    SetBox(wizard.InstallDirTextBox, sorted);
                    if (!sorted.Equals(lastPrimary, StringComparison.OrdinalIgnoreCase)) { customPrimary = true; lastPrimary = sorted; }
                    if (!customSecondary) { lastSuite = ResolveSuiteRootFromCandidate(sorted); SetBox(wizard.SecondaryDirTextBox, lastSuite); }
                    return;
                }
                if (string.IsNullOrWhiteSpace(wizard.SecondaryDirTextBox.Text)) return;
                string suite = ResolveSuiteRootFromCandidate(wizard.SecondaryDirTextBox.Text);
                SetBox(wizard.SecondaryDirTextBox, suite);
                if (!suite.Equals(lastSuite, StringComparison.OrdinalIgnoreCase)) { customSecondary = true; lastSuite = suite; }
                string cur = NormalizeTrimmedFullPath(wizard.InstallDirTextBox.Text);
                if (!customPrimary || string.IsNullOrWhiteSpace(cur) || cur.Equals(lastPrimary, StringComparison.OrdinalIgnoreCase))
                { lastPrimary = BuildSideBySideInstallDir(suite); SetBox(wizard.InstallDirTextBox, lastPrimary); }
            }
            finally { updating = false; }
        }

        HookInputSync(wizard.InstallDirTextBox, wizard.BrowseButton, () => Sync(false));
        Button? sec = EnumerateButtons(wizard).FirstOrDefault(b => b != wizard.BrowseButton && b.Text.StartsWith("Browse", StringComparison.OrdinalIgnoreCase));
        HookInputSync(wizard.SecondaryDirTextBox, sec, () => Sync(true));
        wizard.PrimaryButton.MouseDown += (_, _) => { Sync(true); Sync(false); };
        wizard.PrimaryButton.Enter += (_, _) => { Sync(true); Sync(false); };
    }

    private static string ResolveIsolatedRootOrThrow(InstallerRuntimeContext ctx, string? explicitInstallDir) =>
        !string.IsNullOrWhiteSpace(ctx.DataDirectory) ? ctx.DataDirectory
        : (!string.IsNullOrWhiteSpace(explicitInstallDir) && InstallerRuntimeContext.IsSafeIsolatedInstallDirectory(explicitInstallDir)
            ? Path.GetFullPath(explicitInstallDir)
            : throw new InvalidOperationException("Fail-Closed Isolation Violation: APP_ISOLATED_TEST=1 requires explicit --data-dir."));

    private static string? TryDiscoverHostSuiteRoot(IPlatformRegistry registry, SandboxResolution? sandbox)
    {
        foreach (string key in new[] { ReCartUninstallSubKey, ReEngineUninstallSubKey })
        {
            string? raw = registry.GetValue(PlatformRegistryHive.CurrentUser, key, "InstallLocation")?.AsString();
            if (!string.IsNullOrWhiteSpace(raw) && Path.IsPathFullyQualified(raw.Trim()))
            {
                string norm = NormalizeTrimmedFullPath(raw.Trim());
                if (sandbox is not { IsSandboxActive: true } || SandboxEnvironmentSeam.IsPathWithinSandbox(norm, sandbox))
                    return ResolveSuiteRootFromCandidate(norm);
            }
        }
        string? lastBase = registry.GetValue(PlatformRegistryHive.CurrentUser, StreamItAllSubKey, "LastInstallBaseDir")?.AsString();
        if (string.IsNullOrWhiteSpace(lastBase) || !Path.IsPathFullyQualified(lastBase.Trim())) return null;
        string normBase = NormalizeTrimmedFullPath(lastBase.Trim());
        string cand = string.Equals(Path.GetFileName(normBase), CartEngineConstants.SuiteName, StringComparison.OrdinalIgnoreCase)
            ? normBase : Path.Combine(normBase, CartEngineConstants.StudioName, CartEngineConstants.SuiteName);
        return (sandbox is not { IsSandboxActive: true } || SandboxEnvironmentSeam.IsPathWithinSandbox(cand, sandbox)) ? cand : null;
    }

    private static bool IsOwnEngineManifest(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("engineId", out JsonElement id) &&
                   id.ValueKind == JsonValueKind.String && IsOwnEngineOrAliasToken(id.GetString());
        }
        catch { return false; }
    }

    private static bool IsOwnEngineOrAliasToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        (token.Trim().Equals(CartEngineConstants.EngineId, StringComparison.OrdinalIgnoreCase) ||
         token.Trim().Equals(CartEngineConstants.AppId, StringComparison.OrdinalIgnoreCase) ||
         CartEngineConstants.LegacyAliasEngineIds.Any(a => token.Trim().Equals(a, StringComparison.OrdinalIgnoreCase)));

    private static void WriteAtomicJson(string targetPath, object payload)
    {
        string tmp = targetPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(tmp, targetPath, overwrite: true);
    }

    private static string AssertContainedOrThrow3(string candidate, string root)
    {
        try { return AppEnvironment.AssertPathContained(candidate, root); }
        catch (InvalidOperationException ex) { SandboxEnvironmentSeam.ThrowContainmentViolation(ex.Message, ex); return candidate; }
    }

    private static void HookInputSync(TextBox box, Button? browse, Action sync)
    {
        if (browse is not null) browse.Click += (_, _) => sync();
        box.Leave += (_, _) => sync(); box.Validating += (_, _) => sync();
        box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) sync(); };
    }

    private static void SetBox(TextBox tb, string value)
    {
        if (!string.Equals(tb.Text.Trim(), value, StringComparison.Ordinal)) { tb.Text = value; tb.SelectionStart = value.Length; }
    }

    private static void DisableMnemonics(Control root)
    {
        foreach (Control c in root.Controls) { if (c is Label l) l.UseMnemonic = false; else if (c is ButtonBase b) b.UseMnemonic = false; if (c.HasChildren) DisableMnemonics(c); }
    }

    private static IEnumerable<Button> EnumerateButtons(Control root)
    {
        foreach (Control c in root.Controls) { if (c is Button b) yield return b; foreach (Button n in EnumerateButtons(c)) yield return n; }
    }

    internal static string NormalizeTrimmedFullPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        string trimmed = rawPath.Trim().Trim('"');
        try
        {
            string full = Path.GetFullPath(trimmed), root = Path.GetPathRoot(full) ?? string.Empty;
            return !string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }
}
