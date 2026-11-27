// @module: reCart.Player.PlayerCliOptionHelpers
// @purpose: Strict CLI option parsers, optional --require-backend handling, positional argument resolution, and help formatting for reCart.Player.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, Foundation.Core, reCart.Contracts, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: PlayerCliOptionHelpers

using System.Globalization;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal static class PlayerCliOptionHelpers
{
    internal static readonly HashSet<string> ValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--disc", "--backend", "--aspect-mode", "--scale",
        "--audio-driver", "--execution-tier", "--region", "--overscan",
        "--fps-mode", "--target-fps", "--profile",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--frames",
        "--lockstep-steps", "--dump-screenshot", "--dump-audio", "--automation-port", "--automation-token", "--port", "--ui"
    };

    public static string ParseBackendStrict(string raw)
    {
        if (TryNormalizeBackend(raw, out string canonical)) return canonical;
        throw new ArgumentException($"Unsupported --backend '{raw}'. Expected opengl, vulkan, directx, dx12, parallelCpu, or software.");
    }

    public static bool TryNormalizeBackend(string? raw, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (HostRenderBackendIds.TryNormalize(raw, out canonical)) return true;
        string normalized = CartRenderBackendIds.NormalizeCli(raw);
        return HostRenderBackendIds.TryNormalize(normalized, out canonical);
    }

    public static HostAspectRatioMode ParseAspectStrict(string raw) =>
        HostAspectRatioModeCodec.TryParseStrict(raw, out HostAspectRatioMode mode)
            ? mode
            : throw new ArgumentException($"Unsupported --aspect-mode '{raw}'. Expected nativePar, fourByThree, squareInteger, nativeFit, or horPlus16By9.");

    public static HostFrameRateMode ParseFrameRateModeStrict(string raw) =>
        HostFrameRateModeCodec.TryParseStrict(raw, out HostFrameRateMode mode)
            ? mode
            : throw new ArgumentException($"Unsupported --fps-mode '{raw}'. Expected original or enhanced.");

    public static int ParseScaleStrict(string raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s is 1 or 2 or 4 or 8
            ? s
            : throw new ArgumentException($"Invalid --scale '{raw}'. Expected 1, 2, 4, or 8.");

    public static CartExecutionTierMode ParseExecutionTierStrict(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "staticaotonly" or "aot" or "aotprimary" or "static" => CartExecutionTierMode.StaticAotOnly,
        "aotwithjitfallback" => CartExecutionTierMode.AotWithJitFallback,
        "aotwiththreadedir" => CartExecutionTierMode.AotWithThreadedIr,
        "jitbringup" => CartExecutionTierMode.JitBringup,
        "interpreteroracle" or "oracle" => CartExecutionTierMode.InterpreterOracle,
        _ => throw new ArgumentException($"Unsupported --execution-tier '{raw}'.")
    };

    public static CartVideoRegionStandard ParseRegionStrict(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "auto" => CartVideoRegionStandard.Auto,
        "ntsc" => CartVideoRegionStandard.Ntsc,
        "pal" => CartVideoRegionStandard.Pal,
        "dendy" => CartVideoRegionStandard.Dendy,
        _ => throw new ArgumentException($"Unsupported --region '{raw}'. Expected auto, ntsc, pal, or dendy.")
    };

    public static HostOverscanCrop ParseOverscanStrict(string raw)
    {
        int[] vals = raw.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1)
            .ToArray();
        if (vals.Length == 1 && vals[0] >= 0) return new HostOverscanCrop(vals[0], vals[0], vals[0], vals[0]);
        if (vals.Length == 4 && vals.All(v => v >= 0)) return new HostOverscanCrop(vals[0], vals[1], vals[2], vals[3]);
        throw new ArgumentException($"Invalid --overscan '{raw}'. Expected <t,b,l,r> non-negative integers.");
    }

    public static bool ParseRequireBackend(string[] args, ref string backendId, ref bool hasExplicitBackend)
    {
        bool requireBackend = false;
        for (int i = 0; i < args.Length; i++)
        {
            string raw = (args[i] ?? string.Empty).Trim();
            if (raw.StartsWith("--require-backend=", StringComparison.OrdinalIgnoreCase))
            {
                string inline = raw["--require-backend=".Length..].Trim().Trim('"');
                if (!TryNormalizeBackend(inline, out string canonical))
                    throw new ArgumentException($"Unsupported --require-backend '{inline}'. Expected opengl, vulkan, directx, dx12, parallelCpu, or software.");
                requireBackend = true;
                backendId = canonical;
                hasExplicitBackend = true;
                continue;
            }

            if (!string.Equals(raw, "--require-backend", StringComparison.OrdinalIgnoreCase)) continue;
            requireBackend = true;
            if (i + 1 >= args.Length) continue;
            string next = (args[i + 1] ?? string.Empty).Trim().Trim('"');
            if (next.Length > 0 && !next.StartsWith('-') && TryNormalizeBackend(next, out string nextCanonical))
            {
                backendId = nextCanonical;
                hasExplicitBackend = true;
                i++;
            }
        }
        return requireBackend;
    }

    public static bool? ParseSwitch(string[] args, string positiveFlag, string negativeFlag)
    {
        bool? state = null;
        foreach (string raw in args)
        {
            if (string.Equals(raw, positiveFlag, StringComparison.OrdinalIgnoreCase)) state = true;
            else if (string.Equals(raw, negativeFlag, StringComparison.OrdinalIgnoreCase)) state = false;
        }
        return state;
    }

    public static (bool Flag, string? Path) ReadPreloadMedia(string[] args)
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

    public static string? GetFirstPositional(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                bool skipNext = string.Equals(a, "--preload-media", StringComparison.OrdinalIgnoreCase)
                    ? IsPreloadPathToken(args, i)
                    : !a.Contains('=', StringComparison.Ordinal) &&
                      (ValueFlags.Contains(a) ||
                       (string.Equals(a, "--require-backend", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length &&
                        !args[i + 1].Trim().StartsWith('-') &&
                        TryNormalizeBackend(args[i + 1], out _)));
                if (skipNext && i + 1 < args.Length) i++;
                continue;
            }
            if (!a.StartsWith("-", StringComparison.Ordinal) && !string.Equals(a, "/?", StringComparison.Ordinal)) return a.Trim().Trim('"');
        }
        return null;
    }

    public static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    public static string? GetOption(string[] args, string flag)
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

    public static int? ParseOptionalInt(string? raw, string flag, int min, int max) =>
        raw is null ? null : (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max
            ? v : throw new ArgumentException($"Invalid {flag} value '{raw}'; expected integer in {min}..{max}."));

    public static string? ResolveOptionalPath(string? raw, SandboxResolution sandbox)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string full = Path.GetFullPath(raw.Trim());
        if (sandbox.IsSandboxActive) SandboxEnvironmentSeam.AssertSandboxPathIfActive(full, sandbox);
        return full;
    }

    public static string FormatHelpText() => $$"""
        {{CartEngineConstants.DisplayName}} v{{CartEngineConstants.Version}} — Clean-Room AOT Cart Static Recompilation Player
        Usage: reCart.Player [options] [<module-or-rom-path>]
          --open <path>, --module <path>   Open .recomp package, module.recomp.json, or directory
          --rom <path>, --disc <path>      Supply local BYO .nes / .prg cartridge image (or --preload-media)
          --demo-cart                      Run built-in clean-room Ironframe tactical demo cart
          --backend <id>                   opengl | vulkan | directx | dx12 | parallelCpu | software
          --aspect-mode <mode>             nativePar | fourByThree | squareInteger | nativeFit | horPlus16By9
          --scale <1|2|4|8>                Integer resolution scale multiplier (default: 2)
          --region <auto|ntsc|pal|dendy>   Target clock/scanline region profile (default: auto)
          --execution-tier <tier>          staticAotOnly (default) | interpreterOracle | jitBringup
          --fps-mode <original|enhanced>   Target cadence policy (default: enhanced)
          --target-fps <n>                 Target FPS cap (0 = Max/uncapped)
          --vsync, --no-vsync              Enable or disable swap-interval VSync
          --fast-forward                   Start session with fast-forward enabled
          --open-settings, --show-fps      Open F1 drawer or F3 FPS overlay on startup
          --force-zero-rwx                 Disallow RWX JIT allocation (forces zero-RWX tier)
          --lockstep-oracle                Run quarantined Tier 4 oracle / differential lockstep
          --lockstep-steps <n>             Run N-step differential AOT vs InterpreterOracle check
          --overscan <t,b,l,r>             Source edge overscan pixel crop (e.g., 8,8,8,8)
          --fullscreen, --window           Start in borderless fullscreen or desktop window
          --tray-only, --headless          Start without visible desktop window
          --audio-driver <name>            Override native audio driver (default: wasapi on Windows)
          --data-dir, --config, --log-dir  Override storage directories (Standard 02 containment)
          --save-dir <dir>                 Override QuickSave / SRAM directory
          --profile <name>                 Optional launch profile identifier
          --enable-logs                    Enable runtime diagnostic logging in host overlay
          --frames <n>                     Run fixed headless verification frame count and exit
          --dump-screenshot <png>          Write final composite surface PNG
          --dump-audio <wav>               Write deterministic 16-bit stereo PCM WAV capture
          --require-backend [<id>], --wait Fail closed (Exit 6) if GPU unavailable; wait synchronously
          --verify, --doctor               Verify package/host readiness or run diagnostics
          --version, --help                Print version or show help
        Hotkeys: F1 Settings Drawer | F3 HUD | F5 QuickSave | F9 QuickLoad | F11/Alt+Enter Fullscreen
        """;

    private static bool IsPreloadPathToken(string[] args, int flagIndex) =>
        flagIndex + 1 < args.Length && !args[flagIndex + 1].StartsWith("-", StringComparison.Ordinal) &&
        !args[flagIndex + 1].EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) &&
        !args[flagIndex + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
