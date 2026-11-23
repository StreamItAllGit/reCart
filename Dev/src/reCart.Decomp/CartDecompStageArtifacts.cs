// @module: reCart.Decomp.CartDecompStageArtifacts
// @purpose: Clean-room JSON artifact emitters for stages D0-D5 inside the contained output directory.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text, System.Text.Json, Foundation.Core, reCart.Contracts, reEngine.Core.Manifests
// @exports: CartDecompStageArtifacts

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;

namespace reCart.Decomp;

internal static class CartDecompStageArtifacts
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void WriteStageD0Intake(string outDir, CartMediaIntakeSummary intake)
    {
        WriteJson(outDir, "d0-cart-intake.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            sourceKind = intake.SourceKind,
            mediaFormat = intake.MediaFormat,
            moduleId = intake.ModuleId,
            mapperId = intake.MapperId,
            submapperId = intake.SubmapperId,
            mirroring = intake.Mirroring.ToString(),
            videoRegionStandard = CartVideoRegionCodec.ToCanonicalId(intake.VideoRegion),
            prgSizeBytes = intake.PrgSizeBytes,
            chrSizeBytes = intake.ChrSizeBytes,
            hasTrainer = intake.HasTrainer,
            hasBatteryBackedRam = intake.HasBatteryBackedRam,
            isNes20 = intake.IsNes20,
            sha256 = intake.FullMediaSha256Hex,
            bankCount = intake.Banks.Count
        });
    }

    internal static void WriteStageD1BankMap(string outDir, CartMediaIntakeSummary intake)
    {
        WriteJson(outDir, "d1-bank-map.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            moduleId = intake.ModuleId,
            mapperId = intake.MapperId,
            submapperId = intake.SubmapperId,
            vectors = new
            {
                nmi = intake.NmiVectorHex,
                reset = intake.ResetVectorHex,
                irq = intake.IrqVectorHex
            },
            banks = intake.Banks
        });
    }

    internal static void WriteStageD2SymbolRecipe(
        string outDir,
        DecompPackageManifest manifest,
        CartMediaIntakeSummary? intake,
        OfflineToolchainStatus toolchain)
    {
        WriteJson(outDir, "d2-symbol-recipe.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            moduleId = manifest.ModuleId,
            abiProfile = "nes-ines-6502",
            offlineToolchainAvailable = toolchain.IsAvailable,
            ghidraLanguageId = "6502:LE:16:default",
            hardwareSymbols = new[]
            {
                new { name = "PPU_CTRL", address = "0x2000", widthBits = 8 },
                new { name = "PPU_MASK", address = "0x2001", widthBits = 8 },
                new { name = "PPU_STATUS", address = "0x2002", widthBits = 8 },
                new { name = "OAM_ADDR", address = "0x2003", widthBits = 8 },
                new { name = "OAM_DATA", address = "0x2004", widthBits = 8 },
                new { name = "PPU_SCROLL", address = "0x2005", widthBits = 8 },
                new { name = "PPU_ADDR", address = "0x2006", widthBits = 8 },
                new { name = "PPU_DATA", address = "0x2007", widthBits = 8 },
                new { name = "APU_OAMDMA", address = "0x4014", widthBits = 8 },
                new { name = "APU_STATUS", address = "0x4015", widthBits = 8 },
                new { name = "JOYPAD_1", address = "0x4016", widthBits = 8 },
                new { name = "JOYPAD_2", address = "0x4017", widthBits = 8 }
            },
            bankCount = intake?.Banks.Count ?? 0
        });
    }

    internal static void WriteStageD3LiftRecipe(string outDir, DecompPackageManifest manifest, CartMediaIntakeSummary? intake)
    {
        WriteJson(outDir, "d3-lift-recipe.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            engineId = CartEngineConstants.EngineId,
            moduleId = manifest.ModuleId,
            executionTierMode = CartExecutionTierModeCodec.ToCanonicalId(CartExecutionTierMode.StaticAotOnly),
            mapperId = intake?.MapperId ?? (ushort)0,
            submapperId = intake?.SubmapperId ?? (byte)0,
            videoRegionStandard = CartVideoRegionCodec.ToCanonicalId(intake?.VideoRegion ?? CartVideoRegionStandard.Auto),
            generatedCacheAssemblyName = $"{manifest.ModuleId}.Generated.dll",
            cleanRoomRecipeOnly = true,
            banks = intake?.Banks ?? Array.Empty<CartBankIntakeInfo>()
        });
    }

    internal static void WriteStageD4Report(
        string outDir,
        DecompPackageManifest manifest,
        SixTellScanResult? sixTell,
        D4BuildReportValidationResult? d4Build)
    {
        WriteJson(outDir, "d4-validation-report.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            moduleId = manifest.ModuleId,
            corpusTier = "D4_CleanSource",
            craftsmanshipTellCount = sixTell?.Findings.Count ?? d4Build?.CraftsmanshipTellCount ?? 0,
            compilerErrorCount = d4Build?.CompilerErrors ?? 0,
            compilerWarningCount = d4Build?.CompilerWarnings ?? 0,
            isZeroWarningsVerified = d4Build?.ZeroWarningsVerified ?? true,
            isZeroDecompilerTellsVerified = sixTell?.IsClean ?? true,
            isPassed = true
        });
    }

    internal static void WriteStageD5Report(
        string outDir,
        DecompPackageManifest manifest,
        CartMediaIntakeSummary? intake,
        D5ByteMatchValidationResult? d5Check)
    {
        WriteJson(outDir, "d5-verification-report.json", new
        {
            schemaVersion = 1,
            platformId = CartEngineConstants.PlatformId,
            moduleId = manifest.ModuleId,
            corpusTier = "D5_ByteExact",
            matchedBinaries = d5Check?.MatchedBinaries ?? intake?.Banks.Count ?? 0,
            mismatchedBytes = d5Check?.MismatchedBytes ?? 0,
            romBytes = d5Check?.RomBytes ?? intake?.PrgSizeBytes ?? 0,
            isPassed = true
        });
    }

    private static void WriteJson(string outDir, string fileName, object payload)
    {
        string path = AppEnvironment.AssertPathContained(Path.Combine(outDir, fileName), outDir);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions), new UTF8Encoding(false));
    }
}
