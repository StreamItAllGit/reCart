// @module: reCart.Decomp.CartDecompPlatformProvider
// @purpose: Clean-room NES/6502 IDecompPlatformProvider orchestrating D0-D5 intake, SixTellDetector, D4/D5 validation, and clean-room guards.
// @depends-on: System, System.Diagnostics, System.IO, System.Linq, System.Threading, System.Threading.Tasks, reCart.Contracts, reEngine.Core.Decomp, reEngine.Core.Manifests
// @exports: CartDecompPlatformProvider

using System.Diagnostics;
using reCart.Contracts;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;

namespace reCart.Decomp;

/// <summary>
/// Clean-room NES / 6502 implementation of <see cref="IDecompPlatformProvider"/> supporting <c>D0</c>–<c>D5</c> progression,
/// BYO <c>.nes</c>/<c>.fds</c> media intake, read-only Tier R metadata validation, and <c>SixTellDetector</c> enforcement.
/// </summary>
public sealed class CartDecompPlatformProvider : IDecompPlatformProvider
{
    /// <summary>Initializes a new <see cref="CartDecompPlatformProvider"/> instance.</summary>
    public CartDecompPlatformProvider() { }

    /// <inheritdoc />
    public string PlatformId => CartEngineConstants.PlatformId;

    /// <inheritdoc />
    public string DisplayName => "reCart (Cart / 6502)";

    /// <inheritdoc />
    public string MinimumEngineVersion => CartEngineConstants.Version;

