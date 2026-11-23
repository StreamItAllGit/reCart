// @module: reCart.Decomp.D4BuildReportValidator
// @purpose: Validates d4_build_report.json (both Tier R live schema and Standard 18 schema), d5_byte_match.json, and research.manifest.json.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, reEngine.Core.Decomp
// @exports: D4BuildReportValidator

using System.Text;
using System.Text.Json;
using reEngine.Core.Decomp;

namespace reCart.Decomp;

/// <summary>
/// Validates <c>d4_build_report.json</c>, <c>d5_byte_match.json</c>, and <c>research.manifest.json</c>
/// across both live Tier R research schemas and Standard 18 <c>D4BuildReport</c> schemas.
/// </summary>
public static class D4BuildReportValidator
{
    /// <summary>Validates a <c>d4_build_report.json</c> file on disk without mutating it.</summary>
    public static D4BuildReportValidationResult ValidateD4BuildReportFile(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        string full = Path.GetFullPath(reportPath);
        if (!File.Exists(full))
        {
            return new(false, -1, -1, 0, false, false, Array.Empty<string>(), [$"Missing d4_build_report.json: '{full}'."], "d4_build_report.json not found.");
        }
        return ValidateD4BuildReportJson(File.ReadAllText(full, Encoding.UTF8));
    }

    /// <summary>Validates a <c>d4_build_report.json</c> JSON payload supporting both <c>compilerErrors/parityPassed</c> and <c>compilerErrorCount/compilerFlags/zeroWarningsVerified</c>.</summary>
    public static D4BuildReportValidationResult ValidateD4BuildReportJson(string reportJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportJson);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(reportJson, D4ReportSchemaHelpers.LenientJsonDocOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(false, -1, -1, 0, false, false, Array.Empty<string>(), ["d4_build_report.json root must be a JSON object."], "Invalid d4_build_report.json root.");
            }

            var violations = new List<string>();
            if (DecompD4StrictnessValidator.ContainsCFunctionBody(reportJson))
            {
                violations.Add("d4_build_report.json must not embed C function bodies.");
            }

            if (!D4ReportSchemaHelpers.TryGetInt(root, out int errors, "compilerErrors", "compilerErrorCount"))
            {
                violations.Add("d4_build_report.json missing required 'compilerErrors' or 'compilerErrorCount'.");
                errors = -1;
            }
            else if (errors != 0)
            {
                violations.Add($"Non-zero compiler error count reported (compilerErrors={errors}).");
            }

            if (!D4ReportSchemaHelpers.TryGetInt(root, out int warnings, "compilerWarnings", "compilerWarningCount"))
            {
                violations.Add("d4_build_report.json missing required 'compilerWarnings' or 'compilerWarningCount'.");
                warnings = -1;
            }
            else if (warnings != 0)
            {
                violations.Add($"Non-zero compiler warning count reported (compilerWarnings={warnings}).");
            }

            int tells = 0;
            if (D4ReportSchemaHelpers.TryGetInt(root, out tells, "craftsmanshipTellCount") && tells != 0)
            {
                violations.Add($"craftsmanshipTellCount={tells} (expected 0).");
            }

