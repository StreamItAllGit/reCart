// @module: reCart.Contracts.CartEngineConstants
// @purpose: Canonical identity, alias, protocol, display geometry, audio, backend, and execution tier constants for reCart.
// @depends-on: System, System.Collections.Generic
// @exports: CartEngineConstants, CartRenderBackendIds, CartExecutionTierMode, CartExecutionTierModeCodec

namespace reCart.Contracts;

/// <summary>
/// Canonical engine identity, SDK protocol metadata, timing, and clean-room safety constants for <c>reCart</c>.
/// </summary>
public static class CartEngineConstants
{
    /// <summary>Lowercase canonical application identifier.</summary>
    public const string AppId = "recart";

    /// <summary>Title-case product display name.</summary>
    public const string DisplayName = "reCart";

    /// <summary>Canonical engine identifier registered in <c>reEngine</c> and Dev Bible Standard 18.</summary>
    public const string EngineId = "reCart";

    /// <summary>3-way legacy/alias identifiers resolved symmetrically by <c>reEngine v0.5.0</c>.</summary>
    public static IReadOnlyList<string> LegacyAliasEngineIds { get; } = ["reCartOne", "reNes"];

    /// <summary>Canonical hardware platform identifier shared across <c>.decomp</c> and <c>.recomp</c> manifests.</summary>
    public const string PlatformId = "nes";

    /// <summary>Active <c>reCart</c> engine semantic version.</summary>
    public const string Version = "0.1.4";

    /// <summary>Primary engine track advertised by module and installer manifests.</summary>
    public const string CompatibleEngineTrack = "0.1.x";

    /// <summary>Default ordered list of compatible engine tracks supported by <c>reCart 0.1.x</c> modules.</summary>
    public static IReadOnlyList<string> SupportedEngineTracks { get; } = ["0.1.x"];

    /// <summary>Implemented <c>reEngine.Host</c> protocol version used for CLI/flag routing (<c>&gt;= 0.3.0</c>).</summary>
    public const string HostProtocolVersion = "0.6.2";

    /// <summary>Minimum compatible <c>reEngine</c> suite router version.</summary>
    public const string MinSuiteVersion = "0.6.0";

    /// <summary>Canonical studio umbrella folder under <c>%LOCALAPPDATA%\Programs\</c>.</summary>
    public const string StudioName = "StreamItAll";

    /// <summary>Canonical suite folder name under the studio root.</summary>
    public const string SuiteName = "reEngine";

    /// <summary>Canonical installer suite identifier (Standard 18: <c>suiteId</c>).</summary>
    public const string SuiteId = "reEngine";

    /// <summary>Canonical umbrella subpath for side-by-side suite engine installations.</summary>
    public const string UmbrellaSubpath = "StreamItAll\\reEngine\\engines\\reCart\\v0.1.4";

    /// <summary>Default installer profile identifier.</summary>
    public const string DefaultInstallProfile = "lightweight";

    /// <summary>Brand accent color hex (Warm Amber).</summary>
    public const string BrandAccentHex = "#F59E0B";

    /// <summary>Native NTSC PPU frame cadence in hertz.</summary>
    public const double NtscFrameRateHz = 60.0988;

    /// <summary>Native PPU horizontal resolution in pixels.</summary>
    public const int FrameWidth = 256;

    /// <summary>Native PPU vertical resolution in pixels.</summary>
    public const int FrameHeight = 240;

    /// <summary>Default stereo PCM output sample rate in hertz.</summary>
    public const int DefaultAudioSampleRateHz = 44100;

    /// <summary>Enforces zero-daemon execution with zero background services or Windows Startup entries.</summary>
    public const bool AllowAutoStart = false;
}

/// <summary>
/// Canonical clean-room render backend identifiers aligned with <c>reEngine.Host v0.5.0</c>.
/// </summary>
public static class CartRenderBackendIds
{
    /// <summary>Hardware OpenGL 3.3 Core presenter backend.</summary>
    public const string OpenGl = "opengl";

    /// <summary>Hardware Vulkan 1.3 presenter backend.</summary>
    public const string Vulkan = "vulkan";

    /// <summary>Hardware DirectX 12 presenter backend.</summary>
    public const string DirectX = "directx";

    /// <summary>Deterministic 16-pixel band parallel CPU presenter backend.</summary>
    public const string ParallelCpu = "parallelCpu";

