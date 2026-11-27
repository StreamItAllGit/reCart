// @module: reCart.Player.PlayerCliOptions
// @purpose: Parses and validates reCart.Player CLI flags, Foundation AppEnvironment paths, and host session option overrides.
// @depends-on: System, System.IO, Foundation.Core, reCart.Contracts, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: PlayerCliOptions

using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal sealed record PlayerCliOptions
{
    public AppEnvironment Environment { get; init; } = null!;
    public string? OpenPath { get; init; }
    public string? RomPath { get; init; }
    public bool DemoCart { get; init; }
    public string BackendId { get; init; } = CartRenderBackendIds.OpenGl;
    public bool HasExplicitBackend { get; init; }
    public HostRenderBackendKind PreferredBackend { get; init; } = HostRenderBackendKind.OpenGl;
    public HostAspectRatioMode AspectRatioMode { get; init; } = HostAspectRatioMode.NativePar;
    public bool HasExplicitAspectMode { get; init; }
    public int ResolutionScale { get; init; } = 2;
    public bool HasExplicitScale { get; init; }
    public bool Fullscreen { get; init; }
    public string? AudioDriverOverride { get; init; }
    public CartExecutionTierMode ExecutionTier { get; init; } = CartExecutionTierMode.StaticAotOnly;
    public bool HasExplicitExecutionTier { get; init; }
    public CartVideoRegionStandard Region { get; init; } = CartVideoRegionStandard.Auto;
    public bool HasExplicitRegion { get; init; }
    public HostOverscanCrop OverscanCrop { get; init; }
    public string SaveDirectory { get; init; } = string.Empty;
    public int? Frames { get; init; }
    public bool LockstepOracle { get; init; }
    public int? LockstepSteps { get; init; }
    public string? DumpScreenshotPath { get; init; }
    public string? DumpAudioPath { get; init; }
    public bool PreloadMedia { get; init; }
    public string? PreloadMediaPath { get; init; }
    public bool RequireBackend { get; init; }
    public int? AutomationPort { get; init; }
    public string? AutomationToken { get; init; }
    public bool WaitForExit { get; init; }
    public bool Verify { get; init; }
    public bool Doctor { get; init; }
    public bool ShowVersion { get; init; }
    public bool ShowHelp { get; init; }
    public bool ForceWindow { get; init; }
    public bool Headless { get; init; }
    public bool TrayOnly { get; init; }
    public bool EnableHdPacks { get; init; }
    public HostFrameRateMode FrameRateMode { get; init; } = HostFrameRateMode.Enhanced;
    public int? TargetFps { get; init; }
    public bool? IsVsyncEnabled { get; init; }
    public bool IsFastForwardEnabled { get; init; }
    public bool OpenSettingsInitially { get; init; }
    public bool ShowFpsInitially { get; init; }
    public bool ShouldEnableRuntimeLogs { get; init; }
    public bool ForceZeroRwx { get; init; }
    public string? Profile { get; init; }

    public static PlayerCliOptions Parse(string[]? args)
    {
        string[] cli = args ?? Array.Empty<string>();
        var appEnv = new AppEnvironment(CartEngineConstants.AppId, cli, defaultConfigFileName: "settings.json");

        string? openPath = PlayerCliOptionHelpers.GetOption(cli, "--open") ?? PlayerCliOptionHelpers.GetOption(cli, "--module");
        string? romPath = PlayerCliOptionHelpers.GetOption(cli, "--rom") ?? PlayerCliOptionHelpers.GetOption(cli, "--disc");
        (bool preloadFlag, string? preloadPath) = PlayerCliOptionHelpers.ReadPreloadMedia(cli);
        if (string.IsNullOrWhiteSpace(openPath)) openPath = PlayerCliOptionHelpers.GetFirstPositional(cli);

        string? rawBackend = PlayerCliOptionHelpers.GetOption(cli, "--backend");
        string backendId = rawBackend is null ? CartRenderBackendIds.OpenGl : PlayerCliOptionHelpers.ParseBackendStrict(rawBackend);
        bool hasExplicitBackend = rawBackend is not null;
        bool requireBackend = PlayerCliOptionHelpers.ParseRequireBackend(cli, ref backendId, ref hasExplicitBackend);

        string? rawAspect = PlayerCliOptionHelpers.GetOption(cli, "--aspect-mode");
        HostAspectRatioMode aspectMode = rawAspect is null ? HostAspectRatioMode.NativePar : PlayerCliOptionHelpers.ParseAspectStrict(rawAspect);

        string? rawScale = PlayerCliOptionHelpers.GetOption(cli, "--scale");
        int scale = rawScale is null ? 2 : PlayerCliOptionHelpers.ParseScaleStrict(rawScale);

        string? rawFpsMode = PlayerCliOptionHelpers.GetOption(cli, "--fps-mode");
        HostFrameRateMode fpsMode = rawFpsMode is null ? HostFrameRateMode.Enhanced : PlayerCliOptionHelpers.ParseFrameRateModeStrict(rawFpsMode);

        bool forceZeroRwx = PlayerCliOptionHelpers.HasFlag(cli, "--force-zero-rwx");
        string? rawTier = PlayerCliOptionHelpers.GetOption(cli, "--execution-tier");
        bool hasJitBringup = !forceZeroRwx && PlayerCliOptionHelpers.HasFlag(cli, "--jit-bringup");
        bool hasOracle = PlayerCliOptionHelpers.HasFlag(cli, "--lockstep-oracle");
        CartExecutionTierMode tier = rawTier is not null
            ? PlayerCliOptionHelpers.ParseExecutionTierStrict(rawTier)
            : (hasOracle ? CartExecutionTierMode.InterpreterOracle : hasJitBringup ? CartExecutionTierMode.JitBringup : CartExecutionTierMode.StaticAotOnly);
        if (forceZeroRwx && tier is CartExecutionTierMode.JitBringup or CartExecutionTierMode.AotWithJitFallback)
            tier = CartExecutionTierMode.AotWithThreadedIr;

        string? rawRegion = PlayerCliOptionHelpers.GetOption(cli, "--region");
        CartVideoRegionStandard region = rawRegion is null ? CartVideoRegionStandard.Auto : PlayerCliOptionHelpers.ParseRegionStrict(rawRegion);

        string? rawOverscan = PlayerCliOptionHelpers.GetOption(cli, "--overscan");
        HostOverscanCrop overscan = rawOverscan is null ? HostOverscanCrop.None : PlayerCliOptionHelpers.ParseOverscanStrict(rawOverscan);

        string? rawSaveDir = PlayerCliOptionHelpers.GetOption(cli, "--save-dir");
        string saveDir = Path.GetFullPath(string.IsNullOrWhiteSpace(rawSaveDir) ? Path.Combine(appEnv.DataDirectory, "saves") : rawSaveDir);
        string? dumpShot = PlayerCliOptionHelpers.ResolveOptionalPath(PlayerCliOptionHelpers.GetOption(cli, "--dump-screenshot"), appEnv.Sandbox);
        string? dumpAudio = PlayerCliOptionHelpers.ResolveOptionalPath(PlayerCliOptionHelpers.GetOption(cli, "--dump-audio"), appEnv.Sandbox);
        openPath = PlayerCliOptionHelpers.ResolveOptionalPath(openPath, appEnv.Sandbox);
        romPath = PlayerCliOptionHelpers.ResolveOptionalPath(romPath, appEnv.Sandbox);
        preloadPath = PlayerCliOptionHelpers.ResolveOptionalPath(preloadPath, appEnv.Sandbox);
        if (appEnv.Sandbox.IsSandboxActive)
        {
            _ = PlayerCliOptionHelpers.ResolveOptionalPath(PlayerCliOptionHelpers.GetOption(cli, "--data-dir"), appEnv.Sandbox);
            _ = PlayerCliOptionHelpers.ResolveOptionalPath(PlayerCliOptionHelpers.GetOption(cli, "--config"), appEnv.Sandbox);
            _ = PlayerCliOptionHelpers.ResolveOptionalPath(PlayerCliOptionHelpers.GetOption(cli, "--log-dir"), appEnv.Sandbox);
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(appEnv.DataDirectory, appEnv.Sandbox);
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(appEnv.LogDirectory, appEnv.Sandbox);
            if (!string.IsNullOrWhiteSpace(rawSaveDir))
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(saveDir, appEnv.Sandbox);
        }

        int? frames = PlayerCliOptionHelpers.ParseOptionalInt(PlayerCliOptionHelpers.GetOption(cli, "--frames"), "--frames", 1, int.MaxValue);
        int? lockstepSteps = PlayerCliOptionHelpers.ParseOptionalInt(PlayerCliOptionHelpers.GetOption(cli, "--lockstep-steps"), "--lockstep-steps", 1, 10_000_000);
        int? automationPort = PlayerCliOptionHelpers.ParseOptionalInt(PlayerCliOptionHelpers.GetOption(cli, "--automation-port"), "--automation-port", 0, 65535);
        int? targetFps = PlayerCliOptionHelpers.ParseOptionalInt(PlayerCliOptionHelpers.GetOption(cli, "--target-fps"), "--target-fps", 0, int.MaxValue);

        return new PlayerCliOptions
        {
            Environment = appEnv,
            OpenPath = openPath,
            RomPath = romPath ?? preloadPath,
            DemoCart = PlayerCliOptionHelpers.HasFlag(cli, "--demo-cart"),
            BackendId = backendId,
            HasExplicitBackend = hasExplicitBackend,
            PreferredBackend = ToHostBackendKind(backendId),
            AspectRatioMode = aspectMode,
            HasExplicitAspectMode = rawAspect is not null,
            ResolutionScale = scale,
            HasExplicitScale = rawScale is not null,
            Fullscreen = PlayerCliOptionHelpers.HasFlag(cli, "--fullscreen"),
            AudioDriverOverride = PlayerCliOptionHelpers.GetOption(cli, "--audio-driver"),
            ExecutionTier = tier,
            HasExplicitExecutionTier = rawTier is not null || hasJitBringup || hasOracle || forceZeroRwx,
            Region = region,
            HasExplicitRegion = rawRegion is not null,
            OverscanCrop = overscan,
            SaveDirectory = saveDir,
            Frames = frames,
            LockstepOracle = hasOracle || lockstepSteps.HasValue,
            LockstepSteps = lockstepSteps,
            DumpScreenshotPath = dumpShot,
            DumpAudioPath = dumpAudio,
            PreloadMedia = preloadFlag,
            PreloadMediaPath = preloadPath,
            RequireBackend = requireBackend,
            AutomationPort = automationPort,
            AutomationToken = PlayerCliOptionHelpers.GetOption(cli, "--automation-token"),
            WaitForExit = PlayerCliOptionHelpers.HasFlag(cli, "--wait"),
            Verify = PlayerCliOptionHelpers.HasFlag(cli, "--verify"),
            Doctor = PlayerCliOptionHelpers.HasFlag(cli, "--doctor"),
            ShowVersion = PlayerCliOptionHelpers.HasFlag(cli, "--version"),
            ShowHelp = PlayerCliOptionHelpers.HasFlag(cli, "--help") || PlayerCliOptionHelpers.HasFlag(cli, "-h") || PlayerCliOptionHelpers.HasFlag(cli, "/?"),
            ForceWindow = PlayerCliOptionHelpers.HasFlag(cli, "--window"),
            Headless = PlayerCliOptionHelpers.HasFlag(cli, "--headless"),
            TrayOnly = appEnv.TrayOnly || appEnv.PresentationMode == UiPresentationMode.TrayOrBackground || PlayerCliOptionHelpers.HasFlag(cli, "--tray-only"),
            EnableHdPacks = PlayerCliOptionHelpers.HasFlag(cli, "--hd-packs"),
            FrameRateMode = fpsMode,
            TargetFps = targetFps,
            IsVsyncEnabled = PlayerCliOptionHelpers.ParseSwitch(cli, "--vsync", "--no-vsync"),
            IsFastForwardEnabled = PlayerCliOptionHelpers.ParseSwitch(cli, "--fast-forward", "--no-fast-forward") == true,
            OpenSettingsInitially = PlayerCliOptionHelpers.HasFlag(cli, "--open-settings"),
            ShowFpsInitially = PlayerCliOptionHelpers.HasFlag(cli, "--show-fps"),
            ShouldEnableRuntimeLogs = PlayerCliOptionHelpers.HasFlag(cli, "--enable-logs"),
            ForceZeroRwx = forceZeroRwx,
            Profile = PlayerCliOptionHelpers.GetOption(cli, "--profile")
        };
    }

    public static bool HasAutomationPortFlag(string[]? args)
    {
        if (args is null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--automation-port", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static HostRenderBackendKind ToHostBackendKind(string? backendId) => backendId?.Trim() switch
    {
        CartRenderBackendIds.OpenGl => HostRenderBackendKind.OpenGl,
        CartRenderBackendIds.Vulkan => HostRenderBackendKind.Vulkan,
        CartRenderBackendIds.DirectX => HostRenderBackendKind.DirectX,
        CartRenderBackendIds.ParallelCpu => HostRenderBackendKind.ParallelCpu,
        HostRenderBackendIds.Metal => HostRenderBackendKind.Metal,
        _ => HostRenderBackendKind.Software
    };

    public static string FormatHelpText() => PlayerCliOptionHelpers.FormatHelpText();
}
