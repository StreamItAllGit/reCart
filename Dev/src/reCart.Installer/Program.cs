// @module: reCart.Installer.Program
// @purpose: 4-pillar zero-PII single-EXE installer host for reCart with AssemblyResolve, suite placement, .ready + engine.manifest.json emission, and Release ExitCode = 2 gate.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.CompilerServices, System.Windows.Forms, Foundation.Core, Foundation.Installer, reCart.Contracts
// @exports: Program

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using reCart.Contracts;

namespace reCart.Installer;

internal static class Program
{
    private static readonly string[] EmbeddedAssemblies =
#if RECART_ENABLE_AUTOMATION
        ["Foundation.Core", "Foundation.Installer", "Foundation.Retro", "reEngine.Core", "reCart.Contracts", "Foundation.Automation", "Foundation.UI"];
#else
        ["Foundation.Core", "Foundation.Installer", "Foundation.Retro", "reEngine.Core", "reCart.Contracts"];
#endif
    private static readonly ConcurrentDictionary<string, Assembly> ResolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            string? name = new AssemblyName(e.Name).Name;
            if (string.IsNullOrWhiteSpace(name) || !EmbeddedAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
            if (ResolvedAssemblies.TryGetValue(name, out Assembly? cached)) return cached;

            using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(name + ".dll.gz");
            if (gzStream is null) return null;
            using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            decompressor.CopyTo(ms);
            return ResolvedAssemblies.GetOrAdd(name, Assembly.Load(ms.ToArray()));
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RunMain(string[] args)
    {
#if !RECART_ENABLE_AUTOMATION
        if (InstallerCliAndAutomation.HasAutomationPortFlag(args))
        {
            InstallerCliAndAutomation.AttachParentConsoleIfInteractive(args);
            Console.Error.WriteLine("Automation loopback (--automation-port) is disabled in Release installer builds (ExitCode = 2).");
            return 2;
        }
#endif

        try
        {
            InstallerThemeOptions theme = CreateThemeOptions();
            InstallerAppDescriptor descriptor = CreateDescriptor(theme);
            string[] cleanArgs = InstallerCliAndAutomation.SanitizeArgs(args);

            int? automationPort;
            string? automationToken, suiteDir, reEngineDir, installDir, dataDir;
            string profileId;
            bool silent, dryRun, verifyOnly, uninstall;
            string[] hostArgs;
            try
            {
                string[] withoutAuto = InstallerCliAndAutomation.ExtractAutomationArgs(cleanArgs, out automationPort, out automationToken);
                hostArgs = InstallerCliAndAutomation.ExtractHostArgs(
                    withoutAuto, out profileId, out suiteDir, out reEngineDir,
                    out silent, out dryRun, out verifyOnly, out uninstall, out installDir, out dataDir);
            }
            catch (ArgumentException ex) { Console.Error.WriteLine($"Setup argument error: {ex.Message}"); return 2; }

            InstallerCliOptions cli;
            try { cli = InstallerEngine.ParseCliArgs(hostArgs, descriptor); }
            catch (ArgumentException) { return StandardInstallerHost.Run(descriptor, hostArgs, typeof(Program).Assembly); }

            var runtimeCtx = new InstallerRuntimeContext(cli.DataDir, cli.ConfigPath, cli.LogDir, cli.DryRun, descriptor.EnvPrefix);
            bool forceAutomatedWizard =
#if RECART_ENABLE_AUTOMATION
                automationPort.HasValue;
#else
                false;
#endif
            bool isInteractiveGui = !cli.ShowVersion && !cli.ShowHelp && !cli.Silent && !cli.DryRun && !cli.VerifyOnly &&
                                    ((!InstallerCliAndAutomation.IsIsolatedTestEnv() && string.IsNullOrWhiteSpace(cli.InstallDir)) || forceAutomatedWizard) &&
                                    OperatingSystem.IsWindows();
            if (isInteractiveGui)
                return RunInteractiveWizard(descriptor, runtimeCtx, cli, profileId, suiteDir, reEngineDir, automationPort, automationToken);

            string[] effectiveArgs = hostArgs;
            SuitePlacementResolution? placement = null;
            if (!cli.ShowVersion && !cli.ShowHelp && !verifyOnly)
            {
                placement = ReCartSuitePlacementHelper.ResolvePlacement(runtimeCtx, installDir, suiteDir, reEngineDir);
                effectiveArgs = InstallerCliAndAutomation.ReplaceInstallDirArg(hostArgs, placement.InstallDir);
            }

            int exitCode = StandardInstallerHost.Run(descriptor, effectiveArgs, typeof(Program).Assembly);
            if (exitCode == 0 && placement is not null && !cli.ShowVersion && !cli.ShowHelp && !verifyOnly && !dryRun)
            {
                if (uninstall) ReCartSuitePlacementHelper.RemoveEngineMetadataOnUninstall(placement.InstallDir);
                else FinalizeInstall(placement.InstallDir, placement.SuiteRootDir, profileId);
            }
            return exitCode;
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
    }

    private static int RunInteractiveWizard(
        InstallerAppDescriptor descriptor, InstallerRuntimeContext ctx, InstallerCliOptions cli,
        string profileId, string? suiteDir, string? reEngineDir, int? automationPort, string? automationToken)
    {
        var guiEngine = new InstallerEngine(descriptor, ctx, typeof(Program).Assembly);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        SuitePlacementResolution initial = ReCartSuitePlacementHelper.ResolvePlacement(ctx, cli.InstallDir, suiteDir, reEngineDir);
        using var wizard = new InstallerWizardForm(
            engine: guiEngine, ctx: ctx, initialInstallDir: initial.InstallDir, externalPayloadPath: cli.PayloadPath,
            isUninstallMode: cli.Uninstall, initialCreateShortcuts: !cli.NoShortcuts, initialAutoStart: false,
            initialLaunchAfter: !cli.NoLaunch, initialPurgeData: cli.PurgeData, initialProfileId: profileId,
            initialSecondaryDir: initial.SuiteRootDir, secondaryFlagName: "--suite-dir",
            initialMigrateLegacy: false, showLegacyMigration: false, payloadAssembly: typeof(Program).Assembly);

        ReCartSuitePlacementHelper.WireWizardAutoSortHandlers(wizard);
        using IDisposable? automationHook = InstallerCliAndAutomation.TryAttachWizardAutomation(wizard, automationPort, automationToken, ctx);
        Application.Run(wizard);

        if (wizard.ExitCode == 0)
        {
            string chosenInstall = string.IsNullOrWhiteSpace(wizard.InstallDirTextBox.Text)
                ? initial.InstallDir : ReCartSuitePlacementHelper.NormalizeWizardInstallDir(wizard.InstallDirTextBox.Text);
            if (cli.Uninstall) ReCartSuitePlacementHelper.RemoveEngineMetadataOnUninstall(chosenInstall);
            else
            {
                string chosenSuite = ReCartSuitePlacementHelper.ResolveSuiteRootFromCandidate(
                    !string.IsNullOrWhiteSpace(wizard.SecondaryInstallDir) ? wizard.SecondaryInstallDir : chosenInstall);
                FinalizeInstall(chosenInstall, chosenSuite, InstallerCliAndAutomation.NormalizeProfileId(wizard.SelectedProfileId));
            }
        }
        return wizard.ExitCode;
    }

    private static void FinalizeInstall(string installDir, string suiteRootDir, string profileId)
    {
        if (!Directory.Exists(installDir)) return;
        Directory.CreateDirectory(Path.Combine(suiteRootDir, "engines"));
        ReCartSuitePlacementHelper.WriteEngineMetadataAndReadyMarker(installDir);
        InstallerCliAndAutomation.PersistInstallSettings(installDir, profileId, suiteRootDir);
    }

    internal static InstallerAppDescriptor CreateDescriptor() => CreateDescriptor(CreateThemeOptions());

    private static InstallerAppDescriptor CreateDescriptor(InstallerThemeOptions theme)
    {
        var baseDesc = new InstallerAppDescriptor(
            ProductName: CartEngineConstants.DisplayName, DisplayName: theme.DisplayName, Publisher: CartEngineConstants.StudioName,
            Version: theme.Version, MainExecutableRelativePath: "reCart.exe", Subtitle: theme.Subtitle,
            EmbeddedPayloadResourceName: "reCart.Installer.Embedded.payload.zip", BrandAccentHex: theme.BrandAccentHex,
            AllowAutoStart: false, StudioName: CartEngineConstants.StudioName, SuiteName: CartEngineConstants.SuiteName,
            UmbrellaSubpath: CartEngineConstants.UmbrellaSubpath, DefaultProfile: theme.DefaultProfileId, AllowLegacyMigration: false);

        InstallerMarkupManifest? manifest = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(AppContext.BaseDirectory, typeof(Program).Assembly);
        return manifest is not null
            ? InstallerManifestStore.ApplyToDescriptor(baseDesc, manifest) with
              { AllowAutoStart = false, AllowLegacyMigration = false, SuiteName = CartEngineConstants.SuiteName, EmbeddedPayloadResourceName = "reCart.Installer.Embedded.payload.zip" }
            : baseDesc;
    }

    internal static InstallerThemeOptions CreateThemeOptions()
    {
        string defaultSuite = ReCartSuitePlacementHelper.ResolveDefaultSuiteInstallDir();
        string defaultInstall = ReCartSuitePlacementHelper.BuildSideBySideInstallDir(defaultSuite);
        InstallerMarkupManifest? manifest = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(AppContext.BaseDirectory, typeof(Program).Assembly);
        IReadOnlyList<InstallerProfileOption> profiles =
        [
            new InstallerProfileOption("lightweight", InstallerProfileKind.Lightweight, "Lightweight",
                "Core reCart AOT engine, 6502 lifter, .decomp provider & SDL3 player.", IsDefault: true),
            new InstallerProfileOption("full", InstallerProfileKind.Full, "Full (Suite Integration)",
                "Registers full side-by-side suite configuration with reEngine.", IsDefault: false)
        ];
        var picker = new SecondaryPathPickerOptions("--suite-dir", "reEngine Suite Root:", defaultSuite,
            "%LOCALAPPDATA%\\Programs\\StreamItAll\\reEngine", "Parent StreamItAll\\reEngine suite directory containing side-by-side engines.");

        if (manifest is not null)
        {
            return InstallerManifestStore.ToThemeOptions(manifest, defaultInstall) with
            {
                AllowAutoStart = false, BootstrapSharedSuiteDaemon = false, AllowLegacyMigration = false,
                SuiteName = CartEngineConstants.SuiteName, ExecutableFileName = "reCart.exe",
                AvailableProfiles = profiles, SecondaryPathPicker = picker
            };
        }

        return new InstallerThemeOptions
        {
            AppId = CartEngineConstants.AppId, DisplayName = CartEngineConstants.DisplayName, Version = CartEngineConstants.Version,
            Subtitle = "AOT-Primary 6502 Static Recompilation Engine for reEngine", BrandAccentHex = CartEngineConstants.BrandAccentHex,
            DefaultInstallDir = defaultInstall, ExecutableFileName = "reCart.exe", AllowAutoStart = false,
            BootstrapSharedSuiteDaemon = false, AllowLegacyMigration = false, StudioName = CartEngineConstants.StudioName,
            SuiteName = CartEngineConstants.SuiteName, UmbrellaSubpath = CartEngineConstants.UmbrellaSubpath,
            AvailableProfiles = profiles, DefaultProfileId = CartEngineConstants.DefaultInstallProfile, SecondaryPathPicker = picker
        };
    }
}
