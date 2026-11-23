// @module: reCart.Player.PlayerCliOptions
// @purpose: Parses and validates reCart.Player CLI flags, Foundation AppEnvironment paths, backend/aspect/overscan/region/tier options, and help text.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, Foundation.Core, reCart.Contracts, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: PlayerCliOptions

using System.Globalization;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal sealed record PlayerCliOptions
{
    private static readonly HashSet<string> ValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--backend", "--aspect-mode", "--scale",
        "--audio-driver", "--execution-tier", "--region", "--overscan",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--frames",
        "--lockstep-steps", "--dump-screenshot", "--dump-audio", "--automation-port", "--automation-token", "--port", "--ui"
    };

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
    public bool ShowVersion { get; init; }
    public bool ShowHelp { get; init; }
    public bool ForceWindow { get; init; }
    public bool Headless { get; init; }
    public bool EnableHdPacks { get; init; }

    public static PlayerCliOptions Parse(string[]? args)
    {
        string[] cli = args ?? Array.Empty<string>();
        var appEnv = new AppEnvironment(CartEngineConstants.AppId, cli, defaultConfigFileName: "settings.json");

        string? openPath = GetOption(cli, "--open") ?? GetOption(cli, "--module");
        string? romPath = GetOption(cli, "--rom");
        (bool preloadFlag, string? preloadPath) = ReadPreloadMedia(cli);
        if (string.IsNullOrWhiteSpace(openPath)) openPath = GetFirstPositional(cli);

        string? rawBackend = GetOption(cli, "--backend");
        string backendId = rawBackend is null ? CartRenderBackendIds.OpenGl : ParseBackendStrict(rawBackend);

        string? rawAspect = GetOption(cli, "--aspect-mode");
        HostAspectRatioMode aspectMode = rawAspect is null ? HostAspectRatioMode.NativePar : ParseAspectStrict(rawAspect);

        string? rawScale = GetOption(cli, "--scale");
        int scale = rawScale is null ? 2 : ParseScaleStrict(rawScale);

        string? rawTier = GetOption(cli, "--execution-tier");
        bool hasJitBringup = HasFlag(cli, "--jit-bringup");
        bool hasOracle = HasFlag(cli, "--lockstep-oracle");
        CartExecutionTierMode tier = rawTier is not null
            ? ParseExecutionTierStrict(rawTier)
            : (hasOracle ? CartExecutionTierMode.InterpreterOracle : hasJitBringup ? CartExecutionTierMode.JitBringup : CartExecutionTierMode.StaticAotOnly);

        string? rawRegion = GetOption(cli, "--region");
        CartVideoRegionStandard region = rawRegion is null ? CartVideoRegionStandard.Auto : ParseRegionStrict(rawRegion);

        string? rawOverscan = GetOption(cli, "--overscan");
        HostOverscanCrop overscan = rawOverscan is null ? HostOverscanCrop.None : ParseOverscanStrict(rawOverscan);

        string? rawSaveDir = GetOption(cli, "--save-dir");
        string saveDir = Path.GetFullPath(string.IsNullOrWhiteSpace(rawSaveDir) ? Path.Combine(appEnv.DataDirectory, "saves") : rawSaveDir);
        string? dumpShot = ResolveOptionalPath(GetOption(cli, "--dump-screenshot"), appEnv.Sandbox);
        string? dumpAudio = ResolveOptionalPath(GetOption(cli, "--dump-audio"), appEnv.Sandbox);
        openPath = ResolveOptionalPath(openPath, appEnv.Sandbox);
        romPath = ResolveOptionalPath(romPath, appEnv.Sandbox);
        preloadPath = ResolveOptionalPath(preloadPath, appEnv.Sandbox);
        if (appEnv.Sandbox.IsSandboxActive)
        {
            _ = ResolveOptionalPath(GetOption(cli, "--data-dir"), appEnv.Sandbox);
            _ = ResolveOptionalPath(GetOption(cli, "--config"), appEnv.Sandbox);
            _ = ResolveOptionalPath(GetOption(cli, "--log-dir"), appEnv.Sandbox);
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(appEnv.DataDirectory, appEnv.Sandbox);
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(appEnv.LogDirectory, appEnv.Sandbox);
            if (!string.IsNullOrWhiteSpace(rawSaveDir))
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(saveDir, appEnv.Sandbox);
        }

        int? frames = ParseOptionalInt(GetOption(cli, "--frames"), "--frames", 1, int.MaxValue);
        int? lockstepSteps = ParseOptionalInt(GetOption(cli, "--lockstep-steps"), "--lockstep-steps", 1, 10_000_000);
        int? automationPort = ParseOptionalInt(GetOption(cli, "--automation-port"), "--automation-port", 0, 65535);

        return new PlayerCliOptions
        {
            Environment = appEnv,
            OpenPath = openPath,
            RomPath = romPath ?? preloadPath,
            DemoCart = HasFlag(cli, "--demo-cart"),
            BackendId = backendId,
            HasExplicitBackend = rawBackend is not null,
            PreferredBackend = ToHostBackendKind(backendId),
            AspectRatioMode = aspectMode,
            HasExplicitAspectMode = rawAspect is not null,
            ResolutionScale = scale,
            HasExplicitScale = rawScale is not null,
            Fullscreen = HasFlag(cli, "--fullscreen"),
            AudioDriverOverride = GetOption(cli, "--audio-driver"),
            ExecutionTier = tier,
            HasExplicitExecutionTier = rawTier is not null || hasJitBringup || hasOracle,
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
            RequireBackend = HasFlag(cli, "--require-backend"),
            AutomationPort = automationPort,
            AutomationToken = GetOption(cli, "--automation-token"),
            WaitForExit = HasFlag(cli, "--wait"),
            Verify = HasFlag(cli, "--verify"),
            ShowVersion = HasFlag(cli, "--version"),
            ShowHelp = HasFlag(cli, "--help") || HasFlag(cli, "-h") || HasFlag(cli, "/?"),
            ForceWindow = HasFlag(cli, "--window"),
            Headless = HasFlag(cli, "--headless"),
            EnableHdPacks = HasFlag(cli, "--hd-packs")
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

    public static string FormatHelpText() => $$"""
        {{CartEngineConstants.DisplayName}} v{{CartEngineConstants.Version}} — Clean-Room AOT Cart Static Recompilation Player
        Usage: reCart.Player [options] [<module-or-rom-path>]
          --open <path>, --module <path>   Open .recomp package, module.recomp.json, or directory
          --rom <path>, --preload-media    Supply or discover local BYO .nes / .prg cartridge image
          --demo-cart                      Run built-in clean-room Ironframe tactical demo cart
          --backend <id>                   opengl | vulkan | directx | dx12 | parallelCpu | software
          --aspect-mode <mode>             nativePar | fourByThree | squareInteger | nativeFit | horPlus16By9
          --scale <1|2|4|8>                Integer resolution scale multiplier (default: 2)
          --region <auto|ntsc|pal|dendy>   Target clock/scanline region profile (default: auto)
          --execution-tier <tier>          staticAotOnly (default) | interpreterOracle | jitBringup
          --lockstep-oracle                Run quarantined Tier 4 oracle / differential lockstep
          --lockstep-steps <n>             Run N-step differential AOT vs InterpreterOracle check
          --overscan <t,b,l,r>             Source edge overscan pixel crop (e.g., 8,8,8,8)
          --fullscreen, --window           Start in borderless fullscreen or desktop window
          --audio-driver <name>            Override native audio driver (default: wasapi on Windows)
          --data-dir, --config, --log-dir  Override storage directories (Standard 02 containment)
          --save-dir <dir>                 Override QuickSave / SRAM directory
          --frames <n>                     Run fixed headless verification frame count and exit
          --dump-screenshot <png>          Write final composite surface PNG
          --dump-audio <wav>               Write deterministic 16-bit stereo PCM WAV capture
          --require-backend, --wait        Fail closed (Exit 6) if GPU unavailable; wait synchronously
          --verify, --version, --help      Verify package/host readiness, print version, or show help
        Hotkeys: F1 Settings Drawer | F3 HUD | F5 QuickSave | F9 QuickLoad | F11/Alt+Enter Fullscreen
        """;

    private static string ParseBackendStrict(string raw)
    {
        if (HostRenderBackendIds.TryNormalize(raw, out string canonical)) return canonical;
        string normalized = CartRenderBackendIds.NormalizeCli(raw);
        if (HostRenderBackendIds.TryNormalize(normalized, out canonical)) return canonical;
        throw new ArgumentException($"Unsupported --backend '{raw}'. Expected opengl, vulkan, directx, dx12, parallelCpu, or software.");
    }

    private static HostAspectRatioMode ParseAspectStrict(string raw) =>
        HostAspectRatioModeCodec.TryParseStrict(raw, out HostAspectRatioMode mode)
            ? mode
            : throw new ArgumentException($"Unsupported --aspect-mode '{raw}'. Expected nativePar, fourByThree, squareInteger, nativeFit, or horPlus16By9.");

    private static int ParseScaleStrict(string raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s is 1 or 2 or 4 or 8
            ? s
            : throw new ArgumentException($"Invalid --scale '{raw}'. Expected 1, 2, 4, or 8.");

    private static CartExecutionTierMode ParseExecutionTierStrict(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "staticaotonly" or "aot" or "aotprimary" or "static" => CartExecutionTierMode.StaticAotOnly,
        "aotwithjitfallback" => CartExecutionTierMode.AotWithJitFallback,
        "aotwiththreadedir" => CartExecutionTierMode.AotWithThreadedIr,
        "jitbringup" => CartExecutionTierMode.JitBringup,
        "interpreteroracle" or "oracle" => CartExecutionTierMode.InterpreterOracle,
        _ => throw new ArgumentException($"Unsupported --execution-tier '{raw}'.")
    };

    private static CartVideoRegionStandard ParseRegionStrict(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "auto" => CartVideoRegionStandard.Auto,
        "ntsc" => CartVideoRegionStandard.Ntsc,
        "pal" => CartVideoRegionStandard.Pal,
        "dendy" => CartVideoRegionStandard.Dendy,
        _ => throw new ArgumentException($"Unsupported --region '{raw}'. Expected auto, ntsc, pal, or dendy.")
    };

    private static HostOverscanCrop ParseOverscanStrict(string raw)
    {
        int[] vals = raw.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1)
            .ToArray();
        if (vals.Length == 1 && vals[0] >= 0) return new HostOverscanCrop(vals[0], vals[0], vals[0], vals[0]);
        if (vals.Length == 4 && vals.All(v => v >= 0)) return new HostOverscanCrop(vals[0], vals[1], vals[2], vals[3]);
        throw new ArgumentException($"Invalid --overscan '{raw}'. Expected <t,b,l,r> non-negative integers.");
    }

    private static bool IsPreloadPathToken(string[] args, int flagIndex) =>
        flagIndex + 1 < args.Length && !args[flagIndex + 1].StartsWith("-", StringComparison.Ordinal) &&
        !args[flagIndex + 1].EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) &&
        !args[flagIndex + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static (bool Flag, string? Path) ReadPreloadMedia(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--preload-media=", StringComparison.OrdinalIgnoreCase))
            {
                string val = a["--preload-media=".Length..].Trim().Trim('"');
                return (true, val.Length > 0 ? val : null);
            }
            if (string.Equals(a, "--preload-media", StringComparison.OrdinalIgnoreCase))
                return (true, IsPreloadPathToken(args, i) ? args[i + 1].Trim().Trim('"') : null);
        }
        return (false, null);
    }

    private static string? GetFirstPositional(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                bool skipNext = string.Equals(a, "--preload-media", StringComparison.OrdinalIgnoreCase)
                    ? IsPreloadPathToken(args, i)
                    : !a.Contains('=', StringComparison.Ordinal) && ValueFlags.Contains(a);
                if (skipNext && i + 1 < args.Length) i++;
                continue;
            }
            if (!a.StartsWith("-", StringComparison.Ordinal) && !string.Equals(a, "/?", StringComparison.Ordinal)) return a.Trim().Trim('"');
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string flag)
    {
        string eq = flag + "=";
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith(eq, StringComparison.OrdinalIgnoreCase))
            {
                string v = a[eq.Length..].Trim().Trim('"');
                return v.Length > 0 ? v : throw new ArgumentException($"Missing value for '{flag}'.");
            }
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for '{flag}'.");
                return args[i + 1].Trim().Trim('"');
            }
        }
        return null;
    }

    private static int? ParseOptionalInt(string? raw, string flag, int min, int max) =>
        raw is null ? null : (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max
            ? v : throw new ArgumentException($"Invalid {flag} value '{raw}'; expected integer in {min}..{max}."));

    private static string? ResolveOptionalPath(string? raw, SandboxResolution sandbox)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string full = Path.GetFullPath(raw.Trim());
        if (sandbox.IsSandboxActive) SandboxEnvironmentSeam.AssertSandboxPathIfActive(full, sandbox);
        return full;
    }
}