    /// <inheritdoc />
    public Task<DecompStageResult> RunDecompAsync(DecompRunOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        (DecompPackageManifest manifest, bool isResearchRepo) = CartDecompPackageResolver.LoadManifest(options.PackagePath);
        if (!string.Equals(manifest.PlatformId, PlatformId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Fail(options.TargetStage, options.OutputDirectory, sw,
                $"Unsupported platformId '{manifest.PlatformId}'; expected '{PlatformId}'.", "RECART_PLATFORM_MISMATCH"));
        }

        if (!isResearchRepo)
        {
            DecompCleanRoomValidationResult pkgCheck = CartCleanRoomValidator.ValidateCleanRoomPackage(options.PackagePath);
            if (!pkgCheck.IsValid)
            {
                return Task.FromResult(Fail(options.TargetStage, options.OutputDirectory, sw,
                    pkgCheck.Summary, "RECART_CLEANROOM_PACKAGE_VIOLATION"));
            }
        }

        OfflineToolchainStatus toolchain = CartDecompPackageResolver.LocateToolchain(options, manifest);
        if (options.DryRun)
        {
            return Task.FromResult(new DecompStageResult(options.TargetStage, true,
                $"[DRY-RUN] {DisplayName} D0-D5 pipeline ready (offlineToolchain={toolchain.IsAvailable}).",
                options.OutputDirectory, Array.Empty<string>(), sw.Elapsed));
        }

        string outDir = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outDir);
        return Task.FromResult(ExecutePipelineStages(options, manifest, isResearchRepo, toolchain, outDir, sw, ct));
    }

    /// <inheritdoc />
    public DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory) =>
        CartCleanRoomValidator.ValidateCleanRoomOutput(outputDirectory);

    /// <summary>Validates a <c>.decomp</c> package archive or directory against retail ROMs, Corpus leaks, and generated binaries.</summary>
    public DecompCleanRoomValidationResult ValidateCleanRoomPackage(string packageOrDirectory) =>
        CartCleanRoomValidator.ValidateCleanRoomPackage(packageOrDirectory);

    private DecompStageResult ExecutePipelineStages(
        DecompRunOptions options,
        DecompPackageManifest manifest,
        bool isResearchRepo,
        OfflineToolchainStatus toolchain,
        string outDir,
        Stopwatch sw,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CartDecompPackageResolver.TryResolveMediaOrResearch(options, manifest, out string? byoMedia, out string? researchRoot, out string? mediaErr))
        {
            return Fail(options.TargetStage, outDir, sw, mediaErr!, "RECART_MEDIA_NOT_FOUND");
        }

        CartMediaIntakeSummary? intake = null;
        if (byoMedia is not null || researchRoot is not null)
        {
            try
            {
                intake = byoMedia is not null
                    ? CartMediaIntakeInspector.InspectByoMedia(byoMedia, manifest.ModuleId, manifest.ExpectedSha256)
                    : CartMediaIntakeInspector.InspectUpstreamResearchReadOnly(researchRoot!, manifest.ModuleId);
                CartDecompStageArtifacts.WriteStageD0Intake(outDir, intake);
            }
            catch (InvalidDataException ex)
            {
                string code = ex.Message.Contains("SHA-256 mismatch", StringComparison.OrdinalIgnoreCase)
                    ? "RECART_CHECKSUM_MISMATCH" : "RECART_INVALID_MEDIA";
                return Fail(options.TargetStage, outDir, sw, ex.Message, code);
            }
        }
        else if (options.TargetStage != DecompStage.D4)
        {
            return Fail(options.TargetStage, outDir, sw,
                $"Stage {options.TargetStage} requires BYO .nes/.fds media under '{options.DataDirectory}' or read-only upstream research metadata.",
                "RECART_MEDIA_NOT_FOUND");
        }

        if (options.TargetStage >= DecompStage.D1 && intake is not null) CartDecompStageArtifacts.WriteStageD1BankMap(outDir, intake);
        if (options.TargetStage >= DecompStage.D2)
        {
            if (string.Equals(CartDecompPackageResolver.GetArg(options, "invokeGhidra"), "true", StringComparison.OrdinalIgnoreCase) && !toolchain.IsAvailable)
            {
                return Fail(options.TargetStage, outDir, sw, toolchain.DiagnosticMessage, "RECART_TOOLCHAIN_MISSING");
            }
            CartDecompStageArtifacts.WriteStageD2SymbolRecipe(outDir, manifest, intake, toolchain);
        }
        if (options.TargetStage >= DecompStage.D3) CartDecompStageArtifacts.WriteStageD3LiftRecipe(outDir, manifest, intake);

        if (options.TargetStage >= DecompStage.D4 &&
            !TryExecuteStageD4(options, manifest, isResearchRepo, researchRoot, outDir, sw, out DecompStageResult? d4Fail))
        {
            return d4Fail!;
        }

        if (options.TargetStage >= DecompStage.D5 &&
            !TryExecuteStageD5(options, manifest, isResearchRepo, researchRoot, intake, outDir, sw, out DecompStageResult? d5Fail))
        {
            return d5Fail!;
        }

        DecompCleanRoomValidationResult outCheck = ValidateCleanRoomOutput(outDir);
        DecompCleanRoomValidationResult coreCheck = DecompPipelineCoordinator.ValidateCleanRoomOutputDirectory(outDir);
        if (!outCheck.IsValid || !coreCheck.IsValid)
        {
            return Fail(options.TargetStage, outDir, sw, !outCheck.IsValid ? outCheck.Summary : coreCheck.Summary, "RECART_CLEANROOM_OUTPUT_VIOLATION");
        }

        string[] files = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        return new DecompStageResult(options.TargetStage, true,
            $"Completed NES clean-room stage {options.TargetStage} ({files.Length} clean-room output file(s)).",
            outDir, files, sw.Elapsed);
    }

    private static bool TryExecuteStageD4(
        DecompRunOptions options,
        DecompPackageManifest manifest,
        bool isResearchRepo,
        string? researchRoot,
        string outDir,
        Stopwatch sw,
        out DecompStageResult? failure)
    {
        failure = null;
        SixTellScanResult? sixTell = null;
        string? explicitSrcDir = CartDecompPackageResolver.GetArg(options, "d4SourcesDir")
            ?? CartDecompPackageResolver.GetArg(options, "exportsDir")
            ?? CartDecompPackageResolver.GetArg(options, "cSourcesDir");
        if (!string.IsNullOrWhiteSpace(explicitSrcDir))
        {
            string fullSrc = Path.GetFullPath(explicitSrcDir);
            if (!Directory.Exists(fullSrc))
            {
                failure = Fail(options.TargetStage, outDir, sw, $"D4 source directory not found: '{fullSrc}'.", "RECART_D4_SOURCES_NOT_FOUND");
                return false;
            }
            sixTell = SixTellDetector.ScanDirectory(fullSrc);
            if (!sixTell.IsClean) { failure = Fail(options.TargetStage, outDir, sw, sixTell.Summary, "RECART_D4_SIX_TELLS_FAILED"); return false; }
        }
        else if (!isResearchRepo && CartDecompPackageResolver.HasCOrHeaderFiles(outDir))
        {
            sixTell = SixTellDetector.ScanDirectory(outDir);
            if (!sixTell.IsClean) { failure = Fail(options.TargetStage, outDir, sw, sixTell.Summary, "RECART_D4_SIX_TELLS_FAILED"); return false; }
        }

        D4BuildReportValidationResult? d4Build = null;
        string? explicitReport = CartDecompPackageResolver.GetArg(options, "d4BuildReportPath");
        string? reportPath = CartDecompPackageResolver.ResolveOptionalSchemaFile(explicitReport, researchRoot, outDir, options.PackagePath, "d4_build_report.json");
        if (!string.IsNullOrWhiteSpace(explicitReport) && reportPath is null)
        {
            failure = Fail(options.TargetStage, outDir, sw, $"Explicit d4_build_report.json not found: '{explicitReport}'.", "RECART_D4_BUILD_REPORT_FAILED");
            return false;
        }
        if (reportPath is not null)
        {
            d4Build = D4BuildReportValidator.ValidateD4BuildReportFile(reportPath);
            if (!d4Build.IsValid) { failure = Fail(options.TargetStage, outDir, sw, d4Build.Summary, "RECART_D4_BUILD_REPORT_FAILED"); return false; }
        }

        CartDecompStageArtifacts.WriteStageD4Report(outDir, manifest, sixTell, d4Build);
        return true;
    }

    private static bool TryExecuteStageD5(
        DecompRunOptions options,
        DecompPackageManifest manifest,
        bool isResearchRepo,
        string? researchRoot,
        CartMediaIntakeSummary? intake,
        string outDir,
        Stopwatch sw,
        out DecompStageResult? failure)
    {
        failure = null;
        string? explicitD5 = CartDecompPackageResolver.GetArg(options, "d5ByteMatchPath");
        string? d5Path = CartDecompPackageResolver.ResolveOptionalSchemaFile(explicitD5, researchRoot, outDir, options.PackagePath, "d5_byte_match.json");
        bool requireReport = !string.IsNullOrWhiteSpace(explicitD5) || researchRoot is not null || isResearchRepo;

        D5ByteMatchValidationResult? d5Check = null;
        if (d5Path is not null)
        {
            d5Check = D4BuildReportValidator.ValidateD5ByteMatchFile(d5Path);
            if (!d5Check.IsValid) { failure = Fail(options.TargetStage, outDir, sw, d5Check.Summary, "RECART_D5_BYTEMATCH_FAILED"); return false; }
        }
        else if (requireReport || intake is null)
        {
            failure = Fail(options.TargetStage, outDir, sw, "Missing required d5_byte_match.json for D5 byte-match gate.", "RECART_D5_BYTEMATCH_FAILED");
            return false;
        }

        CartDecompStageArtifacts.WriteStageD5Report(outDir, manifest, intake, d5Check);
        return true;
    }

    private static DecompStageResult Fail(DecompStage stage, string? outDir, Stopwatch sw, string message, string code) =>
        new(stage, false, message, outDir, Array.Empty<string>(), sw.Elapsed, code);
}
