// @module: reCart.Decomp.CartMediaIntakeInspector
// @purpose: Clean-room in-memory BYO .nes/.fds header/bank parser and read-only upstream research metadata reader.
// @depends-on: System, System.Buffers.Binary, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Text, System.Text.Json, reCart.Contracts
// @exports: CartMediaIntakeInspector

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using reCart.Contracts;

namespace reCart.Decomp;

internal static class CartMediaIntakeInspector
{
    private const int FdsHeaderBytes = 16;
    private const int FdsSideBytes = 65500;
    private const int PrgBankBytes = 16384;

    private static readonly byte[] FdsHeaderMagic = [0x46, 0x44, 0x53, 0x1A];
    private static readonly byte[] FdsBlock1Magic = "*NINTENDO-HVC*"u8.ToArray();

    internal static CartMediaIntakeSummary InspectByoMedia(string mediaPath, string? moduleIdOverride = null, string? expectedSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        string fullPath = Path.GetFullPath(mediaPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"BYO cartridge or disk image not found: '{fullPath}'.", fullPath);
        }

        byte[] rawBytes = File.ReadAllBytes(fullPath);
        string sha256Hex = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(sha256Hex, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"BYO media SHA-256 mismatch for '{Path.GetFileName(fullPath)}': got {sha256Hex}, expected {expectedSha256.Trim().ToLowerInvariant()}.");
        }

        string moduleId = !string.IsNullOrWhiteSpace(moduleIdOverride)
            ? moduleIdOverride.Trim()
            : Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant().Replace(' ', '-');

        if (Path.GetExtension(fullPath).Equals(".fds", StringComparison.OrdinalIgnoreCase) ||
            (rawBytes.Length >= 4 && rawBytes.AsSpan(0, 4).SequenceEqual(FdsHeaderMagic)) ||
            (rawBytes.Length >= 15 && rawBytes.AsSpan(1, 14).SequenceEqual(FdsBlock1Magic)))
        {
            return ParseFdsInMemory(rawBytes, moduleId, sha256Hex);
        }

        return ParseNesInMemory(rawBytes, moduleId, sha256Hex);
    }

    internal static CartMediaIntakeSummary InspectUpstreamResearchReadOnly(string researchRootDir, string? moduleIdOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchRootDir);
        string fullRoot = Path.GetFullPath(researchRootDir);
        string manifestPath = ResolveResearchManifestPath(fullRoot)
            ?? throw new FileNotFoundException($"No research.manifest.json found under '{fullRoot}'.", Path.Combine(fullRoot, "Dev", "research.manifest.json"));

        ResearchManifestValidationResult check = D4BuildReportValidator.ValidateResearchManifestFile(manifestPath, CartEngineConstants.PlatformId);
        if (!check.IsValid) throw new InvalidDataException(check.Summary);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8), D4ReportSchemaHelpers.LenientJsonDocOptions);
        JsonElement root = doc.RootElement;
        string moduleId = !string.IsNullOrWhiteSpace(moduleIdOverride) ? moduleIdOverride.Trim() : check.GameId;
        var banks = new List<CartBankIntakeInfo>();

        if (D4ReportSchemaHelpers.TryGetPropertyIgnoreCase(root, "binaries", out JsonElement binsEl) && binsEl.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (JsonElement bin in binsEl.EnumerateArray())
            {
                string name = D4ReportSchemaHelpers.TryGetString(bin, "name", "relpath") ?? $"BANK_{idx:D2}";
                string role = D4ReportSchemaHelpers.TryGetString(bin, "role") ?? "secondary";
                string baseAddr = D4ReportSchemaHelpers.TryGetString(bin, "baseAddress") ?? "0x8000";
                banks.Add(new CartBankIntakeInfo(idx++, name, role, baseAddr, PrgBankBytes, string.Empty));
            }
        }

        return new CartMediaIntakeSummary(
            "upstream-research", "tier-r-metadata", moduleId, 0, 0,
            CartMirroringMode.Vertical, CartVideoRegionStandard.Ntsc,
            banks.Count * PrgBankBytes, 0, false, false, true, string.Empty, null, null, null, banks);
    }

    internal static string? ResolveResearchManifestPath(string rootOrFile)
    {
        if (File.Exists(rootOrFile) && Path.GetFileName(rootOrFile).Equals("research.manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(rootOrFile);
        }
        if (!Directory.Exists(rootOrFile)) return null;
        string devManifest = Path.Combine(rootOrFile, "Dev", "research.manifest.json");
        if (File.Exists(devManifest)) return devManifest;
        string rootManifest = Path.Combine(rootOrFile, "research.manifest.json");
        return File.Exists(rootManifest) ? rootManifest : null;
    }

    private static CartMediaIntakeSummary ParseNesInMemory(byte[] rawBytes, string moduleId, string sha256Hex)
    {
        CartRomHeader header = CartRomHeaderParser.Parse(rawBytes);
        int trainerOffset = CartRomHeaderParser.HeaderSizeBytes + (header.HasTrainer ? CartRomHeaderParser.TrainerSizeBytes : 0);
        int requiredTotal = trainerOffset + header.PrgRomSizeBytes + header.ChrRomSizeBytes;
        if (rawBytes.Length < requiredTotal)
        {
            throw new InvalidDataException($"Truncated .nes image: expected >= {requiredTotal} byte(s), got {rawBytes.Length}.");
        }

        ReadOnlySpan<byte> prg = rawBytes.AsSpan(trainerOffset, header.PrgRomSizeBytes);
        var banks = new List<CartBankIntakeInfo>();
        int bankCount = Math.Max(1, (prg.Length + PrgBankBytes - 1) / PrgBankBytes);
        for (int i = 0; i < bankCount; i++)
        {
            int offset = i * PrgBankBytes;
            int len = Math.Min(PrgBankBytes, prg.Length - offset);
            if (len <= 0) break;
            bool isFixed = i == bankCount - 1;
            string role = isFixed ? "primary" : "secondary";
            string baseAddr = (isFixed || bankCount == 1) ? "0xc000" : "0x8000";
            string name = isFixed ? $"PRG_FIXED_{i:D2}" : $"BANK_{i:D2}";
            string bankSha = Convert.ToHexString(SHA256.HashData(prg.Slice(offset, len))).ToLowerInvariant();
            banks.Add(new CartBankIntakeInfo(i, name, role, baseAddr, len, bankSha));
        }

        string? nmiHex = null, resetHex = null, irqHex = null;
        if (prg.Length >= 6)
        {
            int vecBase = prg.Length - 6;
            nmiHex = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(prg.Slice(vecBase, 2)):x4}";
            resetHex = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(prg.Slice(vecBase + 2, 2)):x4}";
            irqHex = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(prg.Slice(vecBase + 4, 2)):x4}";
        }

        return new CartMediaIntakeSummary(
            "byo-nes", header.IsNes20 ? "nes20" : "ines", moduleId,
            header.MapperId, header.SubmapperId, header.Mirroring, header.VideoRegionStandard,
            header.PrgRomSizeBytes, header.ChrRomSizeBytes, header.HasTrainer, header.HasBatteryBackedRam,
            header.IsNes20, sha256Hex, resetHex, nmiHex, irqHex, banks);
    }

    private static CartMediaIntakeSummary ParseFdsInMemory(byte[] rawBytes, string moduleId, string sha256Hex)
    {
        bool hasHeader = rawBytes.Length >= FdsHeaderBytes && rawBytes.AsSpan(0, 4).SequenceEqual(FdsHeaderMagic);
        int payloadOffset = hasHeader ? FdsHeaderBytes : 0;
        int payloadBytes = rawBytes.Length - payloadOffset;
        if (payloadBytes <= 0 || payloadBytes % FdsSideBytes != 0)
        {
            throw new InvalidDataException($"Invalid .fds image size ({rawBytes.Length} B); expected exact multiple of {FdsSideBytes}-byte disk sides.");
        }

        int sideCount = hasHeader && rawBytes[4] > 0 ? rawBytes[4] : (payloadBytes / FdsSideBytes);
        if (sideCount * FdsSideBytes != payloadBytes)
        {
            throw new InvalidDataException($"FDS header declares {sideCount} side(s), but payload contains {payloadBytes / FdsSideBytes}.");
        }
        if (!rawBytes.AsSpan(payloadOffset + 1, FdsBlock1Magic.Length).SequenceEqual(FdsBlock1Magic))
        {
            throw new InvalidDataException("FDS disk side 0 missing '*NINTENDO-HVC*' block-1 signature.");
        }

        var banks = new List<CartBankIntakeInfo>(sideCount);
        for (int s = 0; s < sideCount; s++)
        {
            ReadOnlySpan<byte> side = rawBytes.AsSpan(payloadOffset + s * FdsSideBytes, FdsSideBytes);
            string sideSha = Convert.ToHexString(SHA256.HashData(side)).ToLowerInvariant();
            banks.Add(new CartBankIntakeInfo(s, $"DISK_SIDE_{s:D2}", s == 0 ? "primary" : "disk-side", "0x6000", FdsSideBytes, sideSha));
        }

        return new CartMediaIntakeSummary(
            "byo-fds", hasHeader ? "fwnes-fds" : "raw-fds", moduleId, 20, 0,
            CartMirroringMode.Horizontal, CartVideoRegionStandard.Ntsc,
            payloadBytes, 0, false, false, false, sha256Hex, null, null, null, banks);
    }
}
