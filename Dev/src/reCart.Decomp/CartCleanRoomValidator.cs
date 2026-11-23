// @module: reCart.Decomp.CartCleanRoomValidator
// @purpose: Enforces clean-room validation on output directories and .decomp packages rejecting ROM/FDS/bank/generated leaks.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, Foundation.Core, reEngine.Core.Decomp, reEngine.Core.Manifests
// @exports: CartCleanRoomValidator

using System.IO.Compression;
using Foundation.Core;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;

namespace reCart.Decomp;

internal static class CartCleanRoomValidator
{
    private static readonly byte[] NesMagic = [0x4E, 0x45, 0x53, 0x1A];
    private static readonly byte[] FdsHeaderMagic = [0x46, 0x44, 0x53, 0x1A];
    private static readonly byte[] FdsBlock1Magic = "*NINTENDO-HVC*"u8.ToArray();

    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nes", ".fds", ".prg", ".chr", ".rom", ".bin", ".iso", ".cue", ".chd", ".img",
        ".sav", ".srm", ".state", ".ips", ".bps", ".ups", ".nsf", ".nsfe", ".fm2", ".bk2", ".exe"
    };

    internal static DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string fullOut = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(fullOut))
        {
            return new DecompCleanRoomValidationResult(true, Array.Empty<string>(), "Output directory does not exist yet.");
        }

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fullOut, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(fullOut, file).Replace('\\', '/');
            if (HasCorpusSegment(rel) || HasForbiddenNameOrExtension(rel) || HasForbiddenMagicBytes(file))
            {
                violations.Add(rel);
            }
        }

        D4StrictnessValidationResult strict = DecompD4StrictnessValidator.ValidateWorkspace(fullOut);
        if (!strict.IsValid)
        {
            violations.AddRange(strict.Violations);
        }

        return FormatCleanRoomResult(violations, fullOut);
    }

    internal static DecompCleanRoomValidationResult ValidateCleanRoomPackage(string packageOrDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageOrDirectory);
        string full = Path.GetFullPath(packageOrDirectory);
        if (Directory.Exists(full)) return ValidateCleanRoomOutput(full);
        if (!File.Exists(full)) return new(false, [full], $"Package or directory not found: '{full}'.");

        if (!IsZipFile(full))
        {
            string name = Path.GetFileName(full);
            bool bad = HasForbiddenNameOrExtension(name) || HasForbiddenMagicBytes(full);
            return bad
                ? new(false, [name], $"Clean-room violation detected in '{name}'.")
                : new(true, Array.Empty<string>(), "Clean-room package manifest verified.");
        }

        var violations = new List<string>();
        string virtualRoot = Path.Combine(Path.GetTempPath(), "recart_cleanroom_zip");
        using (ZipArchive zip = ZipFile.OpenRead(full))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string rel = entry.FullName.Replace('\\', '/');
                _ = AppEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(virtualRoot, rel.Replace('/', Path.DirectorySeparatorChar))), virtualRoot);
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (HasCorpusSegment(rel) || HasForbiddenNameOrExtension(rel) || HasForbiddenZipMagic(entry))
                {
                    violations.Add(rel);
                }
            }
        }

        D4StrictnessValidationResult archiveCheck = DecompD4StrictnessValidator.ValidateDecompArchiveCleanRoom(full);
        if (!archiveCheck.IsValid) violations.AddRange(archiveCheck.Violations);

        return FormatCleanRoomResult(violations, full);
    }

    private static bool HasCorpusSegment(string relPath) =>
        relPath.Split('/', '\\').Any(seg => seg.Equals("Corpus", StringComparison.OrdinalIgnoreCase));

    private static bool HasForbiddenNameOrExtension(string relPath)
    {
        string name = Path.GetFileName(relPath);
        string ext = Path.GetExtension(name);
        return ForbiddenExtensions.Contains(ext) ||
               name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".Generated.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasForbiddenMagicBytes(string filePath)
    {
        Span<byte> buf = stackalloc byte[32];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = fs.Read(buf);
        return HasForbiddenMagicSpan(buf[..read]);
    }

    private static bool HasForbiddenZipMagic(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        Span<byte> buf = stackalloc byte[32];
        int total = 0;
        while (total < buf.Length)
        {
            int n = stream.Read(buf[total..]);
            if (n <= 0) break;
            total += n;
        }
        return HasForbiddenMagicSpan(buf[..total]);
    }

    private static bool HasForbiddenMagicSpan(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && (head[..4].SequenceEqual(NesMagic) || head[..4].SequenceEqual(FdsHeaderMagic))) return true;
        if (head.Length >= 15 && head.Slice(1, FdsBlock1Magic.Length).SequenceEqual(FdsBlock1Magic)) return true;
        return head.Length >= 31 && head.Slice(17, FdsBlock1Magic.Length).SequenceEqual(FdsBlock1Magic);
    }

    private static bool IsZipFile(string path)
    {
        Span<byte> h = stackalloc byte[2];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return fs.Read(h) == 2 && h[0] == 'P' && h[1] == 'K';
    }

    private static DecompCleanRoomValidationResult FormatCleanRoomResult(List<string> violations, string target)
    {
        string[] dedup = violations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return dedup.Length == 0
            ? new(true, Array.Empty<string>(), $"Clean-room verification passed for '{target}'.")
            : new(false, dedup, $"Clean-room violation ({dedup.Length} item(s)): {string.Join("; ", dedup)}");
    }
}
