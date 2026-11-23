// @module: reCart.Compiler.CleanRoomRecompPackager
// @purpose: Packages and validates strictly 3-member clean-room .recomp ZIP archives per Dev Bible Standard 22.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text, Foundation.Core, reCart.Contracts, reEngine.Core
// @exports: CleanRoomArchiveValidationResult, CleanRoomRecompPackager

using System.IO.Compression;
using System.Text;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Manifests;

namespace reCart.Compiler;

/// <summary>
/// Validation outcome returned by <see cref="CleanRoomRecompPackager.ValidateCleanRoomArchive"/>.
/// </summary>
public sealed record CleanRoomArchiveValidationResult(
    bool IsValid,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> Violations,
    string Summary);

/// <summary>
/// Produces and strictly validates 3-member clean-room <c>.recomp</c> ZIP packages per Dev Bible Standard 22.
/// </summary>
public static class CleanRoomRecompPackager
{
    /// <summary>Canonical root manifest entry path inside <c>.recomp</c> ZIP archives.</summary>
    public const string ManifestEntryName = "module.recomp.json";

    /// <summary>Canonical lifted C# source entry path inside <c>.recomp</c> ZIP archives.</summary>
    public const string LiftedSourceEntryName = "lifted/CartLiftedModule.cs";

    /// <summary>Canonical dispatch metadata entry path inside 3-member lifted <c>.recomp</c> ZIP archives.</summary>
    public const string DispatchMetaEntryName = "lifted/dispatch.meta.json";

    /// <summary>Canonical clean-room recipe entry path inside 2-member recipe <c>.recomp</c> ZIP archives.</summary>
    public const string AotRecipeEntryName = "aot-recipe.json";

    private static readonly HashSet<string> ExpectedEntries = new(StringComparer.Ordinal)
    {
        ManifestEntryName,
        LiftedSourceEntryName,
        DispatchMetaEntryName
    };

    private static readonly HashSet<string> ExpectedRecipeEntries = new(StringComparer.Ordinal)
    {
        ManifestEntryName,
        AotRecipeEntryName
    };

    private static readonly string[] ForbiddenSuffixes =
    [
        ".generated.dll", ".g.cs", ".nes", ".fds", ".prg", ".chr", ".rom", ".bin", ".sav", ".dll", ".exe"
    ];

    /// <summary>
    /// Lifts <paramref name="romBytes"/> and packs strictly 3 clean-room entries into <paramref name="outputRecompZipPath"/>.
    /// </summary>
    public static string PackageCleanRoomRecomp(
        ReadOnlySpan<byte> romBytes,
        CartPackageManifest manifest,
        string outputRecompZipPath,
        Lifter6502ToCSharp? lifter = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRecompZipPath);

        LiftedProgramResult lifted = (lifter ?? new Lifter6502ToCSharp()).Lift(romBytes, manifest);
        CartPackageManifest normalized = PrepareManifest(manifest, lifted);
        string manifestJson = normalized.Serialize();

        string fullOutputPath = Path.GetFullPath(outputRecompZipPath);
        string outDir = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException($"Invalid output path '{fullOutputPath}'.");
        Directory.CreateDirectory(outDir);

        string tempPath = Path.Combine(outDir, $".tmp_{Guid.NewGuid():N}.recomp");
        try
        {
            using (FileStream fs = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteUtf8Entry(zip, ManifestEntryName, manifestJson);
                WriteUtf8Entry(zip, LiftedSourceEntryName, lifted.CSharpSource);
                WriteUtf8Entry(zip, DispatchMetaEntryName, lifted.DispatchMetaJson);
            }

            AssertValidCleanRoomArchive(tempPath);
            File.Move(tempPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }
        }

