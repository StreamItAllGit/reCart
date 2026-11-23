// @module: reCart.Decomp.DecompModels
// @purpose: Clean-room D0-D5 models, 6-tell violation DTOs, and validation result contracts for reCart.Decomp.
// @depends-on: System, System.Collections.Generic, reCart.Contracts
// @exports: SixTellKind, DecompTellFinding, SixTellScanResult, D4BuildReportValidationResult, D5ByteMatchValidationResult, ResearchManifestValidationResult, CartBankIntakeInfo, CartMediaIntakeSummary

using reCart.Contracts;

namespace reCart.Decomp;

/// <summary>
/// Canonical six decompiler craftsmanship tell categories enforced at <c>D4_CleanSource</c>.
/// </summary>
public enum SixTellKind
{
    /// <summary>Raw Ghidra/Hex-Rays tokens (undefined*, param_*, CONCAT*, in_*/extraout_*, uVar*/iVar*/pcVar*, FUN_/DAT_).</summary>
    RawDecompilerTokens = 1,

    /// <summary>Banned warning suppression flags (-Wno-everything, -Wno-all, bare -w) in configs or pragmas.</summary>
    BannedWarningSuppression = 2,

    /// <summary>Mechanical line-count split fragment filenames matching _*_01.._NN.c or _01.._NN.c.</summary>
    SplitFragmentFiles = 3,

    /// <summary>Low identifier readability (placeholder locals, Hex-Rays v1/a1 names, SSA clones, or low descriptive ratio).</summary>
    LowIdentifierReadability = 4,

    /// <summary>Missing top-of-file module header comment or missing explanatory comments in non-trivial C files.</summary>
    MissingModuleHeaderOrComments = 5,

    /// <summary>NES 6502 / 16-bit ABI mismatch (host 64-bit target, calling conventions, wide MMIO pointer casts, or .h/.c prototype drift).</summary>
    AbiMismatch = 6
}

/// <summary>
/// Single decompiler tell or build-config finding emitted by <see cref="SixTellDetector"/>.
/// </summary>
public sealed record DecompTellFinding(
    SixTellKind TellKind,
    string RuleId,
    string FilePath,
    int LineNumber,
    string Snippet,
    string Description);

/// <summary>
/// Aggregate outcome of scanning C/header files and build configs through <see cref="SixTellDetector"/>.
/// </summary>
public sealed record SixTellScanResult(
    bool IsClean,
    int FilesScanned,
    IReadOnlyList<SixTellKind> DetectedKinds,
    IReadOnlyList<DecompTellFinding> Findings,
    string Summary);

/// <summary>
/// Validation outcome for <c>d4_build_report.json</c> supporting both Tier R research schemas and Standard 18 schemas.
/// </summary>
public sealed record D4BuildReportValidationResult(
    bool IsValid,
    int CompilerErrors,
    int CompilerWarnings,
    int CraftsmanshipTellCount,
    bool ParityPassed,
    bool ZeroWarningsVerified,
    IReadOnlyList<string> CompilerFlags,
    IReadOnlyList<string> Violations,
    string Summary);

/// <summary>
/// Validation outcome for <c>d5_byte_match.json</c> verifying 1:1 bank and ROM byte parity.
/// </summary>
public sealed record D5ByteMatchValidationResult(
    bool IsValid,
    int MatchedBinaries,
    int MismatchedBytes,
    long RomBytes,
    IReadOnlyList<string> Violations,
    string Summary);

/// <summary>
/// Validation outcome for read-only upstream <c>research.manifest.json</c> metadata.
/// </summary>
public sealed record ResearchManifestValidationResult(
    bool IsValid,
    string GameId,
    string Platform,
    string AbiProfile,
    string CurrentDecompTier,
    string GoalDecompTier,
    string D4ParityTier,
    int BinariesCount,
    IReadOnlyList<string> Violations,
    string Summary);

/// <summary>
/// Clean-room metadata descriptor for one ROM/FDS/research PRG bank (never embeds retail ROM bytes).
/// </summary>
public sealed record CartBankIntakeInfo(
    int BankIndex,
    string Name,
    string Role,
    string BaseAddressHex,
    int SizeBytes,
    string Sha256Hex);

/// <summary>
/// Clean-room media intake summary produced in memory from BYO <c>.nes</c>/<c>.fds</c> images or upstream research metadata.
/// </summary>
public sealed record CartMediaIntakeSummary(
    string SourceKind,
    string MediaFormat,
    string ModuleId,
    ushort MapperId,
    byte SubmapperId,
    CartMirroringMode Mirroring,
    CartVideoRegionStandard VideoRegion,
    int PrgSizeBytes,
    int ChrSizeBytes,
    bool HasTrainer,
    bool HasBatteryBackedRam,
    bool IsNes20,
    string FullMediaSha256Hex,
    string? ResetVectorHex,
    string? NmiVectorHex,
    string? IrqVectorHex,
    IReadOnlyList<CartBankIntakeInfo> Banks);