    /// <summary>Deterministic SDL3 software texture presenter backend.</summary>
    public const string Software = "software";

    /// <summary>Normalizes CLI backend aliases (for example <c>dx12</c> / <c>d3d12</c> -&gt; <c>directx</c>).</summary>
    /// <param name="raw">User or manifest backend identifier.</param>
    /// <returns>Canonical backend identifier defaulting to <see cref="OpenGl"/>.</returns>
    public static string NormalizeCli(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return OpenGl;
        return raw.Trim().ToLowerInvariant() switch
        {
            "gl" or "gl33" or "opengl" or "opengl33" => OpenGl,
            "vk" or "vulkan" => Vulkan,
            "dx" or "dx12" or "d3d12" or "directx" or "directx12" => DirectX,
            "parallelcpu" or "parallel" or "cpu" => ParallelCpu,
            "sw" or "software" => Software,
            _ => raw.Trim().ToLowerInvariant()
        };
    }
}

/// <summary>
/// Supported execution tiers for <c>reCart</c> (Standard 24 static recompilation primary model).
/// </summary>
public enum CartExecutionTierMode
{
    /// <summary>Pure statically AOT-compiled execution; fails closed if an unlifted PC is encountered.</summary>
    StaticAotOnly = 0,

    /// <summary>Reserved for <c>v0.2.0</c> Tier 3 RyuJIT bring-up fallback on unlifted blocks.</summary>
    AotWithJitFallback = 1,

    /// <summary>Reserved for <c>v0.2.0</c> Tier 3 zero-RWX threaded IR bring-up fallback.</summary>
    AotWithThreadedIr = 2,

    /// <summary>Reserved for <c>v0.2.0</c> explicit bring-up JIT trace collection.</summary>
    JitBringup = 3,

    /// <summary>Quarantined Tier 4 reference interpreter strictly for differential lockstep verification.</summary>
    InterpreterOracle = 4
}

/// <summary>
/// Parses and formats <see cref="CartExecutionTierMode"/> values cleanly across CLI and manifests.
/// </summary>
public static class CartExecutionTierModeCodec
{
    /// <summary>Parses a CLI or manifest execution tier token strictly, defaulting to <see cref="CartExecutionTierMode.StaticAotOnly"/>.</summary>
    /// <param name="raw">Raw execution tier string.</param>
    /// <returns>Parsed <see cref="CartExecutionTierMode"/>.</returns>
    public static CartExecutionTierMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return CartExecutionTierMode.StaticAotOnly;
        return raw.Trim().ToLowerInvariant() switch
        {
            "staticaotonly" or "aot" or "static" => CartExecutionTierMode.StaticAotOnly,
            "aotwithjitfallback" => CartExecutionTierMode.AotWithJitFallback,
            "aotwiththreadedir" => CartExecutionTierMode.AotWithThreadedIr,
            "jitbringup" => CartExecutionTierMode.JitBringup,
            "interpreteroracle" or "oracle" => CartExecutionTierMode.InterpreterOracle,
            _ => CartExecutionTierMode.StaticAotOnly
        };
    }

    /// <summary>Returns the canonical camelCase identifier for <paramref name="mode"/>.</summary>
    /// <param name="mode">Execution tier mode.</param>
    /// <returns>Canonical camelCase identifier string.</returns>
    public static string ToCanonicalId(CartExecutionTierMode mode) => mode switch
    {
        CartExecutionTierMode.AotWithJitFallback => "aotWithJitFallback",
        CartExecutionTierMode.AotWithThreadedIr => "aotWithThreadedIr",
        CartExecutionTierMode.JitBringup => "jitBringup",
        CartExecutionTierMode.InterpreterOracle => "interpreterOracle",
        _ => "staticAotOnly"
    };

    /// <summary>Returns the concise human-facing HUD label for <paramref name="mode"/>.</summary>
    /// <param name="mode">Execution tier mode.</param>
    /// <returns>HUD display badge text.</returns>
    public static string ToDisplayLabel(CartExecutionTierMode mode) => mode switch
    {
        CartExecutionTierMode.InterpreterOracle => "Oracle (Quarantined)",
        CartExecutionTierMode.JitBringup or CartExecutionTierMode.AotWithJitFallback or CartExecutionTierMode.AotWithThreadedIr => "AOT + Bring-Up",
        _ => "AOT Primary"
    };
}
