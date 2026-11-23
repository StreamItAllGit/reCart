// @module: reCart.Contracts.CartAotRecipeSpec
// @purpose: Clean-room aot-recipe.json contract declaring PRG bank seeds, jump-table dispatch hints, and watch descriptors without retail bytes.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Text.Json, System.Text.Json.Serialization
// @exports: CartAotRecipeSpec, CartAotRecipeSeed, CartAotJumpTableHint, CartAotTrampolineHint, CartAotMemoryWatchHint

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace reCart.Contracts;

/// <summary>
/// Clean-room <c>aot-recipe.json</c> descriptor packaged alongside <c>module.recomp.json</c> inside zero-binary <c>.recomp</c> archives.
/// Supplies structural entry seeds so <c>reCart.Compiler</c> lifts the user's BYO ROM locally into <c>&lt;dataDir&gt;\cache\</c>.
/// </summary>
public sealed record CartAotRecipeSpec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Recipe schema revision (<c>1</c>+).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Target lowercase-kebab cartridge module identifier matching <c>module.recomp.json</c>.</summary>
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; init; } = string.Empty;

    /// <summary>Target platform identifier (<c>nes</c>).</summary>
    [JsonPropertyName("platformId")]
    public string PlatformId { get; init; } = CartEngineConstants.PlatformId;

    /// <summary>Target iNES / NES 2.0 mapper identifier.</summary>
    [JsonPropertyName("mapperId")]
    public ushort MapperId { get; init; }

    /// <summary>Target NES 2.0 submapper identifier.</summary>
    [JsonPropertyName("submapperId")]
    public byte SubmapperId { get; init; }

    /// <summary>Flat list of hex CPU entry addresses seeded into the fixed bank when bank indices are omitted.</summary>
    [JsonPropertyName("entryAddresses")]
    public List<string> EntryAddresses { get; init; } = [];

    /// <summary>Bank-qualified structural seed entries emitted by offline oracle schema tooling.</summary>
    [JsonPropertyName("seeds")]
    public List<CartAotRecipeSeed> Seeds { get; init; } = [];

    /// <summary>Inline jump-table dispatch hints (e.g., <c>$C6B7</c> RTS dispatch tables).</summary>
    [JsonPropertyName("jumpTables")]
    public List<CartAotJumpTableHint> JumpTables { get; init; } = [];

    /// <summary>Cross-bank PRG trampoline descriptors (e.g., UNROM <c>$C932..$C958</c>).</summary>
    [JsonPropertyName("trampolines")]
    public List<CartAotTrampolineHint> Trampolines { get; init; } = [];

    /// <summary>Logical WRAM telemetry watch regions without retail symbol leakage.</summary>
    [JsonPropertyName("memoryWatches")]
    public List<CartAotMemoryWatchHint> MemoryWatches { get; init; } = [];

    /// <summary>Extension bag preserving forward-compatible recipe metadata.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Deserializes a UTF-8 JSON payload into a <see cref="CartAotRecipeSpec"/>.</summary>
    public static CartAotRecipeSpec Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CartAotRecipeSpec>(json, JsonOptions)
            ?? throw new InvalidDataException("aot-recipe.json deserialized to null.");
    }

    /// <summary>Serializes this recipe specification to camelCase UTF-8 JSON.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Validates required identity fields and well-formed hex addresses.</summary>
    public void Validate()
    {
        if (SchemaVersion < 1)
            throw new InvalidDataException($"aot-recipe.json has invalid schemaVersion ({SchemaVersion}).");
        if (string.IsNullOrWhiteSpace(ModuleId))
            throw new InvalidDataException("aot-recipe.json is missing required 'moduleId'.");
        if (Seeds.Count == 0 && EntryAddresses.Count == 0)
            throw new InvalidDataException($"aot-recipe.json '{ModuleId}' must declare at least one seed or entryAddress.");
        foreach (string rawAddr in EntryAddresses)
            _ = ParseHexAddress(rawAddr);
        foreach (CartAotRecipeSeed seed in Seeds)
            _ = ParseHexAddress(seed.Address);
    }

    /// <summary>Resolves distinct ordered <c>(prgBank, address)</c> seed tuples for static lifting.</summary>
    public IReadOnlyList<(uint PrgBank, ushort Address)> ToSeedTuples(uint fixedBankIndex)
    {
        var seen = new HashSet<(uint Bank, ushort Addr)>();
        var list = new List<(uint Bank, ushort Addr)>();

        foreach (CartAotRecipeSeed seed in Seeds)
        {
            ushort addr = ParseHexAddress(seed.Address);
            uint bank = addr >= 0xC000
                ? fixedBankIndex
                : (seed.PrgBank ?? fixedBankIndex);
            if (seen.Add((bank, addr))) list.Add((bank, addr));
        }

        foreach (string rawAddr in EntryAddresses)
        {
            ushort addr = ParseHexAddress(rawAddr);
            if (seen.Add((fixedBankIndex, addr))) list.Add((fixedBankIndex, addr));
        }

        return list;
    }

    /// <summary>Parses a 16-bit address formatted as <c>0xC001</c>, <c>$C001</c>, or <c>C001</c>.</summary>
    public static ushort ParseHexAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException("Empty address token in aot-recipe.json.");
        string token = raw.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) token = token[2..];
        else if (token.StartsWith("$", StringComparison.Ordinal)) token = token[1..];
        if (!ushort.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
            throw new InvalidDataException($"Invalid 16-bit hex address '{raw}' in aot-recipe.json.");
        return value;
    }
}

/// <summary>Represents one bank-qualified clean-room subroutine seed inside <see cref="CartAotRecipeSpec"/>.</summary>
public sealed record CartAotRecipeSeed(
    [property: JsonPropertyName("prgBank")] uint? PrgBank,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("label")] string? Label = null);

/// <summary>Represents an inline jump-table dispatch hint inside <see cref="CartAotRecipeSpec"/>.</summary>
public sealed record CartAotJumpTableHint(
    [property: JsonPropertyName("prgBank")] uint PrgBank,
    [property: JsonPropertyName("dispatcherAddress")] string DispatcherAddress,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>Represents a cross-bank trampoline hint inside <see cref="CartAotRecipeSpec"/>.</summary>
public sealed record CartAotTrampolineHint(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("targetBank")] uint? TargetBank = null,
    [property: JsonPropertyName("kind")] string? Kind = null);

/// <summary>Represents a WRAM range descriptor exposed to diagnostic overlays inside <see cref="CartAotRecipeSpec"/>.</summary>
public sealed record CartAotMemoryWatchHint(
    [property: JsonPropertyName("regionId")] string RegionId,
    [property: JsonPropertyName("startAddress")] string StartAddress,
    [property: JsonPropertyName("lengthBytes")] int LengthBytes);
