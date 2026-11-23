// @module: reCart.Contracts.CartPackageManifest
// @purpose: Clean-room module.recomp.json manifest model, lifted game module ABI, host interface, and execution diagnostics.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, System.Text.Json.Serialization, Foundation.Retro
// @exports: ICartModuleManifest, CartPackageManifest, ICartRecompGameModule, IReCartHost, CartExecutionDiagnostics

using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Retro;

namespace reCart.Contracts;

/// <summary>
/// Clean-room <c>.recomp</c> cartridge module manifest contract aligned with Dev Bible Standard 18.
/// </summary>
public interface ICartModuleManifest : IRecompModuleManifest
{
    /// <summary>Target engine identifier (<c>reCart</c> or legacy aliases <c>reCartOne</c>/<c>reNes</c>).</summary>
    string EngineId { get; }

    /// <summary>Minimum compatible engine semantic version.</summary>
    string EngineVersion { get; }

    /// <summary>Primary compatible engine version track (<c>0.1.x</c>).</summary>
    string CompatibleEngineTrack { get; }

    /// <summary>Ordered compatible engine version tracks checked before <see cref="CompatibleEngineTrack"/>.</summary>
    IReadOnlyList<string> SupportedEngineTracks { get; }

    /// <summary>Relative preload directory containing BYO cartridge media (defaults to <c>data/rom</c>).</summary>
    string PreloadRomDir { get; }

    /// <summary>Fully qualified <see cref="ICartRecompGameModule"/> implementation type name.</summary>
    string ModuleTypeName { get; }

    /// <summary>Optional cartridge serial or catalog code.</summary>
    string? SerialNumber { get; }

    /// <summary>Optional entry C# source or compiled assembly path.</summary>
    string? EntryAssembly { get; }

    /// <summary>Default timing mode for this module.</summary>
    RecompTimingMode DefaultFramerateMode { get; }

    /// <summary>Declared video region standard (<c>auto</c>, <c>ntsc</c>, <c>pal</c>, <c>dendy</c>).</summary>
    CartVideoRegionStandard VideoRegionStandard { get; }

    /// <summary>Numeric iNES / NES 2.0 mapper identifier (<c>0..4095</c>).</summary>
    ushort MapperId { get; }

    /// <summary>Numeric NES 2.0 submapper identifier (<c>0..15</c>).</summary>
    byte SubmapperId { get; }
}

/// <summary>
/// JSON-serializable clean-room <c>module.recomp.json</c> manifest model and validator.
/// </summary>
public sealed class CartPackageManifest : ICartModuleManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Manifest schema revision (<c>1</c>+).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <inheritdoc />
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>Human-facing module title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc />
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ModuleId : Title;

    /// <inheritdoc />
    [JsonPropertyName("platformId")]
    public string PlatformId { get; set; } = CartEngineConstants.PlatformId;

    /// <inheritdoc />
    [JsonPropertyName("version")]
    public string Version { get; set; } = CartEngineConstants.Version;

    /// <inheritdoc />
    [JsonPropertyName("engineId")]
    public string EngineId { get; set; } = CartEngineConstants.EngineId;

    /// <inheritdoc />
    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = CartEngineConstants.Version;

    /// <inheritdoc />
    [JsonPropertyName("compatibleEngineTrack")]
    public string CompatibleEngineTrack { get; set; } = CartEngineConstants.CompatibleEngineTrack;

    /// <summary>Ordered compatible engine version tracks.</summary>
    [JsonPropertyName("supportedEngineTracks")]
    public List<string> SupportedEngineTracks { get; set; } = [.. CartEngineConstants.SupportedEngineTracks];

    IReadOnlyList<string> ICartModuleManifest.SupportedEngineTracks => SupportedEngineTracks;

    /// <inheritdoc />
    [JsonPropertyName("preloadRomDir")]
    public string PreloadRomDir { get; set; } = "data/rom";

    /// <inheritdoc />
    [JsonPropertyName("moduleTypeName")]
    public string ModuleTypeName { get; set; } = string.Empty;

    /// <inheritdoc />
    [JsonPropertyName("serialNumber")]
    public string? SerialNumber { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("entryAssembly")]
    public string? EntryAssembly { get; set; }

    /// <summary>Preferred clean-room render backend ID.</summary>
    [JsonPropertyName("backendId")]
    public string? BackendId { get; set; } = CartRenderBackendIds.OpenGl;

    /// <summary>Preferred aspect ratio presentation mode.</summary>
    [JsonPropertyName("aspectRatioMode")]
    public string? AspectRatioMode { get; set; } = "nativePar";

    /// <summary>Preferred execution tier identifier (<c>staticAotOnly</c> primary).</summary>
    [JsonPropertyName("executionTierMode")]
    public string? ExecutionTierMode { get; set; } = "staticAotOnly";

    /// <summary>Preferred video region token (<c>auto</c>, <c>ntsc</c>, <c>pal</c>, <c>dendy</c>).</summary>
    [JsonPropertyName("videoRegionStandard")]
    public string VideoRegionStandardName { get; set; } = "auto";

    /// <inheritdoc />
    [JsonIgnore]
    public CartVideoRegionStandard VideoRegionStandard => CartVideoRegionCodec.Parse(VideoRegionStandardName);

    /// <inheritdoc />
    [JsonPropertyName("mapperId")]
    public ushort MapperId { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("submapperId")]
    public byte SubmapperId { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("defaultFramerateMode")]
    public RecompTimingMode DefaultFramerateMode { get; set; } = RecompTimingMode.LockedCap;

    /// <summary>Required user BYO cartridge checksums verified prior to launch.</summary>
    [JsonPropertyName("requiredDiscChecksums")]
    public List<DiscChecksumEntry> RequiredDiscChecksums { get; set; } = [];

    IReadOnlyList<DiscChecksumEntry> IRecompModuleManifest.RequiredDiscChecksums => RequiredDiscChecksums;

    /// <summary>Optional resolved on-disk package path (not serialized).</summary>
    [JsonIgnore]
    public string? ResolvedPackagePath { get; set; }

    /// <summary>Forward-compatible extension bag preserving unknown properties across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Deserializes a UTF-8 JSON string into a <see cref="CartPackageManifest"/>.</summary>
    /// <param name="json">Raw JSON manifest payload.</param>
    /// <returns>Deserialized manifest instance.</returns>
    public static CartPackageManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CartPackageManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Unable to deserialize reCart package manifest JSON.");
    }

    /// <summary>Serializes this manifest into formatted camelCase JSON.</summary>
    /// <returns>Formatted UTF-8 JSON string.</returns>
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Validates mandatory identity fields (<c>moduleId</c>, <c>engineId</c>, <c>version</c>).</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModuleId)) throw new InvalidDataException("Package manifest missing required 'moduleId'.");
        if (string.IsNullOrWhiteSpace(EngineId)) throw new InvalidDataException($"Package manifest '{ModuleId}' missing required 'engineId'.");
        if (string.IsNullOrWhiteSpace(Version)) throw new InvalidDataException($"Package manifest '{ModuleId}' missing required 'version'.");
    }
}

