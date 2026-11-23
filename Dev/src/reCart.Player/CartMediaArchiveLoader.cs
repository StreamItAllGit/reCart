// @module: reCart.Player.CartMediaArchiveLoader
// @purpose: Extracts and SHA-256 validates .nes, .prg, .zip, and .7z ROM containers with fail-closed 7z resolution and ephemeral temp cleanup.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Security.Cryptography, Foundation.Core, Foundation.Retro, reCart.Contracts
// @exports: CartMediaArchiveLoader

using System.IO.Compression;
using System.Security.Cryptography;
using Foundation.Core;
using Foundation.Retro;
using reCart.Contracts;

namespace reCart.Player;

internal static class CartMediaArchiveLoader
{
    public static bool IsDirectRomExtension(string path) =>
        path.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedMediaCandidate(string path) =>
        IsDirectRomExtension(path) ||
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    public static bool ArchiveContainsManifest(string zipPath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            return archive.GetEntry("module.recomp.json") is not null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static List<string> EnumerateRomCandidates(string dir) =>
        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedMediaCandidate)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static byte[] ReadRomBytesFromFileOrArchive(string path, string? sevenZipOverridePath = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("ROM path cannot be empty.");
        string full = Path.GetFullPath(path.Trim());
        if (!File.Exists(full)) throw new FileNotFoundException($"ROM image not found: '{full}'.", full);

        if (full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(full);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => IsDirectRomExtension(e.FullName))
                ?? throw new InvalidDataException($"ZIP archive '{Path.GetFileName(full)}' contains no .nes or .prg ROM image.");
            using Stream stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        if (full.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return ReadSevenZipRomBytes(full, sevenZipOverridePath);
        }

        return File.ReadAllBytes(full);
    }

    public static bool MatchesAnyDeclaredChecksum(CartPackageManifest manifest, string filePath)
    {
        try
        {
            byte[] data = ReadRomBytesFromFileOrArchive(filePath);
            string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            return manifest.RequiredDiscChecksums.Any(c => string.Equals(c.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    public static string? StageVerifiedMediaIfExternal(string sourceFilePath, string dataRomDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath)) return null;
        string fullSource = Path.GetFullPath(sourceFilePath);
        string fullRomDir = Path.GetFullPath(dataRomDirectory);
        if (fullSource.StartsWith(fullRomDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fullSource;
        }

        Directory.CreateDirectory(fullRomDir);
        string targetPath = AppEnvironment.AssertPathContained(Path.Combine(fullRomDir, Path.GetFileName(fullSource)), fullRomDir);
        if (!File.Exists(targetPath)) File.Copy(fullSource, targetPath, overwrite: false);
        return targetPath;
    }

    internal static byte[] ReadSevenZipRomBytes(string archivePath, string? sevenZipOverridePath = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"recart_7z_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var opts = new MediaExtractionOptions(
                SevenZipOverridePath: sevenZipOverridePath,
                ToolsBaseDirectory: AppContext.BaseDirectory,
                ComputeSha256: false);
            MediaExtractionResult res = MediaContainerExtractor.ExtractOrVerifyAsync(archivePath, tempDir, opts).GetAwaiter().GetResult();
            if (!res.Success)
            {
                string prefix = res.IsToolMissingFallback ? "7z extraction tool unavailable" : $"Failed extracting '{Path.GetFileName(archivePath)}'";
                throw new InvalidDataException($"{prefix}: {res.DiagnosticMessage}");
            }

            string? romEntry = res.ExtractedFiles.FirstOrDefault(IsDirectRomExtension);
            if (romEntry is null)
            {
                throw new InvalidDataException($"7z archive '{Path.GetFileName(archivePath)}' contains no .nes or .prg ROM image.");
            }
            string safeEntry = AppEnvironment.AssertPathContained(romEntry, tempDir);
            return File.ReadAllBytes(safeEntry);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        }
    }
}
