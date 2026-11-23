// @module: reCart.Tests.InstallerManifestAndSideBySideTests
// @purpose: Verifies reCart.Installer side-by-side suite placement, peer reEngine protection, atomic .ready/engine.manifest.json v2, AllowAutoStart=false, settings .bak, and Release ExitCode=2 gate.
// @depends-on: System.Text.Json, Xunit, Foundation.Core, Foundation.Installer, reCart.Contracts, reCart.Installer
// @exports: InstallerManifestAndSideBySideTests

using System.Text.Json;
using Foundation.Core;
using Foundation.Installer;
using reCart.Contracts;
using reCart.Installer;
using Xunit;

namespace reCart.Tests;

[Collection("IsolatedProcessEnv")]
public sealed class InstallerManifestAndSideBySideTests
{
    [Fact]
    public void PlacementHelper_DetectsSuiteDirectoriesAndProtectsPeersFromOverwrite()
    {
        string temp = TestPaths.CreateTempDir("recart_inst_place");
        try
        {
            string suiteRoot = Path.Combine(temp, "StreamItAll", "reEngine");
            Directory.CreateDirectory(Path.Combine(suiteRoot, "engines", "reSlate", "v0.1.0"));
            File.WriteAllText(Path.Combine(suiteRoot, "reEngine.exe"), "stub");
            File.WriteAllText(Path.Combine(suiteRoot, "engines", "reSlate", "v0.1.0", "install-manifest.json"), "{\"engineId\":\"reSlate\"}");

            Assert.True(ReCartSuitePlacementHelper.IsReEngineDirectory(suiteRoot));
            Assert.False(ReCartSuitePlacementHelper.HasDirectInstallation(suiteRoot));
            Assert.True(ReCartSuitePlacementHelper.HasOtherInstallManifest(Path.Combine(suiteRoot, "engines", "reSlate", "v0.1.0")));

            string normalizedFromRoot = ReCartSuitePlacementHelper.NormalizeWizardInstallDir(suiteRoot);
            Assert.Equal(Path.Combine(suiteRoot, "engines", "reCart", $"v{CartEngineConstants.Version}"), normalizedFromRoot);

            string normalizedFromSlate = ReCartSuitePlacementHelper.NormalizeWizardInstallDir(Path.Combine(suiteRoot, "engines", "reSlate", "v0.1.0"));
            Assert.Equal(normalizedFromRoot, normalizedFromSlate);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void PlacementHelper_WritesAtomicSchema2ManifestAndReadyAndRemovesCleanly()
    {
        string temp = TestPaths.CreateTempDir("recart_inst_meta");
        try
        {
            string engineDir = Path.Combine(temp, "reEngine", "engines", "reCart", $"v{CartEngineConstants.Version}");
            ReCartSuitePlacementHelper.WriteEngineMetadataAndReadyMarker(engineDir);

            string mfPath = Path.Combine(engineDir, "engine.manifest.json");
            string readyPath = Path.Combine(engineDir, ".ready");
            Assert.True(File.Exists(mfPath));
            Assert.True(File.Exists(readyPath));

            using JsonDocument mf = JsonDocument.Parse(File.ReadAllText(mfPath));
            Assert.Equal(2, mf.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("reCart", mf.RootElement.GetProperty("engineId").GetString());
            Assert.Equal("0.6.2", mf.RootElement.GetProperty("hostProtocolVersion").GetString());
            string[] aliases = mf.RootElement.GetProperty("legacyEngineIds").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.Contains("reCartOne", aliases);
            Assert.Contains("reNes", aliases);

            ReCartSuitePlacementHelper.RemoveEngineMetadataOnUninstall(engineDir);
            Assert.False(File.Exists(mfPath));
            Assert.False(File.Exists(readyPath));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void InstallerCliAndAutomation_StripsAutostartNormalizesProfilesAndCreatesSettingsBak()
    {
        string temp = TestPaths.CreateTempDir("recart_inst_cli");
        try
        {
            string[] clean = InstallerCliAndAutomation.SanitizeArgs(["--autostart", "--auto-start=true", "--profile", "FULL", "--silent"]);
            Assert.Equal(["--profile", "FULL", "--silent"], clean);

            string[] forwarded = InstallerCliAndAutomation.ExtractHostArgs(
                clean, out string profile, out _, out _, out bool silent, out _, out _, out _, out _, out _);
            Assert.Equal("full", profile);
            Assert.True(silent);
            Assert.Throws<ArgumentException>(() => InstallerCliAndAutomation.NormalizeProfileId("invalid"));

            InstallerCliAndAutomation.PersistInstallSettings(temp, "lightweight", Path.Combine(temp, "Suite"));
            InstallerCliAndAutomation.PersistInstallSettings(temp, "full", Path.Combine(temp, "Suite"));
            string settingsPath = Path.Combine(temp, "data", "config", "settings.json");
            Assert.True(File.Exists(settingsPath));
            Assert.True(File.Exists(settingsPath + ".bak"));
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            Assert.Equal("full", doc.RootElement.GetProperty("installProfile").GetString());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void InstallerProgram_EnforcesAutomationGateDryRunAndIsolationContainment()
    {
        string temp = TestPaths.CreateTempDir("recart_inst_prog");
        try
        {
            string dummyZip = Path.Combine(temp, "payload.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(dummyZip, System.IO.Compression.ZipArchiveMode.Create))
                zip.CreateEntry("reCart.exe");
#if !RECART_ENABLE_AUTOMATION
            Assert.Equal(2, Program.RunMain(["--automation-port", "5201", "--data-dir", temp]));
#endif
            Assert.Equal(0, Program.RunMain(["--dry-run", "--silent", "--payload", dummyZip, "--data-dir", temp, "--suite-dir", Path.Combine(temp, "reEngine")]));
            Assert.Equal(3, Program.RunMain(["--dry-run", "--silent", "--payload", dummyZip, "--data-dir", temp, "--install-dir", @"C:\OutsideIsolation\reCart"]));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void PlacementHelper_ResolvesSideBySideUnderSandboxWithIsolatedEnv()
    {
        string temp = TestPaths.CreateTempDir("recart_inst_iso");
        try
        {
            string suiteRoot = Path.Combine(temp, "reEngine");
            Directory.CreateDirectory(suiteRoot);
            var ctx = new InstallerRuntimeContext(temp, Path.Combine(temp, "settings.json"), temp, dryRun: true, envPrefix: "RECART");
            SuitePlacementResolution res = ReCartSuitePlacementHelper.ResolvePlacement(ctx, null, suiteRoot, null);
            Assert.Equal(suiteRoot, res.SuiteRootDir);
            Assert.Equal(Path.Combine(suiteRoot, "engines", "reCart", $"v{CartEngineConstants.Version}"), res.InstallDir);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