            IReadOnlyList<string> flags = D4ReportSchemaHelpers.ExtractCompilerFlags(root);
            D4ReportSchemaHelpers.ValidateCompilerFlags(flags, "d4_build_report.json", violations);
            string? compilerField = D4ReportSchemaHelpers.TryGetString(root, "compiler");
            if (!string.IsNullOrWhiteSpace(compilerField) && compilerField.Contains("-Wno-everything", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add("Banned '-Wno-everything' flag detected in 'compiler' field.");
            }

            bool hasParity = D4ReportSchemaHelpers.TryGetBool(root, out bool parityPassed, "parityPassed");
            bool hasIsPassed = D4ReportSchemaHelpers.TryGetBool(root, out bool isPassed, "isPassed");
            bool hasZeroWarn = D4ReportSchemaHelpers.TryGetBool(root, out bool zeroWarnings, "isZeroWarningsVerified", "zeroWarningsVerified");
            bool hasZeroTells = D4ReportSchemaHelpers.TryGetBool(root, out bool zeroTells, "isZeroDecompilerTellsVerified", "zeroDecompilerTellsVerified");

            if (hasParity && !parityPassed) violations.Add("d4_build_report.json reported parityPassed=false.");
            if (hasIsPassed && !isPassed) violations.Add("d4_build_report.json reported isPassed=false.");
            if (hasZeroWarn && !zeroWarnings) violations.Add("d4_build_report.json reported zeroWarningsVerified=false.");
            if (hasZeroTells && !zeroTells) violations.Add("d4_build_report.json reported zeroDecompilerTellsVerified=false.");

            if (!hasParity && !hasIsPassed && !hasZeroWarn)
            {
                violations.Add("d4_build_report.json missing verification status ('parityPassed', 'isPassed', or 'zeroWarningsVerified').");
            }

            if (D4ReportSchemaHelpers.TryGetInt(root, out int testsTotal, "parityTestsTotal", "assertionsTotalCount") &&
                D4ReportSchemaHelpers.TryGetInt(root, out int testsPassed, "parityTestsPassed", "assertionsPassedCount") &&
                (testsTotal <= 0 || testsPassed != testsTotal))
            {
                violations.Add($"D4 parity test mismatch: {testsPassed}/{testsTotal} passed.");
            }

            if (D4ReportSchemaHelpers.TryGetInt(root, out int totalReadable, "totalReadableFiles") &&
                D4ReportSchemaHelpers.TryGetInt(root, out int compiledFiles, "compiledFiles", "compiledFileCount") &&
                (compiledFiles <= 0 || compiledFiles < totalReadable))
            {
                violations.Add($"D4 compiled file coverage incomplete: {compiledFiles}/{totalReadable}.");
            }

            bool effectiveParity = (!hasParity || parityPassed) && (!hasIsPassed || isPassed) && (hasParity || hasIsPassed || hasZeroWarn);
            bool effectiveZeroWarn = errors == 0 && warnings == 0 && (!hasZeroWarn || zeroWarnings);
            bool ok = violations.Count == 0;
            string summary = ok
                ? $"d4_build_report.json verified (errors=0, warnings=0, tells={tells}, parity={effectiveParity}, zeroWarnings={effectiveZeroWarn})."
                : $"d4_build_report.json validation failed ({violations.Count} violation(s)): {string.Join("; ", violations)}";

            return new(ok, errors, warnings, tells, effectiveParity, effectiveZeroWarn, flags, violations, summary);
        }
        catch (JsonException ex)
        {
            return new(false, -1, -1, 0, false, false, Array.Empty<string>(), [$"Malformed d4_build_report.json: {ex.Message}"], $"Malformed d4_build_report.json: {ex.Message}");
        }
    }

    /// <summary>Validates a <c>d5_byte_match.json</c> file on disk without mutating it.</summary>
    public static D5ByteMatchValidationResult ValidateD5ByteMatchFile(string byteMatchPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(byteMatchPath);
        string full = Path.GetFullPath(byteMatchPath);
        if (!File.Exists(full))
        {
            return new(false, 0, -1, 0, [$"Missing d5_byte_match.json: '{full}'."], "d5_byte_match.json not found.");
        }
        return ValidateD5ByteMatchJson(File.ReadAllText(full, Encoding.UTF8));
    }

    /// <summary>Validates a <c>d5_byte_match.json</c> JSON payload (<c>passed == true</c>, <c>mismatchedBytes == 0</c>, <c>matchedBinaries &gt; 0</c>).</summary>
    public static D5ByteMatchValidationResult ValidateD5ByteMatchJson(string byteMatchJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(byteMatchJson);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(byteMatchJson, D4ReportSchemaHelpers.LenientJsonDocOptions);
            return D4ReportSchemaHelpers.ValidateD5Core(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return new(false, 0, -1, 0, [$"Malformed d5_byte_match.json: {ex.Message}"], $"Malformed d5_byte_match.json: {ex.Message}");
        }
    }

    /// <summary>Validates a read-only upstream <c>research.manifest.json</c> file on disk.</summary>
    public static ResearchManifestValidationResult ValidateResearchManifestFile(string manifestPath, string? expectedPlatformId = "nes")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string full = Path.GetFullPath(manifestPath);
        if (!File.Exists(full))
        {
            return new(false, string.Empty, string.Empty, string.Empty, "none", "none", "none", 0, [$"Missing research.manifest.json: '{full}'."], "research.manifest.json not found.");
        }
        return ValidateResearchManifestJson(File.ReadAllText(full, Encoding.UTF8), expectedPlatformId);
    }

    /// <summary>Validates an upstream <c>research.manifest.json</c> JSON payload for NES platform, ABI, tiers, gates, and clean-room bounds.</summary>
    public static ResearchManifestValidationResult ValidateResearchManifestJson(string manifestJson, string? expectedPlatformId = "nes")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(manifestJson, D4ReportSchemaHelpers.LenientJsonDocOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(false, string.Empty, string.Empty, string.Empty, "none", "none", "none", 0, ["research.manifest.json root must be a JSON object."], "Invalid research.manifest.json root.");
            }

            var violations = new List<string>();
            string gameId = D4ReportSchemaHelpers.TryGetString(root, "gameId", "moduleId", "researchId") ?? string.Empty;
            string platform = D4ReportSchemaHelpers.TryGetString(root, "platform", "platformId") ?? string.Empty;
            string abiProfile = D4ReportSchemaHelpers.TryGetString(root, "abiProfile") ?? string.Empty;
            string currentTier = D4ReportSchemaHelpers.TryGetString(root, "currentDecompTier") ?? "none";
            string goalTier = D4ReportSchemaHelpers.TryGetString(root, "goalDecompTier") ?? "none";
            string parityTier = D4ReportSchemaHelpers.TryGetString(root, "d4ParityTier") ?? "none";

            if (string.IsNullOrWhiteSpace(gameId)) violations.Add("research.manifest.json missing required 'gameId' or 'moduleId'.");
            if (string.IsNullOrWhiteSpace(platform)) violations.Add("research.manifest.json missing required 'platform'.");
            else if (!string.IsNullOrWhiteSpace(expectedPlatformId) && !string.Equals(platform, expectedPlatformId, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"research.manifest.json platform '{platform}' mismatches expected '{expectedPlatformId}'.");
            }

            if (string.Equals(platform, "nes", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(abiProfile) &&
                !abiProfile.Contains("6502", StringComparison.OrdinalIgnoreCase) && !abiProfile.Contains("nes", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"NES research.manifest.json abiProfile '{abiProfile}' is incompatible with 6502/NES.");
            }

            if (!D4ReportSchemaHelpers.IsValidTierToken(currentTier)) violations.Add($"Invalid currentDecompTier '{currentTier}'.");
            if (!D4ReportSchemaHelpers.IsValidTierToken(goalTier)) violations.Add($"Invalid goalDecompTier '{goalTier}'.");

            int tierNum = D4ReportSchemaHelpers.ParseTierNumber(currentTier);
            if (tierNum >= 4 && !string.Equals(parityTier, "D4a", StringComparison.OrdinalIgnoreCase) && !string.Equals(parityTier, "D4b", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"research.manifest.json at tier '{currentTier}' must specify d4ParityTier 'D4a' or 'D4b' (found '{parityTier}').");
            }

            ValidateGatesAndMetrics(root, tierNum, violations, out int binariesCount);

            string? corpusRoot = D4ReportSchemaHelpers.TryGetString(root, "corpusRoot");
            if (!string.IsNullOrWhiteSpace(corpusRoot) && (Path.IsPathRooted(corpusRoot) || corpusRoot.Contains("..", StringComparison.Ordinal)))
            {
                violations.Add($"research.manifest.json corpusRoot '{corpusRoot}' must be a relative contained directory.");
            }

            bool ok = violations.Count == 0;
            string summary = ok
                ? $"research.manifest.json verified ({gameId}, platform={platform}, tier={currentTier}/{goalTier}, parity={parityTier}, binaries={binariesCount})."
                : $"research.manifest.json validation failed ({violations.Count} violation(s)): {string.Join("; ", violations)}";
            return new(ok, gameId, platform, abiProfile, currentTier, goalTier, parityTier, binariesCount, violations, summary);
        }
        catch (JsonException ex)
        {
            return new(false, string.Empty, string.Empty, string.Empty, "none", "none", "none", 0, [$"Malformed research.manifest.json: {ex.Message}"], $"Malformed research.manifest.json: {ex.Message}");
        }
    }

    private static void ValidateGatesAndMetrics(JsonElement root, int tierNum, List<string> violations, out int binariesCount)
    {
        binariesCount = 0;
        if (D4ReportSchemaHelpers.TryGetPropertyIgnoreCase(root, "binaries", out JsonElement binsEl) && binsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement bin in binsEl.EnumerateArray())
            {
                binariesCount++;
                if (string.IsNullOrWhiteSpace(D4ReportSchemaHelpers.TryGetString(bin, "name", "relpath")))
                {
                    violations.Add($"research.manifest.json binary entry #{binariesCount} missing 'name'.");
                }
            }
        }

        if (tierNum >= 0 && D4ReportSchemaHelpers.TryGetPropertyIgnoreCase(root, "gates", out JsonElement gatesEl) && gatesEl.ValueKind == JsonValueKind.Object)
        {
            for (int t = 0; t <= tierNum; t++)
            {
                string key = $"D{t}";
                if (!D4ReportSchemaHelpers.TryGetPropertyIgnoreCase(gatesEl, key, out JsonElement gateObj) ||
                    !D4ReportSchemaHelpers.TryGetBool(gateObj, out bool gatePassed, "passed") || !gatePassed)
                {
                    violations.Add($"research.manifest.json claims currentDecompTier='D{tierNum}' but gate '{key}.passed' is not true.");
                }
            }
        }

        if (D4ReportSchemaHelpers.TryGetPropertyIgnoreCase(root, "metrics", out JsonElement metEl) && metEl.ValueKind == JsonValueKind.Object)
        {
            if (D4ReportSchemaHelpers.TryGetInt(metEl, out int metBins, "binariesCount") && binariesCount > 0 && metBins != binariesCount)
            {
                violations.Add($"research.manifest.json metrics.binariesCount={metBins} != binaries.Count={binariesCount}.");
            }
            if (tierNum >= 3 && D4ReportSchemaHelpers.TryGetInt(metEl, out int failedFuncs, "failedFunctions") && failedFuncs != 0)
            {
                violations.Add($"research.manifest.json metrics.failedFunctions={failedFuncs} (expected 0 at D3+).");
            }
            if (tierNum >= 3 &&
                D4ReportSchemaHelpers.TryGetInt(metEl, out int fTotal, "functionsTotal") &&
                D4ReportSchemaHelpers.TryGetInt(metEl, out int fNamed, "functionsNamed") &&
                fTotal > 0 && fNamed != fTotal)
            {
                violations.Add($"research.manifest.json metrics.functionsNamed={fNamed} != functionsTotal={fTotal} at D3+.");
            }
        }
    }
}
