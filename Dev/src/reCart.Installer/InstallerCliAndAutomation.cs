// @module: reCart.Installer.InstallerCliAndAutomation
// @purpose: CLI argument sanitization, host/automation option parsing, settings persistence, and Debug/Automation-only AutomationLoopbackServer hook.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Runtime.CompilerServices, System.Runtime.InteropServices, System.Text, System.Text.Json, Foundation.Installer, reCart.Contracts
// @exports: InstallerCliAndAutomation

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Installer;
using reCart.Contracts;
#if RECART_ENABLE_AUTOMATION
using System.Security.Cryptography;
using Foundation.Automation;
using Foundation.Core;
#endif

namespace reCart.Installer;

internal static class InstallerCliAndAutomation
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static void AttachParentConsoleIfInteractive(string[]? args)
    {
        if (args is { Length: > 0 } && OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
            _ = AttachConsole(AttachParentProcess);
    }

    public static bool IsIsolatedTestEnv()
    {
        string? v = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST")?.Trim();
        return string.Equals(v, "1", StringComparison.Ordinal) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasAutomationPortFlag(string[]? args) =>
        args is not null && args.Any(r =>
        {
            string t = (r ?? string.Empty).Trim();
            return t.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) || t.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase);
        });

    public static string[] SanitizeArgs(string[]? args) =>
        args is null ? Array.Empty<string>()
            : args.Where(a => !string.IsNullOrWhiteSpace(a) &&
                              !a.Trim().StartsWith("--autostart", StringComparison.OrdinalIgnoreCase) &&
                              !a.Trim().StartsWith("--auto-start", StringComparison.OrdinalIgnoreCase)).ToArray();

    public static string NormalizeProfileId(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile)) return CartEngineConstants.DefaultInstallProfile;
        return rawProfile.Trim().ToLowerInvariant() switch
        {
            "lightweight" => "lightweight",
            "full" => "full",
            _ => throw new ArgumentException($"Invalid --profile '{rawProfile}'. Expected 'lightweight' or 'full'.")
        };
    }

    public static string[] ExtractAutomationArgs(string[] clean, out int? automationPort, out string? automationToken)
    {
        automationPort = null; automationToken = null;
        var forwarded = new List<string>(clean.Length);
        for (int i = 0; i < clean.Length; i++)
        {
            string trimmed = (clean[i] ?? string.Empty).Trim();
            if (TryReadInlineOrNext(clean, ref i, trimmed, "--automation-port", out string rawPort))
            {
                if (!int.TryParse(rawPort.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port is < 0 or > 65535)
                    throw new ArgumentException($"--automation-port expects an integer in 0..65535, got '{rawPort}'.");
                automationPort = port;
                continue;
            }
            if (TryReadInlineOrNext(clean, ref i, trimmed, "--automation-token", out string rawToken))
            {
                if (string.IsNullOrWhiteSpace(rawToken)) throw new ArgumentException("--automation-token requires a non-empty value.");
                automationToken = rawToken.Trim();
                continue;
            }
            forwarded.Add(clean[i]);
        }
        return forwarded.ToArray();
    }

    public static string[] ExtractHostArgs(
        string[] args, out string profileId, out string? suiteDir, out string? reEngineDir,
        out bool silent, out bool dryRun, out bool verifyOnly, out bool uninstall,
        out string? installDir, out string? dataDir)
    {
        string[] clean = SanitizeArgs(args);
        profileId = CartEngineConstants.DefaultInstallProfile;
        suiteDir = null; reEngineDir = null; installDir = null; dataDir = null;
        silent = false; dryRun = false; verifyOnly = false; uninstall = false;

        var forwarded = new List<string>(clean.Length);
        for (int i = 0; i < clean.Length; i++)
        {
            string raw = clean[i].Trim();
            int eq = raw.IndexOf('=');
            string key = eq > 2 && raw.StartsWith("--", StringComparison.Ordinal) ? raw[..eq] : raw;
            string? val = eq > 2 && raw.StartsWith("--", StringComparison.Ordinal) ? raw[(eq + 1)..] : null;
            string lower = key.ToLowerInvariant();

            if (lower is "--profile" or "--suite-dir" or "--reengine-dir" or "--install-dir" or "--target-dir" or "--data-dir")
            {
                val ??= i + 1 < clean.Length ? clean[++i] : null;
                if (val is null) { forwarded.Add(raw); continue; }
                string canon = lower == "--target-dir" ? "--install-dir" : lower;
                if (lower == "--profile") { profileId = NormalizeProfileId(val); val = profileId; }
                else if (lower == "--suite-dir") suiteDir = val;
                else if (lower == "--reengine-dir") reEngineDir = val;
                else if (lower == "--data-dir") dataDir = val;
                else installDir = val;
                forwarded.Add(canon); forwarded.Add(val);
                continue;
            }

            forwarded.Add(clean[i]);
            if (lower is "--silent" or "--quiet") silent = true;
            else if (lower == "--dry-run") dryRun = true;
            else if (lower == "--verify") verifyOnly = true;
            else if (lower == "--uninstall") uninstall = true;
        }
        return forwarded.ToArray();
    }

    public static string[] ReplaceInstallDirArg(string[] args, string resolvedInstallDir)
    {
        var updated = new List<string>(args.Length + 2);
        bool replaced = false;
        for (int i = 0; i < args.Length; i++)
        {
            string t = args[i].Trim();
            if ((t.Equals("--install-dir", StringComparison.OrdinalIgnoreCase) ||
                 t.Equals("--target-dir", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                updated.Add("--install-dir"); updated.Add(resolvedInstallDir); i++; replaced = true;
            }
            else updated.Add(args[i]);
        }
        if (!replaced) { updated.Add("--install-dir"); updated.Add(resolvedInstallDir); }
        return updated.ToArray();
    }

    public static void PersistInstallSettings(string installDir, string profileId, string? suiteDir)
    {
        string settingsPath = Path.Combine(Path.GetFullPath(installDir), "data", "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        JsonObject root = File.Exists(settingsPath)
            ? (JsonNode.Parse(File.ReadAllText(settingsPath, Encoding.UTF8)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        root["schemaVersion"] = 2; root["appId"] = CartEngineConstants.AppId; root["engineId"] = CartEngineConstants.EngineId;
        root["installProfile"] = NormalizeProfileId(profileId);
        if (!string.IsNullOrWhiteSpace(suiteDir)) root["suiteInstallDir"] = Path.GetFullPath(suiteDir);
        root["updatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        string tmpPath = settingsPath + ".tmp";
        File.WriteAllText(tmpPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        if (File.Exists(settingsPath)) File.Copy(settingsPath, settingsPath + ".bak", overwrite: true);
        File.Move(tmpPath, settingsPath, overwrite: true);
    }

    private static bool TryReadInlineOrNext(string[] clean, ref int index, string trimmed, string flag, out string value)
    {
        if (trimmed.Equals(flag, StringComparison.OrdinalIgnoreCase))
        {
            value = index + 1 < clean.Length ? (clean[++index] ?? string.Empty) : throw new ArgumentException($"{flag} requires a value.");
            return true;
        }
        if (trimmed.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = trimmed[(flag.Length + 1)..];
            return true;
        }
        value = string.Empty;
        return false;
    }

#if RECART_ENABLE_AUTOMATION
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDisposable? TryAttachWizardAutomation(
        InstallerWizardForm wizard, int? automationPort, string? automationToken, InstallerRuntimeContext ctx) =>
        automationPort is { } port ? WizardAutomationHook.Attach(wizard, port, automationToken, ctx) : null;

    private sealed class WizardAutomationHook : IDisposable
    {
        private readonly AutomationLoopbackServer _server;
        private bool _disposed;
        private WizardAutomationHook(AutomationLoopbackServer server) => _server = server;

        public static WizardAutomationHook Attach(InstallerWizardForm wizard, int port, string? token, InstallerRuntimeContext ctx)
        {
            ArgumentNullException.ThrowIfNull(wizard); ArgumentNullException.ThrowIfNull(ctx);
            ApplyControlNames(wizard);
            string? envToken = Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN");
            string effectiveToken = !string.IsNullOrWhiteSpace(token) ? token.Trim()
                : (!string.IsNullOrWhiteSpace(envToken) ? envToken.Trim() : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());

            var opts = new AutomationServerOptions(port, effectiveToken, CartEngineConstants.AppId, AllowedSnapshotRoot: null);
            var server = new AutomationLoopbackServer(opts, wizard, null, ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive);
            var hook = new WizardAutomationHook(server);
            wizard.Load += (_, _) =>
            {
                if (hook._disposed || server.IsListening) return;
                server.Start();
                ctx.WriteLog("AUTOMATION", $"Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
                Console.WriteLine($"AUTOMATION: Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
            };
            wizard.FormClosed += (_, _) => hook.Dispose();
            return hook;
        }

        private static void ApplyControlNames(InstallerWizardForm w)
        {
            w.Name = "InstallerWizardRoot"; w.PrimaryButton.Name = "Wizard_PrimaryButton"; w.CancelButton.Name = "Wizard_CancelButton";
            w.BrowseButton.Name = "Wizard_BrowseButton"; w.DetailsToggleButton.Name = "Wizard_DetailsToggle";
            w.InstallDirTextBox.Name = "Wizard_InstallDir"; w.SecondaryDirTextBox.Name = "Wizard_SecondaryDir";
            w.LightweightProfileRadioButton.Name = "Wizard_ProfileLightweight"; w.FullProfileRadioButton.Name = "Wizard_ProfileFull";
            w.ShortcutsCheckBox.Name = "Wizard_Shortcuts"; w.LaunchAfterCheckBox.Name = "Wizard_LaunchAfter";
            w.MigrateLegacyCheckBox.Name = "Wizard_MigrateLegacy"; w.StatusLabel.Name = "Wizard_StatusLabel"; w.LogTextBox.Name = "Wizard_LogTextBox";
        }

        public void Dispose() { if (!_disposed) { _disposed = true; _server.Dispose(); } }
    }
#else
    public static IDisposable? TryAttachWizardAutomation(
        InstallerWizardForm wizard, int? automationPort, string? automationToken, InstallerRuntimeContext ctx) => null;
#endif
}