        AssertValidCleanRoomArchive(fullOutputPath);
        return fullOutputPath;
    }

    /// <summary>
    /// Packs strictly 2 clean-room JSON manifests (<c>module.recomp.json</c> and <c>aot-recipe.json</c>)
    /// into <paramref name="outputRecompZipPath"/> with zero lifted C#, binaries, or ROM bytes.
    /// </summary>
    public static string PackageCleanRoomRecipeRecomp(
        CartPackageManifest manifest,
        CartAotRecipeSpec recipe,
        string outputRecompZipPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRecompZipPath);

        manifest.Validate();
        recipe.Validate();

        string manifestJson = manifest.Serialize();
        string recipeJson = recipe.Serialize();

        string fullOutputPath = Path.GetFullPath(outputRecompZipPath);
        string outDir = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException($"Invalid output path '{fullOutputPath}'.");
        Directory.CreateDirectory(outDir);

        string tempPath = Path.Combine(outDir, $".tmp_{Guid.NewGuid():N}.recomp");
        try
        {
            using (FileStream fs = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteUtf8Entry(zip, ManifestEntryName, manifestJson);
                WriteUtf8Entry(zip, AotRecipeEntryName, recipeJson);
            }

            AssertValidCleanRoomArchive(tempPath);
            File.Move(tempPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }
        }

        AssertValidCleanRoomArchive(fullOutputPath);
        return fullOutputPath;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="zipPath"/> is a 2-member clean-room recipe archive.</summary>
    public static bool IsRecipeArchive(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        string fullZip = Path.GetFullPath(zipPath);
        if (!File.Exists(fullZip)) return false;
        using FileStream fs = File.OpenRead(fullZip);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        return names.SetEquals(ExpectedRecipeEntries);
    }

    /// <summary>
    /// Validates a <c>.recomp</c> archive for clean-room layout (strictly 3-member lifted or 2-member recipe), zero binaries/ROMs, and Zip-Slip safety.
    /// </summary>
    public static CleanRoomArchiveValidationResult ValidateCleanRoomArchive(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        string fullZip = Path.GetFullPath(zipPath);
        var entries = new List<string>();
        var violations = new List<string>();

        if (!File.Exists(fullZip))
        {
            violations.Add($"Archive file not found: '{fullZip}'.");
            return new CleanRoomArchiveValidationResult(false, entries, violations, violations[0]);
        }

        try
        {
            using FileStream fs = File.OpenRead(fullZip);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            string virtualRoot = Path.Combine(Path.GetTempPath(), "recart_zip_guard");

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string rawName = entry.FullName;
                string normalized = rawName.Replace('\\', '/');
                entries.Add(normalized);

                if (normalized.EndsWith('/'))
                {
                    violations.Add($"Directory entries are prohibited in clean-room archive: '{rawName}'.");
                    continue;
                }

                string osRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(osRelative) || normalized.Contains(':', StringComparison.Ordinal) ||
                    normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal))
                {
                    violations.Add($"Zip-Slip violation: entry '{rawName}' escapes archive containment.");
                }
                else
                {
                    try
                    {
                        string candidate = Path.GetFullPath(Path.Combine(virtualRoot, osRelative));
                        _ = AppEnvironment.AssertPathContained(candidate, virtualRoot);
                    }
                    catch (Exception ex)
                    {
                        violations.Add($"Zip-Slip containment error for '{rawName}': {ex.Message}");
                    }
                }

                if (IsForbiddenEntry(normalized))
                {
                    violations.Add($"Standard 22 clean-room violation: forbidden artifact '{rawName}'.");
                }
            }

            var entrySet = entries.ToHashSet(StringComparer.Ordinal);
            bool isLifted3 = entries.Count == 3 && entrySet.SetEquals(ExpectedEntries);
            bool isRecipe2 = entries.Count == 2 && entrySet.SetEquals(ExpectedRecipeEntries);

            if (!isLifted3 && !isRecipe2)
            {
                violations.Add($"Clean-room .recomp archive must contain either 3 members [{string.Join(", ", ExpectedEntries)}] or 2 recipe members [{string.Join(", ", ExpectedRecipeEntries)}], found {entries.Count} [{string.Join(", ", entries)}].");
            }

            if (violations.Count == 0)
            {
                ValidateEmbeddedManifest(archive, fullZip, violations);
                if (isRecipe2)
                {
                    ValidateEmbeddedRecipe(archive, violations);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            violations.Add($"Corrupt ZIP archive '{fullZip}': {ex.Message}");
        }

        bool isValid = violations.Count == 0;
        string summary = isValid
            ? $"Verified clean-room {(entries.Count == 2 ? "2-member recipe" : "3-member")} .recomp archive '{Path.GetFileName(fullZip)}'."
            : $"Rejected .recomp archive '{Path.GetFileName(fullZip)}': {string.Join(" | ", violations)}";
        return new CleanRoomArchiveValidationResult(isValid, entries, violations, summary);
    }

    /// <summary>
    /// Validates <paramref name="zipPath"/> and throws <see cref="InvalidDataException"/> on any clean-room violation.
    /// </summary>
    public static void AssertValidCleanRoomArchive(string zipPath)
    {
        CleanRoomArchiveValidationResult result = ValidateCleanRoomArchive(zipPath);
        if (!result.IsValid)
        {
            throw new InvalidDataException(result.Summary);
        }
    }

    private static void ValidateEmbeddedManifest(ZipArchive archive, string fullZip, List<string> violations)
    {
        ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
        if (manifestEntry is null)
        {
            violations.Add($"Missing '{ManifestEntryName}'.");
            return;
        }

        try
        {
            using Stream s = manifestEntry.Open();
            using var reader = new StreamReader(s, Encoding.UTF8);
            string json = reader.ReadToEnd();
            CartPackageManifest cartManifest = CartPackageManifest.Deserialize(json);
            cartManifest.Validate();
            _ = RecompPackageReader.Load(fullZip);
        }
        catch (Exception ex)
        {
            violations.Add($"Manifest validation failed: {ex.Message}");
        }
    }

    private static void ValidateEmbeddedRecipe(ZipArchive archive, List<string> violations)
    {
        ZipArchiveEntry? recipeEntry = archive.GetEntry(AotRecipeEntryName);
        if (recipeEntry is null)
        {
            violations.Add($"Missing '{AotRecipeEntryName}'.");
            return;
        }

        try
        {
            using Stream s = recipeEntry.Open();
            using var reader = new StreamReader(s, Encoding.UTF8);
            CartAotRecipeSpec spec = CartAotRecipeSpec.Deserialize(reader.ReadToEnd());
            spec.Validate();
        }
        catch (Exception ex)
        {
            violations.Add($"Recipe validation failed: {ex.Message}");
        }
    }

    private static bool IsForbiddenEntry(string normalized)
    {
        foreach (string suffix in ForbiddenSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void WriteUtf8Entry(ZipArchive zip, string entryName, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static CartPackageManifest PrepareManifest(CartPackageManifest src, LiftedProgramResult lifted)
    {
        var copy = new CartPackageManifest
        {
            SchemaVersion = src.SchemaVersion < 1 ? 1 : src.SchemaVersion,
            ModuleId = lifted.ModuleId,
            Title = string.IsNullOrWhiteSpace(src.Title) ? lifted.ModuleId : src.Title.Trim(),
            PlatformId = CartEngineConstants.PlatformId,
            Version = string.IsNullOrWhiteSpace(src.Version) ? CartEngineConstants.Version : src.Version.Trim(),
            EngineId = string.IsNullOrWhiteSpace(src.EngineId) ? CartEngineConstants.EngineId : src.EngineId.Trim(),
            EngineVersion = string.IsNullOrWhiteSpace(src.EngineVersion) ? CartEngineConstants.Version : src.EngineVersion.Trim(),
            CompatibleEngineTrack = string.IsNullOrWhiteSpace(src.CompatibleEngineTrack) ? CartEngineConstants.CompatibleEngineTrack : src.CompatibleEngineTrack.Trim(),
            SupportedEngineTracks = src.SupportedEngineTracks is { Count: > 0 } ? [.. src.SupportedEngineTracks] : [.. CartEngineConstants.SupportedEngineTracks],
            PreloadRomDir = string.IsNullOrWhiteSpace(src.PreloadRomDir) ? "data/rom" : src.PreloadRomDir.Trim(),
            ModuleTypeName = lifted.ModuleTypeName,
            SerialNumber = src.SerialNumber,
            EntryAssembly = src.EntryAssembly,
            BackendId = string.IsNullOrWhiteSpace(src.BackendId) ? CartRenderBackendIds.OpenGl : src.BackendId,
            AspectRatioMode = string.IsNullOrWhiteSpace(src.AspectRatioMode) ? "nativePar" : src.AspectRatioMode,
            ExecutionTierMode = string.IsNullOrWhiteSpace(src.ExecutionTierMode) ? "staticAotOnly" : src.ExecutionTierMode,
            VideoRegionStandardName = string.IsNullOrWhiteSpace(src.VideoRegionStandardName)
                ? CartVideoRegionCodec.ToCanonicalId(lifted.Header.VideoRegionStandard)
                : src.VideoRegionStandardName,
            MapperId = lifted.Header.MapperId,
            SubmapperId = lifted.Header.SubmapperId,
            DefaultFramerateMode = src.DefaultFramerateMode,
            RequiredDiscChecksums = [.. src.RequiredDiscChecksums],
            ExtensionData = src.ExtensionData is null ? null : new Dictionary<string, System.Text.Json.JsonElement>(src.ExtensionData)
        };
        copy.Validate();
        return copy;
    }
}