/// <summary>
/// Execution ABI implemented by statically lifted AOT cartridge assemblies loaded by <c>reCart</c>.
/// </summary>
public interface ICartRecompGameModule : IRecompGameModule
{
    /// <summary>Canonical lowercase-kebab module identifier.</summary>
    string ModuleId { get; }

    /// <summary>Required numeric iNES / NES 2.0 mapper identifier.</summary>
    ushort MapperId { get; }

    /// <summary>Required numeric NES 2.0 submapper identifier.</summary>
    byte SubmapperId { get; }

    /// <summary>Whether differential lockstep verification against <c>interpreterOracle</c> is supported.</summary>
    bool SupportsLockstepOracle { get; }

    /// <summary>Binds host subsystems prior to reset vector execution.</summary>
    void BindHost(IReCartHost host, ICartMemoryBus bus, ICartCpuContext cpu, ICartPpuController ppu, ICartApuSink apu);

    /// <summary>Executes the statically lifted reset routine.</summary>
    void ExecuteResetVector();

    /// <summary>Dispatches statically lifted execution at <paramref name="prgBank"/> and <paramref name="address"/>.</summary>
    bool TryDispatch(uint prgBank, ushort address);

    /// <summary>Invokes the lifted VBlank <c>NMI</c> handler routine.</summary>
    void HandleNmi();

    /// <summary>Invokes the lifted maskable <c>IRQ</c> handler routine.</summary>
    void HandleIrq();
}

/// <summary>
/// Platform host surface exposed to statically compiled cartridge game modules.
/// </summary>
public interface IReCartHost : IRecompHost
{
    /// <summary>Active <c>2A03</c> CPU memory bus and mapper banking controller.</summary>
    ICartMemoryBus Bus { get; }

    /// <summary>Active <c>2A03</c> CPU register context.</summary>
    ICartCpuContext Cpu { get; }

    /// <summary>Active <c>2C02</c> PPU controller and framebuffer source.</summary>
    ICartPpuController Ppu { get; }

    /// <summary>Active <c>2A03</c> APU audio sink.</summary>
    ICartApuSink Apu { get; }

    /// <summary>Validated clean-room manifest for the currently mounted cartridge.</summary>
    ICartModuleManifest Manifest { get; }

    /// <summary>Active clock/scanline timing profile.</summary>
    CartRegionTimingProfile TimingProfile { get; }

    /// <summary>Resolved local user BYO media directory (<c>data\rom\</c>).</summary>
    string PreloadRomDirectory { get; }

    /// <summary>Resolved writable save directory.</summary>
    string SaveDirectory { get; }
}

/// <summary>
/// Mutable runtime execution telemetry counters distinguishing static AOT vs quarantined oracle execution.
/// </summary>
public sealed class CartExecutionDiagnostics
{
    /// <summary>Count of frames or basic blocks dispatched through compiled AOT code.</summary>
    public long StaticDispatchCount { get; set; }

    /// <summary>Count of steps executed inside quarantined Tier 4 <c>interpreterOracle</c> mode.</summary>
    public long InterpreterDispatchCount { get; set; }

    /// <summary>Count of fail-closed unlifted PC trap events recorded.</summary>
    public long UnliftedTrapCount { get; set; }

    /// <summary>Count of audio sample frames dropped by the ring buffer.</summary>
    public long AudioDroppedSamples { get; set; }

    /// <summary>Total frames presented.</summary>
    public long FramesRendered { get; set; }
}
